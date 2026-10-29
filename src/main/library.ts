import { readdir, readFile, stat } from 'node:fs/promises';
import { basename, extname, isAbsolute, join, posix, resolve } from 'node:path';
import { WorkbenchError } from '../shared/workbench';
import type { Origin, ScanProgress, ScanReport, Source } from '../shared/types';
import { IMAGE_EXTENSIONS, sha256, type MediaStore } from './media';
import { now, type Store } from './store';

const SCAN_CONCURRENCY = 4;

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

  /** Imports loose files as unlinked items; they have no source to export back to. */
  async importFiles(projectId: string, paths: string[], collection: string, origin: Origin) {
    const created: string[] = [], failed: { path: string; error: string }[] = [];
    for (const path of paths) {
      try {
        if (!isAbsolute(path)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Paths must be absolute.');
        if (!IMAGE_EXTENSIONS.has(extname(path).toLowerCase())) throw new WorkbenchError('UNSUPPORTED_IMAGE', 'Only PNG, WebP, and JPEG files are supported.');
        const facts = await this.media.ingest(await readFile(path));
        created.push(this.store.createItem(projectId, { sourceId: null, sourcePath: null, name: basename(path, extname(path)), collection, revision: { ...facts, origin: 'imported', provenance: { importedFrom: resolve(path) } } }, origin));
      } catch (error) { failed.push({ path, error: error instanceof Error ? error.message : 'Import failed.' }); }
    }
    return { itemIds: created, failed };
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
