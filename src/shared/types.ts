import type { InstallationInput, Origin, OperationResponse, UiCommand } from './workbench';
import type { ImageBackground, ImageFormat } from './image-models';

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
  pendingExport: boolean; missing: boolean; openNotes: number; selected: RevisionMeta; createdAt: string; updatedAt: string;
}
export interface Item extends ItemSummary { revisions: Revision[]; sourceFile: string | null; notes: NoteThread[] }
export type ItemStatusFilter = 'all' | 'pending_export' | 'missing' | 'open_notes';
export interface ItemSearchResult { total: number; items: ItemSummary[]; collections: { name: string; count: number }[] }

export interface ScanReport { startedAt: string; finishedAt: string; added: number; changed: number; unchanged: number; missing: number; failed: { path: string; error: string }[] }
export interface ScanProgress { done: number; total: number; startedAt: string }
export interface Source {
  id: string; projectId: string; root: string; include: string[]; exclude: string[]; createdAt: string;
  itemCount: number; lastScan: ScanReport | null; scanning: ScanProgress | null;
}

/** A region in image pixels of the note's revision: a point, or a box when width and height are set. */
export interface Pin { x: number; y: number; width?: number; height?: number }
/** Feedback on an item. Root notes may carry a pin and be resolved; replies hang off a root. */
export interface Note {
  id: string; projectId: string; itemId: string; reviewId: string | null; revisionId: string; parentId: string | null;
  author: Origin; body: string; pin: Pin | null; resolvedAt: string | null; resolvedBy: Origin | null; createdAt: string; updatedAt: string;
}
export interface NoteThread extends Note { replies: Note[] }

export type Decision = 'unreviewed' | 'accepted' | 'needs_changes' | 'rejected';
export type ReviewStatus = 'active' | 'completed';
export interface ReviewSummary {
  id: string; projectId: string; title: string; instructions: string; status: ReviewStatus; revision: number;
  counts: Record<Decision, number> & { total: number }; openNotes: number;
  createdAt: string; createdBy: Origin; updatedAt: string; completedAt: string | null; completedBy: Origin | null;
}
/** One item in a review. Stale when the item's selected revision changed after the decision. */
export interface ReviewEntry {
  itemId: string; position: number; decision: Decision; decidedAt: string | null; decidedBy: Origin | null; decidedRevisionId: string | null;
  stale: boolean; item: ItemSummary;
}
export interface Review extends ReviewSummary { entries: ReviewEntry[]; notes: NoteThread[] }
export interface ActivityEntry { seq: number; at: string; origin: Origin; kind: string; itemId: string | null; reviewId: string | null; data: Record<string, unknown> }

export type JobStatus = 'queued' | 'running' | 'completed' | 'failed' | 'uncertain' | 'cancelled';
/** The request one image job runs, as admitted. Prompt is verbatim; `instructions` is the background text PixelChat appended. */
export interface ImageJobInput {
  kind: 'generate' | 'edit'; prompt: string; instructions: string; model: string; quality: string; background: ImageBackground; format: ImageFormat; size: string;
  sourceRevisionId: string | null; referenceRevisionIds: string[]; collection: string | null; name: string | null;
}
/** One provider request producing one image. Uncertain: the request may have been billed but no result was recorded; it is never replayed. */
export interface ImageJob {
  id: string; projectId: string; batchId: string; position: number; status: JobStatus; input: ImageJobInput;
  itemId: string | null; revisionId: string | null; error: string | null; warning: string | null;
  createdBy: Origin; createdAt: string; startedAt: string | null; finishedAt: string | null;
}

export type MessageRole = 'user' | 'assistant' | 'tool';
export type MessageStatus = 'running' | 'completed' | 'failed' | 'cancelled';
/** A revision a tool showed the assistant. The UI draws its thumbnail; the rendered view is not stored. */
export interface ChatAttachment { kind: 'image'; label: string; itemId: string; revisionId: string }
export interface Message { id: string; sessionId: string; role: MessageRole; text: string; status: MessageStatus; attachments: ChatAttachment[]; createdAt: string }
/** What the user is looking at when they send a message, so "this" resolves to an item or review. */
export interface ChatFocus { reviewId?: string; itemId?: string }

export type GenericProviderKind = 'chat-completions' | 'responses' | 'anthropic';
export interface ChatProviderEntry { id: string; label: string; baseUrl: string; kind: GenericProviderKind }
export type CapabilityFlag = true | false | 'unknown';
/** A tested custom chat model. Vision decides whether view_image is offered. */
export interface ChatModelEntry {
  id: string; providerId: string; slug: string; efforts: string[]; maxTokens: number | null; vision: CapabilityFlag;
  effortResults: Record<string, 'pass' | 'fail'>; lastTestedAt: string; testError?: string;
}
export interface ModelCapabilityResult { perEffort: Record<string, 'pass' | 'fail'>; failedEfforts: string[]; maxTokens: number | null; vision: CapabilityFlag; error?: string }
export interface ActiveChatSelection { modelId: string; effort: string }
export interface ChatModelOption { id: string; label: string; providerLabel: string; slug: string; efforts: string[]; builtIn: boolean }
export interface ChatStatus { models: ChatModelOption[]; active: ActiveChatSelection; providers: ChatProviderEntry[] }
export interface ChatContext { model: string; tokens: number; limit: number | null; includesHiddenReasoning: boolean; includesEstimatedMedia: boolean }
export interface ChatProviderInput { id?: string; label: string; baseUrl: string; kind: GenericProviderKind; apiKey?: string }
export interface ChatModelInput { id?: string; providerId: string; slug: string; efforts: string[] }

export interface ProviderStatus {
  secureStorage: boolean;
  codex: { ready: boolean; pending: boolean; error: string | null };
  gemini: { ready: boolean };
}
/** Renderer bootstrap state; views load their own data through operations. */
export interface Snapshot { projects: Project[]; activeProjectId: string; sessions: Session[]; providers: ProviderStatus; chat: ChatStatus; activeTurns: string[] }

export interface PixelApi {
  hostMode: 'desktop' | 'browser';
  mediaUrl(path: string): string;
  operation(name: string, args: unknown): Promise<OperationResponse>;
  snapshot(): Promise<Snapshot>;
  setActiveProject(id: string): Promise<void>;
  chooseDirectory(): Promise<string | null>;
  openLogs(): Promise<void>;
  codexLogin(): Promise<void>;
  codexCancelLogin(): Promise<void>;
  codexLogout(): Promise<void>;
  saveGeminiKey(key: string | null): Promise<void>;
  installIntegration(input: InstallationInput): Promise<OperationResponse>;
  chatMessages(sessionId: string): Promise<Message[]>;
  sendMessage(sessionId: string, text: string, focus?: ChatFocus): Promise<void>;
  cancelTurn(sessionId: string): Promise<void>;
  chatContext(sessionId: string, draft: string): Promise<ChatContext>;
  setActiveChat(selection: ActiveChatSelection): Promise<ActiveChatSelection>;
  saveChatProvider(input: ChatProviderInput): Promise<ChatProviderEntry>;
  deleteChatProvider(id: string): Promise<void>;
  listChatModels(providerId: string): Promise<string[]>;
  testChatModel(input: ChatModelInput): Promise<ModelCapabilityResult>;
  saveChatModel(input: ChatModelInput): Promise<ChatModelEntry>;
  deleteChatModel(id: string): Promise<void>;
  uiReady(): Promise<void>;
  uiAcknowledge(id: string, error?: string): Promise<void>;
  onUiCommand(callback: (command: UiCommand) => void): () => void;
  onChanged(callback: () => void): () => void;
}

export type { Origin };
