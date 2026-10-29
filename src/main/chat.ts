import { randomUUID } from 'node:crypto';
import { WorkbenchError } from '../shared/workbench';
import type { ChatAttachment, ChatFocus, Message } from '../shared/types';
import { CHAT_MODELS } from '../shared/chat-models';
import type { Store } from './store';
import type { ChatStore } from './chat-store';
import type { Operations } from './operations';
import type { ChatRegistry, ResolvedChatTarget } from './chat-registry';
import type { DiagnosticLog } from './diagnostics';
import { redactText } from './diagnostics';
import { instructions, toolActivity, tools, toolSchemas } from './assistant';
import { ContextTokenCounter } from './chat-context';
import { CodexClient } from './providers/codex';
import { diagnosticError } from './providers/errors';
import { AnthropicClient } from './providers/generic/anthropic';
import { ChatCompletionsClient, toChatMessages, toResponsesOutput } from './providers/generic/chat';
import { GenericResponsesClient } from './providers/generic/responses';
import { capabilityNote, toolsForModel } from './providers/generic/capabilities';

interface ChatResult { output: unknown[]; calls: { id: string; name: string; arguments: string }[]; requestId?: string | null }
interface ToolOutcome { output: string; image?: { label: string; dataUrl: string } }
interface RequestOptions { input: unknown[]; instructions: string; signal: AbortSignal; onText: (text: string) => void }

const REQUEST_TIMEOUT_MS = 180_000;
const MAX_TOOL_OUTPUT = 200_000;
const IMAGE_BUDGET = 8;
const IMAGE_MARKER = '[image removed from context to bound its size; call view_image again to see it]';
const safeError = (error: unknown) => redactText(error instanceof Error ? error.message : 'Request failed.');
const logReference = (reference: string | null) => reference ? `\nDiagnostic reference: ${reference}. Open diagnostic logs from the toolbar for details.` : '';

/**
 * The inline assistant: one running turn per conversation, tools executed through the shared
 * operation registry with origin `agent`, every call recorded in the ledger before it runs.
 */
export class ChatService {
  private turns = new Map<string, AbortController>();
  private turnPromises = new Set<Promise<void>>();
  private live = new Map<string, { input: unknown[]; text: string }>();
  private focus = new Map<string, ChatFocus>();
  private counter = new ContextTokenCounter();
  private codex = new CodexClient();
  private stopped = false;
  constructor(private store: Store, private chats: ChatStore, private ops: Operations, private registry: ChatRegistry, private codexToken: () => Promise<string>, private diagnostics: Pick<DiagnosticLog, 'record'>) {}

  active(sessionId: string) { return this.turns.has(sessionId); }
  activeTurns() { return [...this.turns.keys()]; }
  messages(sessionId: string): Message[] { this.store.session(sessionId); return this.chats.messages(sessionId); }

  send(sessionId: string, text: string, focus: ChatFocus = {}) {
    if (this.stopped) throw new WorkbenchError('CLOSING', 'PixelChat is closing.');
    const { projectId } = this.store.session(sessionId);
    if (this.turns.has(sessionId)) throw new WorkbenchError('BUSY', 'A response is already running in this conversation.');
    // Focus comes from the renderer: confirm it names records in this conversation's project.
    if (focus.reviewId) this.store.reviewSummary(focus.reviewId, projectId);
    if (focus.itemId) this.store.item(focus.itemId, projectId);
    this.focus.set(sessionId, focus);
    const controller = new AbortController(); this.turns.set(sessionId, controller);
    this.chats.autoTitle(sessionId, text); this.chats.addMessage(sessionId, 'user', text);
    const promise = this.runTurn(sessionId, projectId, text, controller).finally(() => { this.turns.delete(sessionId); this.turnPromises.delete(promise); this.store.changed(); });
    this.turnPromises.add(promise);
  }
  cancel(sessionId: string) { this.turns.get(sessionId)?.abort(); }

  context(sessionId: string, draft: string) {
    const { projectId } = this.store.session(sessionId);
    const target = this.registry.resolveActive();
    const live = this.live.get(sessionId);
    const input = live?.input ?? this.chats.repairHistory(sessionId, this.chats.history(sessionId)).input;
    const limit = target.kind === 'codex' ? CHAT_MODELS.find(entry => entry.id === target.slug)?.contextLimit ?? null : null;
    return { model: target.modelLabel, limit, ...this.counter.count({ instructions: this.instructions(sessionId, projectId, target), tools: this.tools(target), input, draft, streamingText: live?.text }) };
  }

  async shutdown() {
    this.stopped = true;
    for (const controller of this.turns.values()) controller.abort();
    await Promise.allSettled([...this.turnPromises]);
  }

  private async runTurn(sessionId: string, projectId: string, text: string, controller: AbortController) {
    let message = this.chats.addMessage(sessionId, 'assistant', '', 'running');
    let round = 0, toolsRan = false;
    try {
      const repaired = this.chats.repairHistory(sessionId, this.chats.history(sessionId));
      if (repaired.restoredCallIds.length) this.diagnostics.record('chat.history_repaired', { sessionId, callIds: repaired.restoredCallIds });
      const input = repaired.input;
      const live = { input, text: '' }; this.live.set(sessionId, live);
      input.push({ role: 'user', content: [{ type: 'input_text', text }] });
      this.chats.setHistory(sessionId, input);
      while (!controller.signal.aborted) {
        round++;
        const target = this.registry.resolveActive();
        this.diagnostics.record('chat.request_started', { sessionId, round, model: target.selectionId });
        const response = await this.request(target, {
          input, instructions: this.instructions(sessionId, projectId, target), signal: controller.signal,
          onText: delta => { live.text += delta; message.text += delta; this.chats.updateMessage(message); },
        });
        this.diagnostics.record('chat.request_completed', { sessionId, round, requestId: response.requestId ?? null, calls: response.calls.map(call => call.name) });
        message.status = 'completed'; this.chats.updateMessage(message);
        input.push(...response.output);
        live.text = '';
        for (const call of response.calls) {
          const outcome = controller.signal.aborted
            ? { output: JSON.stringify({ error: 'Turn stopped before this tool ran.' }) }
            : await this.executeTool(sessionId, projectId, call, controller.signal);
          toolsRan = true;
          input.push({ type: 'function_call_output', call_id: call.id, output: outcome.output });
          if (outcome.image) {
            input.push({ role: 'user', content: [{ type: 'input_text', text: `Attached from tool call ${call.id}: ${outcome.image.label}` }, { type: 'input_image', image_url: outcome.image.dataUrl }] });
            evictImages(input);
          }
        }
        this.chats.setHistory(sessionId, input);
        if (!response.calls.length || controller.signal.aborted) break;
        message = this.chats.addMessage(sessionId, 'assistant', '', 'running');
      }
      if (message.status === 'running') { message.status = 'cancelled'; this.chats.updateMessage(message); }
    } catch (error) {
      const stopped = controller.signal.aborted;
      const reference = this.diagnostics.record(stopped ? 'chat.cancelled' : 'chat.failed', { sessionId, round, error: diagnosticError(error) });
      message.status = stopped ? 'cancelled' : 'failed';
      const detail = toolsRan ? 'The assistant follow-up failed. Changes already made remain and were not repeated.' : 'The chat request failed.';
      const note = stopped ? 'Response stopped.' : `${detail}\n${safeError(error)}${logReference(reference)}`;
      message.text = message.text ? `${message.text}\n${note}` : note;
      this.chats.updateMessage(message);
    } finally { this.live.delete(sessionId); }
  }

  private tools(target: ResolvedChatTarget) { return toolsForModel(target.record ?? { vision: true }, tools); }

  private instructions(sessionId: string, projectId: string, target: ResolvedChatTarget) {
    const session = this.store.session(sessionId), project = this.store.project(projectId);
    const titledBy = session.titleSource === 'user' ? 'named by the user; keep it' : session.titleSource === 'assistant' ? 'named by you' : 'automatic';
    const count = (status?: 'pending_export') => this.store.searchItems(projectId, { status, limit: 1, offset: 0 }).total;
    const reviews = this.store.reviews(projectId, 'active').slice(0, 10).map(review => `"${review.title}" (${review.id}, ${review.counts.unreviewed}/${review.counts.total} unreviewed, ${review.openNotes} open notes)`);
    const focus = this.focus.get(sessionId) ?? {};
    const viewing: string[] = [];
    if (focus.reviewId) viewing.push(`review "${this.store.reviewSummary(focus.reviewId, projectId).title}" (${focus.reviewId})`);
    if (focus.itemId) { const item = this.store.item(focus.itemId, projectId); viewing.push(`item "${item.name}" in ${item.collection} (${item.id}, selected version ${item.selectedRevisionId})`); }
    const note = target.record ? capabilityNote(target.record) : '';
    return [instructions,
      `Current project: "${project.name}" with ${count()} items, ${count('pending_export')} pending export, ${this.store.jobs(projectId, { active: true }).length} active image jobs. Active reviews: ${reviews.join('; ') || 'none'}.`,
      `Conversation title: "${session.title}" (${titledBy}).`,
      viewing.length ? `The user is viewing ${viewing.join(' within ')}; "this" refers to it unless they say otherwise.` : 'The user is not focused on a particular item.',
      note].filter(Boolean).join('\n');
  }

  private async request(target: ResolvedChatTarget, options: RequestOptions): Promise<ChatResult> {
    const signal = AbortSignal.any([options.signal, AbortSignal.timeout(REQUEST_TIMEOUT_MS)]);
    const common = { model: target.slug, input: options.input, instructions: options.instructions, tools: this.tools(target), signal, onText: options.onText };
    if (target.kind === 'codex') return this.codex.streamChat({ ...common, effort: target.effort, token: await this.codexToken() });
    const client = { baseUrl: target.provider!.baseUrl, apiKey: await this.registry.providerKey(target.provider!.id) };
    if (target.kind === 'anthropic') return new AnthropicClient(client).streamChat({ ...common, maxTokens: target.record?.maxTokens ?? 8192 });
    if (target.kind === 'responses') return new GenericResponsesClient(client).streamChat({ ...common, effort: target.effort || undefined });
    const result = await new ChatCompletionsClient(client).streamChat({ model: target.slug, messages: toChatMessages(options.input, options.instructions), tools: common.tools, effort: target.effort || undefined, signal, onText: options.onText });
    return { output: toResponsesOutput(result.text, result.calls), calls: result.calls, requestId: result.requestId };
  }

  private async executeTool(sessionId: string, projectId: string, call: { id: string; name: string; arguments: string }, signal: AbortSignal): Promise<ToolOutcome> {
    const recorded = this.chats.beginTool(sessionId, call.id, call.name, call.arguments);
    if (!recorded.fresh) { this.diagnostics.record('tool.restored', { sessionId, callId: call.id, name: call.name }); return { output: recorded.result ?? JSON.stringify({ error: 'This tool outcome is uncertain and was not replayed.' }) }; }
    try {
      if (!Object.hasOwn(toolSchemas, call.name)) throw new WorkbenchError('UNKNOWN_TOOL', `Unknown tool ${call.name}.`);
      const args = JSON.parse(call.arguments || '{}') as Record<string, any>;
      if (!args || typeof args !== 'object' || Array.isArray(args)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Tool arguments must be a JSON object.');
      if (call.name === 'generate_images' || call.name === 'edit_image') {
        // Admission is billed work: record the idempotency key first so an interruption is recoverable, never replayed.
        args.requestId ??= randomUUID();
        this.chats.waitTool(sessionId, call.id, { requestId: args.requestId });
      }
      const fields = { ...args, projectId, ...(call.name === 'rename_session' ? { sessionId } : {}) };
      const pending = this.ops.call(call.name, fields, 'agent');
      // Waiting reads can run for minutes; stopping the turn stops waiting, not the operation.
      const result: any = args.wait ? await Promise.race([pending, stopped(signal)]) : await pending;
      let image: ToolOutcome['image'];
      let output: unknown = result;
      if (call.name === 'view_image') {
        const { media, ...rest } = result;
        const item = this.store.item(args.itemId, projectId);
        const label = `“${item.name}” ${rest.selected ? 'selected version' : `version ${rest.revisionId}`}, ${rest.viewWidth}×${rest.viewHeight} at ${rest.scale}× on ${rest.background}`;
        image = { label, dataUrl: `data:${media.mimeType};base64,${media.data}` };
        output = { ...rest, image: 'attached' };
        const attachment: ChatAttachment = { kind: 'image', label: `Viewed “${item.name}”`, itemId: item.id, revisionId: rest.revisionId };
        this.chats.addMessage(sessionId, 'tool', `Looked at “${item.name}”${rest.pins.length ? ` with ${rest.pins.length} pin${rest.pins.length === 1 ? '' : 's'}` : ''}.`, 'completed', [attachment]);
      }
      let text = JSON.stringify(output);
      if (text.length > MAX_TOOL_OUTPUT) text = JSON.stringify({ error: `The result is too large (${text.length} characters). Narrow it with limit, filters, or ids.` });
      this.chats.finishTool(sessionId, call.id, text);
      const activity = toolActivity(call.name, args, result);
      if (activity) this.chats.addMessage(sessionId, 'tool', activity);
      this.diagnostics.record('tool.completed', { sessionId, callId: call.id, name: call.name });
      return { output: text, image };
    } catch (error) {
      this.diagnostics.record('tool.failed', { sessionId, callId: call.id, name: call.name, error: diagnosticError(error) });
      const code = error instanceof WorkbenchError ? error.code : undefined;
      const output = JSON.stringify({ error: { ...(code ? { code } : {}), message: safeError(error) } });
      this.chats.finishTool(sessionId, call.id, output);
      this.chats.addMessage(sessionId, 'tool', `Could not ${call.name.replaceAll('_', ' ')}: ${safeError(error)}`, 'failed');
      return { output };
    }
  }
}

function stopped(signal: AbortSignal) {
  return new Promise<never>((_resolve, reject) => {
    const fail = () => reject(new WorkbenchError('STOPPED', 'The turn was stopped while waiting.'));
    if (signal.aborted) fail(); else signal.addEventListener('abort', fail, { once: true });
  });
}

/** Keeps only the newest images in persisted history so each request stays bounded. */
function evictImages(input: unknown[]) {
  const parts: { content: unknown[]; index: number }[] = [];
  for (const item of input) {
    const content = item && typeof item === 'object' ? (item as { content?: unknown }).content : undefined;
    if (!Array.isArray(content)) continue;
    content.forEach((part, index) => { if ((part as { type?: unknown } | null)?.type === 'input_image') parts.push({ content, index }); });
  }
  for (const entry of parts.slice(0, Math.max(0, parts.length - IMAGE_BUDGET))) entry.content[entry.index] = { type: 'input_text', text: IMAGE_MARKER };
}
