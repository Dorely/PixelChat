import type { CodexAuth } from './auth';
import { ImageProviderError, providerFetch, type ImageProvider, type ImageRequest, type ProviderImage } from './image-provider';

// Codex app image endpoints: direct image generation, metered on the ChatGPT plan's image allowance.
const IMAGES_ENDPOINT = 'https://chatgpt.com/backend-api/codex/images';
const TIMEOUT_SECONDS = 600;

/** Codex images over the shared ChatGPT sign-in. Returns the endpoint's PNG bytes unchanged. */
export class CodexImages implements ImageProvider {
  readonly id = 'codex' as const;
  constructor(private auth: CodexAuth) {}

  async run(request: ImageRequest): Promise<ProviderImage> {
    // Background stays auto so a prompt asking for alpha can produce it; quality auto is omitted.
    const body = JSON.stringify({
      prompt: request.prompt, model: request.model, size: request.size, background: 'auto',
      ...(request.quality !== 'auto' ? { quality: request.quality } : {}),
      ...(request.images.length ? { images: request.images.map(image => ({ image_url: `data:${image.contentType};base64,${image.data.toString('base64')}` })) } : {}),
    });
    // Reference or source images require the edits endpoint.
    const endpoint = request.images.length ? 'edits' : 'generations';
    let token = await this.token(() => this.auth.getAccessToken());
    let response = await this.post(endpoint, token, body, request.signal);
    if (response.status === 401) {
      await response.body?.cancel();
      token = await this.token(() => this.auth.refreshAfterRejection(token));
      response = await this.post(endpoint, token, body, request.signal);
    }
    const requestId = response.headers.get('x-codex-imagegen-request-id') ?? response.headers.get('x-oai-request-id') ?? response.headers.get('x-request-id') ?? undefined;
    if (!response.ok) {
      const text = await response.text().catch(() => '');
      const kind = classify(response.status, text);
      const detail = errorMessage(text);
      const retryAfter = Number(response.headers.get('retry-after'));
      throw new ImageProviderError(kind, `Codex image request returned ${response.status} (${kind})${detail ? `: ${detail}` : '.'}`, true,
        { status: response.status, requestId, ...(retryAfter > 0 ? { retryAfterSeconds: retryAfter } : {}) });
    }
    let payload: Record<string, any>;
    try { payload = await response.json() as Record<string, any>; }
    catch { throw new ImageProviderError('transport', 'Codex image response could not be read; the request may still have been billed.', false, { requestId }); }
    const image = Array.isArray(payload?.data) ? payload.data[0] : null;
    if (typeof image?.b64_json !== 'string' || !image.b64_json) throw new ImageProviderError('no_image', 'Codex returned no image data.', true, { requestId });
    return {
      data: Buffer.from(image.b64_json, 'base64'), requestId,
      responseId: typeof image.generation_id === 'string' ? image.generation_id : undefined,
      // What the backend actually rendered; it may override the requested size and quality.
      usage: { imageGeneration: payload.usage, served: { size: payload.size, quality: payload.quality, background: payload.background }, planLimits: planLimits(response.headers) },
    };
  }

  private async token(read: () => Promise<string>) {
    try { return await read(); } catch (error) { throw new ImageProviderError('auth', error instanceof Error ? error.message : 'Codex is not signed in.', true); }
  }
  private post(endpoint: string, token: string, body: string, signal: AbortSignal) {
    return providerFetch('Codex', `${IMAGES_ENDPOINT}/${endpoint}`, {
      method: 'POST', body,
      headers: { authorization: `Bearer ${token}`, 'chatgpt-account-id': accountId(token), originator: 'pi', 'user-agent': 'PixelChat', accept: 'application/json', 'content-type': 'application/json' },
    }, signal, TIMEOUT_SECONDS);
  }
}

function accountId(token: string): string {
  try {
    const payload = JSON.parse(Buffer.from(token.split('.')[1] ?? '', 'base64url').toString('utf8')) as Record<string, any>;
    const id = payload['https://api.openai.com/auth']?.chatgpt_account_id;
    if (typeof id === 'string' && id) return id;
  } catch { /* Reported below. */ }
  throw new ImageProviderError('auth', 'The Codex sign-in has no ChatGPT account. Sign in again in Settings.', true);
}

function classify(status: number, body: string): string {
  if (body.includes('input-images') && /per min/i.test(body)) return 'input_rate_limit';
  if (/usage limit/i.test(body)) return 'usage_limit';
  if (status === 429 || /rate limit|too many requests/i.test(body)) return 'rate_limit';
  if (status === 401) return 'auth';
  if (status === 403) return 'forbidden';
  if (/invalid_value/i.test(body) && /gpt-image/i.test(body)) return 'model_invalid';
  return 'api_error';
}

/** A readable backend message, bounded so large payloads are not echoed. */
function errorMessage(body: string): string | null {
  try {
    const parsed = JSON.parse(body) as Record<string, any>;
    const message = parsed?.message ?? parsed?.detail ?? parsed?.error?.message;
    return typeof message === 'string' ? message.slice(0, 500) : null;
  } catch { return body.trim() ? body.slice(0, 300) : null; }
}

/** ChatGPT plan usage windows reported on Codex responses; only named numeric fields are read. */
function planLimits(headers: Headers): Record<string, unknown> | undefined {
  const number = (name: string) => { const value = Number(headers.get(`x-codex-${name}`) ?? NaN); return Number.isFinite(value) ? value : undefined; };
  const window = (name: 'primary' | 'secondary') => {
    const usedPercent = number(`${name}-used-percent`), windowMinutes = number(`${name}-window-minutes`);
    return usedPercent === undefined || !windowMinutes ? undefined : { usedPercent, windowMinutes, resetAt: number(`${name}-reset-at`) };
  };
  const limits = { planType: headers.get('x-codex-plan-type') ?? undefined, activeLimit: headers.get('x-codex-active-limit') ?? undefined, primary: window('primary'), secondary: window('secondary') };
  return Object.values(limits).some(value => value !== undefined) ? limits : undefined;
}
