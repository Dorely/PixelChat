import Database from 'better-sqlite3';
import { randomUUID } from 'node:crypto';
import { WorkbenchError } from '../shared/workbench';
import type { Project, Session, TitleSource } from '../shared/types';

const SCHEMA_VERSION = 1;
const ACTIVE_PROJECT_KEY = 'project-active';
const DEFAULT_PROJECT_NAME = 'Default';
export const now = () => new Date().toISOString();

type Row = Record<string, any>;
const project = (r: Row): Project => ({ id: r.id, name: r.name, createdAt: r.created_at, updatedAt: r.updated_at });
const session = (r: Row): Session => ({ id: r.id, projectId: r.project_id, title: r.title, titleSource: r.title_source, createdAt: r.created_at, updatedAt: r.updated_at });

/** SQLite owner for every workspace record. Callers pass explicit project scope. */
export class Store {
  readonly db: Database.Database;
  constructor(filename: string, readonly changed: () => void = () => {}) {
    this.db = new Database(filename);
    this.db.pragma('journal_mode = WAL');
    this.db.pragma('foreign_keys = ON');
    const version = this.db.pragma('user_version', { simple: true }) as number;
    if (version > SCHEMA_VERSION) { this.db.close(); throw new Error('This library was created by a newer PixelChat version.'); }
    if (version === 0) this.db.transaction(() => {
      this.db.exec(`
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE projects (id TEXT PRIMARY KEY, name TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE TABLE sessions (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, title TEXT NOT NULL, title_source TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE INDEX sessions_project ON sessions(project_id);
        PRAGMA user_version = ${SCHEMA_VERSION};
      `);
    })();
    if (!this.db.prepare('SELECT 1 FROM projects LIMIT 1').get()) this.createProject(DEFAULT_PROJECT_NAME);
    if (!this.setting(ACTIVE_PROJECT_KEY, '') || !this.db.prepare('SELECT 1 FROM projects WHERE id=?').get(this.setting(ACTIVE_PROJECT_KEY, '')))
      this.setSetting(ACTIVE_PROJECT_KEY, this.projects()[0]!.id);
  }
  close() { this.db.close(); }
  tx<T>(fn: () => T): T { const result = this.db.transaction(fn)(); this.changed(); return result; }

  setting(key: string, fallback: string): string { return (this.db.prepare('SELECT value FROM settings WHERE key=?').get(key) as Row | undefined)?.value ?? fallback; }
  setSetting(key: string, value: string) { this.db.prepare('INSERT INTO settings (key,value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(key, value); this.changed(); }

  projects(): Project[] { return (this.db.prepare('SELECT * FROM projects ORDER BY created_at').all() as Row[]).map(project); }
  project(id: string): Project {
    const row = this.db.prepare('SELECT * FROM projects WHERE id=?').get(id) as Row | undefined;
    if (!row) throw new WorkbenchError('NOT_FOUND', 'Project not found.');
    return project(row);
  }
  activeProjectId() { return this.setting(ACTIVE_PROJECT_KEY, ''); }
  setActiveProject(id: string) { this.project(id); this.setSetting(ACTIVE_PROJECT_KEY, id); }
  createProject(name: string): Project {
    const at = now(), id = randomUUID();
    this.tx(() => this.db.prepare('INSERT INTO projects (id,name,created_at,updated_at) VALUES (?,?,?,?)').run(id, name.trim(), at, at));
    return this.project(id);
  }
  renameProject(id: string, name: string): Project {
    this.project(id);
    this.tx(() => this.db.prepare('UPDATE projects SET name=?, updated_at=? WHERE id=?').run(name.trim(), now(), id));
    return this.project(id);
  }
  /** Removes workspace records; original image files stay on disk. */
  deleteProject(id: string) {
    this.project(id);
    this.tx(() => {
      this.db.prepare('DELETE FROM projects WHERE id=?').run(id);
      if (!this.db.prepare('SELECT 1 FROM projects LIMIT 1').get()) {
        const at = now(); this.db.prepare('INSERT INTO projects (id,name,created_at,updated_at) VALUES (?,?,?,?)').run(randomUUID(), DEFAULT_PROJECT_NAME, at, at);
      }
      if (this.activeProjectId() === id) this.db.prepare('UPDATE settings SET value=? WHERE key=?').run(this.projects()[0]!.id, ACTIVE_PROJECT_KEY);
    });
  }

  sessions(projectId: string): Session[] { return (this.db.prepare('SELECT * FROM sessions WHERE project_id=? ORDER BY updated_at DESC').all(projectId) as Row[]).map(session); }
  session(id: string, projectId?: string): Session {
    const row = this.db.prepare('SELECT * FROM sessions WHERE id=?').get(id) as Row | undefined;
    if (!row || (projectId && row.project_id !== projectId)) throw new WorkbenchError('NOT_FOUND', 'Session not found in project.');
    return session(row);
  }
  createSession(projectId: string, title = 'New session', source: TitleSource = 'auto'): Session {
    this.project(projectId);
    const at = now(), id = randomUUID();
    this.tx(() => this.db.prepare('INSERT INTO sessions (id,project_id,title,title_source,created_at,updated_at) VALUES (?,?,?,?,?,?)').run(id, projectId, title.trim(), source, at, at));
    return this.session(id);
  }
  renameSession(id: string, title: string, source: TitleSource): Session {
    this.session(id);
    this.tx(() => this.db.prepare('UPDATE sessions SET title=?, title_source=?, updated_at=? WHERE id=?').run(title.trim(), source, now(), id));
    return this.session(id);
  }
  deleteSession(id: string) { this.session(id); this.tx(() => this.db.prepare('DELETE FROM sessions WHERE id=?').run(id)); }
}
