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

Schema version 1 holds `settings`, `projects`, and `sessions` (cascading on project
delete). A `Default` project is created when none exist, and the active project is kept
in the `project-active` setting.

## Verification policy

This project carries no automated tests. Verification is `npm run typecheck`, the
production build, and live/manual runs of the app, recorded in [verification](verification.md).
