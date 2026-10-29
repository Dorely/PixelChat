import { createServer, type ServerResponse } from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, sep, extname } from 'node:path';
import type { UiCommand } from '../shared/workbench';
import { operationError } from './operations';

/** Development UI transport only. All operations still execute in Electron main. */
export async function startBrowserHost(options: {
  directory: string; invoke(method: unknown, args: unknown): Promise<unknown>;
  media(request: Request): Promise<Response>; disconnected(): void; shutdown(): void;
}) {
  const clients = new Set<ServerResponse>();
  const directory = resolve(options.directory);
  let origin = '', closing = false;
  const json = (response: ServerResponse, value: unknown, status = 200) => {
    response.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' });
    response.end(JSON.stringify(value, (_key, v) => v instanceof Uint8Array ? { $bytes: Buffer.from(v).toString('base64') } : v));
  };
  const server = createServer((request, response) => {
    response.setHeader('x-frame-options', 'DENY');
    response.setHeader('content-security-policy', "frame-ancestors 'none'");
    void (async () => {
      // Exact host prevents DNS rebinding. Privileged requests require this origin
      // and non-simple JSON POST; no CORS is granted to another web origin.
      if (closing || request.headers.host !== new URL(origin).host) { response.writeHead(403); response.end(); return; }
      const url = new URL(request.url ?? '/', origin);
      if (url.pathname === '/shutdown') {
        if (request.method !== 'POST' || request.headers.origin !== origin || request.headers['content-type'] !== 'application/json') { response.writeHead(403); response.end(); return; }
        json(response, { ok: true }); setImmediate(options.shutdown); return;
      }
      if (url.pathname === '/rpc') {
        if (request.method !== 'POST' || request.headers.origin !== origin || request.headers['content-type'] !== 'application/json') { response.writeHead(403); response.end(); return; }
        let size = 0; const chunks: Buffer[] = [];
        for await (const chunk of request) {
          size += chunk.length;
          if (size > 2 * 1024 * 1024) { json(response, { ok: false, error: { message: 'Request exceeds 2 MiB.' } }, 413); return; }
          chunks.push(Buffer.from(chunk));
        }
        const input = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        const result = await options.invoke(input.method, input.args);
        json(response, { ok: true, result }); return;
      }
      if (url.pathname === '/events') {
        if (request.method !== 'GET' || request.headers['sec-fetch-site'] !== 'same-origin') { response.writeHead(403); response.end(); return; }
        response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' });
        response.write(': connected\n\n'); clients.add(response);
        request.on('close', () => { clients.delete(response); if (!clients.size) options.disconnected(); }); return;
      }
      if (url.pathname.startsWith('/media/')) {
        if (request.method !== 'GET' || request.headers['sec-fetch-site'] !== 'same-origin') { response.writeHead(403); response.end(); return; }
        const headers = new Headers(); if (request.headers.range) headers.set('range', request.headers.range);
        const result = await options.media(new Request(`pixel-media://${url.pathname.slice(7)}`, { headers }));
        response.writeHead(result.status, Object.fromEntries([...result.headers].filter(([key]) => key !== 'access-control-allow-origin')));
        response.end(Buffer.from(await result.arrayBuffer())); return;
      }
      if (request.method !== 'GET' || (request.headers['sec-fetch-site'] && !['none', 'same-origin'].includes(String(request.headers['sec-fetch-site'])))) { response.writeHead(403); response.end(); return; }
      const path = resolve(directory, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
      const types: Record<string, string> = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.png': 'image/png' };
      if (!path.startsWith(directory + sep) || !types[extname(path)]) { response.writeHead(404); response.end(); return; }
      try {
        const bytes = await readFile(path);
        response.writeHead(200, { 'content-type': types[extname(path)]!, 'cache-control': 'no-store', 'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer' }); response.end(bytes);
      } catch { response.writeHead(404); response.end(); }
    })().catch(error => { if (!response.headersSent) json(response, operationError(error), 400); else response.end(); });
  });
  await new Promise<void>((accept, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', () => { server.off('error', reject); accept(); }); });
  const address = server.address(); if (!address || typeof address === 'string') throw new Error('Browser development host could not bind loopback.');
  origin = `http://127.0.0.1:${address.port}`;
  return {
    url: origin, available: () => clients.size > 0,
    changed() { for (const client of clients) client.write('event: changed\ndata: {}\n\n'); },
    send(command: UiCommand) { clients.values().next().value?.write(`event: ui-command\ndata: ${JSON.stringify(command)}\n\n`); },
    async close() { closing = true; for (const client of clients) client.end(); server.closeAllConnections(); await new Promise<void>(accept => server.close(() => accept())); }
  };
}
