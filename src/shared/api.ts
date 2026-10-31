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
    installIntegration: input => call('installIntegration', input),
    chatMessages: sessionId => call('chatMessages', sessionId),
    sendMessage: (sessionId, text, focus) => call('sendMessage', sessionId, text, focus),
    cancelTurn: sessionId => call('cancelTurn', sessionId),
    chatContext: (sessionId, draft) => call('chatContext', sessionId, draft),
    setActiveChat: selection => call('setActiveChat', selection),
    saveChatProvider: input => call('saveChatProvider', input),
    deleteChatProvider: id => call('deleteChatProvider', id),
    listChatModels: providerId => call('listChatModels', providerId),
    testChatModel: input => call('testChatModel', input),
    saveChatModel: input => call('saveChatModel', input),
    deleteChatModel: id => call('deleteChatModel', id),
    uiReady: () => call('uiReady'),
    uiAcknowledge: (id, outcome) => call('uiAcknowledge', id, outcome),
  };
}
