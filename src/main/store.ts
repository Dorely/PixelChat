import Database from 'better-sqlite3';
import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { WorkbenchError } from '../shared/workbench';
import type { ActivityEntry, Stroke, Decision, ImageJob, ImageJobInput, Item, ItemSearchResult, ItemStatusFilter, ItemSummary, Note, NoteThread, Origin, Pin, Project, Review, ReviewEntry, ReviewMode, ReviewStatus, ReviewSummary, Revision, RevisionOrigin, ScanReport, Session, Source, TitleSource } from '../shared/types';

const SCHEMA_VERSION = 6;
const ACTIVE_PROJECT_KEY = 'project-active';
const DEFAULT_PROJECT_NAME = 'Default';
export const now = () => new Date().toISOString();

type Row = Record<string, any>;
const project = (r: Row): Project => ({ id: r.id, name: r.name, createdAt: r.created_at, updatedAt: r.updated_at });
const session = (r: Row): Session => ({ id: r.id, projectId: r.project_id, title: r.title, titleSource: r.title_source, createdAt: r.created_at, updatedAt: r.updated_at });
const revision = (r: Row): Revision => ({ id: r.id, itemId: r.item_id, parentId: r.parent_id, origin: r.origin, sha256: r.sha256, bytes: r.bytes, width: r.width, height: r.height, frames: r.frames, format: r.format, hasAlpha: !!r.has_alpha, opaque: !!r.opaque, provenance: JSON.parse(r.provenance), createdAt: r.created_at });
const OPEN_NOTE = 'n.item_id = i.id AND n.parent_id IS NULL AND n.resolved_at IS NULL';
const ITEM_COLUMNS = `i.*, r.origin AS r_origin, r.width AS r_width, r.height AS r_height, r.frames AS r_frames, r.format AS r_format, r.has_alpha AS r_has_alpha, r.opaque AS r_opaque, r.created_at AS r_created_at,
  (SELECT COUNT(*) FROM notes n WHERE ${OPEN_NOTE}) AS open_notes`;
const ITEM_FROM = 'FROM items i JOIN revisions r ON r.id = i.selected_revision_id';
const ITEM_SELECT = `SELECT ${ITEM_COLUMNS} ${ITEM_FROM}`;
const PENDING = 'i.source_id IS NOT NULL AND i.selected_revision_id IS NOT i.synced_revision_id';
const itemSummary = (r: Row): ItemSummary => ({
  id: r.id, projectId: r.project_id, sourceId: r.source_id, sourcePath: r.source_path, name: r.name, collection: r.collection,
  description: r.description, tags: JSON.parse(r.tags), syncedRevisionId: r.synced_revision_id, selectedRevisionId: r.selected_revision_id,
  pendingExport: !!r.source_id && r.selected_revision_id !== r.synced_revision_id, missing: !!r.missing, openNotes: r.open_notes,
  selected: { id: r.selected_revision_id, origin: r.r_origin, width: r.r_width, height: r.r_height, frames: r.r_frames, format: r.r_format, hasAlpha: !!r.r_has_alpha, opaque: !!r.r_opaque, createdAt: r.r_created_at },
  createdAt: r.created_at, updatedAt: r.updated_at,
});
const note = (r: Row): Note => ({
  id: r.id, projectId: r.project_id, itemId: r.item_id, reviewId: r.review_id, revisionId: r.revision_id, parentId: r.parent_id, author: r.author,
  body: r.body, pin: r.pin ? JSON.parse(r.pin) : null, sketch: r.sketch ? JSON.parse(r.sketch) : null, resolvedAt: r.resolved_at, resolvedBy: r.resolved_by, createdAt: r.created_at, updatedAt: r.updated_at,
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
  (SELECT COUNT(*) FROM notes n JOIN review_entries x ON x.revision_id = n.revision_id WHERE x.review_id = v.id AND n.parent_id IS NULL AND n.resolved_at IS NULL) AS open_notes
  FROM reviews v LEFT JOIN (SELECT review_id, COUNT(*) AS total, SUM(decision = 'accepted') AS accepted, SUM(decision = 'needs_changes') AS needs_changes, SUM(decision = 'rejected') AS rejected
  FROM review_entries WHERE role = 'subject' GROUP BY review_id) c ON c.review_id = v.id`;
const reviewSummary = (r: Row): ReviewSummary => ({
  id: r.id, projectId: r.project_id, title: r.title, instructions: r.instructions, mode: r.mode, status: r.status, revision: r.revision,
  counts: { total: r.total, unreviewed: r.total - r.accepted - r.needs_changes - r.rejected, accepted: r.accepted, needs_changes: r.needs_changes, rejected: r.rejected },
  openNotes: r.open_notes, createdAt: r.created_at, createdBy: r.created_by, updatedAt: r.updated_at, completedAt: r.completed_at, completedBy: r.completed_by,
});
const job = (r: Row): ImageJob => ({
  id: r.id, projectId: r.project_id, batchId: r.batch_id, position: r.position, status: r.status, input: JSON.parse(r.input), itemId: r.item_id, revisionId: r.revision_id,
  error: r.error, warning: r.warning, createdBy: r.created_by, createdAt: r.created_at, startedAt: r.started_at, finishedAt: r.finished_at,
});
const ACTIVE_JOB = "status IN ('queued', 'running')";
const IN_COMPLETED_REVIEW = "SELECT 1 FROM review_entries x JOIN reviews v ON v.id = x.review_id WHERE v.status = 'completed' AND x.item_id IN";
const revisionConflict = (revision: number) => new WorkbenchError('REVISION_CONFLICT', `The review is at revision ${revision}. Read it again and reconcile before retrying.`, { revision });
/** Rounds a pin to whole pixels inside the image. */
export function clampPin(pin: Pin, width: number, height: number): Pin {
  const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, Math.round(value)));
  if ((pin.width === undefined) !== (pin.height === undefined)) throw new WorkbenchError('INVALID_ARGUMENTS', 'A box pin needs both width and height; a point pin needs neither.');
  const x = clamp(pin.x, 0, width - 1), y = clamp(pin.y, 0, height - 1);
  if (pin.width === undefined || pin.height === undefined) return { x, y };
  return { x, y, width: clamp(pin.width, 1, width - x), height: clamp(pin.height, 1, height - y) };
}
/** Keeps drawing points inside the image, rounded to a tenth of a pixel. */
export function clampSketch(strokes: Stroke[], width: number, height: number): Stroke[] {
  const round = (value: number, max: number) => Math.round(Math.min(max, Math.max(0, value)) * 10) / 10;
  return strokes.map(stroke => ({ points: stroke.points.map((value, index) => round(value, index % 2 ? height : width)), size: Math.max(0.1, Math.round(stroke.size * 10) / 10), color: stroke.color.toLowerCase() }));
}

/** Stored facts for a new revision; MediaStore writes the file first. */
export interface RevisionInput { id: string; file: string; sha256: string; bytes: number; width: number; height: number; frames: number; format: Revision['format']; hasAlpha: boolean; opaque: boolean; origin: RevisionOrigin; parentId?: string | null; provenance?: Record<string, unknown> }
export interface ItemPatch { name?: string; description?: string; tags?: string[]; selectedRevisionId?: string }
export interface ReviewPatch {
  title?: string; instructions?: string; mode?: ReviewMode; revisionIds?: string[]; addRevisionIds?: string[]; referenceRevisionIds?: string[]; addReferenceRevisionIds?: string[]
}
export interface NoteInput { itemId: string; body: string; reviewId?: string; revisionId?: string; parentId?: string; pin?: Pin; sketch?: Stroke[] }
export interface NotePatch { body?: string; pin?: Pin | null; sketch?: Stroke[] | null; resolved?: boolean }

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
        CREATE TABLE jobs (id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE, batch_id TEXT NOT NULL, position INTEGER NOT NULL, status TEXT NOT NULL,
          input TEXT NOT NULL, request TEXT NOT NULL, item_id TEXT REFERENCES items(id) ON DELETE SET NULL, revision_id TEXT, error TEXT, warning TEXT,
          created_by TEXT NOT NULL, created_at TEXT NOT NULL, started_at TEXT, finished_at TEXT);
        CREATE INDEX jobs_project ON jobs(project_id, created_at);
        CREATE INDEX jobs_batch ON jobs(batch_id, position);
        CREATE INDEX jobs_status ON jobs(status);
        PRAGMA user_version = 1;
      `);
    })();
    if (version < 2) this.db.transaction(() => {
      this.db.exec(`
        CREATE TABLE messages (id TEXT PRIMARY KEY, session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE, role TEXT NOT NULL, text TEXT NOT NULL, status TEXT NOT NULL,
          attachments TEXT NOT NULL DEFAULT '[]', created_at TEXT NOT NULL);
        CREATE INDEX messages_session ON messages(session_id);
        CREATE TABLE chat_history (session_id TEXT PRIMARY KEY REFERENCES sessions(id) ON DELETE CASCADE, input TEXT NOT NULL);
        CREATE TABLE tool_calls (session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE, id TEXT NOT NULL, name TEXT NOT NULL, args TEXT NOT NULL, status TEXT NOT NULL, result TEXT,
          PRIMARY KEY (session_id, id));
        PRAGMA user_version = 2;
      `);
    })();
    // Review entries name the exact version shown; a decided entry keeps the version it was decided on.
    if (version < 3) this.db.transaction(() => {
      this.db.exec(`
        CREATE TABLE review_entries (review_id TEXT NOT NULL REFERENCES reviews(id) ON DELETE CASCADE, revision_id TEXT NOT NULL REFERENCES revisions(id) ON DELETE CASCADE,
          item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE, position INTEGER NOT NULL, decision TEXT NOT NULL DEFAULT 'unreviewed', decided_at TEXT, decided_by TEXT,
          PRIMARY KEY (review_id, revision_id));
        INSERT OR IGNORE INTO review_entries (review_id, revision_id, item_id, position, decision, decided_at, decided_by)
          SELECT x.review_id, COALESCE(x.decided_revision_id, i.selected_revision_id), x.item_id, x.position, x.decision, x.decided_at, x.decided_by FROM review_items x JOIN items i ON i.id = x.item_id;
        DROP TABLE review_items;
        CREATE INDEX review_entries_item ON review_entries(item_id);
        CREATE INDEX review_entries_revision ON review_entries(revision_id);
        PRAGMA user_version = 3;
      `);
    })();
    // Animated GIF and WebP versions record their frame count; earlier imports are treated as stills.
    if (version < 4) this.db.transaction(() => {
      this.db.exec(`
        ALTER TABLE revisions ADD COLUMN frames INTEGER NOT NULL DEFAULT 1;
        PRAGMA user_version = 4;
      `);
    })();
    // A review is a standard decision list or a contest between candidates; an entry is a subject of the decision or reference context.
    if (version < 5) this.db.transaction(() => {
      this.db.exec(`
        ALTER TABLE reviews ADD COLUMN mode TEXT NOT NULL DEFAULT 'standard';
        ALTER TABLE review_entries ADD COLUMN role TEXT NOT NULL DEFAULT 'subject';
        PRAGMA user_version = 5;
      `);
    })();
    // A root note may carry a freehand drawing beside or instead of its pin.
    if (version < 6) this.db.transaction(() => {
      this.db.exec(`
        ALTER TABLE notes ADD COLUMN sketch TEXT;
        PRAGMA user_version = 6;
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
  createSession(projectId: string, title = 'New conversation', source: TitleSource = 'auto'): Session {
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
    if (this.db.prepare(`${IN_COMPLETED_REVIEW} (SELECT id FROM items WHERE source_id = ?) LIMIT 1`).get(sourceId))
      throw new WorkbenchError('IN_COMPLETED_REVIEW', 'Completed reviews show art from this source and keep it. Delete those reviews first.');
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
    this.db.prepare('INSERT INTO revisions (id,item_id,parent_id,origin,file,sha256,bytes,width,height,frames,format,has_alpha,opaque,provenance,created_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)')
      .run(input.id, itemId, input.parentId ?? null, input.origin, input.file, input.sha256, input.bytes, input.width, input.height, input.frames, input.format, input.hasAlpha ? 1 : 0, input.opaque ? 1 : 0, JSON.stringify(input.provenance ?? {}), now());
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
  /** Adds a version to an item's lineage. A synced revision matches the source file; the selection follows it unless another version was chosen. */
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
    if (this.db.prepare(`${IN_COMPLETED_REVIEW} (?) LIMIT 1`).get(id)) throw new WorkbenchError('IN_COMPLETED_REVIEW', 'A completed review shows this item and keeps it. Delete that review first.');
    const files = (this.db.prepare('SELECT file FROM revisions WHERE item_id=?').all(id) as Row[]).map(r => r.file as string);
    this.tx(() => {
      this.db.prepare('DELETE FROM items WHERE id=?').run(id);
      this.appendActivity(projectId, origin, 'item.deleted', { itemId: id });
    });
    return files;
  }
  /** Ordered activity for a project, optionally narrowed to one review or item. */
  activity(projectId: string, filter: { reviewId?: string; itemId?: string; origin?: Origin; afterSeq: number; limit: number }): ActivityEntry[] {
    const where = ['project_id = ?', 'seq > ?'], args: unknown[] = [projectId, filter.afterSeq];
    if (filter.reviewId) { where.push('review_id = ?'); args.push(filter.reviewId); }
    if (filter.origin) { where.push('origin = ?'); args.push(filter.origin); }
    if (filter.itemId) { where.push('item_id = ?'); args.push(filter.itemId); }
    return (this.db.prepare(`SELECT * FROM activity WHERE ${where.join(' AND ')} ORDER BY seq LIMIT ?`).all(...args, filter.limit) as Row[])
      .map(r => ({ seq: r.seq, at: r.at, origin: r.origin, kind: r.kind, itemId: r.item_id, reviewId: r.review_id, data: JSON.parse(r.data) }));
  }
  latestSeq(projectId: string): number { return (this.db.prepare('SELECT MAX(seq) AS seq FROM activity WHERE project_id=?').get(projectId) as Row).seq ?? 0; }

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
  /** A review with its ordered entries (each an exact version) and the note threads on those versions. */
  review(id: string, projectId: string, filter: { decision?: Decision; includeResolved?: boolean } = {}): Review {
    const summary = this.reviewSummary(id, projectId);
    const entries = (this.db.prepare(`SELECT ${ITEM_COLUMNS}, x.revision_id AS x_revision_id, x.role AS x_role, x.position AS x_position, x.decision AS x_decision, x.decided_at AS x_decided_at, x.decided_by AS x_decided_by,
      e.origin AS e_origin, e.width AS e_width, e.height AS e_height, e.frames AS e_frames, e.format AS e_format, e.has_alpha AS e_has_alpha, e.opaque AS e_opaque, e.created_at AS e_created_at,
      (SELECT COUNT(*) FROM revisions later WHERE later.item_id = i.id AND later.created_at > e.created_at) AS x_newer,
      (SELECT COUNT(*) FROM notes n WHERE n.revision_id = x.revision_id AND n.parent_id IS NULL AND n.resolved_at IS NULL) AS x_open_notes
      ${ITEM_FROM} JOIN review_entries x ON x.item_id = i.id JOIN revisions e ON e.id = x.revision_id WHERE x.review_id = ? ORDER BY x.role DESC, x.position`).all(id) as Row[])
      .map((r): ReviewEntry => ({
        revisionId: r.x_revision_id, itemId: r.id, role: r.x_role, position: r.x_position, decision: r.x_decision, decidedAt: r.x_decided_at, decidedBy: r.x_decided_by,
        revision: { id: r.x_revision_id, origin: r.e_origin, width: r.e_width, height: r.e_height, frames: r.e_frames, format: r.e_format, hasAlpha: !!r.e_has_alpha, opaque: !!r.e_opaque, createdAt: r.e_created_at },
        newerVersions: r.x_newer, openNotes: r.x_open_notes, item: itemSummary(r),
      }))
      .filter(entry => !filter.decision || entry.decision === filter.decision);
    const visible = new Set(entries.map(entry => entry.revisionId));
    // Replies follow their root, whichever version they name.
    const notes = threads(this.db.prepare(`SELECT n.* FROM notes n JOIN notes root ON root.id = COALESCE(n.parent_id, n.id) JOIN review_entries x ON x.revision_id = root.revision_id AND x.review_id = ?
      ORDER BY x.position, n.created_at`).all(id) as Row[])
      .filter(thread => visible.has(thread.revisionId) && (filter.includeResolved || !thread.resolvedAt));
    return { ...summary, entries, notes };
  }
  /** Each item's selected version, for reviewing items as they are now. */
  selectedRevisions(projectId: string, itemIds: string[]): string[] {
    const find = this.db.prepare('SELECT selected_revision_id FROM items WHERE id=? AND project_id=?');
    return itemIds.map(itemId => {
      const row = find.get(itemId, projectId) as Row | undefined;
      if (!row) throw new WorkbenchError('NOT_FOUND', `Item ${itemId} not found in project.`);
      return row.selected_revision_id as string;
    });
  }
  private editableReview(id: string, projectId: string, expectedRevision?: number): ReviewSummary {
    const summary = this.reviewSummary(id, projectId);
    if (summary.status === 'completed') throw new WorkbenchError('REVIEW_COMPLETED', 'The review is completed. Reopen it before changing it.');
    if (expectedRevision !== undefined && expectedRevision !== summary.revision) throw revisionConflict(summary.revision);
    return summary;
  }
  private touchReview(id: string) { this.db.prepare('UPDATE reviews SET revision = revision + 1, updated_at = ? WHERE id = ?').run(now(), id); }
  /**
   * Replaces the ordered entries: subjects (what the decision is about) and references (context only).
   * One entry per version; retained subjects keep their decisions, and a reference never has one.
   */
  private setEntries(projectId: string, reviewId: string, mode: ReviewMode, subjectIds: string[], referenceIds: string[]) {
    const subjects = [...new Set(subjectIds)], references = [...new Set(referenceIds)];
    if (!subjects.length) throw new WorkbenchError('INVALID_ARGUMENTS', 'A review needs at least one version to decide on.');
    if (mode === 'contest' && subjects.length < 2) throw new WorkbenchError('INVALID_ARGUMENTS', 'A contest needs at least two candidates.');
    if (references.some(revisionId => subjects.includes(revisionId))) throw new WorkbenchError('INVALID_ARGUMENTS', 'A version is either decided on or reference, not both.');
    const owner = this.db.prepare('SELECT r.item_id FROM revisions r JOIN items i ON i.id = r.item_id WHERE r.id=? AND i.project_id=?');
    const itemOf = (revisionId: string) => {
      const row = owner.get(revisionId, projectId) as Row | undefined;
      if (!row) throw new WorkbenchError('NOT_FOUND', `Version ${revisionId} not found in project.`);
      return row.item_id as string;
    };
    const rows = [...subjects.map(id => ({ id, role: 'subject' as const })), ...references.map(id => ({ id, role: 'reference' as const }))].map(entry => ({ ...entry, itemId: itemOf(entry.id) }));
    const keep = new Set(rows.map(row => row.id)), remove = this.db.prepare('DELETE FROM review_entries WHERE review_id=? AND revision_id=?');
    for (const row of this.db.prepare('SELECT revision_id FROM review_entries WHERE review_id=?').all(reviewId) as Row[]) if (!keep.has(row.revision_id)) remove.run(reviewId, row.revision_id);
    const upsert = this.db.prepare(`INSERT INTO review_entries (review_id,revision_id,item_id,position,role) VALUES (?,?,?,?,?)
      ON CONFLICT(review_id,revision_id) DO UPDATE SET position=excluded.position, role=excluded.role,
        decision=CASE WHEN excluded.role='reference' THEN 'unreviewed' ELSE decision END, decided_at=CASE WHEN excluded.role='reference' THEN NULL ELSE decided_at END, decided_by=CASE WHEN excluded.role='reference' THEN NULL ELSE decided_by END`);
    const next = { subject: 0, reference: 0 };
    for (const row of rows) upsert.run(reviewId, row.id, row.itemId, next[row.role]++, row.role);
  }
  private entryIds(reviewId: string, role: 'subject' | 'reference'): string[] {
    return (this.db.prepare('SELECT revision_id FROM review_entries WHERE review_id=? AND role=? ORDER BY position').all(reviewId, role) as Row[]).map(r => r.revision_id as string);
  }
  createReview(projectId: string, input: { title: string; instructions: string; mode?: ReviewMode; revisionIds: string[]; referenceRevisionIds?: string[] }, origin: Origin): ReviewSummary {
    const id = randomUUID(), at = now(), mode = input.mode ?? 'standard';
    this.tx(() => {
      this.db.prepare('INSERT INTO reviews (id,project_id,title,instructions,mode,status,revision,created_at,created_by,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?)').run(id, projectId, input.title.trim(), input.instructions.trim(), mode, 'active', 1, at, origin, at);
      this.setEntries(projectId, id, mode, input.revisionIds, input.referenceRevisionIds ?? []);
      this.appendActivity(projectId, origin, 'review.created', { reviewId: id }, { title: input.title.trim(), mode, entries: new Set(input.revisionIds).size, references: new Set(input.referenceRevisionIds ?? []).size });
    });
    return this.reviewSummary(id, projectId);
  }
  updateReview(id: string, projectId: string, expectedRevision: number, patch: ReviewPatch, origin: Origin): ReviewSummary {
    const current = this.editableReview(id, projectId, expectedRevision);
    this.tx(() => {
      const mode = patch.mode ?? current.mode;
      this.db.prepare('UPDATE reviews SET title=?, instructions=?, mode=? WHERE id=?').run(patch.title?.trim() ?? current.title, patch.instructions?.trim() ?? current.instructions, mode, id);
      if (patch.revisionIds || patch.addRevisionIds || patch.referenceRevisionIds || patch.addReferenceRevisionIds || mode !== current.mode) {
        const subjects = [...(patch.revisionIds ?? this.entryIds(id, 'subject')), ...(patch.addRevisionIds ?? [])];
        const references = [...(patch.referenceRevisionIds ?? this.entryIds(id, 'reference')), ...(patch.addReferenceRevisionIds ?? [])];
        this.setEntries(projectId, id, mode, subjects, references);
      }
      this.touchReview(id);
      this.appendActivity(projectId, origin, 'review.updated', { reviewId: id }, { fields: Object.keys(patch) });
    });
    return this.reviewSummary(id, projectId);
  }
  /**
   * Records decisions on review entries; each applies to that entry's exact version. Reference entries
   * take none. In a contest only one candidate is accepted: accepting another clears the earlier pick.
   */
  decide(id: string, projectId: string, decisions: { revisionId: string; decision: Decision }[], origin: Origin): ReviewSummary {
    const { mode } = this.editableReview(id, projectId);
    if (mode === 'contest' && decisions.filter(entry => entry.decision === 'accepted').length > 1) throw new WorkbenchError('INVALID_ARGUMENTS', 'A contest has one winner; accept a single candidate.');
    this.tx(() => {
      const entry = this.db.prepare('SELECT item_id, role FROM review_entries WHERE review_id=? AND revision_id=?');
      const update = this.db.prepare('UPDATE review_entries SET decision=?, decided_at=?, decided_by=? WHERE review_id=? AND revision_id=?');
      for (const { revisionId, decision } of decisions) {
        const row = entry.get(id, revisionId) as Row | undefined;
        if (!row) throw new WorkbenchError('NOT_FOUND', `Version ${revisionId} is not in this review.`);
        if (row.role === 'reference') throw new WorkbenchError('INVALID_ARGUMENTS', `Version ${revisionId} is reference material and takes no decision.`);
        if (mode === 'contest' && decision === 'accepted') {
          const previous = this.db.prepare("SELECT revision_id, item_id FROM review_entries WHERE review_id=? AND decision='accepted' AND revision_id<>?").all(id, revisionId) as Row[];
          for (const other of previous) {
            update.run('unreviewed', null, null, id, other.revision_id);
            this.appendActivity(projectId, origin, 'review.decided', { reviewId: id, itemId: other.item_id }, { decision: 'unreviewed', revisionId: other.revision_id, replacedBy: revisionId });
          }
        }
        const decided = decision !== 'unreviewed';
        update.run(decision, decided ? now() : null, decided ? origin : null, id, revisionId);
        this.appendActivity(projectId, origin, 'review.decided', { reviewId: id, itemId: row.item_id }, { decision, revisionId });
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
  /**
   * Adds a root note or a reply. A review note is on one of the review's versions (default: the
   * item's last entry there); otherwise the default is the selected version. Pins are clamped to its pixels.
   */
  addNote(projectId: string, input: NoteInput, origin: Origin): Note {
    const item = this.item(input.itemId, projectId);
    let reviewId = input.reviewId ?? null, parentId: string | null = null;
    const reviewed = reviewId && !input.revisionId
      ? (this.db.prepare('SELECT revision_id FROM review_entries WHERE review_id=? AND item_id=? ORDER BY position DESC LIMIT 1').get(reviewId, item.id) as Row | undefined)?.revision_id as string | undefined : undefined;
    let revisionId = input.revisionId ?? reviewed ?? item.selectedRevisionId;
    if (input.parentId) {
      const parent = this.noteRow(input.parentId, projectId);
      if (parent.item_id !== item.id) throw new WorkbenchError('INVALID_ARGUMENTS', 'A reply must be on the same item as its note.');
      if (input.pin || input.sketch) throw new WorkbenchError('INVALID_ARGUMENTS', 'Replies cannot carry pins or drawings. Add a new note for another region.');
      parentId = parent.parent_id ?? parent.id;
      reviewId = input.reviewId ?? parent.review_id;
      revisionId = input.revisionId ?? parent.revision_id;
    } else if (reviewId && this.reviewSummary(reviewId, projectId).status === 'completed') throw new WorkbenchError('REVIEW_COMPLETED', 'The review is completed. Reopen it before adding notes to it.');
    if (reviewId && !parentId && !this.db.prepare('SELECT 1 FROM review_entries x JOIN reviews v ON v.id = x.review_id WHERE x.review_id=? AND x.revision_id=? AND v.project_id=?').get(reviewId, revisionId, projectId))
      throw new WorkbenchError('INVALID_ARGUMENTS', 'That version is not in the review.');
    if (!item.revisions.some(entry => entry.id === revisionId)) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
    const size = this.revisionSize(revisionId);
    const pin = input.pin ? clampPin(input.pin, size.width, size.height) : null;
    const sketch = input.sketch ? clampSketch(input.sketch, size.width, size.height) : null;
    const id = randomUUID(), at = now();
    this.tx(() => {
      this.db.prepare('INSERT INTO notes (id,project_id,item_id,review_id,revision_id,parent_id,author,body,pin,sketch,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)')
        .run(id, projectId, item.id, reviewId, revisionId, parentId, origin, input.body.trim(), pin ? JSON.stringify(pin) : null, sketch ? JSON.stringify(sketch) : null, at, at);
      this.appendActivity(projectId, origin, 'note.added', { itemId: item.id, reviewId: reviewId ?? undefined }, { noteId: id, parentId, revisionId, pinned: !!pin, drawn: !!sketch });
    });
    return note(this.noteRow(id, projectId));
  }
  /** Agents may change only their own notes; anyone may resolve or reopen a thread. */
  updateNote(id: string, projectId: string, patch: NotePatch, origin: Origin): Note {
    const row = this.noteRow(id, projectId);
    if ((patch.body !== undefined || patch.pin !== undefined || patch.sketch !== undefined) && origin === 'agent' && row.author !== 'agent') throw new WorkbenchError('FORBIDDEN', "Agents cannot edit the user's notes. Reply instead.");
    if (row.parent_id && (patch.pin || patch.sketch || patch.resolved !== undefined)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Only root notes carry pins, drawings, and resolution.');
    const size = this.revisionSize(row.revision_id);
    let pin: string | null = row.pin, sketch: string | null = row.sketch;
    if (patch.pin !== undefined) pin = patch.pin ? JSON.stringify(clampPin(patch.pin, size.width, size.height)) : null;
    if (patch.sketch !== undefined) sketch = patch.sketch ? JSON.stringify(clampSketch(patch.sketch, size.width, size.height)) : null;
    const resolving = patch.resolved !== undefined && patch.resolved !== !!row.resolved_at;
    const kind = resolving ? (patch.resolved ? 'note.resolved' : 'note.reopened') : 'note.updated';
    this.tx(() => {
      this.db.prepare('UPDATE notes SET body=?, pin=?, sketch=?, resolved_at=?, resolved_by=?, updated_at=? WHERE id=?').run(patch.body?.trim() ?? row.body, pin, sketch,
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

  /** Records an export. Writing to the linked source makes the exported revision the synced one. */
  recordExport(projectId: string, itemId: string, revisionId: string, destination: string | null, origin: Origin) {
    this.tx(() => {
      if (!destination) this.db.prepare('UPDATE items SET synced_revision_id=?, missing=0, updated_at=? WHERE id=?').run(revisionId, now(), itemId);
      this.appendActivity(projectId, origin, 'item.exported', { itemId }, { revisionId, ...(destination ? { destination } : {}) });
    });
  }

  /** Admits a batch of image jobs. A repeated batch id returns the original jobs when the request matches. */
  createJobs(projectId: string, batchId: string, request: string, itemId: string | null, inputs: ImageJobInput[], origin: Origin): ImageJob[] {
    const existing = this.db.prepare('SELECT project_id, request FROM jobs WHERE batch_id=? LIMIT 1').get(batchId) as Row | undefined;
    if (existing) {
      if (existing.project_id !== projectId || existing.request !== request) throw new WorkbenchError('REQUEST_CONFLICT', 'That requestId was already used with different arguments.');
      return this.jobs(projectId, { batchId });
    }
    const at = now(), insert = this.db.prepare('INSERT INTO jobs (id,project_id,batch_id,position,status,input,request,item_id,created_by,created_at) VALUES (?,?,?,?,?,?,?,?,?,?)');
    this.tx(() => {
      inputs.forEach((input, position) => insert.run(randomUUID(), projectId, batchId, position, 'queued', JSON.stringify(input), request, itemId, origin, at));
      this.appendActivity(projectId, origin, 'images.requested', itemId ? { itemId } : {}, { batchId, kind: inputs[0]!.kind, model: inputs[0]!.model, count: inputs.length });
    });
    return this.jobs(projectId, { batchId });
  }
  jobs(projectId: string, filter: { batchId?: string; itemId?: string; jobIds?: string[]; active?: boolean; limit?: number }): ImageJob[] {
    const where = ['project_id = ?'], args: unknown[] = [projectId];
    if (filter.batchId) { where.push('batch_id = ?'); args.push(filter.batchId); }
    if (filter.itemId) { where.push('item_id = ?'); args.push(filter.itemId); }
    if (filter.jobIds) { where.push(`id IN (${filter.jobIds.map(() => '?').join(',')})`); args.push(...filter.jobIds); }
    if (filter.active) where.push(ACTIVE_JOB);
    return (this.db.prepare(`SELECT * FROM jobs WHERE ${where.join(' AND ')} ORDER BY created_at DESC, position LIMIT ?`).all(...args, filter.limit ?? 200) as Row[]).map(job);
  }
  job(id: string, projectId: string): ImageJob {
    const row = this.db.prepare('SELECT * FROM jobs WHERE id=? AND project_id=?').get(id, projectId) as Row | undefined;
    if (!row) throw new WorkbenchError('NOT_FOUND', 'Job not found in project.');
    return job(row);
  }
  hasActiveJobs(projectId?: string) { return !!this.db.prepare(`SELECT 1 FROM jobs WHERE ${ACTIVE_JOB}${projectId ? ' AND project_id = ?' : ''} LIMIT 1`).get(...(projectId ? [projectId] : [])); }
  /** Moves the oldest queued job to running. */
  claimJob(): ImageJob | null {
    const row = this.db.prepare("SELECT id FROM jobs WHERE status='queued' ORDER BY created_at, position LIMIT 1").get() as Row | undefined;
    if (!row) return null;
    this.tx(() => this.db.prepare("UPDATE jobs SET status='running', started_at=? WHERE id=?").run(now(), row.id));
    return job(this.db.prepare('SELECT * FROM jobs WHERE id=?').get(row.id) as Row);
  }
  finishJob(id: string, patch: { status: 'completed' | 'failed' | 'uncertain'; itemId?: string; revisionId?: string; error?: string; warning?: string }) {
    const current = this.db.prepare('SELECT project_id, batch_id, item_id, created_by FROM jobs WHERE id=?').get(id) as Row | undefined;
    if (!current) return;
    const itemId = patch.itemId ?? current.item_id;
    this.tx(() => {
      this.db.prepare('UPDATE jobs SET status=?, item_id=?, revision_id=?, error=?, warning=?, finished_at=? WHERE id=?')
        .run(patch.status, itemId, patch.revisionId ?? null, patch.error ?? null, patch.warning ?? null, now(), id);
      this.appendActivity(current.project_id, current.created_by, `job.${patch.status}`, itemId ? { itemId } : {}, { jobId: id, batchId: current.batch_id, ...(patch.revisionId ? { revisionId: patch.revisionId } : {}), ...(patch.error ? { error: patch.error } : {}) });
    });
  }
  cancelJob(id: string, projectId: string, origin: Origin): ImageJob {
    const current = this.job(id, projectId);
    if (current.status !== 'queued') throw new WorkbenchError('INVALID_STATE', current.status === 'running' ? 'The request is already with the provider and may be billed; it cannot be cancelled.' : `The job is already ${current.status}.`);
    this.tx(() => {
      this.db.prepare("UPDATE jobs SET status='cancelled', finished_at=? WHERE id=?").run(now(), id);
      this.appendActivity(projectId, origin, 'job.cancelled', current.itemId ? { itemId: current.itemId } : {}, { jobId: id, batchId: current.batchId });
    });
    return this.job(id, projectId);
  }
  /** Startup recovery: requests in flight when PixelChat stopped are recorded as uncertain, never replayed. */
  interruptJobs() {
    this.tx(() => this.db.prepare("UPDATE jobs SET status='uncertain', error=?, finished_at=? WHERE status='running'").run('PixelChat stopped during the provider request. It may have been billed and was not replayed.', now()));
  }

  /** Revision file and format, scoped to a project, for media serving. */
  revisionFile(id: string, projectId: string): { file: string; format: Revision['format']; width: number; height: number } | null {
    const row = this.db.prepare('SELECT r.file, r.format, r.width, r.height FROM revisions r JOIN items i ON i.id = r.item_id WHERE r.id=? AND i.project_id=?').get(id, projectId) as Row | undefined;
    return row ? { file: row.file, format: row.format, width: row.width, height: row.height } : null;
  }
}
