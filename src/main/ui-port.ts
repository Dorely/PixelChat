import type { BrowserWindow } from 'electron';
import { randomUUID } from 'node:crypto';
import type { Store } from './store';
import { WorkbenchError, type UiCommand } from '../shared/workbench';
import type { UiPort } from './operations';

/** Acknowledged navigation commands from operations to the one owning renderer. */
export class RendererPort implements UiPort {
  private pending = new Map<string, { resolve(value: unknown): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> }>();
  private ready = false;
  constructor(private window: () => BrowserWindow | null, private store: Store, private browser?: () => { available(): boolean; send(command: UiCommand): void } | null) {}
  markReady() { this.ready = true; }
  acknowledge(id: string, error?: string) {
    const pending = this.pending.get(id); if (!pending) return;
    clearTimeout(pending.timer); this.pending.delete(id);
    error ? pending.reject(new WorkbenchError('UI_FAILED', error)) : pending.resolve({ status: 'opened' });
  }
  async send(input: Omit<UiCommand, 'id'>) {
    const window = this.window(), browser = this.browser?.();
    if (((!window || window.isDestroyed()) && !browser?.available()) || !this.ready) throw new WorkbenchError('UI_UNAVAILABLE', 'The workspace UI is not ready. Try again after it opens.');
    if (this.pending.size) throw new WorkbenchError('UI_BUSY', 'Another client is changing the workspace view. Retry shortly.');
    this.store.setActiveProject(input.projectId); window?.show(); window?.focus();
    const command: UiCommand = { ...input, id: randomUUID() };
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(command.id); reject(new WorkbenchError('UI_TIMEOUT', 'The workspace UI did not acknowledge the command.')); }, 15000);
      this.pending.set(command.id, { resolve, reject, timer });
      if (browser?.available()) browser.send(command); else window?.webContents.send('pixel:ui-command', command);
    });
  }
  unavailable() {
    this.ready = false;
    for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(new WorkbenchError('UI_UNAVAILABLE', 'The workspace UI closed.')); }
    this.pending.clear();
  }
}
