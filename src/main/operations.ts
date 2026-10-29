import { z } from 'zod';
import { WorkbenchError, type Origin, type OperationResponse, type UiCommand } from '../shared/workbench';
import { redactText } from './diagnostics';
import type { Store } from './store';
import type { Library } from './library';
import type { MediaStore } from './media';

export const idSchema = z.string().uuid();
const nameSchema = z.string().trim().min(1).max(60);
const titleSchema = z.string().trim().min(1).max(70);
const project = { projectId: idSchema };
const session = { ...project, sessionId: idSchema };
const source = { ...project, sourceId: idSchema };
const item = { ...project, itemId: idSchema };
const globs = z.array(z.string().trim().min(1).max(200)).max(50);
const tagsSchema = z.array(z.string().trim().min(1).max(40)).max(30);
const obj = <T extends z.ZodRawShape>(shape: T) => z.object(shape).strict();

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
  search_items: { schema: obj({ ...project, query: z.string().trim().max(200).optional(), collection: z.string().max(500).optional(), sourceId: idSchema.optional(), status: z.enum(['all', 'pending_export', 'missing']).optional(), limit: z.number().int().min(1).max(2000).optional(), offset: z.number().int().min(0).optional() }), description: 'Find items by name, path, description, or tag; filter by collection (source folder), source, or status. Returns totals and collection counts.' },
  item_get: { schema: obj(item), description: 'Inspect an item with every revision, its provenance, and its linked source file.' },
  item_update: { schema: obj({ ...item, name: z.string().trim().min(1).max(120).optional(), description: z.string().max(4000).optional(), tags: tagsSchema.optional(), selectedRevisionId: idSchema.optional() }), description: 'Edit item metadata or choose which revision is selected. Selecting a revision other than the synced one makes the item pending export.' },
  item_delete: { schema: obj(item), description: 'Delete an item with its revisions, review entries, and notes. A linked item reappears on the next rescan while its file exists. Files in source folders are not touched. Requires explicit user intent.' },
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
