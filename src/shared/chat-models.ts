// Built-in chat models served through the Codex account sign-in, with the
// reasoning.effort values the Codex Responses backend accepts. Context limits
// are the account defaults, not API maxima; the composer meter is advisory.
// Custom endpoint models carry no known context limit.
export const DEFAULT_MODEL = 'gpt-5.6-sol';
export const DEFAULT_EFFORT = 'medium';
export const CODEX_EFFORTS = ['low', 'medium', 'high', 'xhigh', 'max'] as const;
export const CHAT_MODELS = [
  { id: 'gpt-5.6-luna', label: 'GPT-5.6 Luna', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
  { id: 'gpt-5.6-sol', label: 'GPT-5.6 Sol', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
  { id: 'gpt-6-astra', label: 'GPT-6 Astra', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
  { id: 'gpt-6-luna', label: 'GPT-6 Luna', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
  { id: 'gpt-6-sol', label: 'GPT-6 Sol', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
  { id: 'gpt-6.1-sol', label: 'GPT-6.1 Sol', contextLimit: 272_000, efforts: [...CODEX_EFFORTS] },
] as const;

/** A direct Gemini endpoint reuses the Gemini image key instead of a separate chat key. */
export function isDirectGemini(baseUrl: string): boolean {
  try { const url = new URL(baseUrl); return url.protocol === 'https:' && url.hostname === 'generativelanguage.googleapis.com' && !url.port; } catch { return false; }
}
