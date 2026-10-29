import { ImageProviderError, providerFetch, type ImageProvider, type ImageRequest, type ProviderImage } from './image-provider';
import { resolveGeminiSize } from './image-size';

const ENDPOINT = 'https://generativelanguage.googleapis.com/v1beta/interactions';
const TIMEOUT_SECONDS = 180;
const MAX_REQUEST_BYTES = 20_000_000;

/** Gemini images over the Interactions API with one stored API key. Returns the final image's bytes unchanged. */
export class GeminiImages implements ImageProvider {
  readonly id = 'gemini' as const;
  constructor(private key: () => Promise<string | null>) {}

  async run(request: ImageRequest): Promise<ProviderImage> {
    const size = resolveGeminiSize(request.size, request.model);
    // `store: false` keeps no conversation state. `delivery` is omitted: the live endpoint rejects it (2026-10-14).
    const body = JSON.stringify({
      model: request.model, store: false, stream: false,
      input: [{ type: 'text', text: request.prompt }, ...request.images.map(image => ({ type: 'image', mime_type: image.contentType, data: image.data.toString('base64') }))],
      response_format: { type: 'image', ...(size.aspectRatio ? { aspect_ratio: size.aspectRatio } : {}), ...(size.imageSize ? { image_size: size.imageSize } : {}) },
    });
    if (Buffer.byteLength(body) > MAX_REQUEST_BYTES) throw new ImageProviderError('usage', 'The Gemini request exceeds 20 MB of inline images. Use fewer or smaller references.', true);
    const key = await this.key().catch(() => null);
    if (!key) throw new ImageProviderError('auth', 'No Gemini API key is saved. Add one in Settings.', true);
    const response = await providerFetch('Gemini', ENDPOINT, { method: 'POST', headers: { 'content-type': 'application/json', 'x-goog-api-key': key }, body }, request.signal, TIMEOUT_SECONDS);
    if (!response.ok) {
      // Error bodies are not echoed; they can repeat request content.
      const failure = await response.json().catch(() => null) as { error?: { details?: { reason?: string }[] } } | null;
      const invalidKey = failure?.error?.details?.some(detail => detail.reason === 'API_KEY_INVALID' || detail.reason === 'API_KEY_EXPIRED');
      const auth = invalidKey || response.status === 401 || response.status === 403;
      throw new ImageProviderError(auth ? 'auth' : response.status === 429 ? 'rate_limit' : response.status >= 500 ? 'server' : 'api_error',
        auth ? 'Gemini rejected the API key or project access. Check the key in Settings.' : `Gemini returned HTTP ${response.status}. Check model access, request options, and project quota.`,
        true, { status: response.status, ...(Number(response.headers.get('retry-after')) > 0 ? { retryAfterSeconds: Number(response.headers.get('retry-after')) } : {}) });
    }
    let interaction: { id?: string; status?: string; steps?: { type?: string; content?: { type?: string; data?: string; mime_type?: string; thought?: boolean }[] }[] };
    try { interaction = await response.json() as typeof interaction; }
    catch { throw new ImageProviderError('transport', 'Gemini response could not be read; the request may still have been billed.', false); }
    if (interaction?.status !== 'completed') throw new ImageProviderError(interaction?.status === 'failed' ? 'blocked' : 'incomplete', 'Gemini did not complete the image. Check the prompt and model access.', true);
    // The last non-thought image of the final model output is the result.
    const image = (Array.isArray(interaction.steps) ? interaction.steps : [])
      .filter(step => step?.type === 'model_output').flatMap(step => Array.isArray(step.content) ? step.content : [])
      .filter(part => part?.type === 'image' && !part.thought).at(-1);
    if (typeof image?.data !== 'string' || !image.data) throw new ImageProviderError('no_image', 'Gemini returned no final image. Refine the prompt and try again.', true);
    return { data: Buffer.from(image.data, 'base64'), responseId: typeof interaction.id === 'string' ? interaction.id : undefined, usage: { requested: { aspectRatio: size.aspectRatio ?? 'auto', imageSize: size.imageSize ?? 'auto' } } };
  }
}
