import type { PixelApi } from './types';
import type { UiCommand } from './workbench';

/** Fixed app capabilities; transports never expose arbitrary privileged methods. */
export function createApi({ call, onChanged, onUiCommand, mediaUrl, hostMode }: {
  call(method: string, ...args: unknown[]): Promise<any>;
  onChanged(callback: () => void): () => void;
  onUiCommand(callback: (command: UiCommand) => void): () => void;
  mediaUrl(path: string): string; hostMode: 'desktop' | 'browser';
}): PixelApi {
  return {
    hostMode, mediaUrl, onChanged, onUiCommand,
    operation: (name, args) => call('operation', name, args),
    snapshot: () => call('snapshot'),
    setActiveProject: id => call('setActiveProject', id),
    chooseDirectory: () => call('chooseDirectory'),
    openLogs: () => call('openLogs'),
    codexLogin: () => call('codexLogin'),
    codexCancelLogin: () => call('codexCancelLogin'),
    codexLogout: () => call('codexLogout'),
    saveGeminiKey: key => call('saveGeminiKey', key),
    uiReady: () => call('uiReady'),
    uiAcknowledge: (id, error) => call('uiAcknowledge', id, error),
  };
}
