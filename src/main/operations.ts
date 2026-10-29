import { z } from 'zod';
import { WorkbenchError, type Origin, type OperationResponse, type UiCommand } from '../shared/workbench';
import { redactText } from './diagnostics';
import type { NoteInput, Store } from './store';
import type { Library } from './library';
import type { MediaStore } from './media';

export const idSchema = z.string().uuid();
const nameSchema = z.string().trim().min(1).max(60);
const titleSchema = z.string().trim().min(1).max(70);
const project = { projectId: idSchema };
const session = { ...project, sessionId: idSchema };
const source = { ...project, sourceId: idSchema };
const item = { ...project, itemId: idSchema };
const review = { ...project, reviewId: idSchema };
const note = { ...project, noteId: idSchema };
const globs = z.array(z.string().trim().min(1).max(200)).max(50);
const tagsSchema = z.array(z.string().trim().min(1).max(40)).max(30);
const obj = <T extends z.ZodRawShape>(shape: T) => z.object(shape).strict();
const itemFilter = { query: z.string().trim().max(200).optional(), collection: z.string().max(500).optional(), sourceId: idSchema.optional(), status: z.enum(['all', 'pending_export', 'missing', 'open_notes']).optional() };
const itemIdsSchema = z.array(idSchema).min(1).max(5000);
const decisionSchema = z.enum(['unreviewed', 'accepted', 'needs_changes', 'rejected']);
const expectedRevision = z.number().int().positive();
const pinSchema = obj({ x: z.number().finite(), y: z.number().finite(), width: z.number().positive().optional(), height: z.number().positive().optional() })
  .describe('Image pixels of the noted revision. A point, or a box when width and height are both set. Clamped to the image.');
const reviewTitle = z.string().trim().min(1).max(120);
const instructionsSchema = z.string().max(8000);
const noteBody = z.string().trim().min(1).max(4000);

/** One registry serves the UI, the in-app assistant, the CLI and MCP. */
export const operationDefinitions = {
  status: { schema: obj({}), description: 'App readiness and provider status, without secrets.' },
  projects_list: { schema: obj({}), description: 'List projects.' },
  project_create: { schema: obj({ name: nameSchema }), description: 'Create a project without changing the visible project.' },
  project_get: { schema: obj(project), description: 'Inspect a project.' },
  project_rename: { schema: obj({ ...project, name: nameSchema }), description: 'Rename a project.' },
  project_delete: { schema: obj(project), description: 'Permanently delete a project with its workspace records and stored image copies. Files in source folders are not touched. Requires explicit user intent.' },
  sessions_list: { schema: obj(project), description: 'List conversations in a project.' },
  session_create: { schema: obj({ ...project, title: titleSchema }), description: 'Create and label a conversation in a project.' },
  session_get: { schema: obj(session), description: 'Inspect a conversation.' },
  rename_session: { schema: obj({ ...session, title: titleSchema }), description: 'Keep a conversation label specific to the current work.' },
  session_delete: { schema: obj(session), description: 'Permanently delete a conversation. Project art, reviews and notes remain. Requires explicit user intent.' },
  sources_list: { schema: obj(project), description: 'List linked source folders with item counts, scan progress, and the last scan report.' },
  source_add: { schema: obj({ ...project, root: z.string().min(1).max(1000), include: globs.optional(), exclude: globs.optional() }), description: 'Link an absolute folder and start importing its PNG, WebP, and JPEG files. Globs match forward-slash paths relative to the root, e.g. exclude ["concepts/**"]. Dot-folders are skipped. Returns immediately; use source_get with wait to follow the scan.' },
  source_get: { schema: obj({ ...source, wait: z.number().int().min(0).max(120).optional() }), description: 'Inspect a source; with wait (seconds), first wait for a running scan to finish.' },
  source_rescan: { schema: obj(source), description: 'Rescan a source: new files are added, changed files become new synced revisions, absent files are marked missing. Reviews and notes stay attached.' },
  source_remove: { schema: obj(source), description: 'Remove a source and its items, revisions, and stored copies from the project. Files in the source folder are not touched. Requires explicit user intent.' },
  import_files: { schema: obj({ ...project, paths: z.array(z.string().min(1).max(1000)).min(1).max(200), collection: z.string().trim().max(200).optional() }), description: 'Import loose image files (absolute paths) as unlinked items. Unlinked items cannot be exported back to a source.' },
  search_items: { schema: obj({ ...project, ...itemFilter, limit: z.number().int().min(1).max(2000).optional(), offset: z.number().int().min(0).optional() }), description: 'Find items by name, path, description, or tag; filter by collection (source folder), source, or status (pending_export, missing, open_notes). Returns totals and collection counts.' },
  item_get: { schema: obj(item), description: 'Inspect an item with every revision, its provenance, its linked source file, and its note threads.' },
  item_update: { schema: obj({ ...item, name: z.string().trim().min(1).max(120).optional(), description: z.string().max(4000).optional(), tags: tagsSchema.optional(), selectedRevisionId: idSchema.optional() }), description: 'Edit item metadata or choose which revision is selected. Selecting a revision other than the synced one makes the item pending export.' },
  item_delete: { schema: obj(item), description: 'Delete an item with its revisions, review entries, and notes. A linked item reappears on the next rescan while its file exists. Files in source folders are not touched. Requires explicit user intent.' },
  review_list: { schema: obj({ ...project, status: z.enum(['active', 'completed', 'all']).default('active') }), description: 'List reviews with decision counts and open notes. Defaults to active; completed finds archived reviews.' },
  review_create: { schema: obj({ ...project, title: reviewTitle, instructions: instructionsSchema.optional(), itemIds: itemIdsSchema.optional(), filter: obj(itemFilter).optional(), open: z.boolean().default(true) }), description: 'Create a review from ordered itemIds or from a search_items filter (up to 5000 items). Opens it in the PixelChat window unless open is false.' },
  review_get: { schema: obj({ ...review, decision: decisionSchema.optional(), includeResolved: z.boolean().default(false) }), description: 'Read a review: entries with decisions (stale when the item changed after the decision) and the note threads on its items, including pins. Resolved threads are omitted unless includeResolved.' },
  review_update: { schema: obj({ ...review, expectedRevision, title: reviewTitle.optional(), instructions: instructionsSchema.optional(), itemIds: itemIdsSchema.optional(), addItemIds: itemIdsSchema.optional() }), description: 'Change title, instructions, or entries only if the review revision still matches. itemIds replaces the ordered list (retained items keep decisions); addItemIds appends.' },
  review_decide: { schema: obj({ ...review, decisions: z.array(obj({ itemId: idSchema, decision: decisionSchema })).min(1).max(5000) }), description: 'Record accept, needs_changes, reject, or unreviewed for review entries. Decisions apply to each item\'s currently selected revision. Decisions are the user\'s call; agents record them only when asked.' },
  review_complete: { schema: obj({ ...review, expectedRevision }), description: 'Mark a review completed. Locks decisions, entries, and new notes until review_reopen.' },
  review_reopen: { schema: obj({ ...review, expectedRevision }), description: 'Restore a completed review to active. Completion stays in review_history.' },
  review_delete: { schema: obj(review), description: 'Delete a review and its decisions. Notes stay on their items. Requires explicit user intent.' },
  review_history: { schema: obj({ ...review, afterSeq: z.number().int().min(0).default(0), limit: z.number().int().min(1).max(500).default(200) }), description: 'Attributed activity for a review in order: creation, edits, decisions, notes, completion. Page with afterSeq.' },
  ui_open: { schema: obj({ ...project, reviewId: idSchema.optional(), itemId: idSchema.optional() }), description: 'Show a review (optionally focused on one of its items) or a single item in the PixelChat window.' },
  note_add: { schema: obj({ ...item, body: noteBody, reviewId: idSchema.optional(), revisionId: idSchema.optional(), parentId: idSchema.optional(), pin: pinSchema.optional() }), description: 'Add a note on an item, optionally in a review and pinned to a region of a revision (default: the selected revision). parentId replies to a thread; replies inherit its review and revision and cannot be pinned.' },
  note_update: { schema: obj({ ...note, body: noteBody.optional(), pin: pinSchema.nullable().optional(), resolved: z.boolean().optional() }), description: 'Edit a note body or pin (agents: own notes only), or resolve/reopen a root thread. Resolve a thread after its change is made, and reply to say what changed.' },
  note_delete: { schema: obj(note), description: 'Delete a note with its replies. Agents may delete only their own notes.' },
};
export type OperationName = keyof typeof operationDefinitions;
export interface UiPort { send(command: Omit<UiCommand, 'id'>): Promise<unknown> }
export function operationCatalog() {
  return Object.entries(operationDefinitions).map(([name, value]) => ({ name, description: value.description, inputSchema: z.toJSONSchema(value.schema) }));
}

export class Operations {
  private pending = new Set<Promise<unknown>>();
  private stopped = false;
  constructor(private store: Store, private library: Library, private media: MediaStore, private ui: UiPort) {}
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
      case 'status': return { version: 1, ready: true };
      case 'projects_list': return store.projects();
      case 'project_create': return store.createProject(a.name);
      case 'project_get': return store.project(a.projectId);
      case 'project_rename': return store.renameProject(a.projectId, a.name);
      case 'project_delete': { const files = store.projectRevisionFiles(a.projectId); store.deleteProject(a.projectId); await this.media.remove(files); return { deleted: true }; }
      case 'sessions_list': return store.sessions(a.projectId);
      case 'session_create': return store.createSession(a.projectId, a.title, titleSource);
      case 'session_get': return store.session(a.sessionId, a.projectId);
      case 'rename_session': return store.renameSession(a.sessionId, a.title, titleSource);
      case 'session_delete': store.deleteSession(a.sessionId); return { deleted: true };
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
      case 'review_get': return store.review(a.reviewId, a.projectId, { decision: a.decision, includeResolved: a.includeResolved });
      case 'review_update': { const { projectId, reviewId, expectedRevision, ...patch } = a; return store.updateReview(reviewId, projectId, expectedRevision, patch, origin); }
      case 'review_decide': return store.decide(a.reviewId, a.projectId, a.decisions, origin);
      case 'review_complete': return store.setReviewStatus(a.reviewId, a.projectId, a.expectedRevision, 'completed', origin);
      case 'review_reopen': return store.setReviewStatus(a.reviewId, a.projectId, a.expectedRevision, 'active', origin);
      case 'review_delete': store.deleteReview(a.reviewId, a.projectId, origin); return { deleted: true };
      case 'review_history': store.reviewSummary(a.reviewId, a.projectId); return store.activity(a.projectId, { reviewId: a.reviewId, afterSeq: a.afterSeq, limit: a.limit });
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
