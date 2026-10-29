import { contextBridge } from 'electron';
contextBridge.exposeInMainWorld('pixel', {});
