# PixelChat architecture

## Runtime and ownership

PixelChat uses Electron, React/TypeScript, electron-vite, and electron-builder with a
Windows x64 NSIS target. Exact versions live in the lockfile. This document describes
implementation; [verification](verification.md) records actual checks.
[VISION](../VISION.md) remains product intent.

The main process owns persistence, media, providers, credentials, jobs, and turns.
React owns presentation, selection, and forms. The sandboxed, isolated preload exposes
fixed typed operations. The renderer has no Node integration.

The profile lives at `%APPDATA%/PixelChat`: `library.sqlite` (WAL, foreign keys on) and
`logs/diagnostics.jsonl`. Credentials go only through `providers/secrets.ts` (Electron
`safeStorage`); none are stored in SQLite, logs, or prompts.

## Operations and renderer bridge

`src/main/operations.ts` is the single operation registry. Each operation has a strict
zod schema and description; the UI calls it with origin `user`, and the inline assistant,
the CLI, and MCP (through the local bridge) call it with origin `agent`. Failures return `{ ok: false,
error: { code, message } }` envelopes with redacted messages.

The renderer reaches the main process through one IPC channel, `pixel:call`, limited to
fixed commands (`snapshot`, `setActiveProject`, `operation`, `chooseDirectory`,
`openLogs`, `uiReady`, `uiAcknowledge`, `installIntegration`, and the credential commands
`codexLogin`, `codexCancelLogin`, `codexLogout`, `saveGeminiKey`, and the chat commands
`chatMessages`, `sendMessage`, `cancelTurn`, `chatContext`, `setActiveChat`,
`saveChatProvider`, `deleteChatProvider`, `listChatModels`, `testChatModel`,
`saveChatModel`, `deleteChatModel`) with zod tuple schemas. Requests from any frame
other than the main window's top frame are rejected. Store changes are debounced into
`pixel:changed`; the renderer then refreshes its snapshot. Operations can drive the view
through `ui-port.ts`, which sends `pixel:ui-command` and waits up to 15 seconds for the
renderer's acknowledgement.

The window has no menu, denies `window.open`, navigation, and permission requests, and
loads media only through the `pixel-media://` protocol.

## Browser development host

`npm run dev:browser` builds the app and starts Electron with `PIXELCHAT_BROWSER=1`
(unpackaged only). Instead of a window, a loopback HTTP server serves the built renderer
plus `/rpc` (the same fixed commands), `/events` (server-sent change and UI-command
events), and `/media/`. It uses the real profile, so browser checks touch the same
library as the desktop app.

## Persistence

Schema version 2 holds `settings`, `projects`, `sessions`, `sources`, `items`,
`revisions`, `activity`, `reviews`, `review_items`, `notes`, `jobs`, and the chat tables
`messages`, `chat_history`, and `tool_calls`; migrations run in order, and project and
session deletion cascade. A `Default` project is created
when none exist, and the active project is kept in the `project-active` setting.

Every mutation appends an `activity` row (project, origin, kind, item or review, data).
`activity_get` pages it by `seq` (optionally for one review or item) and can wait for the
next matching change; agents follow it to see what the user did.

## Library and media

An **item** is one tracked artwork. A **revision** is an immutable image: its exact
bytes live at `originals/<revisionId>.<ext>` under the profile, with a 384 px WebP
thumbnail at `thumbnails/<revisionId>.webp`. Files are written to a `.partial` name and
renamed into place. Width, height, format, alpha presence, and whether any pixel is
transparent come from sharp. Provenance is stored as JSON per revision.

A **source** links a folder (absolute root, include and exclude globs over forward-slash
relative paths, case-insensitive). Dot-folders such as `.godot` are skipped, and only
PNG, WebP, and JPEG files are imported. Scans run in the background, four files at a
time, and report progress through `sources_list`/`source_get`:

- a new path creates an item whose collection is its folder;
- a path whose bytes hash differently from the synced revision gets a new revision
  (parent = previous synced), which becomes synced, and selected too unless another
  version was chosen;
- linked items not seen are marked missing, and cleared when the file returns.

Each linked item tracks `syncedRevisionId` (matches the file on disk) and
`selectedRevisionId` (the version the user wants). **Pending export** means a linked item
whose selected revision is not its synced one. Loose-file imports create unlinked items,
which have no source to export to.

Search uses `LIKE` over name, source path, description, and tags rather than FTS: the
first libraries are a few thousand items, and the query is simple to replace later.

Media is served by `pixel-media://original/<revisionId>` and
`pixel-media://thumb/<revisionId>`, only for revisions in the active project, with
immutable cache headers. The browser host maps `/media/...` to the same handler.
Removing a source, item, or project deletes its stored copies; files in source folders
are never modified by import or removal.

## Reviews and notes

A **review** is an ordered set of items with a title, optional instructions, and a status
(`active` or `completed`). Each entry carries a decision (`unreviewed`, `accepted`,
`needs_changes`, `rejected`) with who decided it, when, and against which selected
revision. An entry is **stale** when the item's selected revision has changed since the
decision, so revised art comes back for another look. Reviews are created from item ids
or from a `search_items` filter (up to 5000 items).

Every review change increments its `revision`. Title, instructions, entry changes,
completion, and reopening require `expectedRevision` and fail with `REVISION_CONFLICT`
when it is out of date. Decisions do not, because each one targets a single entry.
Completing locks decisions, entries, and new review notes until the review is reopened;
replies and resolution stay open so agents can answer after completion. Deleting a review
keeps its notes on their items.

A **note** belongs to an item and a revision, and optionally to a review. Root notes may
carry a pin in that revision's pixels: a point `{x, y}` or a box `{x, y, width, height}`,
rounded and clamped to the image. Replies hang off a root (replies to replies attach to
the root) and carry no pin. Root notes are resolved or reopened by either side; agents may
edit or delete only their own notes. Item summaries count open root notes, and
`search_items` filters by `open_notes`.

`ui_open` shows a review (optionally at one of its items) or a single item. The main
process switches to the command's project; the renderer selects the view and
acknowledges.

In the viewer, pins are drawn over the image's rendered content box (measured with a
`ResizeObserver`, accounting for `object-fit`). Click places a point; drag places a box.
Pins written against another revision are dashed. Review keys: A accept and advance,
R reject and advance, C needs changes and focus the note composer, U clear, N note.

## Image providers and jobs

The model decides the provider (`src/shared/image-models.ts`): Codex models run over the
ChatGPT sign-in (`providers/codex-images.ts`), Gemini models over an API key
(`providers/gemini-images.ts`). Both implement one `ImageProvider.run` contract and map
failures to `ImageProviderError`, whose `responded` flag says whether the provider
answered. Provider details are in [knowledge](knowledge/README.md).

Credentials are set only through the fixed credential commands, never through operations.
Codex OAuth tokens and the Gemini key are kept in `<profile>/secrets` by `SecureStore`;
the snapshot and `status` report only readiness.

`generate_images` and `edit_image` (`src/main/images.ts`) validate the request and queue
one durable `jobs` row per image. A caller `requestId` is the batch id: repeating it
returns the original jobs, and reusing it with different arguments is a
`REQUEST_CONFLICT`. Admission enforces model quality, input-image limits, and
transparency: `background: auto` on a transparent source means keep transparency, which
needs a model with native alpha and a non-JPEG format. Background instructions are chosen
by code and appended to the user's prompt, which is otherwise sent verbatim. Edits default
to the source's dimensions and to the format of the file they will be exported to.

A scheduler runs up to four jobs at once. A 429 with a Retry-After of at most 120 seconds
is retried up to twice; nothing else is retried. When the provider never answered
(timeout, transport, abort), the job becomes `uncertain`: it may have been billed and is
never replayed. Jobs running at shutdown become `uncertain` on the next start; queued jobs
resume. Only queued jobs can be cancelled. `jobs_get` lists jobs and can wait for them.

A result is stored as a new revision (`generated` or `edited`, parent = source) of the
target item, or as a new item in the "Generated" collection. The selection does not
change. When the provider's format differs from the requested one, the result is
converted without resizing and the provider's bytes are kept beside it
(`provenance.providerFile`). Provenance records the job, batch, provider, model, quality,
background, requested and resolved size, exact prompt and instructions, input revisions,
request and response ids, and usage. A transparency request that yields a fully opaque
result finishes with a warning.

## Export

An item is pending export when its selected revision differs from its synced one.
`export_item` writes the selected revision's exact bytes to the linked source file
(or an absolute destination) through a temporary file and rename. It refuses with
`FORMAT_MISMATCH` when the file extension does not match the format, `SOURCE_CHANGED`
when the source file no longer hashes to the synced revision or is missing, and
`DESTINATION_EXISTS` for an existing destination; `force` overrides the last two.
Exporting to the source makes the exported revision the synced one.

In the viewer, the Edit panel runs the user's prompt on the viewed version, can append
open note threads (with pin coordinates) to the prompt, and lists the item's jobs.
Versions show the model and a prompt excerpt; "Export to source" appears while the item
is pending export and asks before overwriting a changed or missing file.

## Inline assistant

A conversation is a session. `chat.ts` runs at most one turn per conversation: it stores the
user's message (naming an automatically titled conversation after it), then loops model
request → tool calls → tool outputs until the model answers without calls or the user stops
it. The provider input history is kept in `chat_history`; visible messages (user, assistant,
and activity lines for mutating tools) in `messages`.

Tools are the operation registry (`assistant.ts`) minus project and conversation
management, with `projectId` (and `sessionId` for `rename_session`) filled in. Every call is
recorded in the `tool_calls` ledger before it runs, and a call id already in the ledger
returns its recorded result instead of running again. `generate_images` and `edit_image`
get a `requestId` stored in the ledger before admission. At startup, running replies become
failed and in-flight calls uncertain, with the requestId to recover rather than replay.
Waiting reads stop waiting when the turn is stopped.

Each request's instructions add the project's item, pending-export, active-job, and review
counts, the conversation title and who set it, and what the user has open (the viewed
review and item), which the renderer sends with each message. `view_image` attaches its PNG
to the next request as an input image and shows a thumbnail in the conversation; only the
newest eight images stay in history. Outputs over 200,000 characters are refused with a
request to narrow.

Chat models (`chat-registry.ts`) are the built-in Codex models over the ChatGPT sign-in
(`providers/codex.ts`) plus custom endpoints (`providers/generic/`: Chat Completions,
Responses, Anthropic Messages). A custom model is saved only after a capability test passes
for every listed effort; the test also probes image input, and a model that cannot see
images is not offered `view_image`. Endpoint keys are in secure storage; a direct Gemini
endpoint uses the Gemini image key. The composer's context meter estimates tokens with
tiktoken (images by pixel count) against the Codex default context size.

## Agent workbench

The app publishes a token-authenticated named-pipe bridge (`bridge.ts`) whose discovery
file is DPAPI-protected. The bundled CLI (`src/cli/index.ts`, built to `out/agent/cli.cjs`
with a pinned Node runtime) forwards operations over it and serves them as a stdio MCP
server with review and pending-export resources. `view_image` renders a revision for
agent vision: flattened onto a chosen backdrop, enlarged by whole pixels or reduced to a
bound, with open note pins drawn and numbered. The skill and its references live in
`resources/skill`; `shared/guidance.ts` embeds the same texts for the `guidance`
operation and the installer. See [agent workbench](agent-workbench.md).

## Verification policy

This project carries no automated tests. Verification is `npm run typecheck`, the
production build, and live/manual runs of the app, recorded in [verification](verification.md).
