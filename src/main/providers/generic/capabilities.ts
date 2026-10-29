import { AnthropicClient } from './anthropic';
import { ChatCompletionsClient, toChatMessages } from './chat';
import { GenericResponsesClient } from './responses';
import type { ChatProvider, ChatModelRecord } from './registry';
import { chatPath } from './registry';
import type { ModelCapabilityResult } from '../../../shared/types';

export interface CapabilityProbe {
  provider: ChatProvider;
  slug: string;
  efforts: string[];
  apiKey?: string | null;
  signal?: AbortSignal;
  fetch?: typeof globalThis.fetch;
}

export type CapabilityOutcome = ModelCapabilityResult;

const PIXEL_PNG_BASE64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';
const MAX_TOKENS_LADDER = [128000, 64000, 32000, 16384, 8192] as const;
const ANTHROPIC_SAFE_FLOOR = 8192;

function probeTools(): unknown[] {
  return [{ type: 'function', name: 'probe', description: 'Connectivity probe.', parameters: { type: 'object', properties: {} } }];
}

function probeInstructions(): string {
  return 'Reply with the single word ok.';
}

function probeInput(): unknown[] {
  return [{ role: 'user', content: [{ type: 'input_text', text: 'Reply with the single word ok.' }] }];
}

function namesMaxTokens(error: unknown): boolean {
  return error instanceof Error && /max[_-]?tokens/i.test(error.message);
}

export async function runCapabilityProbe(probe: CapabilityProbe): Promise<CapabilityOutcome> {
  const secret = probe.apiKey?.trim() ? probe.apiKey.trim() : null;
  const perEffort: Record<string, 'pass' | 'fail'> = {};
  const failedEfforts: string[] = [];
  const targets = probe.efforts.length ? probe.efforts : [''];
  let lastError: string | undefined;
  for (const effort of targets) {
    try {
      await runBasicPing(probe, effort || undefined, secret);
      perEffort[effort || '(default)'] = 'pass';
    } catch (error) {
      perEffort[effort || '(default)'] = 'fail';
      failedEfforts.push(effort || '(default)');
      lastError = error instanceof Error ? error.message : 'Probe failed.';
    }
  }
  if (failedEfforts.length) return { perEffort, failedEfforts, maxTokens: null, vision: 'unknown', error: lastError };
  const maxTokens = probe.provider.kind === 'anthropic' ? await discoverMaxTokens(probe, targets[0] || undefined, secret) : null;
  return { perEffort, failedEfforts, maxTokens, vision: await probeVision(probe, targets[0] || undefined, secret) };
}

async function runBasicPing(probe: CapabilityProbe, effort: string | undefined, secret: string | null): Promise<void> {
  const baseUrl = probe.provider.baseUrl;
  const options = { fetch: probe.fetch, baseUrl, apiKey: secret };
  const input = probeInput();
  if (probe.provider.kind === 'anthropic') {
    const discovered = await discoverMaxTokens(probe, effort, secret);
    await new AnthropicClient(options).streamChat({ model: probe.slug, input, instructions: probeInstructions(), tools: probeTools(), maxTokens: discovered, signal: probe.signal });
    return;
  }
  if (probe.provider.kind === 'responses') {
    await new GenericResponsesClient(options).streamChat({ model: probe.slug, input, instructions: probeInstructions(), tools: probeTools(), effort, signal: probe.signal });
    return;
  }
  await new ChatCompletionsClient(options).streamChat({ model: probe.slug, messages: toChatMessages(input, probeInstructions()), tools: probeTools(), effort, signal: probe.signal });
}

/** Sends one 1x1 PNG; a 400/422 refusal means no image input, anything else inconclusive is unknown. */
async function probeVision(probe: CapabilityProbe, effort: string | undefined, secret: string | null): Promise<true | false | 'unknown'> {
  try {
    const baseUrl = probe.provider.baseUrl;
    const fetcher = probe.fetch ?? globalThis.fetch.bind(globalThis);
    let url: string, headers: Record<string, string>, body: unknown;
    if (probe.provider.kind === 'anthropic') {
      url = chatPath(baseUrl, 'anthropic');
      headers = new AnthropicClient({ fetch: probe.fetch, baseUrl, apiKey: secret }).headers();
      body = {
        model: probe.slug, max_tokens: ANTHROPIC_SAFE_FLOOR, system: probeInstructions(), stream: false,
        messages: [{ role: 'user', content: [{ type: 'text', text: 'Describe this pixel.' }, { type: 'image', source: { type: 'base64', media_type: 'image/png', data: PIXEL_PNG_BASE64 } }] }],
      };
    } else {
      url = `${baseUrl.replace(/\/+$/, '')}${probe.provider.kind === 'responses' ? '/responses' : '/chat/completions'}`;
      headers = { 'content-type': 'application/json', ...(secret ? { authorization: `Bearer ${secret}` } : {}) };
      body = probe.provider.kind === 'responses'
        ? {
          model: probe.slug, instructions: probeInstructions(), stream: false, ...(effort ? { reasoning_effort: effort } : {}),
          input: [{ role: 'user', content: [{ type: 'input_text', text: 'Describe this pixel.' }, { type: 'input_image', image_url: `data:image/png;base64,${PIXEL_PNG_BASE64}` }] }],
        }
        : {
          model: probe.slug, stream: false, ...(effort ? { reasoning_effort: effort } : {}),
          messages: [{ role: 'user', content: [{ type: 'text', text: 'Describe this pixel.' }, { type: 'image_url', image_url: { url: `data:image/png;base64,${PIXEL_PNG_BASE64}` } }] }],
        };
    }
    const response = await fetcher(url, { method: 'POST', headers, body: JSON.stringify(body), signal: probe.signal });
    await response.body?.cancel().catch(() => undefined);
    if (response.ok) return true;
    return response.status === 400 || response.status === 422 ? false : 'unknown';
  } catch {
    return 'unknown';
  }
}

async function discoverMaxTokens(probe: CapabilityProbe, effort: string | undefined, secret: string | null): Promise<number> {
  const client = new AnthropicClient({ fetch: probe.fetch, baseUrl: probe.provider.baseUrl, apiKey: secret });
  for (const value of MAX_TOKENS_LADDER) {
    try {
      await client.streamChat({ model: probe.slug, input: probeInput(), instructions: probeInstructions(), tools: probeTools(), maxTokens: value, signal: probe.signal });
      return value;
    } catch (error) {
      if (!namesMaxTokens(error)) throw error;
    }
  }
  return ANTHROPIC_SAFE_FLOOR;
}

/** Tools whose output is only useful to a model that can see images. */
const VISION_TOOLS = new Set(['view_image']);

export function toolsForModel(model: Pick<ChatModelRecord, 'vision'>, tools: { name?: string }[] | unknown[]): unknown[] {
  return (tools as { name?: string }[]).filter(tool => !(model.vision === false && typeof tool?.name === 'string' && VISION_TOOLS.has(tool.name)));
}

export function capabilityNote(model: Pick<ChatModelRecord, 'vision'>): string {
  return model.vision === false ? 'The active model cannot view images, so view_image is unavailable. Do not claim to have seen any art; rely on metadata, notes, and the user\'s description.' : '';
}
