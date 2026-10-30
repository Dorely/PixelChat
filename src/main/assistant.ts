import { z } from 'zod';
import { operationDefinitions, type OperationName } from '../shared/operation-catalog';

/** Project and conversation management stays with the user; the assistant works inside the current conversation's project. */
const EXCLUDED: OperationName[] = ['projects_list', 'project_create', 'project_get', 'project_rename', 'project_delete', 'sessions_list', 'session_create', 'session_get', 'session_delete', 'review_wait'];

export const instructions = `You are the PixelChat assistant, working inside the user's art review workspace. PixelChat tracks artwork items imported from linked source folders (often a game's asset tree), immutable revisions of each item with provenance, reviews with per-item decisions, and note threads that can be pinned to a point or box in image pixels.
Your tools act on the current project only. Call guidance with topics workflow, library, or image-editing before substantial work in that area. You cannot read arbitrary files or run commands.
Look before you judge: call view_image to see art before describing, comparing, or critiquing it, and choose a background unlike the art to judge edges and transparency. What you see is your observation, not the user's review. Review decisions are the user's call; record them with review_decide only when the user asks.
Notes: write specific, actionable notes, pinned to the region they describe when one applies. Pin coordinates are pixels of the noted revision. When you finish a change a note asked for, reply to the thread saying what changed and resolve it.
Image generation and editing are billed to the user's provider account. Run generate_images and edit_image only for the user's request, with the count they asked for (default 1), and never repeat a request whose outcome is uncertain; pass the same requestId to recover it instead. Edit results become new versions for the user to choose; do not change the selection unless asked. Follow queued jobs with jobs_get and wait, then view the results.
Exports write into the user's source folders. Export only when asked, never with force unless the user explicitly says to overwrite, and report SOURCE_CHANGED conflicts instead of working around them. Deleting items, sources, or reviews needs explicit user intent.
Keep this conversation titled: call rename_session once the subject is clear, and again if it changes materially, unless the user named the conversation.`;

const strip = (schema: z.ZodObject) => z.object(Object.fromEntries(Object.entries(schema.shape).filter(([key]) => key !== 'projectId' && key !== 'sessionId'))).strict();
export const toolSchemas = Object.fromEntries((Object.keys(operationDefinitions) as OperationName[])
  .filter(name => !EXCLUDED.includes(name)).map(name => [name, strip(operationDefinitions[name].schema)])) as Record<string, z.ZodObject>;

const descriptions: Partial<Record<OperationName, string>> = {
  view_image: `${operationDefinitions.view_image.description} The image is attached for you to see; the result describes its scale and pins.`,
  rename_session: 'Rename this conversation with a short, specific title of about 3-7 words in sentence case naming the subject and deliverable; no greeting, generic label, or trailing period.',
};
export const tools = Object.entries(toolSchemas).map(([name, schema]) => ({
  type: 'function', name, description: descriptions[name as OperationName] ?? operationDefinitions[name as OperationName].description,
  parameters: z.toJSONSchema(schema, { target: 'draft-7' }), strict: false,
}));

type Args = Record<string, any>;
/** Visible activity lines for tool calls that change the workspace; reads stay silent. */
const ACTIVITY: Partial<Record<OperationName, (args: Args, result: any) => string>> = {
  source_add: args => `Linked source folder ${args.root}.`,
  source_rescan: () => 'Rescanned a source folder.',
  source_remove: () => 'Removed a source folder from the project.',
  import_files: args => `Imported ${args.paths.length} file${args.paths.length === 1 ? '' : 's'}${args.itemId ? ' as new versions of an item' : ''}.`,
  item_update: args => args.selectedRevisionId ? 'Changed the selected version of an item.' : 'Updated item details.',
  item_delete: () => 'Deleted an item.',
  review_create: (_args, result) => `Created review “${result.title}”.`,
  review_update: () => 'Updated a review.',
  review_decide: args => `Recorded ${args.decisions.length} review decision${args.decisions.length === 1 ? '' : 's'}.`,
  review_complete: () => 'Completed a review.',
  review_reopen: () => 'Reopened a review.',
  review_delete: () => 'Deleted a review.',
  generate_images: (args, result) => `Queued ${result.length} image generation${result.length === 1 ? '' : 's'}: ${args.prompt}`,
  edit_image: (args, result) => `Queued ${result.length} image edit${result.length === 1 ? '' : 's'}: ${args.prompt}`,
  job_cancel: () => 'Cancelled an image job.',
  export_item: (_args, result) => result.written ? `Exported to ${result.path}.` : `${result.path} already holds the selected version.`,
  note_add: args => args.parentId ? 'Replied to a note.' : 'Added a note.',
  note_update: args => args.resolved === true ? 'Resolved a note.' : args.resolved === false ? 'Reopened a note.' : 'Edited a note.',
  note_delete: () => 'Deleted a note.',
  rename_session: args => `Renamed this conversation to “${args.title}”.`,
  ui_open: () => 'Opened it in the workspace.',
};
export function toolActivity(name: string, args: Args, result: unknown): string | null {
  return ACTIVITY[name as OperationName]?.(args, result) ?? null;
}
