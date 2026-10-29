import { app, BrowserWindow } from 'electron';
import { join } from 'node:path';

app.setName('PixelChat');
app.setPath('userData', join(app.getPath('appData'), 'PixelChat'));

void app.whenReady().then(() => {
  const window = new BrowserWindow({ width: 1440, height: 900, backgroundColor: '#17181d', icon: join(__dirname, '../../resources/icon.png'), webPreferences: { preload: join(__dirname, '../preload/index.js'), sandbox: true, contextIsolation: true, nodeIntegration: false } });
  if (process.env.ELECTRON_RENDERER_URL) void window.loadURL(process.env.ELECTRON_RENDERER_URL);
  else void window.loadFile(join(__dirname, '../renderer/index.html'));
});
app.on('window-all-closed', () => app.quit());
