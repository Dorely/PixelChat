import { WorkbenchError } from '../../shared/workbench';

/** A provider-valid raster: Codex sends `size`; Gemini sends `aspectRatio` and `imageSize`. */
export interface ImageSize { width: number; height: number; size: string; aspectRatio?: string; imageSize?: string }
const invalid = (message: string) => new WorkbenchError('INVALID_ARGUMENTS', message);
const PRESETS: Record<string, string> = { square: '1:1', portrait: '2:3', landscape: '3:2', wide: '16:9', widescreen: '16:9' };
const PIXELS = /^(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)$/;
const RATIO = /^(\d+(?:\.\d+)?)\s*[:/]\s*(\d+(?:\.\d+)?)$/;

// Codex images: multiples of 16, 0.66–8.3 MP, edges up to 3840, aspect 1:3 to 3:1 (ImageGenCli size policy).
const STEP = 16, MIN_PIXELS = 655_360, MAX_PIXELS = 8_294_400, MAX_EDGE = 3840, TARGET_PIXELS = 1_572_864, MAX_ASPECT = 3, ASPECT_TOLERANCE = 0.001;

function codexCandidates(aspect: number) {
  const candidates: { width: number; height: number; aspectError: number }[] = [];
  for (let width = STEP; width <= MAX_EDGE; width += STEP) {
    const nearest = Math.round(width / aspect / STEP) * STEP;
    for (const height of [nearest - STEP, nearest, nearest + STEP]) {
      if (height <= 0 || height > MAX_EDGE || width * height < MIN_PIXELS || width * height > MAX_PIXELS) continue;
      const actual = width / height;
      if (actual < 1 / MAX_ASPECT || actual > MAX_ASPECT) continue;
      candidates.push({ width, height, aspectError: Math.abs(Math.log(actual / aspect)) });
    }
  }
  return candidates;
}
const codexSize = (width: number, height: number): ImageSize => ({ width, height, size: `${width}x${height}` });

/** Nearest Codex raster. WIDTHxHEIGHT picks the closest valid pixels; aspects and presets aim near 1.57 MP. Aspects beyond 3:1 are clamped. */
export function resolveCodexSize(value: string): ImageSize {
  const normalized = PRESETS[value.trim().toLowerCase()] ?? value.trim().toLowerCase();
  const pixels = PIXELS.exec(normalized), ratio = RATIO.exec(normalized);
  const [w, h] = pixels ? [Number(pixels[1]), Number(pixels[2])] : ratio ? [Number(ratio[1]), Number(ratio[2])] : normalized === 'auto' || !normalized ? [1, 1] : [NaN, NaN];
  if (!(w > 0 && h > 0)) throw invalid(`Invalid size "${value}". Use auto, a preset (${Object.keys(PRESETS).join(', ')}), an aspect such as 16:9, or WIDTHxHEIGHT.`);
  const aspect = Math.min(MAX_ASPECT, Math.max(1 / MAX_ASPECT, w / h));
  const candidates = codexCandidates(aspect);
  const close = candidates.filter(candidate => candidate.aspectError <= ASPECT_TOLERANCE);
  const pool = close.length ? close : candidates;
  const score = pixels
    ? (c: { width: number; height: number }) => (c.width - w) ** 2 + (c.height - h) ** 2
    : (c: { width: number; height: number }) => Math.abs(c.width * c.height - TARGET_PIXELS);
  const best = pool.sort((a, b) => score(a) - score(b) || a.aspectError - b.aspectError || a.width - b.width)[0];
  if (!best) throw invalid('No Codex raster fits that size.');
  return codexSize(best.width, best.height);
}

// Gemini native rasters per documented aspect ratio (ImageGenCli Gemini research, 2026-10-14).
const GEMINI_RASTERS: Record<string, [number, number]> = {
  '1:1': [1024, 1024], '1:4': [512, 2048], '1:8': [384, 3072], '2:3': [848, 1264], '3:2': [1264, 848], '3:4': [896, 1200], '4:1': [2048, 512],
  '4:3': [1200, 896], '4:5': [928, 1152], '5:4': [1152, 928], '8:1': [3072, 384], '9:16': [768, 1376], '16:9': [1376, 768], '21:9': [1584, 672],
};
const PRO_EXCLUDED = new Set(['1:4', '4:1', '1:8', '8:1']);

/** Nearest Gemini aspect and tier. auto leaves both to the model. WIDTHxHEIGHT also picks the closest 1K/2K/4K tier. */
export function resolveGeminiSize(value: string, model: string): ImageSize {
  const normalized = PRESETS[value.trim().toLowerCase()] ?? value.trim().toLowerCase();
  if (normalized === 'auto' || !normalized) return { width: 1024, height: 1024, size: 'auto' };
  const pixels = PIXELS.exec(normalized), ratio = RATIO.exec(normalized);
  const [w, h] = pixels ? [Number(pixels[1]), Number(pixels[2])] : ratio ? [Number(ratio[1]), Number(ratio[2])] : [NaN, NaN];
  if (!(w > 0 && h > 0)) throw invalid(`Invalid size "${value}". Use auto, a preset (${Object.keys(PRESETS).join(', ')}), an aspect such as 16:9, or WIDTHxHEIGHT.`);
  const ratios = Object.keys(GEMINI_RASTERS).filter(key => model !== 'gemini-3-pro-image' || !PRO_EXCLUDED.has(key));
  const distance = (key: string) => { const [x, y] = key.split(':').map(Number); return Math.abs(Math.log(x! / y! / (w / h))); };
  const aspectRatio = ratios.sort((a, b) => distance(a) - distance(b))[0]!;
  const [baseWidth, baseHeight] = GEMINI_RASTERS[aspectRatio]!;
  const scale = pixels ? [1, 2, 4].sort((a, b) => ((baseWidth * a - w) ** 2 + (baseHeight * a - h) ** 2) - ((baseWidth * b - w) ** 2 + (baseHeight * b - h) ** 2))[0]! : 1;
  return { width: baseWidth * scale, height: baseHeight * scale, size: `${baseWidth * scale}x${baseHeight * scale}`, aspectRatio, imageSize: `${scale}K` };
}
