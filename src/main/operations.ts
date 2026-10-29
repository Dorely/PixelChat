import { z } from 'zod';
import { WorkbenchError, type Origin, type OperationResponse, type UiCommand } from '../shared/workbench';
import { redactText } from './diagnostics';
import type { Store } from './store';

export const idSchema = z.string().uuid();
const nameSchema = z.string().trim().min(1).max(60);
const titleSchema = z.string().trim().min(1).max(70);
const project = { projectId: idSchema };
const session = { ...project, sessionId: idSchema };
const obj = <T extends z.ZodRawShape>(shape: T) => z.object(shape).strict();

/** One registry serves the UI, the in-app assistant, the CLI and MCP. */
export const operationDefinitions = {
  status: { schema: obj({}), description: 'App readiness and provider status, without secrets.' },
  projects_list: { schema: obj({}), description: 'List projects.' },
  project_create: { schema: obj({ name: nameSchema }), description: 'Create a project without changing the visible project.' },
  project_get: { schema: obj(project), description: 'Inspect a project.' },
  project_rename: { schema: obj({ ...project, name: nameSchema }), description: 'Rename a project.' },
  project_delete: { schema: obj(project), description: 'Permanently delete a project and its workspace records. Original image files remain on disk. Requires explicit user intent.' },
  sessions_list: { schema: obj(project), description: 'List conversations in a project.' },
  session_create: { schema: obj({ ...project, title: titleSchema }), description: 'Create and label a conversation in a project.' },
  session_get: { schema: obj(session), description: 'Inspect a conversation.' },
  rename_session: { schema: obj({ ...session, title: titleSchema }), description: 'Keep a conversation label specific to the current work.' },
  session_delete: { schema: obj(session), description: 'Permanently delete a conversation. Project art, reviews and notes remain. Requires explicit user intent.' },
};
export type OperationName = keyof typeof operationDefinitions;
export interface UiPort { send(command: Omit<UiCommand, 'id'>): Promise<unknown> }
export function operationCatalog() {
  return Object.entries(operationDefinitions).map(([name, value]) => ({ name, description: value.description, inputSchema: z.toJSONSchema(value.schema) }));
}

export class Operations {
  private pending = new Set<Promise<unknown>>();
  private stopped = false;
  constructor(private store: Store, private ui: UiPort) {}
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
    const titleSource = origin === 'user' ? 'user' : 'assistant';
    switch (name) {
      case 'status': return { version: 1, ready: true };
      case 'projects_list': return store.projects();
      case 'project_create': return store.createProject(a.name);
      case 'project_get': return store.project(a.projectId);
      case 'project_rename': return store.renameProject(a.projectId, a.name);
      case 'project_delete': store.deleteProject(a.projectId); return { deleted: true };
      case 'sessions_list': return store.sessions(a.projectId);
      case 'session_create': return store.createSession(a.projectId, a.title, titleSource);
      case 'session_get': return store.session(a.sessionId, a.projectId);
      case 'rename_session': return store.renameSession(a.sessionId, a.title, titleSource);
      case 'session_delete': store.deleteSession(a.sessionId); return { deleted: true };
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
