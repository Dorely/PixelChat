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
zod schema and description; the UI calls it with origin `user`, and the assistant, CLI,
and MCP will call it with origin `agent`. Failures return `{ ok: false, error: { code,
message } }` envelopes with redacted messages.

The renderer reaches the main process through one IPC channel, `pixel:call`, limited to
fixed commands (`snapshot`, `setActiveProject`, `operation`, `chooseDirectory`,
`openLogs`, `uiReady`, `uiAcknowledge`) with zod tuple schemas. Requests from any frame
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

Schema version 1 holds `settings`, `projects`, `sessions`, `sources`, `items`,
`revisions`, and `activity`; project deletion cascades. A `Default` project is created
when none exist, and the active project is kept in the `project-active` setting.

Every mutation appends an `activity` row (project, origin, kind, item or review, data).
It is the record agents will follow for changes.

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

## Verification policy

This project carries no automated tests. Verification is `npm run typecheck`, the
production build, and live/manual runs of the app, recorded in [verification](verification.md).
