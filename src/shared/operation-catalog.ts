import { z } from 'zod';
import { DEFAULT_IMAGE_MODEL, IMAGE_MODELS, MAX_IMAGE_COUNT } from './image-models';

export const idSchema = z.string().uuid();
const nameSchema = z.string().trim().min(1).max(60);
const titleSchema = z.string().trim().min(1).max(70);
const project = { projectId: idSchema };
const session = { ...project, sessionId: idSchema };
const source = { ...project, sourceId: idSchema };
const item = { ...project, itemId: idSchema };
const review = { ...project, reviewId: idSchema };
const note = { ...project, noteId: idSchema };
const globs = z.array(z.string().trim().min(1).max(200)).max(50);
const tagsSchema = z.array(z.string().trim().min(1).max(40)).max(30);
const obj = <T extends z.ZodRawShape>(shape: T) => z.object(shape).strict();
const itemFilter = { query: z.string().trim().max(200).optional(), collection: z.string().max(500).optional(), sourceId: idSchema.optional(), status: z.enum(['all', 'pending_export', 'missing', 'open_notes']).optional() };
const itemIdsSchema = z.array(idSchema).min(1).max(5000);
const decisionSchema = z.enum(['unreviewed', 'accepted', 'needs_changes', 'rejected']);
const expectedRevision = z.number().int().positive();
const pinSchema = obj({ x: z.number().finite(), y: z.number().finite(), width: z.number().positive().optional(), height: z.number().positive().optional() })
  .describe('Image pixels of the noted revision. A point, or a box when width and height are both set. Clamped to the image.');
const reviewTitle = z.string().trim().min(1).max(120);
const instructionsSchema = z.string().max(8000);
const noteBody = z.string().trim().min(1).max(4000);
const waitSeconds = z.number().int().min(0).max(300);
const imageOptions = {
  prompt: z.string().trim().min(1).max(32000),
  model: z.enum(IMAGE_MODELS.map(model => model.id) as [string, ...string[]]).optional().describe(`Default ${DEFAULT_IMAGE_MODEL}. The model selects its provider; see status for each model's qualities and transparency.`),
  quality: z.string().trim().max(20).optional().describe('Default auto. Providers may serve a different quality.'),
  background: z.enum(['auto', 'opaque', 'transparent']).optional().describe(`transparent requests native alpha (Image 2.5 Flare or Sunburst, PNG or WebP). auto keeps an edit source's transparency. PixelChat appends the background instruction to the prompt.`),
  format: z.enum(['png', 'webp', 'jpeg']).optional().describe(`Default: the item's file format, else PNG. Converted locally without resizing; the provider original is kept.`),
  size: z.string().trim().max(32).optional().describe('auto, square, portrait, landscape, wide, an aspect such as 16:9, or WIDTHxHEIGHT. Edits default to the source size. The provider returns its nearest supported raster.'),
  count: z.number().int().min(1).max(MAX_IMAGE_COUNT).optional().describe('Images to make; each is a separate provider request.'),
  referenceItemIds: z.array(idSchema).max(4).optional().describe('Items whose selected versions are sent as reference images.'),
  requestId: idSchema.optional().describe('Idempotency key. Repeating a request with the same requestId returns the original jobs instead of paying again.'),
};

/** One registry serves the UI, the in-app assistant, the CLI and MCP. */
export const operationDefinitions = {
  status: { schema: obj({}), description: 'App readiness, image models, and provider status, without secrets.' },
  guidance: { schema: obj({ topics: z.array(z.string().min(1)).min(1).max(10).optional() }), description: 'Read the PixelChat skill references by topic (workflow, library, image-editing). Omit topics for all. The hash covers the complete set; compare it with the installed skill.' },
  projects_list: { schema: obj({}), description: 'List projects.' },
  project_create: { schema: obj({ name: nameSchema }), description: 'Create a project without changing the visible project.' },
  project_get: { schema: obj(project), description: 'Inspect a project.' },
  project_rename: { schema: obj({ ...project, name: nameSchema }), description: 'Rename a project.' },
  project_delete: { schema: obj(project), description: 'Permanently delete a project with its workspace records and stored image copies. Files in source folders are not touched. Requires explicit user intent.' },
  sessions_list: { schema: obj(project), description: 'List conversations in a project.' },
  session_create: { schema: obj({ ...project, title: titleSchema.optional() }), description: 'Create a conversation in a project. Without a title it is named from its first message.' },
  session_get: { schema: obj(session), description: 'Inspect a conversation.' },
  rename_session: { schema: obj({ ...session, title: titleSchema }), description: 'Keep a conversation label specific to the current work.' },
  session_delete: { schema: obj(session), description: 'Permanently delete a conversation. Project art, reviews and notes remain. Requires explicit user intent.' },
  sources_list: { schema: obj(project), description: 'List linked source folders with item counts, scan progress, and the last scan report.' },
  source_add: { schema: obj({ ...project, root: z.string().min(1).max(1000), include: globs.optional(), exclude: globs.optional() }), description: 'Link an absolute folder and start importing its PNG, WebP, and JPEG files. Globs match forward-slash paths relative to the root, e.g. exclude ["concepts/**"]. Dot-folders are skipped. Returns immediately; use source_get with wait to follow the scan.' },
  source_get: { schema: obj({ ...source, wait: z.number().int().min(0).max(120).optional() }), description: 'Inspect a source; with wait (seconds), first wait for a running scan to finish.' },
  source_rescan: { schema: obj(source), description: 'Rescan a source: new files are added, changed files become new synced revisions, absent files are marked missing. Reviews and notes stay attached.' },
  source_remove: { schema: obj(source), description: 'Remove a source and its items, revisions, and stored copies from the project. Files in the source folder are not touched. Requires explicit user intent.' },
  import_files: { schema: obj({ ...project, paths: z.array(z.string().min(1).max(1000)).min(1).max(200), collection: z.string().trim().max(200).optional() }), description: 'Import loose image files (absolute paths) as unlinked items. Unlinked items cannot be exported back to a source.' },
  search_items: { schema: obj({ ...project, ...itemFilter, limit: z.number().int().min(1).max(2000).optional(), offset: z.number().int().min(0).optional() }), description: 'Find items by name, path, description, or tag; filter by collection (source folder), source, or status (pending_export, missing, open_notes). Returns totals and collection counts.' },
  item_get: { schema: obj(item), description: 'Inspect an item with every revision, its provenance, its linked source file, and its note threads.' },
  item_update: { schema: obj({ ...item, name: z.string().trim().min(1).max(120).optional(), description: z.string().max(4000).optional(), tags: tagsSchema.optional(), selectedRevisionId: idSchema.optional() }), description: 'Edit item metadata or choose which revision is selected. Selecting a revision other than the synced one makes the item pending export.' },
  item_delete: { schema: obj(item), description: 'Delete an item with its revisions, review entries, and notes. A linked item reappears on the next rescan while its file exists. Files in source folders are not touched. Requires explicit user intent.' },
  review_list: { schema: obj({ ...project, status: z.enum(['active', 'completed', 'all']).default('active') }), description: 'List reviews with decision counts and open notes. Defaults to active; completed finds archived reviews.' },
  review_create: { schema: obj({ ...project, title: reviewTitle, instructions: instructionsSchema.optional(), itemIds: itemIdsSchema.optional(), filter: obj(itemFilter).optional(), open: z.boolean().default(true) }), description: 'Create a review from ordered itemIds or from a search_items filter (up to 5000 items). Opens it in the PixelChat window unless open is false.' },
  review_get: { schema: obj({ ...review, decision: decisionSchema.optional(), includeResolved: z.boolean().default(false), afterRevision: z.number().int().min(0).optional(), wait: waitSeconds.optional() }), description: 'Read a review: entries with decisions (stale when the item changed after the decision) and the note threads on its items, including pins. Resolved threads are omitted unless includeResolved. With afterRevision and wait (seconds), first wait until the review revision passes afterRevision; compare revisions to tell a change from a timeout.' },
  review_update: { schema: obj({ ...review, expectedRevision, title: reviewTitle.optional(), instructions: instructionsSchema.optional(), itemIds: itemIdsSchema.optional(), addItemIds: itemIdsSchema.optional() }), description: 'Change title, instructions, or entries only if the review revision still matches. itemIds replaces the ordered list (retained items keep decisions); addItemIds appends.' },
  review_decide: { schema: obj({ ...review, decisions: z.array(obj({ itemId: idSchema, decision: decisionSchema })).min(1).max(5000) }), description: 'Record accept, needs_changes, reject, or unreviewed for review entries. Decisions apply to each item\'s currently selected revision. Decisions are the user\'s call; agents record them only when asked.' },
  review_complete: { schema: obj({ ...review, expectedRevision }), description: 'Mark a review completed. Locks decisions, entries, and new notes until review_reopen.' },
  review_reopen: { schema: obj({ ...review, expectedRevision }), description: 'Restore a completed review to active. Completion stays in the activity log.' },
  review_delete: { schema: obj(review), description: 'Delete a review and its decisions. Notes stay on their items. Requires explicit user intent.' },
  activity_get: { schema: obj({ ...project, reviewId: idSchema.optional(), itemId: idSchema.optional(), afterSeq: z.number().int().min(0).default(0), limit: z.number().int().min(1).max(500).default(200), wait: waitSeconds.optional() }), description: 'Attributed changes in order (decisions, notes, selections, imports, jobs, exports), optionally for one review or item. Returns entries and a cursor; pass the cursor as afterSeq to continue. With wait (seconds), first wait until a matching change exists after afterSeq.' },
  generate_images: { schema: obj({ ...project, ...imageOptions, itemId: idSchema.optional(), collection: z.string().trim().min(1).max(200).optional(), name: z.string().trim().min(1).max(120).optional() }), description: `Generate images from a prompt. Billed to the user's provider account: run only for the user's request. Results become new items in collection (default "Generated"), or new versions of itemId. Returns queued jobs; follow them with jobs_get.` },
  edit_image: { schema: obj({ ...item, ...imageOptions, revisionId: idSchema.optional() }), description: `Edit an item's selected version (or revisionId) with a prompt. Billed to the user's provider account: run only for the user's request. Each result becomes a new version of the item for the user to choose; the selection is not changed. Returns queued jobs; follow them with jobs_get.` },
  jobs_get: { schema: obj({ ...project, jobIds: z.array(idSchema).min(1).max(200).optional(), batchId: idSchema.optional(), itemId: idSchema.optional(), active: z.boolean().optional(), wait: z.number().int().min(0).max(600).optional() }), description: 'List image jobs (newest first), filtered by ids, batch, item, or active. With wait (seconds), first wait until the matching jobs finish. Uncertain jobs may have been billed without a recorded result and are never replayed.' },
  job_cancel: { schema: obj({ ...project, jobId: idSchema }), description: 'Cancel a queued image job. A running request is already with the provider and cannot be cancelled.' },
  export_item: { schema: obj({ ...item, destination: z.string().min(1).max(1000).optional(), force: z.boolean().optional() }), description: `Write an item's selected version to its linked source file, making it the synced version, or to an absolute destination file. Refuses with SOURCE_CHANGED when the source file differs from what PixelChat last read, and with DESTINATION_EXISTS for an existing destination; force overwrites only with explicit user intent. Find work to export with search_items status pending_export.` },
  view_image: { schema: obj({ ...item, revisionId: idSchema.optional(), pins: z.boolean().default(true), background: z.string().regex(/^#[0-9a-fA-F]{6}$/).default('#808080'), maxSize: z.number().int().min(256).max(2048).default(1024) }), description: 'Look at an item version (default: selected) as a PNG image: transparency flattened onto background, open note pins drawn and numbered in viewer order (dashed when written on another version). Small art is enlarged by whole pixels, large art reduced to maxSize. Use a background unlike the art to judge edges.' },
  ui_open: { schema: obj({ ...project, reviewId: idSchema.optional(), itemId: idSchema.optional() }), description: 'Show a review (optionally focused on one of its items) or a single item in the PixelChat window.' },
  note_add: { schema: obj({ ...item, body: noteBody, reviewId: idSchema.optional(), revisionId: idSchema.optional(), parentId: idSchema.optional(), pin: pinSchema.optional() }), description: 'Add a note on an item, optionally in a review and pinned to a region of a revision (default: the selected revision). parentId replies to a thread; replies inherit its review and revision and cannot be pinned.' },
  note_update: { schema: obj({ ...note, body: noteBody.optional(), pin: pinSchema.nullable().optional(), resolved: z.boolean().optional() }), description: 'Edit a note body or pin (agents: own notes only), or resolve/reopen a root thread. Resolve a thread after its change is made, and reply to say what changed.' },
  note_delete: { schema: obj(note), description: 'Delete a note with its replies. Agents may delete only their own notes.' },
};
export type OperationName = keyof typeof operationDefinitions;

/** The MCP tool list; built into the CLI so agents connect without waiting on the app. */
export function operationCatalog() {
  return Object.entries(operationDefinitions).map(([name, value]) => ({ name, description: value.description, inputSchema: z.toJSONSchema(value.schema) }));
}
