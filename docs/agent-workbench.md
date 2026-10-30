# Agent workbench

PixelChat ships a Windows x64 console command and stdio MCP server for Codex, Claude Code,
and other harnesses. Both drive the desktop app's library, reviews, notes, image jobs, and
exports through the same operation registry as the UI, with origin `agent`. They need no
separate Node installation, credentials, daemon, or remote service. The desktop starts on
demand and stays open when clients disconnect.

## Install

The installer adds `resources\agent` to the user PATH (removed again on uninstall). Open a
new terminal afterward:

```powershell
pixelchat help
pixelchat skill install --scope project --project C:\work\game --target claude --target codex --mcp --dry-run
pixelchat skill install --scope project --project C:\work\game --target claude --target codex --mcp
pixelchat skill install --scope user --target claude --mcp
pixelchat skill status --scope project --project C:\work\game --target claude --mcp
pixelchat skill uninstall --scope project --project C:\work\game --target claude --mcp
```

Settings → Agent integration offers the same install, preview, status, uninstall, and
conflict replacement, plus a connection check.

| Scope | Codex skill | Claude Code skill | MCP configuration |
| --- | --- | --- | --- |
| Project | `<project>/.agents/skills/pixelchat` | `<project>/.claude/skills/pixelchat` | `<project>/.codex/config.toml`, `<project>/.mcp.json` |
| User | `~/.agents/skills/pixelchat` | `~/.claude/skills/pixelchat` | `$CODEX_HOME/config.toml` (default `~/.codex`), `~/.claude.json` |

MCP registration happens only with `--mcp`. The server is named `pixelchat` and points at
the installed runtime and CLI by absolute path, so project registration is machine-local
and moving the app requires reinstalling. Identical files are not rewritten. Differing
skill files or a different `pixelchat` MCP entry fail before writing unless `--force`,
which backs the originals up beside them. Uninstall removes only installed files and the
server entry; backups remain. `--directory` installs the skill under a custom parent.

The skill (`resources/skill`) holds the workflow, library/export, and image-editing
references. Its `guidance.json` records the reference hash; the `guidance` operation
returns the app's current hash so an agent can tell when its installed copy is stale.

## CLI

```powershell
pixelchat status
pixelchat tools
pixelchat guidance
pixelchat call search_items --json '{"projectId":"...","status":"pending_export"}'
pixelchat call review_get --input request.json
pixelchat mcp
```

Every command prints one JSON envelope (`{ ok, result }` or `{ ok: false, error: { code,
message, details? } }`) and exits 1 on failure. `tools` lists every operation with its JSON
schema. Requests time out after 11 minutes, covering the longest operation wait.

## MCP

Tools mirror the operation catalog. Results arrive as JSON text and structured content;
`view_image` also returns its PNG as image content so the agent sees the art. Review
results carry a resource link.

Resources, with subscription notifications (modern clients receive updates without
subscribing):

- `pixelchat://projects/{projectId}/reviews/{reviewId}`: the review with entries,
  decisions, and note threads. Updates when the review revision changes.
- `pixelchat://projects/{projectId}/pending-exports`: items whose selected version
  differs from the source file. Updates when that set or a selected version changes,
  for example after the user picks an edit result or an export lands.
- `pixelchat://guidance`: the current references and model catalog.

Notifications are a convenience. The durable path is polling with waits:
`review_get` with `afterRevision` and `wait`, `activity_get` with `afterSeq` (the previous
`cursor`) and `wait`, and `jobs_get` with `wait`. Background watches never launch the
desktop; they retry with backoff while it is closed.

## Bridge and security

The app listens on a named pipe `\\.\pipe\pixelchat-<random>` and accepts one
newline-delimited JSON request per connection, authenticated with a random 256-bit token
compared in constant time. Unauthenticated connections are dropped after 60 seconds and
requests over 2 MiB are refused. The pipe name and token are written to
`%APPDATA%\PixelChat\bridge\endpoint.bin`, encrypted with DPAPI for the current user, in a
directory whose ACL admits only that user and SYSTEM. Both are regenerated on every start,
and the file is removed on quit. If the bridge cannot start, the app records
`bridge.failed` in its diagnostic log and runs without agent access.

The CLI checks a discovered bridge with a 5-second `status` probe. When discovery is
missing or stale, or the probe fails, it starts the app (packaged `PixelChat.exe` beside
`resources`, or the repository's Electron in development) and waits up to 30 seconds for
the bridge. If the launched process exits first (it handed off to an already running
instance whose bridge does not answer, such as one paused in a debugger), it fails at once
with `APP_UNAVAILABLE`.

`pixelchat mcp` and `pixelchat tools` use the catalog built into the CLI and do not contact
the app, so an MCP client connects immediately. Each tool call reaches the app as above;
when it cannot, the call returns an error result with the `APP_UNAVAILABLE` message.

`native/helper.cpp` (`pixelchat-helper.exe`) does the Windows work: DPAPI protect and
unprotect, the private directory ACL, and the installer's PATH entry, which it records in
`HKCU\Software\PixelChat` so uninstall removes only what it added.

## Build

`npm run build` runs `scripts/build-agent.mjs` after the app build. It bundles the CLI to
`out/agent/cli.cjs`, writes the skill to `out/skill`, downloads the pinned Node runtime
(`v24.21.0`, SHA-256 checked) as `pixelchat-runtime.exe`, writes `pixelchat.cmd`, and
builds the helper with CMake (falling back to the Visual Studio copy found by vswhere).
electron-builder copies `out/agent` and `out/skill` to `resources\agent` and
`resources\skill`; `installer/agent.nsh` adds and removes the PATH entry.
