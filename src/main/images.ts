import { randomUUID } from 'node:crypto';
import sharp from 'sharp';
import { WorkbenchError, type Origin } from '../shared/workbench';
import { DEFAULT_IMAGE_MODEL, imageModel, type ImageBackground, type ImageFormat, type ImageProviderId } from '../shared/image-models';
import type { ImageJob, ImageJobInput, Revision } from '../shared/types';
import { imageFacts, MEDIA_TYPES, type MediaStore } from './media';
import type { Store } from './store';
import { redactText, type DiagnosticLog } from './diagnostics';
import { ImageProviderError, type ImageInput, type ImageProvider } from './providers/image-provider';
import { resolveCodexSize, resolveGeminiSize } from './providers/image-size';

const CONCURRENCY = 4;
const RATE_LIMIT_RETRIES = 2, MAX_RETRY_WAIT_SECONDS = 120;
/** Background treatment is chosen by code and appended to the user's prompt, which is otherwise sent verbatim. */
const INSTRUCTIONS = {
  transparent: 'Background: real alpha transparency around the subject, with clean edges. No painted checkerboard, solid backdrop, scenery, floor, or cast shadow unless described above. Keep meaningful translucency.',
  keepTransparent: 'Keep the transparent alpha background: real transparency outside the subject, clean edges, and no added backdrop, checkerboard, or shadow.',
  opaque: 'Background: fully opaque, with no transparency.',
};

export interface ImageArgs {
  prompt: string; model?: string; quality?: string; background?: ImageBackground; format?: ImageFormat; size?: string; count?: number;
  referenceItemIds?: string[]; requestId?: string;
  /** Generate: optional item to receive the results as new versions. Edit: required source item. */
  itemId?: string; revisionId?: string; collection?: string; name?: string;
}

/** Durable image jobs: admission, a bounded scheduler, provider calls, and revision recording with provenance. */
export class ImageJobs {
  private running = new Map<string, AbortController>();
  private tasks = new Set<Promise<void>>();
  private stopped = false;
  constructor(private store: Store, private media: MediaStore, private providers: Record<ImageProviderId, ImageProvider>, private diagnostics: DiagnosticLog) {}

  /** Records requests that were in flight at the last shutdown, then resumes queued jobs. */
  start() { this.store.interruptJobs(); this.pump(); }
  async close() {
    this.stopped = true;
    for (const controller of this.running.values()) controller.abort();
    await Promise.allSettled([...this.tasks]);
  }

  /** Validates a request and queues one job per image. A repeated requestId returns the original jobs. */
  async request(projectId: string, kind: ImageJobInput['kind'], args: ImageArgs, origin: Origin): Promise<ImageJob[]> {
    const model = imageModel(args.model ?? DEFAULT_IMAGE_MODEL);
    if (!model) throw new WorkbenchError('INVALID_ARGUMENTS', 'Unknown image model.');
    const quality = args.quality ?? 'auto';
    if (!model.qualities.includes(quality)) throw new WorkbenchError('INVALID_ARGUMENTS', `${model.label} supports quality ${model.qualities.join(', ')}.`);
    const target = args.itemId ? this.store.item(args.itemId, projectId) : null;
    if (kind === 'edit' && !target) throw new WorkbenchError('INVALID_ARGUMENTS', 'Edits need the item to edit.');
    const revision = (id: string) => {
      const found = target?.revisions.find(entry => entry.id === id);
      if (!found) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
      return found;
    };
    const source = kind === 'edit' ? revision(args.revisionId ?? target!.selectedRevisionId) : null;
    const references = (args.referenceItemIds ?? []).map(id => this.store.item(id, projectId).selectedRevisionId);
    if ((source ? 1 : 0) + references.length > model.maxInputs)
      throw new WorkbenchError('INVALID_ARGUMENTS', `${model.label} takes at most ${model.maxInputs} input images per request${source ? ', including the edit source' : ''}.`);

    const sourceTransparent = !!source && source.hasAlpha && !source.opaque;
    const background = args.background ?? 'auto';
    const alpha = background === 'transparent' || (background === 'auto' && sourceTransparent);
    // Results default to the format of the file they will be exported to.
    const synced: Revision | undefined = target?.revisions.find(entry => entry.id === target.syncedRevisionId);
    const format = args.format ?? synced?.format ?? source?.format ?? 'png';
    if (alpha && !model.transparency) throw new WorkbenchError('INVALID_ARGUMENTS', background === 'auto'
      ? `The source has transparency, which ${model.label} cannot keep. Choose Image 2.5 Flare or Sunburst, or set background to opaque.`
      : `${model.label} has no native transparency. Choose Image 2.5 Flare or Sunburst.`);
    if (alpha && format === 'jpeg') throw new WorkbenchError('INVALID_ARGUMENTS', 'JPEG cannot store transparency. Choose PNG or WebP, or an opaque background.');
    const size = args.size ?? (source ? `${source.width}x${source.height}` : 'auto');
    if (model.provider === 'codex') resolveCodexSize(size); else resolveGeminiSize(size, model.id);

    const input: ImageJobInput = {
      kind, prompt: args.prompt, model: model.id, quality, format, size,
      background: alpha ? 'transparent' : background,
      instructions: alpha ? (sourceTransparent ? INSTRUCTIONS.keepTransparent : INSTRUCTIONS.transparent) : background === 'opaque' ? INSTRUCTIONS.opaque : '',
      sourceRevisionId: source?.id ?? null, referenceRevisionIds: references,
      collection: target ? null : args.collection ?? 'Generated', name: target ? null : args.name ?? promptName(args.prompt),
    };
    const { requestId, ...request } = args;
    const jobs = this.store.createJobs(projectId, requestId ?? randomUUID(), JSON.stringify({ kind, ...request }), target?.id ?? null,
      Array.from({ length: args.count ?? 1 }, () => input), origin);
    this.pump();
    return jobs;
  }

  private pump() {
    while (!this.stopped && this.running.size < CONCURRENCY) {
      const job = this.store.claimJob();
      if (!job) return;
      const controller = new AbortController();
      this.running.set(job.id, controller);
      const task = this.run(job, controller.signal).finally(() => { this.running.delete(job.id); this.tasks.delete(task); this.pump(); });
      this.tasks.add(task);
    }
  }

  private async run(job: ImageJob, signal: AbortSignal) {
    const { input } = job;
    const model = imageModel(input.model)!;
    let stored: { id: string; file: string } | null = null;
    try {
      const images = await Promise.all([input.sourceRevisionId, ...input.referenceRevisionIds].filter((id): id is string => !!id).map(id => this.imageInput(id, job.projectId)));
      const prompt = input.instructions ? `${input.prompt}\n\n${input.instructions}` : input.prompt;
      const size = model.provider === 'codex' ? resolveCodexSize(input.size).size : input.size;
      const result = await this.attempt(() => this.providers[model.provider].run({ prompt, model: model.id, quality: input.quality, size, images, signal }), signal);
      const { providerFile, ...facts } = await this.ingest(result.data, input.format);
      stored = facts;
      const revision = {
        ...facts, origin: input.kind === 'edit' ? 'edited' as const : 'generated' as const, parentId: input.sourceRevisionId,
        provenance: {
          jobId: job.id, batchId: job.batchId, provider: model.provider, model: model.id, quality: input.quality, background: input.background, size: input.size,
          requestedRaster: model.provider === 'codex' ? size : resolveGeminiSize(input.size, model.id).size,
          prompt: input.prompt, instructions: input.instructions, sourceRevisionId: input.sourceRevisionId, referenceRevisionIds: input.referenceRevisionIds,
          requestId: result.requestId ?? null, responseId: result.responseId ?? null, usage: result.usage ?? null,
          ...(providerFile ? { providerFile, convertedTo: input.format } : {}),
        },
      };
      let itemId = job.itemId;
      if (itemId) this.store.addRevision(job.projectId, itemId, revision, false, job.createdBy);
      else if (input.collection !== null) itemId = this.store.createItem(job.projectId, { sourceId: null, sourcePath: null, name: input.name!, collection: input.collection, revision }, job.createdBy);
      else throw new WorkbenchError('NOT_FOUND', 'The item was deleted before its result arrived; the result was discarded.');
      stored = null;
      const warning = input.background === 'transparent' && facts.opaque ? 'Transparency was requested, but the result is fully opaque.' : undefined;
      this.store.finishJob(job.id, { status: 'completed', itemId, revisionId: facts.id, ...(warning ? { warning } : {}) });
    } catch (error) {
      if (stored) await this.media.remove([stored.file]);
      const uncertain = error instanceof ImageProviderError && !error.responded;
      const message = redactText(error instanceof Error ? error.message : 'The image job failed.');
      this.store.finishJob(job.id, { status: uncertain ? 'uncertain' : 'failed', error: message });
      this.diagnostics.record('image.job_failed', { jobId: job.id, model: model.id, uncertain, ...(error instanceof ImageProviderError ? { kind: error.kind, ...error.details } : { message }) });
    }
  }

  /** Retries only rate limits that name a short wait; the provider answered, so nothing was rendered or billed. */
  private async attempt<T>(call: () => Promise<T>, signal: AbortSignal): Promise<T> {
    for (let retries = 0; ; retries++) {
      try { return await call(); } catch (error) {
        const wait = error instanceof ImageProviderError && error.responded && error.details.status === 429 ? error.details.retryAfterSeconds : undefined;
        if (!wait || wait > MAX_RETRY_WAIT_SECONDS || retries >= RATE_LIMIT_RETRIES || signal.aborted) throw error;
        await new Promise(resolve => { const timer = setTimeout(resolve, wait * 1000); signal.addEventListener('abort', () => { clearTimeout(timer); resolve(undefined); }, { once: true }); });
      }
    }
  }

  private async imageInput(revisionId: string, projectId: string): Promise<ImageInput> {
    const revision = this.store.revisionFile(revisionId, projectId);
    if (!revision) throw new WorkbenchError('NOT_FOUND', 'An input revision was deleted before the request was sent.');
    return { data: await this.media.read(revision.file), contentType: MEDIA_TYPES[revision.format] };
  }

  /** Keeps provider bytes as the revision when the format matches; otherwise converts without resizing and keeps the original beside it. */
  private async ingest(data: Buffer, format: ImageFormat) {
    const facts = await imageFacts(data);
    if (facts.format === format) return this.media.ingest(data);
    const converted = await sharp(data).toFormat(format, format === 'webp' ? { lossless: true } : { quality: 92 }).toBuffer();
    return this.media.ingest(converted, { data, format: facts.format });
  }
}

/** A short item name from the prompt's opening words. */
function promptName(prompt: string) {
  const words = prompt.replace(/[^\p{L}\p{N}\s'-]+/gu, ' ').trim().split(/\s+/).slice(0, 6).join(' ');
  return (words || 'Generated image').slice(0, 60);
}
