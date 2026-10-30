import { connect } from 'node:net';
import { readFile, access } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { McpServer, ResourceTemplate } from '@modelcontextprotocol/server';
import { setTimeout as delay } from 'node:timers/promises';
import { serveStdio } from '@modelcontextprotocol/server/stdio';
import { z } from 'zod';
import { native } from '../shared/native';
import { installIntegration, skillText } from '../shared/install';
import { operationCatalog } from '../shared/operation-catalog';
import { WorkbenchError, type OperationResponse } from '../shared/workbench';
import type { ActivityEntry, ItemSearchResult, ReviewSummary, ReviewWait } from '../shared/types';

const helper = join(__dirname, 'pixelchat-helper.exe');
const root = join(process.env.APPDATA || '', 'PixelChat');
async function discovery() {
  const bytes = await native(helper, ['unprotect'], await readFile(join(root, 'bridge', 'endpoint.bin')));
  const value = JSON.parse(bytes.toString());
  if (value.version !== 1 || !/^\\\\\.\\pipe\\pixelchat-[a-f0-9]{64}$/.test(value.pipe) || !/^[a-f0-9]{64}$/.test(value.token) || !Number.isInteger(value.pid)) throw new Error('Invalid bridge discovery.');
  return value as { pipe: string; token: string; pid: number };
}
async function startApp() {
  const packaged = resolve(__dirname, '../../PixelChat.exe');
  try { await access(packaged); } catch {
    const repository = resolve(__dirname, '../..'), electron = join(repository, 'node_modules/electron/dist/electron.exe');
    try { await access(electron); } catch { throw new WorkbenchError('APP_NOT_FOUND', 'Install the PixelChat desktop app with its bundled CLI.'); }
    // Development only: a caller inside a packaged app's container starts this Electron there too.
    return launch(electron, [repository]);
  }
  // Callers may run inside a packaged app's container (Claude Desktop is MSIX), where a child's %APPDATA% is
  // redirected to the caller's private storage. The shell starts PixelChat outside it, on the real profile.
  return launch(join(process.env.SystemRoot || 'C:\\Windows', 'explorer.exe'), [packaged]);
}
async function launch(command: string, args: string[]) {
  const child = spawn(command, args, { detached: true, stdio: 'ignore', windowsHide: true });
  await new Promise<void>((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); }); child.unref();
}
function alive(pid: number) { try { process.kill(pid, 0); return true; } catch (e) { return (e as NodeJS.ErrnoException).code === 'EPERM'; } }
// A paused or hung app accepts the pipe but never answers; probe briefly instead of the full request timeout.
const probe = (e: { pipe: string; token: string }) => exchange(e, 'status', {}, AbortSignal.timeout(5000));
async function endpoint() {
  let found: Awaited<ReturnType<typeof discovery>> | undefined;
  try { found = await discovery(); await probe(found); return found; } catch { /* absent, stale, or not answering */ }
  // Launching would only hand off to the app that published discovery, which is still running.
  if (found && alive(found.pid)) throw new WorkbenchError('APP_UNAVAILABLE', 'PixelChat is open but its agent bridge is not answering. If it is paused in a debugger, resume it; otherwise restart PixelChat and check its diagnostic logs.');
  await startApp();
  const until = Date.now() + 30000;
  while (Date.now() < until) {
    try { const e = await discovery(); await probe(e); return e; } catch { await delay(250); }
  }
  throw new WorkbenchError('APP_UNAVAILABLE', 'PixelChat did not start its agent bridge within 30 seconds. Open PixelChat and check its diagnostic logs.');
}
// Long enough for the longest operation wait (review_wait up to 1800 s) plus the request itself.
const REQUEST_TIMEOUT = 1860000;
function exchange(e: { pipe: string; token: string }, operation: string, args: unknown, signal?: AbortSignal): Promise<OperationResponse> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) { reject(signal.reason); return; }
    const socket = connect(e.pipe); let buffer = '';
    const abort = () => { socket.destroy(); reject(signal?.reason); };
    signal?.addEventListener('abort', abort, { once: true });
    socket.once('close', () => signal?.removeEventListener('abort', abort));
    socket.setEncoding('utf8'); socket.setTimeout(REQUEST_TIMEOUT, () => { socket.destroy(); reject(new WorkbenchError('TIMEOUT', 'Request timed out. Inspect existing jobs before resubmitting.')); });
    socket.on('connect', () => socket.write(JSON.stringify({ token: e.token, operation, args }) + '\n'));
    socket.on('error', reject);
    socket.on('data', data => { buffer += data; if (Buffer.byteLength(buffer) > 64 * 1024 * 1024) { socket.destroy(); reject(new Error('Bridge response exceeds 64 MiB.')); } });
    socket.on('end', () => { try { const response = JSON.parse(buffer); resolve(response); } catch { reject(new WorkbenchError('CONNECTION_LOST', 'Connection closed without a result. Do not repeat paid work with a new request ID.')); } });
  });
}
async function call(operation: string, args: unknown = {}) { return exchange(await endpoint(), operation, args); }
async function result<T>(operation: string, args: unknown, signal?: AbortSignal) {
  // Discovery only: background observation never launches the desktop.
  const response = signal ? await exchange(await discovery(), operation, args, signal) : await call(operation, args);
  if (!response.ok) throw new WorkbenchError(response.error?.code ?? 'OPERATION_FAILED', response.error?.message ?? 'Operation failed.');
  return response.result as T;
}
function argsFrom(argv: string[]) {
  const positional: string[] = [], flags: Record<string, string[]> = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith('--')) { positional.push(argv[i]); continue; }
    const key = argv[i].slice(2); const value = argv[i+1] && !argv[i+1].startsWith('--') ? argv[++i] : 'true';
    (flags[key] ??= []).push(value);
  }
  return { positional, flags };
}

const reviewUri = (r: { projectId: string; id: string }) => `pixelchat://projects/${r.projectId}/reviews/${r.id}`;
const pendingUri = (projectId: string) => `pixelchat://projects/${projectId}/pending-exports`;
const pendingExports = (projectId: string, signal?: AbortSignal) => result<ItemSearchResult>('search_items', { projectId, status: 'pending_export', limit: 2000 }, signal);
// What an agent acts on: which items await export, and at which selected version.
const pendingKey = (found: ItemSearchResult) => found.items.map(item => `${item.id}:${item.selectedRevisionId}`).sort().join(',');

function serveMcp(catalog: { name: string; description: string; inputSchema: Record<string, unknown> }[]) {
  serveStdio(({ era }) => {
    const server = new McpServer({ name: 'pixelchat', version: '0.3.0' }, { capabilities: { resources: { subscribe: true } } });
    const watches = new Map<string, AbortController>(), subscribed = new Set<string>();
    let closed = false;
    const notify = async (uri: string) => { if (era === 'modern' || subscribed.has(uri)) await server.server.sendResourceUpdated({ uri }); };
    // Each watch long-polls one durable operation; failures back off and retry until the connection closes.
    const watch = (uri: string, poll: (signal: AbortSignal) => Promise<'changed' | 'same' | 'gone'>) => {
      if (closed || watches.has(uri)) return;
      // Bound idle connection state; durable get/wait operations remain available for everything.
      if (watches.size >= 128) { const oldest = watches.keys().next().value!; watches.get(oldest)!.abort(); watches.delete(oldest); subscribed.delete(oldest); }
      const controller = new AbortController(); watches.set(uri, controller);
      void (async () => {
        let backoff = 1000;
        while (!controller.signal.aborted) {
          try {
            const outcome = await poll(controller.signal);
            if (outcome !== 'same') await notify(uri);
            if (outcome === 'gone') { watches.delete(uri); subscribed.delete(uri); break; }
            backoff = 1000;
          } catch {
            if (controller.signal.aborted) break;
            await delay(backoff, undefined, { signal: controller.signal }).catch(() => {});
            backoff = Math.min(backoff * 2, 30000);
          }
        }
      })();
    };
    const watchReview = (initial: ReviewSummary) => {
      let cursor: number | undefined;
      watch(reviewUri(initial), async signal => {
        try {
          const page = await result<ReviewWait>('review_wait', { projectId: initial.projectId, reviewId: initial.id, from: 'any', wait: 30, ...(cursor === undefined ? {} : { afterSeq: cursor }) }, signal);
          cursor = page.cursor; return page.events.length ? 'changed' : 'same';
        } catch (error) { if (error instanceof WorkbenchError && error.code === 'NOT_FOUND') return 'gone'; throw error; }
      });
    };
    const watchPending = async (projectId: string, initial: ItemSearchResult) => {
      let key = pendingKey(initial), cursor = 0;
      // Start after the newest change so the first wait sees only new work.
      for (;;) { const page = await result<{ entries: ActivityEntry[]; cursor: number }>('activity_get', { projectId, afterSeq: cursor, limit: 500 }); if (!page.entries.length) break; cursor = page.cursor; }
      watch(pendingUri(projectId), async signal => {
        try {
          const page = await result<{ entries: ActivityEntry[]; cursor: number }>('activity_get', { projectId, afterSeq: cursor, limit: 500, wait: 30 }, signal);
          if (!page.entries.length) return 'same';
          cursor = page.cursor;
          const next = pendingKey(await pendingExports(projectId, signal));
          if (next === key) return 'same';
          key = next; return 'changed';
        } catch (error) { if (error instanceof WorkbenchError && error.code === 'NOT_FOUND') return 'gone'; throw error; }
      });
    };
    const readReview = async (uri: string) => {
      const match = /^pixelchat:\/\/projects\/([^/]+)\/reviews\/([^/]+)$/.exec(uri);
      if (!match) throw new Error('Invalid review resource URI.');
      const review = await result<ReviewSummary>('review_get', { projectId: match[1], reviewId: match[2] });
      watchReview(review); return review;
    };
    const readPending = async (uri: string) => {
      const match = /^pixelchat:\/\/projects\/([^/]+)\/pending-exports$/.exec(uri);
      if (!match) throw new Error('Invalid pending-exports resource URI.');
      const found = await pendingExports(match[1]);
      if (!watches.has(uri)) await watchPending(match[1], found);
      return found;
    };
    const read = (uri: string) => uri.endsWith('/pending-exports') ? readPending(uri) : readReview(uri);
    server.server.onclose = () => { closed = true; for (const controller of watches.values()) controller.abort(); watches.clear(); subscribed.clear(); };
    if (era === 'legacy') {
      server.server.setRequestHandler('resources/subscribe', async request => { await read(request.params.uri); subscribed.add(request.params.uri); return {}; });
      server.server.setRequestHandler('resources/unsubscribe', async request => { subscribed.delete(request.params.uri); return {}; });
    }
    server.registerResource('review', new ResourceTemplate('pixelchat://projects/{projectId}/reviews/{reviewId}', { list: undefined }), { mimeType: 'application/json', description: 'Review entries, decisions, and note threads with pins. Subscribe for saved changes; review_wait works without notifications.' }, async uri => ({ contents: [{ uri: uri.href, mimeType: 'application/json', text: JSON.stringify(await readReview(uri.href)) }] }));
    server.registerResource('pending-exports', new ResourceTemplate('pixelchat://projects/{projectId}/pending-exports', { list: undefined }), { mimeType: 'application/json', description: 'Items whose selected version differs from their source file, awaiting export_item. Subscribe to hear when the user selects a new version or an export lands; activity_get with wait works without notifications.' }, async uri => ({ contents: [{ uri: uri.href, mimeType: 'application/json', text: JSON.stringify(await readPending(uri.href)) }] }));
    server.registerResource('guidance', 'pixelchat://guidance', { mimeType: 'application/json', description: 'Full current workflow, library, and image-editing guidance.' }, async () => ({ contents: [{ uri: 'pixelchat://guidance', mimeType: 'application/json', text: JSON.stringify(await call('guidance')) }] }));
    for (const item of catalog) server.registerTool(item.name, { description: item.description, inputSchema: z.fromJSONSchema(item.inputSchema) as z.ZodObject<any> }, async (args: unknown) => {
      // The app is reached (or started) per call, so an unreachable app is a tool error, not a failed connection.
      const response = await call(item.name, args).catch((error): OperationResponse => ({ ok: false, error: { code: error instanceof WorkbenchError ? error.code : 'APP_UNAVAILABLE', message: error instanceof Error ? error.message : 'PixelChat could not be reached.' } }));
      if (!response.ok) return { isError: true, content: [{ type: 'text' as const, text: JSON.stringify(response.error) }] };
      const value = response.result as any;
      const review = item.name.startsWith('review_') && value?.id && value?.projectId && typeof value?.revision === 'number' ? value as ReviewSummary : null;
      if (review) watchReview(review);
      const content: any[] = [];
      if (value?.media) content.push(value.media);
      if (review) content.push({ type: 'resource_link', uri: reviewUri(review), name: review.title, mimeType: 'application/json', description: `Review ${review.status}; subscribe for saved changes.` });
      const lean = value?.media ? { ...value, media: { type: value.media.type, mimeType: value.media.mimeType } } : value;
      content.unshift({ type: 'text', text: JSON.stringify(lean) });
      return { content, structuredContent: { result: lean } };
    });
    return server;
  });
}

/**
 * Streams a review's events as JSON lines until it is completed or deleted, so an agent can hold one
 * connection and respond as the user reviews. Survives app restarts by resuming from its cursor.
 */
async function watchReview(projectId: string, reviewId: string, from: string, once: boolean) {
  const line = (value: unknown) => process.stdout.write(JSON.stringify(value) + '\n');
  let cursor: number | undefined, backoff = 1000;
  for (;;) {
    // The first call returns at once so the watch reports its start (and an already completed review) immediately.
    let page: ReviewWait;
    try { page = await result<ReviewWait>('review_wait', { projectId, reviewId, from, ...(cursor === undefined ? { wait: 0 } : { wait: 300, afterSeq: cursor }) }); backoff = 1000; }
    catch (error) {
      if (error instanceof WorkbenchError && ['NOT_FOUND', 'INVALID_ARGUMENTS'].includes(error.code)) throw error;
      line({ kind: 'watch.retrying', error: error instanceof Error ? error.message : 'PixelChat could not be reached.' });
      await delay(backoff); backoff = Math.min(backoff * 2, 30000); continue;
    }
    if (cursor === undefined) line({ kind: 'watch.started', review: page.review, cursor: page.cursor });
    cursor = page.cursor;
    for (const event of page.events) line(event);
    if (page.review.status === 'completed') { line({ kind: 'watch.ended', reason: 'completed', review: page.review }); return; }
    if (once && page.events.length) { line({ kind: 'watch.ended', reason: 'once', review: page.review, cursor }); return; }
  }
}

async function main() {
  const { positional, flags } = argsFrom(process.argv.slice(2)); const [command, name] = positional;
  const installation = ['skill', 'install', 'uninstall'].includes(command);
  const allowed = new Set(installation ? ['scope', 'target', 'project', 'directory', 'mcp', 'dry-run', 'force', 'help'] : command === 'watch-review' ? ['from', 'once', 'help'] : ['input', 'json', 'help']);
  for (const [key, values] of Object.entries(flags)) {
    if (!allowed.has(key) || (key !== 'target' && values.length !== 1)) throw new WorkbenchError('INVALID_ARGUMENTS', `Unknown or repeated option: --${key}`);
    if (['mcp', 'dry-run', 'force', 'help', 'once'].includes(key) ? values[0] !== 'true' : values.some(v => v === 'true')) throw new WorkbenchError('INVALID_ARGUMENTS', `Invalid value for --${key}`);
  }
  if (positional.length > (['call', 'skill'].includes(command) ? 2 : command === 'watch-review' ? 3 : 1)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Unexpected positional arguments.');
  if (command === 'watch-review' && !flags.help) {
    if (positional.length !== 3) throw new WorkbenchError('INVALID_ARGUMENTS', 'Usage: pixelchat watch-review <projectId> <reviewId> [--from user|any] [--once]');
    const from = flags.from?.[0] ?? 'user';
    if (!['user', 'any'].includes(from)) throw new WorkbenchError('INVALID_ARGUMENTS', 'Invalid value for --from');
    await watchReview(positional[1], positional[2], from, !!flags.once); return;
  }
  if (command === 'mcp') { serveMcp(operationCatalog()); return; }
  if (command === 'tools') { output({ ok: true, result: operationCatalog() }); return; }
  if (command === 'skill' && !name) { process.stdout.write(skillText); return; }
  if (command === 'skill' || command === 'install' || command === 'uninstall') {
    const action = (command === 'skill' ? name : command) as 'install' | 'uninstall' | 'status';
    const installed = await installIntegration({ action, scope: flags.scope?.[0] as 'project' | 'user', targets: flags.target as ('codex' | 'claude')[] ?? [], projectDirectory: flags.project?.[0] ? resolve(flags.project[0]) : undefined, directory: flags.directory?.[0] ? resolve(flags.directory[0]) : undefined, skill: true, mcp: !!flags.mcp, dryRun: !!flags['dry-run'], force: !!flags.force }, process.execPath, join(__dirname, 'cli.cjs'));
    output({ ok: true, result: installed }); return;
  }
  if (!command || command === 'help' || flags.help) {
    output({ ok: true, result: { commands: ['status', 'tools', 'guidance', 'call <operation> --input <file.json> | --json <JSON>', 'watch-review <projectId> <reviewId> [--from user|any] [--once]', 'mcp', 'skill', 'skill install|uninstall|status --scope project|user --target codex|claude [--project <absolute-directory>] [--directory <skills-parent>] [--mcp] [--dry-run] [--force]'], note: 'Operations take project IDs; image jobs return durable IDs. The app starts on demand. No provider credentials are accepted by this CLI.' } }); return;
  }
  if (command === 'call' && !name) throw new WorkbenchError('INVALID_ARGUMENTS', 'Specify an operation; run pixelchat tools for schemas.');
  if (flags.input && flags.json) throw new WorkbenchError('INVALID_ARGUMENTS', 'Use either --input or --json.');
  const raw = flags.input ? await readFile(resolve(flags.input[0]), 'utf8') : flags.json?.[0] ?? '{}';
  output(await call(command === 'call' ? name : command, JSON.parse(raw)));
}
function output(response: OperationResponse) { process.stdout.write(JSON.stringify(response) + '\n'); if (!response.ok) process.exitCode = 1; }
main().catch(error => { const response: OperationResponse = { ok: false, error: { code: error instanceof WorkbenchError ? error.code : 'CLI_FAILED', message: error instanceof Error ? error.message : 'CLI failed.' } }; if (process.argv[2] === 'mcp') { process.stderr.write(JSON.stringify(response) + '\n'); process.exitCode = 1; } else output(response); });
