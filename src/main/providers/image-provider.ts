import type { ImageProviderId } from '../../shared/image-models';

export interface ImageInput { data: Buffer; contentType: string }
/** One image per request. Edits put the source first in `images`. */
export interface ImageRequest { prompt: string; model: string; quality: string; size: string; images: ImageInput[]; signal: AbortSignal }
/** Exact provider bytes plus what the provider reported about the request. */
export interface ProviderImage { data: Buffer; requestId?: string; responseId?: string; usage?: Record<string, unknown> }
export interface ImageProvider { readonly id: ImageProviderId; run(request: ImageRequest): Promise<ProviderImage> }

/**
 * `responded` means the provider answered with an error, so nothing was rendered.
 * Other failures (transport, timeout) leave billing uncertain and are never retried.
 */
export class ImageProviderError extends Error {
  constructor(readonly kind: string, message: string, readonly responded: boolean, readonly details: { status?: number; requestId?: string; retryAfterSeconds?: number } = {}) {
    super(message);
    this.name = 'ImageProviderError';
  }
}

/** Runs a provider fetch with a timeout joined to the job's abort signal; transport failures become uncertain errors. */
export async function providerFetch(provider: string, url: string, init: RequestInit, signal: AbortSignal, timeoutSeconds: number): Promise<Response> {
  const timeout = AbortSignal.timeout(timeoutSeconds * 1000);
  try {
    return await fetch(url, { ...init, signal: AbortSignal.any([signal, timeout]) });
  } catch {
    if (signal.aborted) throw new ImageProviderError('cancelled', `${provider} request was stopped before a response arrived; it may still have been billed.`, false);
    if (timeout.aborted) throw new ImageProviderError('timeout', `${provider} did not answer within ${timeoutSeconds} seconds; the request may still have been billed.`, false);
    throw new ImageProviderError('transport', `${provider} request failed in transit; it may still have been billed.`, false);
  }
}
