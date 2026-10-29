import { z } from 'zod';
export type Origin = 'user' | 'agent';
export interface UiCommand { id: string; action: 'open-review' | 'open-item'; projectId: string; reviewId?: string; itemId?: string }
export interface OperationResponse { ok: boolean; result?: unknown; error?: { code: string; message: string; details?: unknown } }
export class WorkbenchError extends Error {
  constructor(public code: string, message: string, public details?: unknown) { super(message); }
}

/** Skill and MCP registration for Codex and Claude Code, from the CLI or Settings. */
export const installationSchema = z.object({
  action: z.enum(['install', 'uninstall', 'status']).default('install'), scope: z.enum(['project', 'user']),
  targets: z.array(z.enum(['codex', 'claude'])).default([]), projectDirectory: z.string().optional(), directory: z.string().optional(),
  skill: z.boolean().default(true), mcp: z.boolean().default(false), dryRun: z.boolean().default(false), force: z.boolean().default(false),
}).strict();
export type InstallationInput = z.input<typeof installationSchema>;
