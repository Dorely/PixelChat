import { WorkbenchError, type Origin, type OperationResponse, type UiCommand } from '../shared/workbench';
import { redactText } from './diagnostics';
import type { NoteInput, Store } from './store';
import type { Library } from './library';
import type { MediaStore } from './media';
import type { ImageArgs, ImageJobs } from './images';
import { DEFAULT_IMAGE_MODEL, IMAGE_MODELS } from '../shared/image-models';
import { operationDefinitions, type OperationName } from '../shared/operation-catalog';
import type { ProviderStatus } from '../shared/types';
import { guidanceDocuments, guidanceHash, selectGuidance } from '../shared/guidance';
import { viewImage } from './view-image';

export interface UiPort { send(command: Omit<UiCommand, 'id'>): Promise<unknown> }

export class Operations {
  private pending = new Set<Promise<unknown>>();
  private stopped = false;
  /** Set by the chat service: a conversation with a running turn cannot be deleted under it. */
  sessionBusy?: (sessionId: string) => boolean;
  constructor(private store: Store, private library: Library, private media: MediaStore, private images: ImageJobs, private ui: UiPort, private providers: () => Promise<ProviderStatus>) {}
  async close() { this.stopped = true; await Promise.allSettled([...this.pending]); }
  call(name: string, raw: unknown, origin: Origin = 'agent'): Promise<unknown> {
    if (this.stopped) return Promise.reject(new WorkbenchError('CLOSING', 'PixelChat is closing.'));
    if (!Object.hasOwn(operationDefinitions, name)) return Promise.reject(new WorkbenchError('UNKNOWN_OPERATION', 'Unknown workbench operation.'));
    const promise = this.execute(name as OperationName, raw, origin);
    this.pending.add(promise); void promise.finally(() => this.pending.delete(promise)).catch(() => {}); return promise;
  }
  private async execute(name: OperationName, raw: unknown, origin: Origin): Promise<unknown> {
    const parsed = operationDefinitions[name].schema.safeParse(raw);
    if (!parsed.success) throw new WorkbenchError('INVALID_ARGUMENTS', parsed.error.issues.map(i => `${i.path.join('.')}: ${i.message}`).join('; '));
    const a = parsed.data as Record<string, any>;
    const store = this.store;
    if (a.projectId) store.project(a.projectId);
    if (a.sessionId) store.session(a.sessionId, a.projectId);
    const library = this.library;
    const titleSource = origin === 'user' ? 'user' : 'assistant';
    switch (name) {
      case 'status': return { version: 1, ready: true, providers: await this.providers(), imageModels: IMAGE_MODELS, defaultImageModel: DEFAULT_IMAGE_MODEL };
      case 'guidance': {
        let documents: Record<string, string>;
        try { documents = selectGuidance(a.topics); } catch (error) { throw new WorkbenchError('INVALID_ARGUMENTS', error instanceof Error ? error.message : 'Unknown guidance topic.'); }
        return { version: 1, hash: guidanceHash(), topics: Object.keys(guidanceDocuments), documents };
      }
      case 'projects_list': return store.projects();
      case 'project_create': return store.createProject(a.name);
      case 'project_get': return store.project(a.projectId);
      case 'project_rename': return store.renameProject(a.projectId, a.name);
      case 'project_delete': {
        if (store.hasActiveJobs(a.projectId)) throw new WorkbenchError('BUSY', 'Image jobs are still running in this project. Delete it after they finish.');
        if (store.sessions(a.projectId).some(entry => this.sessionBusy?.(entry.id))) throw new WorkbenchError('BUSY', 'The assistant is responding in this project. Stop it before deleting.');
        const files = store.projectRevisionFiles(a.projectId); store.deleteProject(a.projectId); await this.media.remove(files); return { deleted: true }; }
      case 'sessions_list': return store.sessions(a.projectId);
      case 'session_create': return a.title ? store.createSession(a.projectId, a.title, titleSource) : store.createSession(a.projectId);
      case 'session_get': return store.session(a.sessionId, a.projectId);
      case 'rename_session': return store.renameSession(a.sessionId, a.title, titleSource);
      case 'session_delete':
        if (this.sessionBusy?.(a.sessionId)) throw new WorkbenchError('BUSY', 'The assistant is responding in this conversation. Stop it before deleting.');
        store.deleteSession(a.sessionId); return { deleted: true };
      case 'sources_list': return library.sources(a.projectId);
      case 'source_add': return library.addSource(a.projectId, a.root, a.include ?? [], a.exclude ?? [], origin);
      case 'source_get': return a.wait ? library.waitForScan(a.projectId, a.sourceId, a.wait * 1000) : library.source(a.projectId, a.sourceId);
      case 'source_rescan': return library.rescan(a.projectId, a.sourceId, origin);
      case 'source_remove': await library.removeSource(a.projectId, a.sourceId, origin); return { removed: true };
      case 'import_files': return library.importFiles(a.projectId, a.paths, a.collection ?? 'Imported', origin);
      case 'search_items': return store.searchItems(a.projectId, { query: a.query || undefined, collection: a.collection, sourceId: a.sourceId, status: a.status, limit: a.limit ?? 100, offset: a.offset ?? 0 });
      case 'item_get': return store.item(a.itemId, a.projectId);
      case 'item_delete': await this.media.remove(store.deleteItem(a.itemId, a.projectId, origin)); return { deleted: true };
      case 'item_update': { const { projectId, itemId, ...patch } = a; return store.updateItem(itemId, projectId, patch, origin); }
      case 'review_list': return store.reviews(a.projectId, a.status);
      case 'review_create': {
        if (!a.itemIds === !a.filter) throw new WorkbenchError('INVALID_ARGUMENTS', 'Pass either itemIds or filter.');
        const itemIds: string[] = a.itemIds ?? store.searchItems(a.projectId, { ...a.filter, limit: 5000, offset: 0 }).items.map(entry => entry.id);
        const created = store.createReview(a.projectId, { title: a.title, instructions: a.instructions ?? '', itemIds }, origin);
        if (!a.open) return created;
        // Creating succeeded; a missing window only means it could not be shown.
        try { await this.ui.send({ action: 'open-review', projectId: a.projectId, reviewId: created.id }); return { ...created, opened: true }; }
        catch (error) { return { ...created, opened: false, openError: error instanceof Error ? error.message : 'Could not open the review.' }; }
      }
      case 'review_get': {
        if (a.wait && a.afterRevision !== undefined) await this.waitUntil(() => store.reviewSummary(a.reviewId, a.projectId).revision > a.afterRevision, a.wait * 1000);
        return store.review(a.reviewId, a.projectId, { decision: a.decision, includeResolved: a.includeResolved });
      }
      case 'review_update': { const { projectId, reviewId, expectedRevision, ...patch } = a; return store.updateReview(reviewId, projectId, expectedRevision, patch, origin); }
      case 'review_decide': return store.decide(a.reviewId, a.projectId, a.decisions, origin);
      case 'review_complete': return store.setReviewStatus(a.reviewId, a.projectId, a.expectedRevision, 'completed', origin);
      case 'review_reopen': return store.setReviewStatus(a.reviewId, a.projectId, a.expectedRevision, 'active', origin);
      case 'review_delete': store.deleteReview(a.reviewId, a.projectId, origin); return { deleted: true };
      case 'activity_get': {
        if (a.reviewId) store.reviewSummary(a.reviewId, a.projectId);
        const filter = { reviewId: a.reviewId, itemId: a.itemId, afterSeq: a.afterSeq };
        if (a.wait) await this.waitUntil(() => store.activity(a.projectId, { ...filter, limit: 1 }).length > 0, a.wait * 1000);
        const entries = store.activity(a.projectId, { ...filter, limit: a.limit });
        return { entries, cursor: entries.at(-1)?.seq ?? a.afterSeq };
      }
      case 'generate_images': { const { projectId, ...args } = a; return this.images.request(projectId, 'generate', args as ImageArgs, origin); }
      case 'edit_image': { const { projectId, ...args } = a; return this.images.request(projectId, 'edit', args as ImageArgs, origin); }
      case 'jobs_get': {
        const filter = { batchId: a.batchId, itemId: a.itemId, jobIds: a.jobIds, active: a.active };
        if (a.wait) await this.waitUntil(() => !store.jobs(a.projectId, { ...filter, active: true, limit: 1 }).length, a.wait * 1000);
        return store.jobs(a.projectId, filter);
      }
      case 'job_cancel': return store.cancelJob(a.jobId, a.projectId, origin);
      case 'export_item': return library.exportItem(a.projectId, a.itemId, { destination: a.destination, force: a.force }, origin);
      case 'view_image': return viewImage(store, this.media, a.projectId, a.itemId, { revisionId: a.revisionId, pins: a.pins, background: a.background, maxSize: a.maxSize });
      case 'ui_open': {
        if (a.reviewId) {
          store.reviewSummary(a.reviewId, a.projectId);
          if (a.itemId && !store.review(a.reviewId, a.projectId).entries.some(entry => entry.itemId === a.itemId)) throw new WorkbenchError('INVALID_ARGUMENTS', 'The item is not in that review.');
          return this.ui.send({ action: 'open-review', projectId: a.projectId, reviewId: a.reviewId, itemId: a.itemId });
        }
        if (!a.itemId) throw new WorkbenchError('INVALID_ARGUMENTS', 'Pass reviewId, itemId, or both.');
        store.item(a.itemId, a.projectId);
        return this.ui.send({ action: 'open-item', projectId: a.projectId, itemId: a.itemId });
      }
      case 'note_add': { const { projectId, ...input } = a; return store.addNote(projectId, input as NoteInput, origin); }
      case 'note_update': { const { projectId, noteId, ...patch } = a; return store.updateNote(noteId, projectId, patch, origin); }
      case 'note_delete': store.deleteNote(a.noteId, a.projectId, origin); return { deleted: true };
    }
  }
  protected async waitUntil(done: () => boolean, timeout: number) {
    const until = Date.now() + timeout;
    while (!this.stopped && !done() && Date.now() < until) await new Promise(resolve => setTimeout(resolve, Math.min(250, until - Date.now())));
  }
}

export function operationError(error: unknown): OperationResponse {
  const known = error instanceof WorkbenchError;
  return { ok: false, error: { code: known ? error.code : 'OPERATION_FAILED', message: redactText(error instanceof Error ? error.message : 'Operation failed.'), ...(known && error.details ? { details: error.details } : {}) } };
}
