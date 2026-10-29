import type { Origin, OperationResponse, UiCommand } from './workbench';

export interface Project { id: string; name: string; createdAt: string; updatedAt: string }
export type TitleSource = 'auto' | 'user' | 'assistant';
export interface Session { id: string; projectId: string; title: string; titleSource: TitleSource; createdAt: string; updatedAt: string }

/** Renderer bootstrap state; views load their own data through operations. */
export interface Snapshot { projects: Project[]; activeProjectId: string; sessions: Session[] }

export interface PixelApi {
  hostMode: 'desktop' | 'browser';
  mediaUrl(path: string): string;
  operation(name: string, args: unknown): Promise<OperationResponse>;
  snapshot(): Promise<Snapshot>;
  setActiveProject(id: string): Promise<void>;
  chooseDirectory(): Promise<string | null>;
  openLogs(): Promise<void>;
  uiReady(): Promise<void>;
  uiAcknowledge(id: string, error?: string): Promise<void>;
  onUiCommand(callback: (command: UiCommand) => void): () => void;
  onChanged(callback: () => void): () => void;
}

export type { Origin };
