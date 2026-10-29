import { contextBridge, ipcRenderer } from 'electron';
import { createApi } from '../shared/api';
import type { UiCommand } from '../shared/workbench';

const api = createApi({
  hostMode: 'desktop', mediaUrl: path => `pixel-media://${path}`,
  call: (method, ...args) => ipcRenderer.invoke('pixel:call', method, args),
  onUiCommand: callback => { const listener = (_event: unknown, command: UiCommand) => callback(command); ipcRenderer.on('pixel:ui-command', listener); return () => ipcRenderer.removeListener('pixel:ui-command', listener); },
  onChanged: callback => { const listener = () => callback(); ipcRenderer.on('pixel:changed', listener); return () => ipcRenderer.removeListener('pixel:changed', listener); }
});
contextBridge.exposeInMainWorld('pixel', api);
