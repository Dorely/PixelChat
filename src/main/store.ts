import Database from 'better-sqlite3';
import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { WorkbenchError } from '../shared/workbench';
import type { ActivityEntry, Decision, Item, ItemSearchResult, ItemStatusFilter, ItemSummary, Note, NoteThread, Origin, Pin, Project, Review, ReviewEntry, ReviewStatus, ReviewSummary, Revision, RevisionOrigin, ScanReport, Session, Source, TitleSource } from '../shared/types';

const SCHEMA_VERSION = 1;
const ACTIVE_PROJECT_KEY = 'project-active';
const DEFAULT_PROJECT_NAME = 'Default';
export const now = () => new Date().toISOString();

type Row = Record<string, any>;
const project = (r: Row): Project => ({ id: r.id, name: r.name, createdAt: r.created_at, updatedAt: r.updated_at });
const session = (r: Row): Session => ({ id: r.id, projectId: r.project_id, title: r.title, titleSource: r.title_source, createdAt: r.created_at, updatedAt: r.updated_at });
const revision = (r: Row): Revision => ({ id: r.id, itemId: r.item_id, parentId: r.parent_id, origin: r.origin, sha256: r.sha256, bytes: r.bytes, width: r.width, height: r.height, format: r.format, hasAlpha: !!r.has_alpha, opaque: !!r.opaque, provenance: JSON.parse(r.provenance), createdAt: r.created_at });
const OPEN_NOTE = 'n.item_id = i.id AND n.parent_id IS NULL AND n.resolved_at IS NULL';
const ITEM_COLUMNS = `i.*, r.origin AS r_origin, r.width AS r_width, r.height AS r_height, r.format AS r_format, r.has_alpha AS r_has_alpha, r.opaque AS r_opaque, r.created_at AS r_created_at,
  (SELECT COUNT(*) FROM notes n WHERE ${OPEN_NOTE}) AS open_notes`;
const ITEM_FROM = 'FROM items i JOIN revisions r ON r.id = i.selected_revision_id';
const ITEM_SELECT = `SELECT ${ITEM_COLUMNS} ${ITEM_FROM}`;
const PENDING = 'i.source_id IS NOT NULL AND i.selected_revision_id IS NOT i.synced_revision_id';
const itemSummary = (r: Row): ItemSummary => ({
  id: r.id, projectId: r.project_id, sourceId: r.source_id, sourcePath: r.source_path, name: r.name, collection: r.collection,
  description: r.description, tags: JSON.parse(r.tags), syncedRevisionId: r.synced_revision_id, selectedRevisionId: r.selected_revision_id,
  pendingExport: !!r.source_id && r.selected_revision_id !== r.synced_revision_id, missing: !!r.missing, openNotes: r.open_notes,
  selected: { id: r.selected_revision_id, origin: r.r_origin, width: r.r_width, height: r.r_height, format: r.r_format, hasAlpha: !!r.r_has_alpha, opaque: !!r.r_opaque, createdAt: r.r_created_at },
  createdAt: r.created_at, updatedAt: r.updated_at,
});
const note = (r: Row): Note => ({
  id: r.id, projectId: r.project_id, itemId: r.item_id, reviewId: r.review_id, revisionId: r.revision_id, parentId: r.parent_id, author: r.author,
  body: r.body, pin: r.pin ? JSON.parse(r.pin) : null, resolvedAt: r.resolved_at, resolvedBy: r.resolved_by, createdAt: r.created_at, updatedAt: r.updated_at,
});
/** Groups notes ordered by creation into root threads with their replies. */
const threads = (rows: Row[]): NoteThread[] => {
  const roots = new Map<string, NoteThread>();
  for (const row of rows) {
    const entry = note(row);
    if (!entry.parentId) roots.set(entry.id, { ...entry, replies: [] });
    else roots.get(entry.parentId)?.replies.push(entry);
  }
  return [...roots.values()];
};
const REVIEW_SELECT = `SELECT v.*, COALESCE(c.total, 0) AS total, COALESCE(c.accepted, 0) AS accepted, COALESCE(c.needs_changes, 0) AS needs_changes, COALESCE(c.rejected, 0) AS rejected,
  (SELECT COUNT(*) FROM notes n JOIN review_items x ON x.item_id = n.item_id WHERE x.review_id = v.id AND n.parent_id IS NULL AND n.resolved_at IS NULL) AS open_notes
  FROM reviews v LEFT JOIN (SELECT review_id, COUNT(*) AS total, SUM(decision = 'accepted') AS accepted, SUM(decision = 'needs_changes') AS needs_changes, SUM(decision = 'rejected') AS rejected
  FROM review_items GROUP BY review_id) c ON c.review_id = v.id`;
const reviewSummary = (r: Row): ReviewSummary => ({
  id: r.id, projectId: r.project_id, title: r.title, instructions: r.instructions, status: r.status, revision: r.revision,
  counts: { total: r.total, unreviewed: r.total - r.accepted - r.needs_changes - r.rejected, accepted: r.accepted, needs_changes: r.needs_changes, rejected: r.rejected },
  openNotes: r.open_notes, createdAt: r.created_at, createdBy: r.created_by, updatedAt: r.updated_at, completedAt: r.completed_at, completedBy: r.completed_by,
});
const revisionConflict = (revision: number) => new WorkbenchError('REVISION_CONFLICT', `The review is at revision ${revision}. Read it again and reconcile before retrying.`, { revision });
/** Rounds a pin to whole pixels inside the image. */
export function clampPin(pin: Pin, width: number, height: number): Pin {
  const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, Math.round(value)));
  if ((pin.width === undefined) !== (pin.height === undefined)) throw new WorkbenchError('INVALID_ARGUMENTS', 'A box pin needs both width and height; a point pin needs neither.');
  const x = clamp(pin.x, 0, width - 1), y = clamp(pin.y, 0, height - 1);
  if (pin.width === undefined || pin.height === undefined) return { x, y };
  return { x, y, width: clamp(pin.width, 1, width - x), height: clamp(pin.height, 1, height - y) };
}

/** Stored facts for a new revision; MediaStore writes the file first. */
export interface RevisionInput { id: string; file: string; sha256: string; bytes: number; width: number; height: number; format: Revision['format']; hasAlpha: boolean; opaque: boolean; origin: RevisionOrigin; parentId?: string | null; provenance?: Record<string, unknown> }
export interface ItemPatch { name?: string; description?: string; tags?: string[]; selectedRevisionId?: string }
export interface ReviewPatch { title?: string; instructions?: string; itemIds?: string[]; addItemIds?: string[] }
export interface NoteInput { itemId: string; body: string; reviewId?: string; revisionId?: string; parentId?: string; pin?: Pin }
export interface NotePatch { body?: string; pin?: Pin | null; resolved?: boolean }

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
        CREATE TABLE reviews (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, title TEXT NOT NULL, instructions TEXT NOT NULL, status TEXT NOT NULL,
          revision INTEGER NOT NULL, created_at TEXT NOT NULL, created_by TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT, completed_by TEXT);
        CREATE INDEX reviews_project ON reviews(project_id, status);
        CREATE TABLE review_items (review_id TEXT NOT NULL REFERENCES reviews(id) ON DELETE CASCADE, item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE, position INTEGER NOT NULL,
          decision TEXT NOT NULL DEFAULT 'unreviewed', decided_at TEXT, decided_by TEXT, decided_revision_id TEXT, PRIMARY KEY (review_id, item_id));
        CREATE INDEX review_items_item ON review_items(item_id);
        CREATE TABLE notes (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
          review_id TEXT REFERENCES reviews(id) ON DELETE SET NULL, revision_id TEXT NOT NULL, parent_id TEXT REFERENCES notes(id) ON DELETE CASCADE, author TEXT NOT NULL, body TEXT NOT NULL,
          pin TEXT, resolved_at TEXT, resolved_by TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE INDEX notes_item ON notes(item_id, created_at);
        CREATE INDEX notes_review ON notes(review_id);
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
    if (filter.status === 'open_notes') where.push(`EXISTS (SELECT 1 FROM notes n WHERE ${OPEN_NOTE})`);
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
    const notes = threads(this.db.prepare('SELECT * FROM notes WHERE item_id=? ORDER BY created_at').all(id) as Row[]);
    return { ...itemSummary(row), sourceFile: root && row.source_path ? join(root, row.source_path) : null, revisions, notes };
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
  /** Ordered activity for a project, optionally narrowed to one review or item. */
  activity(projectId: string, filter: { reviewId?: string; itemId?: string; afterSeq: number; limit: number }): ActivityEntry[] {
    const where = ['project_id = ?', 'seq > ?'], args: unknown[] = [projectId, filter.afterSeq];
    if (filter.reviewId) { where.push('review_id = ?'); args.push(filter.reviewId); }
    if (filter.itemId) { where.push('item_id = ?'); args.push(filter.itemId); }
    return (this.db.prepare(`SELECT * FROM activity WHERE ${where.join(' AND ')} ORDER BY seq LIMIT ?`).all(...args, filter.limit) as Row[])
      .map(r => ({ seq: r.seq, at: r.at, origin: r.origin, kind: r.kind, itemId: r.item_id, reviewId: r.review_id, data: JSON.parse(r.data) }));
  }

  reviews(projectId: string, status: ReviewStatus | 'all'): ReviewSummary[] {
    const rows = status === 'all'
      ? this.db.prepare(`${REVIEW_SELECT} WHERE v.project_id = ? ORDER BY v.updated_at DESC`).all(projectId)
      : this.db.prepare(`${REVIEW_SELECT} WHERE v.project_id = ? AND v.status = ? ORDER BY v.updated_at DESC`).all(projectId, status);
    return (rows as Row[]).map(reviewSummary);
  }
  reviewSummary(id: string, projectId: string): ReviewSummary {
    const row = this.db.prepare(`${REVIEW_SELECT} WHERE v.id = ? AND v.project_id = ?`).get(id, projectId) as Row | undefined;
    if (!row) throw new WorkbenchError('NOT_FOUND', 'Review not found in project.');
    return reviewSummary(row);
  }
  /** A review with its ordered entries and the note threads on its items. */
  review(id: string, projectId: string, filter: { decision?: Decision; includeResolved?: boolean } = {}): Review {
    const summary = this.reviewSummary(id, projectId);
    const entries = (this.db.prepare(`SELECT ${ITEM_COLUMNS}, x.position AS x_position, x.decision AS x_decision, x.decided_at AS x_decided_at, x.decided_by AS x_decided_by,
      x.decided_revision_id AS x_decided_revision_id ${ITEM_FROM} JOIN review_items x ON x.item_id = i.id WHERE x.review_id = ? ORDER BY x.position`).all(id) as Row[])
      .map((r): ReviewEntry => ({
        itemId: r.id, position: r.x_position, decision: r.x_decision, decidedAt: r.x_decided_at, decidedBy: r.x_decided_by, decidedRevisionId: r.x_decided_revision_id,
        stale: r.x_decision !== 'unreviewed' && r.x_decided_revision_id !== r.selected_revision_id, item: itemSummary(r),
      }))
      .filter(entry => !filter.decision || entry.decision === filter.decision);
    const visible = new Set(entries.map(entry => entry.itemId));
    const notes = threads(this.db.prepare('SELECT n.* FROM notes n JOIN review_items x ON x.item_id = n.item_id AND x.review_id = ? ORDER BY x.position, n.created_at').all(id) as Row[])
      .filter(thread => visible.has(thread.itemId) && (filter.includeResolved || !thread.resolvedAt));
    return { ...summary, entries, notes };
  }
  private editableReview(id: string, projectId: string, expectedRevision?: number): ReviewSummary {
    const summary = this.reviewSummary(id, projectId);
    if (summary.status === 'completed') throw new WorkbenchError('REVIEW_COMPLETED', 'The review is completed. Reopen it before changing it.');
    if (expectedRevision !== undefined && expectedRevision !== summary.revision) throw revisionConflict(summary.revision);
    return summary;
  }
  private touchReview(id: string) { this.db.prepare('UPDATE reviews SET revision = revision + 1, updated_at = ? WHERE id = ?').run(now(), id); }
  /** Replaces the ordered entries. Retained items keep their decisions. */
  private setEntries(projectId: string, reviewId: string, itemIds: string[]) {
    const ids = [...new Set(itemIds)];
    if (!ids.length) throw new WorkbenchError('INVALID_ARGUMENTS', 'A review needs at least one item.');
    const exists = this.db.prepare('SELECT 1 FROM items WHERE id=? AND project_id=?');
    for (const itemId of ids) if (!exists.get(itemId, projectId)) throw new WorkbenchError('NOT_FOUND', `Item ${itemId} not found in project.`);
    const keep = new Set(ids), remove = this.db.prepare('DELETE FROM review_items WHERE review_id=? AND item_id=?');
    for (const row of this.db.prepare('SELECT item_id FROM review_items WHERE review_id=?').all(reviewId) as Row[]) if (!keep.has(row.item_id)) remove.run(reviewId, row.item_id);
    const upsert = this.db.prepare('INSERT INTO review_items (review_id,item_id,position) VALUES (?,?,?) ON CONFLICT(review_id,item_id) DO UPDATE SET position=excluded.position');
    ids.forEach((itemId, position) => upsert.run(reviewId, itemId, position));
  }
  createReview(projectId: string, input: { title: string; instructions: string; itemIds: string[] }, origin: Origin): ReviewSummary {
    const id = randomUUID(), at = now();
    this.tx(() => {
      this.db.prepare('INSERT INTO reviews (id,project_id,title,instructions,status,revision,created_at,created_by,updated_at) VALUES (?,?,?,?,?,?,?,?,?)').run(id, projectId, input.title.trim(), input.instructions.trim(), 'active', 1, at, origin, at);
      this.setEntries(projectId, id, input.itemIds);
      this.appendActivity(projectId, origin, 'review.created', { reviewId: id }, { title: input.title.trim(), items: new Set(input.itemIds).size });
    });
    return this.reviewSummary(id, projectId);
  }
  updateReview(id: string, projectId: string, expectedRevision: number, patch: ReviewPatch, origin: Origin): ReviewSummary {
    const current = this.editableReview(id, projectId, expectedRevision);
    this.tx(() => {
      this.db.prepare('UPDATE reviews SET title=?, instructions=? WHERE id=?').run(patch.title?.trim() ?? current.title, patch.instructions?.trim() ?? current.instructions, id);
      if (patch.itemIds || patch.addItemIds) {
        const base = patch.itemIds ?? (this.db.prepare('SELECT item_id FROM review_items WHERE review_id=? ORDER BY position').all(id) as Row[]).map(r => r.item_id as string);
        this.setEntries(projectId, id, [...base, ...(patch.addItemIds ?? [])]);
      }
      this.touchReview(id);
      this.appendActivity(projectId, origin, 'review.updated', { reviewId: id }, { fields: Object.keys(patch) });
    });
    return this.reviewSummary(id, projectId);
  }
  /** Records decisions against each item's currently selected revision. */
  decide(id: string, projectId: string, decisions: { itemId: string; decision: Decision }[], origin: Origin): ReviewSummary {
    this.editableReview(id, projectId);
    this.tx(() => {
      const entry = this.db.prepare('SELECT i.selected_revision_id FROM review_items x JOIN items i ON i.id = x.item_id WHERE x.review_id=? AND x.item_id=?');
      const update = this.db.prepare('UPDATE review_items SET decision=?, decided_at=?, decided_by=?, decided_revision_id=? WHERE review_id=? AND item_id=?');
      for (const { itemId, decision } of decisions) {
        const row = entry.get(id, itemId) as Row | undefined;
        if (!row) throw new WorkbenchError('NOT_FOUND', `Item ${itemId} is not in this review.`);
        const decided = decision !== 'unreviewed';
        update.run(decision, decided ? now() : null, decided ? origin : null, decided ? row.selected_revision_id : null, id, itemId);
        this.appendActivity(projectId, origin, 'review.decided', { reviewId: id, itemId }, { decision, revisionId: decided ? row.selected_revision_id : null });
      }
      this.touchReview(id);
    });
    return this.reviewSummary(id, projectId);
  }
  setReviewStatus(id: string, projectId: string, expectedRevision: number, status: ReviewStatus, origin: Origin): ReviewSummary {
    const current = this.reviewSummary(id, projectId);
    if (current.status === status) throw new WorkbenchError('CONFLICT', status === 'completed' ? 'The review is already completed.' : 'The review is already active.');
    if (expectedRevision !== current.revision) throw revisionConflict(current.revision);
    const completed = status === 'completed';
    this.tx(() => {
      this.db.prepare('UPDATE reviews SET status=?, completed_at=?, completed_by=? WHERE id=?').run(status, completed ? now() : null, completed ? origin : null, id);
      this.touchReview(id);
      this.appendActivity(projectId, origin, completed ? 'review.completed' : 'review.reopened', { reviewId: id });
    });
    return this.reviewSummary(id, projectId);
  }
  /** Removes the review and its decisions. Notes stay on their items. */
  deleteReview(id: string, projectId: string, origin: Origin) {
    const current = this.reviewSummary(id, projectId);
    this.tx(() => {
      this.db.prepare('DELETE FROM reviews WHERE id=?').run(id);
      this.appendActivity(projectId, origin, 'review.deleted', {}, { reviewId: id, title: current.title });
    });
  }

  private noteRow(id: string, projectId: string): Row {
    const row = this.db.prepare('SELECT * FROM notes WHERE id=? AND project_id=?').get(id, projectId) as Row | undefined;
    if (!row) throw new WorkbenchError('NOT_FOUND', 'Note not found in project.');
    return row;
  }
  private revisionSize(id: string) { return this.db.prepare('SELECT width, height FROM revisions WHERE id=?').get(id) as { width: number; height: number }; }
  /** Adds a root note or a reply. Pins are clamped to the referenced revision's pixels. */
  addNote(projectId: string, input: NoteInput, origin: Origin): Note {
    const item = this.item(input.itemId, projectId);
    let reviewId = input.reviewId ?? null, revisionId = input.revisionId ?? item.selectedRevisionId, parentId: string | null = null;
    if (input.parentId) {
      const parent = this.noteRow(input.parentId, projectId);
      if (parent.item_id !== item.id) throw new WorkbenchError('INVALID_ARGUMENTS', 'A reply must be on the same item as its note.');
      if (input.pin) throw new WorkbenchError('INVALID_ARGUMENTS', 'Replies cannot carry pins. Add a new note for another region.');
      parentId = parent.parent_id ?? parent.id;
      reviewId = input.reviewId ?? parent.review_id;
      revisionId = input.revisionId ?? parent.revision_id;
    } else if (reviewId && this.reviewSummary(reviewId, projectId).status === 'completed') throw new WorkbenchError('REVIEW_COMPLETED', 'The review is completed. Reopen it before adding notes to it.');
    if (reviewId && !this.db.prepare('SELECT 1 FROM review_items x JOIN reviews v ON v.id = x.review_id WHERE x.review_id=? AND x.item_id=? AND v.project_id=?').get(reviewId, item.id, projectId))
      throw new WorkbenchError('INVALID_ARGUMENTS', 'The item is not in that review.');
    if (!item.revisions.some(entry => entry.id === revisionId)) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
    const size = this.revisionSize(revisionId);
    const pin = input.pin ? clampPin(input.pin, size.width, size.height) : null;
    const id = randomUUID(), at = now();
    this.tx(() => {
      this.db.prepare('INSERT INTO notes (id,project_id,item_id,review_id,revision_id,parent_id,author,body,pin,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?)')
        .run(id, projectId, item.id, reviewId, revisionId, parentId, origin, input.body.trim(), pin ? JSON.stringify(pin) : null, at, at);
      this.appendActivity(projectId, origin, 'note.added', { itemId: item.id, reviewId: reviewId ?? undefined }, { noteId: id, parentId, revisionId, pinned: !!pin });
    });
    return note(this.noteRow(id, projectId));
  }
  /** Agents may change only their own notes; anyone may resolve or reopen a thread. */
  updateNote(id: string, projectId: string, patch: NotePatch, origin: Origin): Note {
    const row = this.noteRow(id, projectId);
    if ((patch.body !== undefined || patch.pin !== undefined) && origin === 'agent' && row.author !== 'agent') throw new WorkbenchError('FORBIDDEN', "Agents cannot edit the user's notes. Reply instead.");
    if (row.parent_id && (patch.pin || patch.resolved !== undefined)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Only root notes carry pins and resolution.');
    let pin: string | null = row.pin;
    if (patch.pin !== undefined) { const size = this.revisionSize(row.revision_id); pin = patch.pin ? JSON.stringify(clampPin(patch.pin, size.width, size.height)) : null; }
    const resolving = patch.resolved !== undefined && patch.resolved !== !!row.resolved_at;
    const kind = resolving ? (patch.resolved ? 'note.resolved' : 'note.reopened') : 'note.updated';
    this.tx(() => {
      this.db.prepare('UPDATE notes SET body=?, pin=?, resolved_at=?, resolved_by=?, updated_at=? WHERE id=?').run(patch.body?.trim() ?? row.body, pin,
        resolving ? (patch.resolved ? now() : null) : row.resolved_at, resolving ? (patch.resolved ? origin : null) : row.resolved_by, now(), id);
      this.appendActivity(projectId, origin, kind, { itemId: row.item_id, reviewId: row.review_id ?? undefined }, { noteId: id, fields: Object.keys(patch) });
    });
    return note(this.noteRow(id, projectId));
  }
  /** Deletes a note with its replies. Agents may delete only their own. */
  deleteNote(id: string, projectId: string, origin: Origin) {
    const row = this.noteRow(id, projectId);
    if (origin === 'agent' && row.author !== 'agent') throw new WorkbenchError('FORBIDDEN', "Agents cannot delete the user's notes.");
    this.tx(() => {
      this.db.prepare('DELETE FROM notes WHERE id=?').run(id);
      this.appendActivity(projectId, origin, 'note.deleted', { itemId: row.item_id, reviewId: row.review_id ?? undefined }, { noteId: id });
    });
  }

  /** Revision file and format, scoped to a project, for media serving. */
  revisionFile(id: string, projectId: string): { file: string; format: Revision['format'] } | null {
    const row = this.db.prepare('SELECT r.file, r.format FROM revisions r JOIN items i ON i.id = r.item_id WHERE r.id=? AND i.project_id=?').get(id, projectId) as Row | undefined;
    return row ? { file: row.file, format: row.format } : null;
  }
}
