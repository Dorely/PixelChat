import type { BrowserWindow } from 'electron';
import { randomUUID } from 'node:crypto';
import { WorkbenchError, type UiCommand, type UiOutcome } from '../shared/workbench';
import type { UiPort } from './operations';

/** Acknowledged navigation commands from operations to the one owning renderer. */
export class RendererPort implements UiPort {
  private pending = new Map<string, { resolve(value: { status: 'opened' | 'offered' }): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> }>();
  private ready = false;
  constructor(private window: () => BrowserWindow | null, private browser?: () => { available(): boolean; send(command: UiCommand): void } | null) {}
  markReady() { this.ready = true; }
  acknowledge(id: string, outcome: UiOutcome) {
    const pending = this.pending.get(id); if (!pending) return;
    clearTimeout(pending.timer); this.pending.delete(id);
    if ('error' in outcome) { pending.reject(new WorkbenchError('UI_FAILED', outcome.error)); return; }
    // Only a target the window actually switched to brings it forward.
    if (outcome.status === 'opened') { const window = this.window(); if (window && !window.isDestroyed()) { window.show(); window.focus(); } }
    pending.resolve(outcome);
  }
  /** The renderer switches project and view itself, or only offers the target while the user is in another review. */
  async send(input: Omit<UiCommand, 'id'>): Promise<{ status: 'opened' | 'offered' }> {
    const window = this.window(), browser = this.browser?.();
    if (((!window || window.isDestroyed()) && !browser?.available()) || !this.ready) throw new WorkbenchError('UI_UNAVAILABLE', 'The workspace UI is not ready. Try again after it opens.');
    if (this.pending.size) throw new WorkbenchError('UI_BUSY', 'Another client is changing the workspace view. Retry shortly.');
    const command: UiCommand = { ...input, id: randomUUID() };
    return new Promise<{ status: 'opened' | 'offered' }>((resolve, reject) => {
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
