import type { Origin, OperationResponse, UiCommand } from './workbench';

export interface Project { id: string; name: string; createdAt: string; updatedAt: string }
export type TitleSource = 'auto' | 'user' | 'assistant';
export interface Session { id: string; projectId: string; title: string; titleSource: TitleSource; createdAt: string; updatedAt: string }

export type RevisionOrigin = 'imported' | 'generated' | 'edited';
/** Immutable image bytes stored by PixelChat. Provenance records how they were made. */
export interface Revision {
  id: string; itemId: string; parentId: string | null; origin: RevisionOrigin;
  sha256: string; bytes: number; width: number; height: number; format: 'png' | 'webp' | 'jpeg';
  hasAlpha: boolean; opaque: boolean; provenance: Record<string, unknown>; createdAt: string;
}
export type RevisionMeta = Pick<Revision, 'id' | 'origin' | 'width' | 'height' | 'format' | 'hasAlpha' | 'opaque' | 'createdAt'>;
/** A tracked artwork. Pending export when the selected revision differs from the one on disk. */
export interface ItemSummary {
  id: string; projectId: string; sourceId: string | null; sourcePath: string | null; name: string; collection: string;
  description: string; tags: string[]; syncedRevisionId: string | null; selectedRevisionId: string;
  pendingExport: boolean; missing: boolean; selected: RevisionMeta; createdAt: string; updatedAt: string;
}
export interface Item extends ItemSummary { revisions: Revision[]; sourceFile: string | null }
export type ItemStatusFilter = 'all' | 'pending_export' | 'missing';
export interface ItemSearchResult { total: number; items: ItemSummary[]; collections: { name: string; count: number }[] }

export interface ScanReport { startedAt: string; finishedAt: string; added: number; changed: number; unchanged: number; missing: number; failed: { path: string; error: string }[] }
export interface ScanProgress { done: number; total: number; startedAt: string }
export interface Source {
  id: string; projectId: string; root: string; include: string[]; exclude: string[]; createdAt: string;
  itemCount: number; lastScan: ScanReport | null; scanning: ScanProgress | null;
}

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
