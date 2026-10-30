import { randomUUID } from 'node:crypto';
import { readdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import { basename, dirname, extname, isAbsolute, join, posix, resolve } from 'node:path';
import { WorkbenchError } from '../shared/workbench';
import type { Origin, Revision, ScanProgress, ScanReport, Source } from '../shared/types';
import { IMAGE_EXTENSIONS, sha256, type MediaStore } from './media';
import { now, type Store } from './store';

const SCAN_CONCURRENCY = 4;
const FORMAT_EXTENSIONS: Record<Revision['format'], string[]> = { png: ['.png'], webp: ['.webp'], jpeg: ['.jpg', '.jpeg'] };

/** Glob over forward-slash relative paths: `**`, `*`, `?`, and `{a,b}`; case-insensitive like Windows paths. */
export function globMatcher(patterns: string[]) {
  const expressions = patterns.map(pattern => {
    let source = '', depth = 0;
    for (let index = 0; index < pattern.length; index++) {
      const char = pattern[index]!;
      if (char === '*' && pattern[index + 1] === '*') { index++; if (pattern[index + 1] === '/') { index++; source += '(?:.*/)?'; } else source += '.*'; }
      else if (char === '*') source += '[^/]*';
      else if (char === '?') source += '[^/]';
      else if (char === '{') { depth++; source += '(?:'; }
      else if (char === '}' && depth) { depth--; source += ')'; }
      else if (char === ',' && depth) source += '|';
      else source += char.replace(/[.+^$()|[\]\\]/g, '\\$&');
    }
    return new RegExp(`^${source}$`, 'i');
  });
  return (path: string) => expressions.some(expression => expression.test(path));
}

/** Snapshots artwork from linked folders and loose files into immutable revisions. */
export class Library {
  private scans = new Map<string, { progress: ScanProgress; promise: Promise<ScanReport> }>();
  private stopped = false;
  constructor(private store: Store, private media: MediaStore) {}
  async close() { this.stopped = true; await Promise.allSettled([...this.scans.values()].map(scan => scan.promise)); }
  scanning = (sourceId: string) => this.scans.get(sourceId)?.progress ?? null;
  sources(projectId: string) { return this.store.sources(projectId, this.scanning); }
  source(projectId: string, sourceId: string) { return this.store.source(sourceId, projectId, this.scanning); }

  async addSource(projectId: string, root: string, include: string[], exclude: string[], origin: Origin): Promise<Source> {
    if (!isAbsolute(root)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Source folder must be an absolute path.');
    const folder = resolve(root);
    if (!(await stat(folder).catch(() => null))?.isDirectory()) throw new WorkbenchError('NOT_FOUND', 'Source folder does not exist.');
    const id = this.store.createSource(projectId, folder, include, exclude, origin);
    this.startScan(projectId, id, origin);
    return this.source(projectId, id);
  }
  /** Starts a rescan unless one is running; new and changed files become revisions. */
  rescan(projectId: string, sourceId: string, origin: Origin): Source {
    this.source(projectId, sourceId);
    if (!this.scans.has(sourceId)) this.startScan(projectId, sourceId, origin);
    return this.source(projectId, sourceId);
  }
  async waitForScan(projectId: string, sourceId: string, timeoutMs: number): Promise<Source> {
    const scan = this.scans.get(sourceId);
    if (scan) await Promise.race([scan.promise.catch(() => undefined), new Promise(resolve => setTimeout(resolve, timeoutMs))]);
    return this.source(projectId, sourceId);
  }
  async removeSource(projectId: string, sourceId: string, origin: Origin) {
    this.source(projectId, sourceId);
    if (this.scans.has(sourceId)) throw new WorkbenchError('BUSY', 'The source is scanning. Remove it after the scan finishes.');
    await this.media.remove(this.store.deleteSource(projectId, sourceId, origin));
  }

  /**
   * Imports loose files as unlinked items, which have no source to export back to, or as new
   * versions in one item's lineage, each derived from the version selected at import.
   */
  async importFiles(projectId: string, paths: string[], target: { collection: string; itemId?: string }, origin: Origin) {
    const lineage = target.itemId ? this.store.item(target.itemId, projectId) : null;
    const created: string[] = [], revisions: string[] = [], failed: { path: string; error: string }[] = [];
    for (const path of paths) {
      try {
        if (!isAbsolute(path)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Paths must be absolute.');
        if (!IMAGE_EXTENSIONS.has(extname(path).toLowerCase())) throw new WorkbenchError('UNSUPPORTED_IMAGE', 'Only PNG, WebP, and JPEG files are supported.');
        const facts = await this.media.ingest(await readFile(path));
        const revision = { ...facts, origin: 'imported' as const, provenance: { importedFrom: resolve(path) } };
        if (lineage) { this.store.addRevision(projectId, lineage.id, { ...revision, parentId: lineage.selectedRevisionId }, false, origin); revisions.push(facts.id); }
        else created.push(this.store.createItem(projectId, { sourceId: null, sourcePath: null, name: basename(path, extname(path)), collection: target.collection, revision }, origin));
      } catch (error) { failed.push({ path, error: error instanceof Error ? error.message : 'Import failed.' }); }
    }
    return lineage ? { itemId: lineage.id, revisionIds: revisions, failed } : { itemIds: created, failed };
  }

  /**
   * Writes the selected revision to its linked source file, or to `destination` for any item.
   * The source file must still match the synced revision unless `force`; otherwise SOURCE_CHANGED.
   */
  async exportItem(projectId: string, itemId: string, options: { destination?: string; force?: boolean }, origin: Origin) {
    const item = this.store.item(itemId, projectId);
    const selected = item.revisions.find(entry => entry.id === item.selectedRevisionId)!;
    const target = options.destination ?? item.sourceFile;
    if (!target) throw new WorkbenchError('INVALID_ARGUMENTS', 'This item has no linked source file. Pass a destination path.');
    if (!isAbsolute(target)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Destination must be an absolute path.');
    if (!FORMAT_EXTENSIONS[selected.format].includes(extname(target).toLowerCase()))
      throw new WorkbenchError('FORMAT_MISMATCH', `The selected version is ${selected.format.toUpperCase()}, but ${basename(target)} expects ${extname(target) || 'no extension'}. Make a version in that format first.`);
    if (item.sourceId && this.scans.has(item.sourceId)) throw new WorkbenchError('BUSY', 'The source is scanning. Export after the scan finishes.');
    const current = await readFile(target).catch((error: NodeJS.ErrnoException) => { if (error.code === 'ENOENT') return null; throw error; });
    if (options.destination) {
      if (current && !options.force) throw new WorkbenchError('DESTINATION_EXISTS', 'A file already exists at the destination. Pass force to overwrite it.');
    } else {
      if (!item.pendingExport && !options.force) return { path: target, revisionId: selected.id, written: false };
      const synced = item.revisions.find(entry => entry.id === item.syncedRevisionId);
      if (!options.force && (!current || sha256(current) !== synced?.sha256))
        throw new WorkbenchError('SOURCE_CHANGED', current ? 'The source file changed since PixelChat last read it. Rescan to bring the change in, or force to overwrite it.' : 'The source file is missing. Rescan, or force to write it again.', { state: current ? 'changed' : 'missing', path: target });
    }
    const bytes = await this.media.read(this.store.revisionFile(selected.id, projectId)!.file);
    const temp = join(dirname(target), `.${basename(target)}.${randomUUID()}.partial`);
    try { await writeFile(temp, bytes, { flag: 'wx' }); await rename(temp, target); }
    finally { await rm(temp, { force: true }).catch(() => undefined); }
    this.store.recordExport(projectId, itemId, selected.id, options.destination ?? null, origin);
    return { path: target, revisionId: selected.id, written: true };
  }

  private startScan(projectId: string, sourceId: string, origin: Origin) {
    const progress: ScanProgress = { done: 0, total: 0, startedAt: now() };
    const promise = this.scan(projectId, sourceId, progress, origin).finally(() => { this.scans.delete(sourceId); this.store.changed(); });
    promise.catch(() => undefined);
    this.scans.set(sourceId, { progress, promise });
  }
  private async scan(projectId: string, sourceId: string, progress: ScanProgress, origin: Origin): Promise<ScanReport> {
    const source = this.store.source(sourceId, projectId);
    const included = globMatcher(source.include.length ? source.include : ['**']), excluded = globMatcher(source.exclude);
    const files = (await walk(source.root)).filter(path => IMAGE_EXTENSIONS.has(posix.extname(path).toLowerCase()) && included(path) && !excluded(path));
    progress.total = files.length; this.store.changed();
    const report = { startedAt: progress.startedAt, added: 0, changed: 0, unchanged: 0, failed: [] as ScanReport['failed'] };
    const seen = new Set<string>();
    let next = 0;
    const worker = async () => {
      while (!this.stopped && next < files.length) {
        const path = files[next++]!;
        try {
          seen.add(path);
          const bytes = await readFile(join(source.root, path));
          const existing = this.store.sourceItem(sourceId, path);
          if (existing && existing.syncedSha === sha256(bytes)) report.unchanged++;
          else {
            const facts = await this.media.ingest(bytes);
            const provenance = { importedFrom: join(source.root, path) };
            if (existing) { this.store.addRevision(projectId, existing.id, { ...facts, origin: 'imported', parentId: existing.syncedRevisionId, provenance }, true, origin); report.changed++; }
            else { this.store.createItem(projectId, { sourceId, sourcePath: path, name: posix.basename(path, posix.extname(path)), collection: posix.dirname(path) === '.' ? '' : posix.dirname(path), revision: { ...facts, origin: 'imported', provenance } }, origin); report.added++; }
          }
        } catch (error) { report.failed.push({ path, error: error instanceof Error ? error.message : 'Import failed.' }); }
        progress.done++;
        if (progress.done % 25 === 0) this.store.changed();
      }
    };
    await Promise.all(Array.from({ length: SCAN_CONCURRENCY }, worker));
    if (this.stopped) throw new WorkbenchError('CLOSING', 'PixelChat closed during the scan.');
    return this.store.finishScan(projectId, sourceId, seen, { ...report, finishedAt: now() }, origin);
  }
}

/** Relative forward-slash file paths under a root, skipping dot-folders such as `.godot`. */
async function walk(root: string, prefix = ''): Promise<string[]> {
  const entries = await readdir(join(root, prefix), { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const path = prefix ? `${prefix}/${entry.name}` : entry.name;
    if (entry.isDirectory()) return entry.name.startsWith('.') ? [] : walk(root, path);
    return entry.isFile() ? [path] : [];
  }));
  return nested.flat().sort();
}
