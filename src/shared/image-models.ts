export type ImageProviderId = 'codex' | 'gemini';
export type ImageBackground = 'auto' | 'opaque' | 'transparent';
export type ImageFormat = 'png' | 'webp' | 'jpeg';

/** A selectable image model. The model decides the provider; requests never name a provider separately. */
export interface ImageModel {
  id: string; provider: ImageProviderId; label: string; qualities: string[];
  /** Native alpha output has been verified for this model. */
  transparency: boolean;
  /** Input images per request, including an edit source. */
  maxInputs: number;
  use: string;
}

const CODEX_QUALITIES = ['auto', 'low', 'medium', 'high'];
export const IMAGE_MODELS: ImageModel[] = [
  { id: 'gpt-image-2.5-flare', provider: 'codex', label: 'Image 2.5 Flare', qualities: [...CODEX_QUALITIES, 'xhigh', 'max'], transparency: true, maxInputs: 4, use: 'Fast generation, iteration, and edits; native alpha.' },
  { id: 'gpt-image-2.5-sunburst', provider: 'codex', label: 'Image 2.5 Sunburst', qualities: [...CODEX_QUALITIES, 'xhigh', 'max'], transparency: true, maxInputs: 4, use: 'Demanding detail, text, or subject preservation when Flare falls short; native alpha.' },
  { id: 'gpt-image-2', provider: 'codex', label: 'Image 2', qualities: CODEX_QUALITIES, transparency: false, maxInputs: 4, use: 'Existing workflows; no native alpha.' },
  { id: 'gemini-nano-banana-2.1', provider: 'gemini', label: 'Nano Banana 2.1', qualities: ['auto'], transparency: false, maxInputs: 5, use: 'Gemini default for generation and edits; no native alpha.' },
  { id: 'gemini-3-pro-image', provider: 'gemini', label: 'Gemini 3 Pro Image', qualities: ['auto'], transparency: false, maxInputs: 5, use: 'Gemini precision and consistency; no native alpha.' },
];
export const DEFAULT_IMAGE_MODEL = 'gpt-image-2.5-flare';
export const PROVIDER_LABELS: Record<ImageProviderId, string> = { codex: 'Codex (ChatGPT account)', gemini: 'Gemini API key' };
/** Size requests: auto, a preset, an aspect such as 16:9, or WIDTHxHEIGHT. Providers return their nearest supported raster. */
export const SIZE_PRESETS = ['auto', 'square', 'portrait', 'landscape', 'wide'] as const;
export const MAX_IMAGE_COUNT = 4;

export const imageModel = (id: string) => IMAGE_MODELS.find(model => model.id === id);
