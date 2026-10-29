import Database from 'better-sqlite3';
import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { WorkbenchError } from '../shared/workbench';
import type { Item, ItemSearchResult, ItemStatusFilter, ItemSummary, Origin, Project, Revision, RevisionOrigin, ScanReport, Session, Source, TitleSource } from '../shared/types';

const SCHEMA_VERSION = 1;
const ACTIVE_PROJECT_KEY = 'project-active';
const DEFAULT_PROJECT_NAME = 'Default';
export const now = () => new Date().toISOString();

type Row = Record<string, any>;
const project = (r: Row): Project => ({ id: r.id, name: r.name, createdAt: r.created_at, updatedAt: r.updated_at });
const session = (r: Row): Session => ({ id: r.id, projectId: r.project_id, title: r.title, titleSource: r.title_source, createdAt: r.created_at, updatedAt: r.updated_at });
const revision = (r: Row): Revision => ({ id: r.id, itemId: r.item_id, parentId: r.parent_id, origin: r.origin, sha256: r.sha256, bytes: r.bytes, width: r.width, height: r.height, format: r.format, hasAlpha: !!r.has_alpha, opaque: !!r.opaque, provenance: JSON.parse(r.provenance), createdAt: r.created_at });
const ITEM_SELECT = `SELECT i.*, r.origin AS r_origin, r.width AS r_width, r.height AS r_height, r.format AS r_format, r.has_alpha AS r_has_alpha, r.opaque AS r_opaque, r.created_at AS r_created_at
  FROM items i JOIN revisions r ON r.id = i.selected_revision_id`;
const PENDING = 'i.source_id IS NOT NULL AND i.selected_revision_id IS NOT i.synced_revision_id';
const itemSummary = (r: Row): ItemSummary => ({
  id: r.id, projectId: r.project_id, sourceId: r.source_id, sourcePath: r.source_path, name: r.name, collection: r.collection,
  description: r.description, tags: JSON.parse(r.tags), syncedRevisionId: r.synced_revision_id, selectedRevisionId: r.selected_revision_id,
  pendingExport: !!r.source_id && r.selected_revision_id !== r.synced_revision_id, missing: !!r.missing,
  selected: { id: r.selected_revision_id, origin: r.r_origin, width: r.r_width, height: r.r_height, format: r.r_format, hasAlpha: !!r.r_has_alpha, opaque: !!r.r_opaque, createdAt: r.r_created_at },
  createdAt: r.created_at, updatedAt: r.updated_at,
});

/** Stored facts for a new revision; MediaStore writes the file first. */
export interface RevisionInput { id: string; file: string; sha256: string; bytes: number; width: number; height: number; format: Revision['format']; hasAlpha: boolean; opaque: boolean; origin: RevisionOrigin; parentId?: string | null; provenance?: Record<string, unknown> }
export interface ItemPatch { name?: string; description?: string; tags?: string[]; selectedRevisionId?: string }

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
        CREATE TABLE sources (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, root TEXT NOT NULL, include TEXT NOT NULL, exclude TEXT NOT NULL, last_scan TEXT, created_at TEXT NOT NULL);
        CREATE INDEX sources_project ON sources(project_id);
        CREATE TABLE items (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, source_id TEXT REFERENCES sources(id) ON DELETE CASCADE, source_path TEXT,
          name TEXT NOT NULL, collection TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', tags TEXT NOT NULL DEFAULT '[]',
          synced_revision_id TEXT, selected_revision_id TEXT NOT NULL, missing INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE (source_id, source_path));
        CREATE INDEX items_project ON items(project_id, collection, name);
        CREATE TABLE revisions (id TEXT PRIMARY KEY, item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE, parent_id TEXT, origin TEXT NOT NULL, file TEXT NOT NULL,
          sha256 TEXT NOT NULL, bytes INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, format TEXT NOT NULL, has_alpha INTEGER NOT NULL, opaque INTEGER NOT NULL,
          provenance TEXT NOT NULL, created_at TEXT NOT NULL);
        CREATE INDEX revisions_item ON revisions(item_id, created_at);
        CREATE TABLE activity (seq INTEGER PRIMARY KEY AUTOINCREMENT, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, at TEXT NOT NULL, origin TEXT NOT NULL,
          kind TEXT NOT NULL, item_id TEXT, review_id TEXT, data TEXT NOT NULL);
        CREATE INDEX activity_project ON activity(project_id, seq);
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

  /** Every workspace mutation appends here; agents follow it to see what changed. */
  appendActivity(projectId: string, origin: Origin, kind: string, refs: { itemId?: string; reviewId?: string }, data: Record<string, unknown> = {}) {
    this.db.prepare('INSERT INTO activity (project_id,at,origin,kind,item_id,review_id,data) VALUES (?,?,?,?,?,?,?)').run(projectId, now(), origin, kind, refs.itemId ?? null, refs.reviewId ?? null, JSON.stringify(data));
  }

  sources(projectId: string, scanning: (id: string) => Source['scanning'] = () => null): Source[] {
    return (this.db.prepare('SELECT s.*, (SELECT COUNT(*) FROM items i WHERE i.source_id = s.id) AS item_count FROM sources s WHERE project_id=? ORDER BY created_at').all(projectId) as Row[])
      .map(r => ({ id: r.id, projectId: r.project_id, root: r.root, include: JSON.parse(r.include), exclude: JSON.parse(r.exclude), createdAt: r.created_at, itemCount: r.item_count, lastScan: r.last_scan ? JSON.parse(r.last_scan) : null, scanning: scanning(r.id) }));
  }
  source(id: string, projectId: string, scanning?: (id: string) => Source['scanning']): Source {
    const found = this.sources(projectId, scanning).find(entry => entry.id === id);
    if (!found) throw new WorkbenchError('NOT_FOUND', 'Source not found in project.');
    return found;
  }
  createSource(projectId: string, root: string, include: string[], exclude: string[], origin: Origin): string {
    if (this.db.prepare('SELECT 1 FROM sources WHERE project_id=? AND root=? COLLATE NOCASE').get(projectId, root)) throw new WorkbenchError('CONFLICT', 'That folder is already a source in this project. Rescan it instead.');
    const id = randomUUID();
    this.tx(() => {
      this.db.prepare('INSERT INTO sources (id,project_id,root,include,exclude,created_at) VALUES (?,?,?,?,?,?)').run(id, projectId, root, JSON.stringify(include), JSON.stringify(exclude), now());
      this.appendActivity(projectId, origin, 'source.added', {}, { sourceId: id, root });
    });
    return id;
  }
  /** Records a scan: linked items not seen on disk are marked missing. */
  finishScan(projectId: string, sourceId: string, seen: Set<string>, report: Omit<ScanReport, 'missing'>, origin: Origin): ScanReport {
    return this.tx(() => {
      const rows = this.db.prepare('SELECT id, source_path, missing FROM items WHERE source_id=?').all(sourceId) as Row[];
      const mark = this.db.prepare('UPDATE items SET missing=?, updated_at=? WHERE id=?');
      let missing = 0;
      for (const row of rows) {
        const gone = !seen.has(row.source_path);
        if (gone) missing++;
        if (gone !== !!row.missing) mark.run(gone ? 1 : 0, now(), row.id);
      }
      const full: ScanReport = { ...report, missing };
      this.db.prepare('UPDATE sources SET last_scan=? WHERE id=?').run(JSON.stringify(full), sourceId);
      this.appendActivity(projectId, origin, 'source.scanned', {}, { sourceId, added: full.added, changed: full.changed, missing, failed: full.failed.length });
      return full;
    });
  }
  /** Deletes a source with its items and returns their revision files for removal. */
  deleteSource(projectId: string, sourceId: string, origin: Origin): string[] {
    const files = (this.db.prepare('SELECT r.file FROM revisions r JOIN items i ON i.id = r.item_id WHERE i.source_id=?').all(sourceId) as Row[]).map(r => r.file as string);
    this.tx(() => {
      this.db.prepare('DELETE FROM sources WHERE id=? AND project_id=?').run(sourceId, projectId);
      this.appendActivity(projectId, origin, 'source.removed', {}, { sourceId });
    });
    return files;
  }
  projectRevisionFiles(projectId: string): string[] {
    return (this.db.prepare('SELECT r.file FROM revisions r JOIN items i ON i.id = r.item_id WHERE i.project_id=?').all(projectId) as Row[]).map(r => r.file as string);
  }

  /** Linked item at a source path with the hash of its synced revision. */
  sourceItem(sourceId: string, sourcePath: string): { id: string; syncedRevisionId: string | null; syncedSha: string | null } | null {
    const row = this.db.prepare('SELECT i.id, i.synced_revision_id, r.sha256 FROM items i LEFT JOIN revisions r ON r.id = i.synced_revision_id WHERE i.source_id=? AND i.source_path=?').get(sourceId, sourcePath) as Row | undefined;
    return row ? { id: row.id, syncedRevisionId: row.synced_revision_id, syncedSha: row.sha256 ?? null } : null;
  }
  private insertRevision(itemId: string, input: RevisionInput) {
    this.db.prepare('INSERT INTO revisions (id,item_id,parent_id,origin,file,sha256,bytes,width,height,format,has_alpha,opaque,provenance,created_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)')
      .run(input.id, itemId, input.parentId ?? null, input.origin, input.file, input.sha256, input.bytes, input.width, input.height, input.format, input.hasAlpha ? 1 : 0, input.opaque ? 1 : 0, JSON.stringify(input.provenance ?? {}), now());
  }
  createItem(projectId: string, input: { sourceId: string | null; sourcePath: string | null; name: string; collection: string; revision: RevisionInput }, origin: Origin): string {
    const id = randomUUID(), at = now();
    this.tx(() => {
      this.db.prepare('INSERT INTO items (id,project_id,source_id,source_path,name,collection,synced_revision_id,selected_revision_id,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?)')
        .run(id, projectId, input.sourceId, input.sourcePath, input.name, input.collection, input.sourceId ? input.revision.id : null, input.revision.id, at, at);
      this.insertRevision(id, input.revision);
      this.appendActivity(projectId, origin, 'item.added', { itemId: id }, { name: input.name, revisionId: input.revision.id, origin: input.revision.origin });
    });
    return id;
  }
  /** Adds a revision. A synced revision matches the source file; the selection follows it unless another version was chosen. */
  addRevision(projectId: string, itemId: string, input: RevisionInput, synced: boolean, origin: Origin) {
    this.tx(() => {
      const before = this.db.prepare('SELECT synced_revision_id, selected_revision_id FROM items WHERE id=?').get(itemId) as Row;
      this.insertRevision(itemId, input);
      if (synced) {
        const selected = before.selected_revision_id === before.synced_revision_id ? input.id : before.selected_revision_id;
        this.db.prepare('UPDATE items SET synced_revision_id=?, selected_revision_id=?, missing=0, updated_at=? WHERE id=?').run(input.id, selected, now(), itemId);
      } else this.db.prepare('UPDATE items SET updated_at=? WHERE id=?').run(now(), itemId);
      this.appendActivity(projectId, origin, synced ? 'item.source_changed' : 'revision.added', { itemId }, { revisionId: input.id, origin: input.origin });
    });
  }

  searchItems(projectId: string, filter: { query?: string; collection?: string; sourceId?: string; status?: ItemStatusFilter; limit: number; offset: number }): ItemSearchResult {
    const where = ['i.project_id = ?'], args: unknown[] = [projectId];
    if (filter.query) {
      where.push(`(i.name LIKE ? ESCAPE '!' OR i.source_path LIKE ? ESCAPE '!' OR i.description LIKE ? ESCAPE '!' OR i.tags LIKE ? ESCAPE '!')`);
      const pattern = `%${filter.query.replace(/[!%_]/g, match => '!' + match)}%`;
      args.push(pattern, pattern, pattern, pattern);
    }
    if (filter.collection !== undefined) { where.push('i.collection = ?'); args.push(filter.collection); }
    if (filter.sourceId) { where.push('i.source_id = ?'); args.push(filter.sourceId); }
    if (filter.status === 'pending_export') where.push(PENDING);
    if (filter.status === 'missing') where.push('i.missing = 1');
    const clause = where.join(' AND ');
    const total = (this.db.prepare(`SELECT COUNT(*) AS n FROM items i WHERE ${clause}`).get(...args) as Row).n as number;
    const items = (this.db.prepare(`${ITEM_SELECT} WHERE ${clause} ORDER BY i.collection, i.name LIMIT ? OFFSET ?`).all(...args, filter.limit, filter.offset) as Row[]).map(itemSummary);
    const collections = (this.db.prepare('SELECT collection AS name, COUNT(*) AS count FROM items WHERE project_id=? GROUP BY collection ORDER BY collection').all(projectId) as Row[]).map(r => ({ name: r.name as string, count: r.count as number }));
    return { total, items, collections };
  }
  item(id: string, projectId: string): Item {
    const row = this.db.prepare(`${ITEM_SELECT} WHERE i.id=? AND i.project_id=?`).get(id, projectId) as Row | undefined;
    if (!row) throw new WorkbenchError('NOT_FOUND', 'Item not found in project.');
    const root = row.source_id ? (this.db.prepare('SELECT root FROM sources WHERE id=?').get(row.source_id) as Row).root as string : null;
    const revisions = (this.db.prepare('SELECT * FROM revisions WHERE item_id=? ORDER BY created_at').all(id) as Row[]).map(revision);
    return { ...itemSummary(row), sourceFile: root && row.source_path ? join(root, row.source_path) : null, revisions };
  }
  updateItem(id: string, projectId: string, patch: ItemPatch, origin: Origin): Item {
    const current = this.item(id, projectId);
    if (patch.selectedRevisionId && !current.revisions.some(entry => entry.id === patch.selectedRevisionId)) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
    this.tx(() => {
      this.db.prepare('UPDATE items SET name=?, description=?, tags=?, selected_revision_id=?, updated_at=? WHERE id=?')
        .run(patch.name ?? current.name, patch.description ?? current.description, JSON.stringify(patch.tags ?? current.tags), patch.selectedRevisionId ?? current.selectedRevisionId, now(), id);
      this.appendActivity(projectId, origin, 'item.updated', { itemId: id }, { fields: Object.keys(patch) });
    });
    return this.item(id, projectId);
  }
  /** Deletes an item with its revisions and returns their files for removal. */
  deleteItem(id: string, projectId: string, origin: Origin): string[] {
    this.item(id, projectId);
    const files = (this.db.prepare('SELECT file FROM revisions WHERE item_id=?').all(id) as Row[]).map(r => r.file as string);
    this.tx(() => {
      this.db.prepare('DELETE FROM items WHERE id=?').run(id);
      this.appendActivity(projectId, origin, 'item.deleted', { itemId: id });
    });
    return files;
  }
  /** Revision file and format, scoped to a project, for media serving. */
  revisionFile(id: string, projectId: string): { file: string; format: Revision['format'] } | null {
    const row = this.db.prepare('SELECT r.file, r.format FROM revisions r JOIN items i ON i.id = r.item_id WHERE r.id=? AND i.project_id=?').get(id, projectId) as Row | undefined;
    return row ? { file: row.file, format: row.format } : null;
  }
}
