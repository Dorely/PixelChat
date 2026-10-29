import { Tiktoken } from 'js-tiktoken/lite';
import o200kBase from 'js-tiktoken/ranks/o200k_base';

let tokenizer: Tiktoken | undefined;

// Coarse documented estimate for image parts: base64 payloads must never be
// tokenized as prose. Image ≈ pixels/750, with a flat fallback when a PNG header
// cannot be read. Advisory like the text estimate.
const IMAGE_TOKENS_PER_PIXEL = 750;
const MEDIA_FALLBACK_TOKENS = 1000;

function imageDataTokens(dataUrl: string): number {
  const comma = dataUrl.indexOf(',');
  if (comma < 0 || !/png/i.test(dataUrl.slice(0, comma))) return MEDIA_FALLBACK_TOKENS;
  try {
    const header = Buffer.from(dataUrl.slice(comma + 1, comma + 65), 'base64');
    if (header.length >= 24 && header.toString('ascii', 12, 16) === 'IHDR') {
      const width = header.readUInt32BE(16);
      const height = header.readUInt32BE(20);
      if (width > 0 && height > 0) return Math.max(1, Math.ceil((width * height) / IMAGE_TOKENS_PER_PIXEL));
    }
  } catch { /* fall through to the flat estimate */ }
  return MEDIA_FALLBACK_TOKENS;
}

function extractMedia(item: unknown): { stripped: unknown; tokens: number } | null {
  if (!item || typeof item !== 'object') return null;
  const content = (item as { content?: unknown }).content;
  if (!Array.isArray(content)) return null;
  let tokens = 0;
  let found = false;
  const stripped = content.filter(part => {
    const entry = part as { type?: unknown; image_url?: unknown } | null;
    if (entry?.type === 'input_image') {
      found = true;
      tokens += imageDataTokens(String(entry.image_url ?? ''));
      return false;
    }
    return true;
  });
  if (!found) return null;
  return { stripped: { ...(item as Record<string, unknown>), content: stripped }, tokens };
}

// A textual request estimate: Responses framing and private reasoning aren't
// reproducible locally. Never tokenize encrypted reasoning as if it were prose.
export class ContextTokenCounter {
  private cache = new Map<string, number>();
  private cachedCharacters = 0;

  private textTokens(text: string): number {
    const cached = this.cache.get(text);
    if (cached !== undefined) return cached;
    tokenizer ??= new Tiktoken(o200kBase);
    const count = tokenizer.encode(text, [], []).length;
    // Reuse settled history/tool counts between draft edits and stream updates.
    // Bound retained strings, including for long conversations.
    if (text.length <= 1_000_000) {
      while (this.cache.size && (this.cachedCharacters + text.length > 1_000_000 || this.cache.size >= 512)) {
        const key = this.cache.keys().next().value!;
        this.cache.delete(key); this.cachedCharacters -= key.length;
      }
      this.cache.set(text, count); this.cachedCharacters += text.length;
    }
    return count;
  }

  count(args: { instructions: string; tools: unknown[]; input: unknown[]; draft?: string; streamingText?: string }) {
    let includesHiddenReasoning = false;
    let includesEstimatedMedia = false;
    const serialize = (value: unknown) => JSON.stringify(value, (key, entry: unknown) => {
      if (key === 'encrypted_content') { if (entry) includesHiddenReasoning = true; return undefined; }
      return entry;
    });
    let tokens = this.textTokens(args.instructions) + this.textTokens(serialize(args.tools));
    for (const item of args.input) {
      const media = extractMedia(item);
      if (media) {
        includesEstimatedMedia = true;
        tokens += media.tokens + this.textTokens(serialize(media.stripped));
      } else {
        tokens += this.textTokens(serialize(item));
      }
    }
    if (args.streamingText) tokens += this.textTokens(serialize({ role: 'assistant', content: [{ type: 'output_text', text: args.streamingText }] }));
    if (args.draft?.trim()) tokens += this.textTokens(serialize({ role: 'user', content: [{ type: 'input_text', text: args.draft.trim() }] }));
    return { tokens, includesHiddenReasoning, includesEstimatedMedia };
  }
}
