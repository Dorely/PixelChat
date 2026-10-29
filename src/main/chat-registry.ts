import { randomUUID } from 'node:crypto';
import type { Store } from './store';
import { secretName, type SecureStore } from './providers/secrets';
import type { ActiveChatSelection, ChatModelEntry, ChatModelInput, ChatModelOption, ChatProviderInput } from '../shared/types';
import { CHAT_MODELS, DEFAULT_EFFORT, DEFAULT_MODEL, isDirectGemini } from '../shared/chat-models';
import { effortsKey, isProviderKind, normalizeBaseUrl, normalizeEfforts, normalizeSlug, parseModels, parseProviders, validateProviderFields, type ChatModelRecord, type ChatProvider } from './providers/generic/registry';
import { runCapabilityProbe, type CapabilityOutcome } from './providers/generic/capabilities';
import { AnthropicClient } from './providers/generic/anthropic';
import { ChatCompletionsClient } from './providers/generic/chat';
import { GenericResponsesClient } from './providers/generic/responses';

export interface ResolvedChatTarget {
  selectionId: string; modelLabel: string; providerLabel: string; slug: string; effort: string;
  kind: 'codex' | 'chat-completions' | 'responses' | 'anthropic';
  provider?: ChatProvider; record?: ChatModelRecord;
}
interface PendingTest { outcome: CapabilityOutcome; keyPresent: boolean; at: number }

const TEST_TTL_MS = 10 * 60 * 1000;
const PROVIDERS_KEY = 'chat-providers', MODELS_KEY = 'chat-models', ACTIVE_KEY = 'chat-active';
const DEFAULT_SELECTION: ActiveChatSelection = { modelId: `codex:${DEFAULT_MODEL}`, effort: DEFAULT_EFFORT };

/**
 * Chat model choice: built-in Codex models through the account sign-in, plus custom endpoints whose
 * models are saved only after a passing capability test. Endpoint keys live in secure storage; a
 * direct Gemini endpoint uses the Gemini key saved for images.
 */
export class ChatRegistry {
  private pending = new Map<string, PendingTest>();
  constructor(private readonly store: Store, private readonly secrets: SecureStore, private readonly geminiKey: () => Promise<string | null>, private readonly now: () => number = Date.now) {}

  providers(): ChatProvider[] { return parseProviders(this.store.setting(PROVIDERS_KEY, '[]')); }
  models(): ChatModelRecord[] { return parseModels(this.store.setting(MODELS_KEY, '[]')); }
  private writeProviders(providers: ChatProvider[]) { this.store.setSetting(PROVIDERS_KEY, JSON.stringify(providers)); }
  private writeModels(models: ChatModelRecord[]) { this.store.setSetting(MODELS_KEY, JSON.stringify(models)); }

  activeSelection(): ActiveChatSelection {
    try {
      const parsed = JSON.parse(this.store.setting(ACTIVE_KEY, '')) as Partial<ActiveChatSelection>;
      if (typeof parsed.modelId === 'string' && parsed.modelId) return { modelId: parsed.modelId, effort: typeof parsed.effort === 'string' ? parsed.effort : '' };
    } catch { /* unset or unreadable: use the default */ }
    return DEFAULT_SELECTION;
  }

  resolveActive(): ResolvedChatTarget {
    const selection = this.activeSelection();
    const builtIn = CHAT_MODELS.find(entry => `codex:${entry.id}` === selection.modelId) ?? (this.models().some(entry => entry.id === selection.modelId) ? undefined : CHAT_MODELS.find(entry => entry.id === DEFAULT_MODEL)!);
    if (builtIn) {
      const effort = (builtIn.efforts as readonly string[]).includes(selection.effort) ? selection.effort : DEFAULT_EFFORT;
      return { selectionId: `codex:${builtIn.id}`, modelLabel: builtIn.label, providerLabel: 'Codex', slug: builtIn.id, effort, kind: 'codex' };
    }
    const record = this.models().find(entry => entry.id === selection.modelId)!;
    const provider = this.providers().find(entry => entry.id === record.providerId);
    if (!provider) throw new Error('The chat provider for the selected model is missing. Select another model.');
    const effort = record.efforts.includes(selection.effort) ? selection.effort : record.efforts[0] ?? '';
    return { selectionId: record.id, modelLabel: `${provider.label}:${record.slug}`, providerLabel: provider.label, slug: record.slug, effort, kind: provider.kind, provider, record };
  }

  options(): ChatModelOption[] {
    const providers = this.providers();
    return [
      ...CHAT_MODELS.map(entry => ({ id: `codex:${entry.id}`, label: entry.label, providerLabel: 'Codex', slug: entry.id, efforts: [...entry.efforts], builtIn: true })),
      ...this.models().flatMap(model => {
        const provider = providers.find(entry => entry.id === model.providerId);
        return provider ? [{ id: model.id, label: `${provider.label}:${model.slug}`, providerLabel: provider.label, slug: model.slug, efforts: model.efforts, builtIn: false }] : [];
      }),
    ];
  }

  async saveProvider(input: ChatProviderInput): Promise<ChatProvider> {
    const fields = validateProviderFields(input.label, input.baseUrl, input.kind);
    if (isDirectGemini(fields.baseUrl) && input.apiKey?.trim()) throw new Error('Direct Gemini endpoints use the Gemini key saved in Settings. Save this endpoint without a separate key.');
    const providers = this.providers();
    const provider: ChatProvider = { id: input.id?.trim() || randomUUID(), ...fields };
    const existing = providers.findIndex(entry => entry.id === provider.id);
    if (existing >= 0) providers[existing] = provider; else providers.push(provider);
    if (!isDirectGemini(provider.baseUrl) && input.apiKey?.trim()) await this.secrets.set(secretName('chat-key', provider.id), input.apiKey.trim());
    else await this.secrets.delete(secretName('chat-key', provider.id)).catch(() => undefined);
    this.writeProviders(providers);
    return provider;
  }

  async deleteProvider(id: string): Promise<void> {
    this.writeProviders(this.providers().filter(entry => entry.id !== id));
    this.writeModels(this.models().filter(entry => entry.providerId !== id));
    await this.secrets.delete(secretName('chat-key', id)).catch(() => undefined);
    if (!this.options().some(entry => entry.id === this.activeSelection().modelId)) this.store.setSetting(ACTIVE_KEY, JSON.stringify(DEFAULT_SELECTION));
  }

  async providerKey(providerId: string): Promise<string | null> {
    const provider = this.providers().find(entry => entry.id === providerId);
    if (provider && isDirectGemini(provider.baseUrl)) return this.geminiKey();
    try { return await this.secrets.get(secretName('chat-key', providerId)); } catch { return null; }
  }

  async listModels(providerId: string): Promise<string[]> {
    const provider = this.provider(providerId);
    const options = { baseUrl: normalizeBaseUrl(provider.baseUrl), apiKey: await this.providerKey(providerId) };
    if (provider.kind === 'anthropic') return new AnthropicClient(options).listModels();
    if (provider.kind === 'responses') return new GenericResponsesClient(options).listModels();
    return new ChatCompletionsClient(options).listModels();
  }

  /** Probes every effort and image input. The outcome is held for ten minutes so saveModel can require it. */
  async testModel(input: ChatModelInput): Promise<CapabilityOutcome> {
    const provider = this.provider(input.providerId);
    if (!isProviderKind(provider.kind)) throw new Error('Chat provider type is invalid.');
    const slug = normalizeSlug(input.slug), efforts = normalizeEfforts(input.efforts);
    const key = await this.providerKey(provider.id);
    const outcome = await runCapabilityProbe({ provider: { ...provider, baseUrl: normalizeBaseUrl(provider.baseUrl) }, slug, efforts, apiKey: key });
    this.pending.set(this.pendingKey(provider, slug, efforts), { outcome, keyPresent: !!key?.trim(), at: this.now() });
    return outcome;
  }

  async saveModel(input: ChatModelInput): Promise<ChatModelEntry> {
    const provider = this.provider(input.providerId);
    const slug = normalizeSlug(input.slug), efforts = normalizeEfforts(input.efforts);
    const key = await this.providerKey(provider.id);
    const pending = this.pending.get(this.pendingKey(provider, slug, efforts));
    if (!pending || this.now() - pending.at > TEST_TTL_MS || pending.keyPresent !== !!key?.trim() || pending.outcome.failedEfforts.length) {
      throw new Error('Test this provider, model, and every effort before saving. A passing test is required.');
    }
    const record: ChatModelRecord = {
      id: input.id?.trim() || randomUUID(), providerId: provider.id, slug, efforts, maxTokens: pending.outcome.maxTokens, vision: pending.outcome.vision,
      effortResults: pending.outcome.perEffort, lastTestedAt: new Date(this.now()).toISOString(), ...(pending.outcome.error ? { testError: pending.outcome.error } : {}),
    };
    const models = this.models();
    const existing = models.findIndex(entry => entry.id === record.id);
    if (existing >= 0) models[existing] = record; else models.push(record);
    this.writeModels(models);
    return record;
  }

  async deleteModel(id: string): Promise<void> {
    this.writeModels(this.models().filter(entry => entry.id !== id));
    if (this.activeSelection().modelId === id) this.store.setSetting(ACTIVE_KEY, JSON.stringify(DEFAULT_SELECTION));
  }

  setActive(selection: ActiveChatSelection): ActiveChatSelection {
    const option = this.options().find(entry => entry.id === selection.modelId);
    if (!option) throw new Error('Chat model is not available. Add and test it in Settings first.');
    const effort = option.efforts.length ? (option.efforts.includes(selection.effort.trim()) ? selection.effort.trim() : option.efforts[0]!) : '';
    const resolved = { modelId: option.id, effort };
    this.store.setSetting(ACTIVE_KEY, JSON.stringify(resolved));
    return resolved;
  }

  private provider(id: string): ChatProvider {
    const provider = this.providers().find(entry => entry.id === id);
    if (!provider) throw new Error('Chat provider not found.');
    return provider;
  }
  private pendingKey(provider: ChatProvider, slug: string, efforts: string[]): string {
    return `${provider.id}|${normalizeBaseUrl(provider.baseUrl)}|${provider.kind}|${slug}|${effortsKey(efforts)}`;
  }
}
