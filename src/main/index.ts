import { app, BrowserWindow, dialog, ipcMain, protocol, shell } from 'electron';
import { join } from 'node:path';
import { mkdir } from 'node:fs/promises';
import { z } from 'zod';
import { Store } from './store';
import { Operations, idSchema, operationError } from './operations';
import { RendererPort } from './ui-port';
import { startBrowserHost } from './browser-host';
import { DiagnosticLog, redactText } from './diagnostics';
import type { Snapshot } from '../shared/types';

protocol.registerSchemesAsPrivileged([{ scheme: 'pixel-media', privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true, corsEnabled: true } }]);
app.setName('PixelChat');
app.setPath('userData', join(app.getPath('appData'), 'PixelChat'));
const browserMode = !app.isPackaged && process.env.PIXELCHAT_BROWSER === '1';
let browserHost: Awaited<ReturnType<typeof startBrowserHost>> | null = null;
let window: BrowserWindow | null = null;
let store: Store | null = null;
let operations: Operations | null = null;
let rendererPort: RendererPort | null = null;
let quitting = false;
let eventTimer: ReturnType<typeof setTimeout> | null = null;
const diagnostics = new DiagnosticLog(app.getPath('userData'));
const changed = () => {
  if (eventTimer) return;
  eventTimer = setTimeout(() => { eventTimer = null; if (window && !window.isDestroyed()) window.webContents.send('pixel:changed'); browserHost?.changed(); }, 40);
};
const safeError = (error: unknown) => redactText(error instanceof Error ? error.message : 'Operation failed.');

/** Fixed renderer commands. Workspace behavior goes through the shared operation registry. */
const commandSchemas = {
  snapshot: z.tuple([]),
  setActiveProject: z.tuple([idSchema]),
  operation: z.tuple([z.string().min(1).max(64), z.unknown()]),
  chooseDirectory: z.tuple([]),
  openLogs: z.tuple([]),
  uiReady: z.tuple([]),
  uiAcknowledge: z.tuple([z.string().uuid(), z.string().max(2000).optional()]),
};
type Command = keyof typeof commandSchemas;

if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { if (window && !window.isDestroyed()) { window.show(); window.focus(); } });
  void app.whenReady().then(async () => {
    const root = app.getPath('userData'); await mkdir(root, { recursive: true });
    diagnostics.record('app.started', { version: app.getVersion(), packaged: app.isPackaged });
    const library = new Store(join(root, 'library.sqlite'), changed); store = library;
    rendererPort = new RendererPort(() => window, library, () => browserHost);
    const ops = new Operations(library, rendererPort); operations = ops;
    const handlers: Record<Command, (...args: any[]) => unknown> = {
      snapshot: (): Snapshot => { const activeProjectId = library.activeProjectId(); return { projects: library.projects(), activeProjectId, sessions: library.sessions(activeProjectId) }; },
      setActiveProject: (id: string) => library.setActiveProject(id),
      operation: async (name: string, args: unknown) => { try { return { ok: true, result: await ops.call(name, args, 'user') }; } catch (e) { return operationError(e); } },
      chooseDirectory: async () => { const result = await dialog.showOpenDialog(window!, { properties: ['openDirectory'] }); return result.canceled ? null : result.filePaths[0]; },
      openLogs: async () => { const directory = join(root, 'logs'); await mkdir(directory, { recursive: true }); const error = await shell.openPath(directory); if (error) throw new Error(error); },
      uiReady: () => rendererPort!.markReady(),
      uiAcknowledge: (id: string, error?: string) => rendererPort!.acknowledge(id, error),
    };
    const invoke = async (method: unknown, rawArgs: unknown) => {
      if (typeof method !== 'string' || !Object.hasOwn(commandSchemas, method)) throw new Error('Unknown application request.');
      const parsed = commandSchemas[method as Command].safeParse(rawArgs);
      if (!parsed.success) throw new Error('Invalid operation arguments.');
      try { return await handlers[method as Command](...parsed.data); } catch (error) {
        const reference = diagnostics.record('operation.failed', { method, message: safeError(error) });
        throw new Error(`${safeError(error)}${reference ? `\nDiagnostic reference: ${reference}.` : ''}`);
      }
    };
    const serveMedia = async (_request: Request) => new Response(null, { status: 404 });
    protocol.handle('pixel-media', serveMedia);
    ipcMain.handle('pixel:call', async (event, method: unknown, rawArgs: unknown) => {
      if (!window || event.sender !== window.webContents || event.senderFrame !== window.webContents.mainFrame) throw new Error('Untrusted application request.');
      return invoke(method, rawArgs);
    });
    if (browserMode) {
      browserHost = await startBrowserHost({ directory: join(__dirname, '../renderer'), invoke, media: serveMedia, disconnected: () => rendererPort?.unavailable(), shutdown: () => app.quit() });
      process.stdout.write(`PixelChat browser: ${browserHost.url}\n`);
      return;
    }
    const icon = app.isPackaged ? join(process.resourcesPath, 'icon.ico') : join(__dirname, '../../resources/icon.ico');
    window = new BrowserWindow({ width: 1440, height: 940, minWidth: 1000, minHeight: 650, title: 'PixelChat', icon, backgroundColor: '#151716', show: false, webPreferences: { preload: join(__dirname, '../preload/index.js'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
    window.removeMenu();
    window.on('closed', () => rendererPort?.unavailable());
    window.webContents.on('render-process-gone', (_event, details) => { rendererPort?.unavailable(); diagnostics.record('renderer.stopped', { reason: details.reason, exitCode: details.exitCode }); });
    window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    window.webContents.on('will-navigate', event => event.preventDefault());
    window.webContents.session.setPermissionRequestHandler((_contents, _permission, callback) => callback(false));
    window.on('ready-to-show', () => window?.show());
    const devUrl = process.env.ELECTRON_RENDERER_URL;
    if (!app.isPackaged && devUrl) await window.loadURL(devUrl); else await window.loadFile(join(__dirname, '../renderer/index.html'));
  }).catch(error => { diagnostics.record('app.start_failed', { message: safeError(error) }); dialog.showErrorBox('PixelChat could not start', safeError(error)); app.exit(1); });
  app.on('window-all-closed', () => app.quit());
  app.on('before-quit', event => {
    if (quitting) return; event.preventDefault(); quitting = true;
    rendererPort?.unavailable();
    void Promise.all([browserHost?.close(), operations?.close()]).finally(() => { if (eventTimer) clearTimeout(eventTimer); store?.close(); app.quit(); });
  });
}
