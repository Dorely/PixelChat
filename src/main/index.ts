import { app, BrowserWindow, dialog, ipcMain, protocol, safeStorage, shell } from 'electron';
import { join } from 'node:path';
import { mkdir, readFile } from 'node:fs/promises';
import { z } from 'zod';
import { Store } from './store';
import { MediaStore, MEDIA_TYPES } from './media';
import { Library } from './library';
import { Operations, idSchema, operationError } from './operations';
import { RendererPort } from './ui-port';
import { startBridge } from './bridge';
import { installIntegration } from '../shared/install';
import { installationSchema, type InstallationInput } from '../shared/workbench';
import { startBrowserHost } from './browser-host';
import { DiagnosticLog, redactText } from './diagnostics';
import { ImageJobs } from './images';
import { SecureStore } from './providers/secrets';
import { CodexAuth } from './providers/auth';
import { CodexImages } from './providers/codex-images';
import { GeminiImages } from './providers/gemini-images';
import { ChatStore } from './chat-store';
import { ChatRegistry } from './chat-registry';
import { ChatService } from './chat';
import type { ActiveChatSelection, ChatFocus, ChatModelInput, ChatProviderInput, ProviderStatus, Snapshot } from '../shared/types';

protocol.registerSchemesAsPrivileged([{ scheme: 'pixel-media', privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true, corsEnabled: true } }]);
app.setName('PixelChat');
app.setPath('userData', join(app.getPath('appData'), 'PixelChat'));
const browserMode = !app.isPackaged && process.env.PIXELCHAT_BROWSER === '1';
let browserHost: Awaited<ReturnType<typeof startBrowserHost>> | null = null;
let window: BrowserWindow | null = null;
let store: Store | null = null;
let operations: Operations | null = null;
let library: Library | null = null;
let images: ImageJobs | null = null;
let auth: CodexAuth | null = null;
let chat: ChatService | null = null;
let rendererPort: RendererPort | null = null;
let bridge: Awaited<ReturnType<typeof startBridge>> | null = null;
let quitting = false;
let eventTimer: ReturnType<typeof setTimeout> | null = null;
const diagnostics = new DiagnosticLog(app.getPath('userData'));
const changed = () => {
  if (eventTimer) return;
  eventTimer = setTimeout(() => { eventTimer = null; if (window && !window.isDestroyed()) window.webContents.send('pixel:changed'); browserHost?.changed(); }, 40);
};
const safeError = (error: unknown) => redactText(error instanceof Error ? error.message : 'Operation failed.');
const GEMINI_KEY = 'gemini-key';
const providerKinds = z.enum(['chat-completions', 'responses', 'anthropic']);
const effortList = z.array(z.string().trim().max(40)).max(12);
const providerInput = z.object({ id: idSchema.optional(), label: z.string().trim().min(1).max(80), baseUrl: z.string().trim().min(1).max(500), kind: providerKinds, apiKey: z.string().trim().max(400).optional() }).strict();
const modelInput = z.object({ id: idSchema.optional(), providerId: idSchema, slug: z.string().trim().min(1).max(200), efforts: effortList }).strict();

/** Fixed renderer commands. Workspace behavior goes through the shared operation registry. */
const commandSchemas = {
  snapshot: z.tuple([]),
  setActiveProject: z.tuple([idSchema]),
  operation: z.tuple([z.string().min(1).max(64), z.unknown()]),
  chooseDirectory: z.tuple([]),
  openLogs: z.tuple([]),
  codexLogin: z.tuple([]),
  codexCancelLogin: z.tuple([]),
  codexLogout: z.tuple([]),
  saveGeminiKey: z.tuple([z.string().trim().max(400).nullable()]),
  installIntegration: z.tuple([installationSchema]),
  chatMessages: z.tuple([idSchema]),
  sendMessage: z.tuple([idSchema, z.string().trim().min(1).max(20000), z.object({ reviewId: idSchema.optional(), itemId: idSchema.optional() }).strict().optional()]),
  cancelTurn: z.tuple([idSchema]),
  chatContext: z.tuple([idSchema, z.string().max(20000)]),
  setActiveChat: z.tuple([z.object({ modelId: z.string().min(1).max(200), effort: z.string().max(40) }).strict()]),
  saveChatProvider: z.tuple([providerInput]),
  deleteChatProvider: z.tuple([idSchema]),
  listChatModels: z.tuple([idSchema]),
  testChatModel: z.tuple([modelInput]),
  saveChatModel: z.tuple([modelInput]),
  deleteChatModel: z.tuple([idSchema]),
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
    const db = new Store(join(root, 'library.sqlite'), changed); store = db;
    const media = new MediaStore(root); await media.init();
    library = new Library(db, media);
    // Credentials live only in OS-protected storage under the profile, never in the database or logs.
    const secrets = new SecureStore({ directory: join(root, 'secrets'), encrypt: value => safeStorage.encryptString(value), decrypt: value => safeStorage.decryptString(value), isAvailable: () => safeStorage.isEncryptionAvailable() && (process.platform !== 'linux' || safeStorage.getSelectedStorageBackend() !== 'basic_text') });
    const codex = new CodexAuth({ store: secrets, openExternal: url => shell.openExternal(url), onChange: status => { if (status.error) diagnostics.record('auth.failed', { message: status.error }); changed(); } }); auth = codex;
    await codex.initialize();
    const geminiKey = async () => (await secrets.available()) ? secrets.get(GEMINI_KEY) : null;
    const providerStatus = async (): Promise<ProviderStatus> => ({ secureStorage: await secrets.available(), codex: codex.status, gemini: { ready: !!(await geminiKey().catch(() => null)) } });
    const jobs = new ImageJobs(db, media, { codex: new CodexImages(codex), gemini: new GeminiImages(geminiKey) }, diagnostics); images = jobs;
    jobs.start();
    rendererPort = new RendererPort(() => window, db, () => browserHost);
    const ops = new Operations(db, library, media, jobs, rendererPort, providerStatus); operations = ops;
    const chats = new ChatStore(db); chats.interrupt();
    const registry = new ChatRegistry(db, secrets, geminiKey);
    const assistant = new ChatService(db, chats, ops, registry, () => codex.getAccessToken(), diagnostics); chat = assistant;
    ops.sessionBusy = id => assistant.active(id);
    const chatStatus = () => { const target = registry.resolveActive(); return { models: registry.options(), active: { modelId: target.selectionId, effort: target.effort }, providers: registry.providers() }; };
    const agentRoot = app.isPackaged ? join(process.resourcesPath, 'agent') : join(__dirname, '../agent');
    // Agents are optional: the workspace stays usable when the helper is missing or the pipe cannot be published.
    try { bridge = await startBridge(root, join(agentRoot, 'pixelchat-helper.exe'), ops); } catch (error) { diagnostics.record('bridge.failed', { message: safeError(error) }); }
    const handlers: Record<Command, (...args: any[]) => unknown> = {
      snapshot: async (): Promise<Snapshot> => { const activeProjectId = db.activeProjectId(); return { projects: db.projects(), activeProjectId, sessions: db.sessions(activeProjectId), providers: await providerStatus(), chat: chatStatus(), activeTurns: assistant.activeTurns() }; },
      setActiveProject: (id: string) => db.setActiveProject(id),
      operation: async (name: string, args: unknown) => { try { return { ok: true, result: await ops.call(name, args, 'user') }; } catch (e) { return operationError(e); } },
      chooseDirectory: async () => { const result = await dialog.showOpenDialog(window!, { properties: ['openDirectory'] }); return result.canceled ? null : result.filePaths[0]; },
      openLogs: async () => { const directory = join(root, 'logs'); await mkdir(directory, { recursive: true }); const error = await shell.openPath(directory); if (error) throw new Error(error); },
      codexLogin: () => codex.startLogin(),
      codexCancelLogin: () => codex.cancelLogin(),
      codexLogout: () => codex.logout(),
      saveGeminiKey: async (key: string | null) => { if (key) await secrets.set(GEMINI_KEY, key); else await secrets.delete(GEMINI_KEY); changed(); },
      installIntegration: async (input: InstallationInput) => { try { return { ok: true, result: await installIntegration(input, join(agentRoot, 'pixelchat-runtime.exe'), join(agentRoot, 'cli.cjs')) }; } catch (e) { return operationError(e); } },
      chatMessages: (id: string) => assistant.messages(id),
      sendMessage: (id: string, text: string, focus?: ChatFocus) => assistant.send(id, text, focus),
      cancelTurn: (id: string) => assistant.cancel(id),
      chatContext: (id: string, draft: string) => assistant.context(id, draft),
      setActiveChat: (selection: ActiveChatSelection) => { const active = registry.setActive(selection); changed(); return active; },
      saveChatProvider: async (input: ChatProviderInput) => { const provider = await registry.saveProvider(input); changed(); return provider; },
      deleteChatProvider: async (id: string) => { await registry.deleteProvider(id); changed(); },
      listChatModels: (providerId: string) => registry.listModels(providerId),
      testChatModel: (input: ChatModelInput) => registry.testModel(input),
      saveChatModel: async (input: ChatModelInput) => { const record = await registry.saveModel(input); changed(); return record; },
      deleteChatModel: async (id: string) => { await registry.deleteModel(id); changed(); },
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
    /** `pixel-media://original/<revisionId>` and `pixel-media://thumb/<revisionId>`, scoped to the active project. */
    const serveMedia = async (request: Request): Promise<Response> => {
      try {
        const url = new URL(request.url);
        const id = idSchema.safeParse(url.pathname.slice(1));
        const revision = request.method === 'GET' && id.success ? db.revisionFile(id.data, db.activeProjectId()) : null;
        if (!revision || !['original', 'thumb'].includes(url.hostname)) return new Response(null, { status: 404 });
        const original = url.hostname === 'original';
        const bytes = original ? await media.read(revision.file) : await readFile(media.thumbnailPath(id.data!));
        return new Response(new Uint8Array(bytes), { headers: { 'content-type': original ? MEDIA_TYPES[revision.format] : 'image/webp', 'cache-control': 'private, max-age=31536000, immutable', 'x-content-type-options': 'nosniff' } });
      } catch { return new Response(null, { status: 404 }); }
    };
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
    void Promise.all([chat?.shutdown(), browserHost?.close(), bridge?.close(), operations?.close(), library?.close(), images?.close(), auth?.cancelLogin()]).finally(() => { if (eventTimer) clearTimeout(eventTimer); store?.close(); app.quit(); });
  });
}
