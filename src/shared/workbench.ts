export type Origin = 'user' | 'agent';
export interface UiCommand { id: string; action: 'open-review' | 'open-item'; projectId: string; reviewId?: string; itemId?: string }
export interface OperationResponse { ok: boolean; result?: unknown; error?: { code: string; message: string; details?: unknown } }
export class WorkbenchError extends Error {
  constructor(public code: string, message: string, public details?: unknown) { super(message); }
}
