# Verification evidence

Dated record of checks actually run. Compilation never establishes provider, login,
secure-storage, or platform integration.

## 2026-10-29 — Stack reset

- Removed the .NET solution and test project.
- `npm install`, `npm run typecheck`, and `npm run build` succeeded on Windows 10 x64, Node 22.18.

## 2026-10-29 — Core infrastructure and app shell

- `npm run typecheck` and `npm run build` succeeded.
- `npm run dev:browser` served the shell at the printed loopback URL. In the built-in
  browser: created a conversation, renamed it by double-click, deleted it through the
  confirmation dialog; created a project (it became active), switched projects, deleted
  the project (the remaining `Default` became active); switched themes. No console errors.
  The host was stopped afterward.
- A desktop launch recorded `app.started` with no start failure and was terminated.
- Not yet exercised: secure storage (no caller yet), `pixel-media` serving (placeholder
  404), and UI commands (no operation sends one yet).

## 2026-10-29 — Library and import

- `npm run typecheck` and `npm run build` succeeded.
- Reset the local profile database (schema v1 gained library tables; it held only test data).
- `npm run dev:browser`, built-in browser, project "Validation Art":
  - Added `MermaidGame/assets/characters` through the Add folder dialog (read-only scan).
    157 of 157 PNG files imported in about 2 seconds, in 11 folder collections; progress
    showed while scanning. A rescan reported 157 unchanged.
  - Opened items in the viewer: original image over the checker backdrop, facts, and the
    version list. Arrow keys moved between items; `1` and `+` zoomed; backdrops switched.
  - On a scratch copy of the clam and eel folders (with a `.godot` folder, an excluded
    `concepts/**` folder, and an invalid `broken.png`): 8 added, `broken.png` reported as
    failed, the dot-folder and exclusion skipped. Adding the same folder again returned
    `CONFLICT`. After flipping one file and deleting another, a rescan reported 1 changed
    and 1 missing; the changed item had two revisions with the correct parent, and the new
    one was synced and selected. Selecting the older version made the item pending export
    (filter and badge). Removing the scratch source deleted its stored copies: the profile
    held exactly 157 originals and thumbnails afterward.
  - `import_files` rejected a relative path and imported an absolute one; `item_delete`
    removed it. No startup error banner after reload, and data persisted across host
    restarts.
- A desktop launch recorded `app.started` with no start failure and was terminated. The
  desktop `pixel-media` protocol shares the handler exercised through the browser host but
  was not inspected visually.

## 2026-10-29 — Reviews and notes

- `npm run typecheck` and `npm run build` succeeded.
- Reset the local profile database again (schema v1 gained review and note tables) and
  re-imported `MermaidGame/assets/characters` read-only into "Validation Art": 157 items.
- `npm run dev:browser`, built-in browser:
  - Library filtered to `clam`, then "Review these": the dialog showed 8 items; the review
    opened with its instructions and decision filters.
  - "Continue reviewing" then A, R, C pressed in quick succession recorded accepted,
    rejected, and needs changes on the first three items. The first attempt put all three
    on one item (stale index in the key handler); fixed with a position ref and checked
    again.
  - Dragged a box pin and clicked a point pin on `lid`, adding a note to each; both drew
    numbered over the image. A note typed while the previous one was saving was lost on
    the first attempt; the composer now clears only the text it submitted.
  - Through operations: a reply attached to its thread and inherited the review; a pinned
    reply and a half-specified box were rejected; a pin at (9999, -20) was clamped to
    (709, 0) with the box shrunk to fit; resolving hid the thread unless
    `includeResolved`; `open_notes` filtered to the one item. `review_complete` with an
    old revision returned `REVISION_CONFLICT`; after completion, decisions and new review
    notes returned `REVIEW_COMPLETED` while replies still worked; the review moved to the
    completed list, and reopening restored it. Reordering entries kept their decisions;
    `review_history` listed creation, decisions, notes, resolution, completion, reopening,
    and the update.
  - On a scratch copy of the eel folder: after accepting all five and pinning a note on
    `head`, replacing `head.png` and rescanning gave one changed item. The board showed
    "Changed since" with its own filter, the viewer badge read "Accepted · changed since",
    and the note's pin was dashed with an "Other version" badge. The scratch source and
    its review were removed afterward.
  - `ui_open` with a review and item opened the board viewer at that item; with only an
    item it switched to Library and opened it, without the decision bar.
  - After restarting the host, the review, decisions, notes, replies, and resolution
    persisted; no error banner appeared.
- A desktop launch recorded `app.started` with no start failure and was terminated.
- Not covered: agent-origin rules (agents editing or deleting only their own notes)
  have no agent caller until the CLI and MCP land; they are checked in code only.

## 2026-10-29 — Image providers, jobs, and export

- `npm run typecheck` and `npm run build` succeeded.
- Reset the local profile database (schema v1 gained the jobs table) and imported a
  scratch copy of the eel folder (5 items); the real game repository was not touched.
- `npm run dev:browser`, built-in browser, with no provider signed in:
  - Settings showed Codex "Not signed in" with a sign-in button and Gemini "No key" with
    a key field; secure storage reported available. Sign-in and key saving were not
    exercised.
  - The viewer's Edit panel listed all five models as not signed in and disabled Run
    with a sign-in hint. "Add open notes" appended both open threads, the pinned one
    with its box coordinates.
  - Through operations: an Image 2.5 Flare edit with count 2 queued two jobs; repeating
    the requestId returned the same jobs, and changing the prompt returned
    `REQUEST_CONFLICT`. Gemini 3 Pro Image on the transparent source and a transparent
    JPEG request were refused at admission. `jobs_get` with wait returned both jobs
    failed with "Codex is not signed in." (no network request), and the job list showed
    them.
  - Export on the scratch copy: flipped `head.png` and rescanned (new synced version),
    selected the original version (pending export), then changed the file again. "Export
    to source" opened the source-changed confirmation; overwriting wrote the selected
    version's exact bytes (SHA-256 matched), left no temporary file, made it the synced
    version, and cleared pending export.
  - The viewer opened on a stale version after a selection change made just before
    opening; it now starts from the loaded item's selection, checked again.
  - No console errors.
- A desktop launch recorded `app.started` with no start failure and was terminated.
- Gaps: no live Codex or Gemini request has run, so provider wire shapes, result
  ingestion, format conversion, provenance from a real result, and the opaque-result
  warning are unverified. Codex sign-in, Gemini key save, and desktop (non-browser)
  behavior were not exercised.

## 2026-10-29 — Agent workbench

- `npm run typecheck` and `npm run build` succeeded, including the CLI bundle, skill
  output, and the CMake/MSVC build of `pixelchat-helper.exe`. The pinned Node runtime
  matched its SHA-256.
- Browser host on the scratch eel project (5 items), through the built CLI:
  - `status`, `tools` (41 operations), and `guidance` worked over the bridge; the
    guidance hash matched `out/skill/guidance.json`, and an unknown topic returned
    `INVALID_ARGUMENTS`.
  - `review_get` with `afterRevision` and `wait: 2` returned the unchanged review after
    about 2.2 s; with `wait: 20` it returned revision 2 about 2 s after a decision made
    1.5 s in. A concurrent `activity_get` wait woke with `review.decided` and advanced
    its cursor.
  - `view_image`: the 1282×256 body reduced to 1024×204 on a cyan backdrop with a box pin
    and a point pin numbered 1 and 2 (inspected visually); `pins: false` drew none; a
    245×269 head was first left at scale 1, fixed to enlarge by 2 to 490×538 and checked
    again; a non-hex background returned `INVALID_ARGUMENTS`.
- MCP over raw stdio JSON-RPC (protocol 2025-06-18): 41 tools; `view_image` returned
  text plus PNG image content; both resource templates listed; the pending-exports
  resource listed `head`. With subscriptions, selecting `head`'s synced version sent
  `resources/updated` for pending-exports, and a review decision sent it for the review.
- `skill install --scope project --target claude --target codex --mcp` into a scratch
  directory: dry run listed the files; install wrote both skills, `.mcp.json`, and
  `.codex/config.toml` with absolute runtime and CLI paths; status reported them
  up to date; uninstall removed them and kept backups.
- Settings → Agent integration rendered in the browser host and its Installation status
  call returned the scratch directory's file states.
- With a stale discovery file, the CLI started the development desktop app and
  connected. `npm run package` built `PixelChat Setup 0.3.0.exe`; the unpacked build's
  bundled CLI started the packaged app (`app.started`, packaged) and served `status` and
  `view_image`. All app processes were terminated.
- Gaps: the installer itself was not run, so the NSIS PATH hooks are unverified. No
  real Codex or Claude Code session connected; MCP was exercised with a raw client. A
  modern-era (2026-07-28) client was not tried.

## 2026-10-29 — Inline chat

- `npm run typecheck` and `npm run build` succeeded.
- Browser host on the scratch eel project:
  - A new conversation was titled from its first message. The composer showed the context
    meter (about 9,600 tokens of 272,000), the model and effort pickers, and "Viewing an
    item" while the head viewer was open; closing the viewer cleared it. A changed effort
    persisted in the snapshot.
  - With Codex not signed in, a message failed with "Codex is not signed in." and a
    diagnostic reference; no request left the machine.
  - A focus naming an item outside the project was refused; saving an untested custom
    model was refused.
- A local stand-in Chat Completions server (scratch script, no billed calls) was added as a
  custom endpoint: model listing, the capability test (vision true), save, and selection
  worked. A turn called `view_image` on `head`: the activity line and thumbnail showed in
  the conversation, the follow-up request carried the PNG, and the model's reply
  completed. This found that Chat Completions requests sent Responses-shaped tools, so the
  model saw no tool names; the client now nests them as `function` entries. The endpoint
  was removed afterward.
- A desktop launch recorded `app.started` with no start failure and was terminated.
- Gaps: no live Codex, Responses, or Anthropic chat request has run. Stop during a turn,
  startup interruption of a running turn, the billed-tool requestId path, history image
  eviction, and chat in the desktop window were not exercised.

## 2026-10-30 — MCP startup without the app

- Claude Code reported `pixelchat` connection timeouts (30 s) against a debug run of the
  app. `pixelchat mcp` waited for the app before answering `initialize`, and an
  unreachable bridge meant a 30-second launch wait, the same as Claude Code's limit.
- `npm run typecheck` and `npm run build` succeeded.
- Raw stdio MCP client against the running debug app: `initialize` in about 100 ms,
  `tools/list` 41 tools, `tools/call status` returned the app status.
- Same client in a sandbox whose view of `%APPDATA%` held a stale discovery file:
  `initialize` and `tools/list` answered at once; `tools/call status` returned an error
  result with `APP_UNAVAILABLE` ("open but its agent bridge is not answering") after
  about 0.4 s, when the launched second instance exited.
- `pixelchat tools` listed 41 tools with no app reachable. `claude mcp get pixelchat` from
  a game project reported Connected.
- Gaps: a cold start (no app running) launched from a tool call, and the 5-second probe
  against a paused main process, were not exercised.

## 2026-10-30 — App launch outside agent containers

- Claude Code's `pixelchat` tools in Claude Desktop failed with `APP_UNAVAILABLE` while the
  installed app answered an unsandboxed CLI. Claude Desktop is MSIX: its child processes
  read `%APPDATA%` through `Packages\AnthropicPBC.Claude_…\LocalCache\Roaming`, which
  held a PixelChat profile from an app launched in an earlier session. Moving that copy
  aside made the tools answer (`status`, `projects_list`, `search_items`, `review_list`).
- From a shell inside Claude's container, a script started directly wrote its `%APPDATA%`
  file into the container copy; the same script started through `explorer.exe` wrote to
  the real `%APPDATA%`.
- `npm run typecheck` and `npm run package` succeeded; the new `cli.cjs` was copied into
  the per-user install. With PixelChat closed, the installed CLI run inside Claude's
  container (`call projects_list`) started PixelChat and returned the real profile's
  project in 0.8 s. The app's parent was the desktop `explorer.exe`, the real profile
  logged `app.started` (packaged) and a new discovery file, and no container copy appeared.
- Gaps: the fail-fast path for a running app whose bridge does not answer, and the
  development CLI's direct Electron launch, were not exercised.

## 2026-10-30 — Immutable versions, review waits, fit view, card decisions

- `npm run typecheck` and `npm run build` succeeded. `@iarna/toml` parses the installer's
  `tool_timeout_sec = 1900` as a number, so a matching Codex entry reads as identical.
- A temporary helper outside the repository ran the real `Store` and `Operations` on a copy
  of the profile library (the live profile untouched):
  - The copy migrated to schema 3. The completed Chimera review kept every decision on the
    version it was made on; the one item edited after its rejection showed the rejected
    older version with one newer version, the rest were on their current versions.
  - On the completed review, `review_decide`, `review_update` (add items), and a root
    `note_add` returned `REVIEW_COMPLETED`; deleting one of its items returned
    `IN_COMPLETED_REVIEW`.
  - A new review created from the same item ids held 5 entries, all unreviewed, all on the
    current versions. A review holding two versions of one item kept separate decisions.
  - `review_wait`: a 1-second wait with no activity timed out with no events; a waiting
    call ignored an agent note and woke about 1.05 s after a user decision, returning only
    the decision; `from: "any"` returned both; a user note without a revision attached to
    the reviewed version; `until: "completed"` returned the note and `review.completed`.
- With the user's installed app closed (the profile backed up first), `npm run dev:browser`
  started outside Claude's container on the real profile, which migrated to schema 3:
  - The completed Chimera review showed 5 versions with their decisions; each card had
    Accept / Changes / Reject, all disabled; the bracer card showed its rejected older
    version with "1 newer". `review_decide` on it returned `REVIEW_COMPLETED`.
  - In a scratch project (copies of three stored images): `import_files` with `itemId` added
    a version to one item; a review from the item ids showed that item's entry as "1 newer".
    Card buttons accepted and rejected entries, and a second click on Accept returned the
    entry to unreviewed. The viewer opened in Fit. `review_update` with `addRevisionIds` put
    the new version beside the old one as its own unreviewed entry.
  - `pixelchat watch-review` printed each user decision as it was made. After the start
    fix, `--from any` printed `watch.started` at once, then the agent's `review.updated`,
    the user's `review.completed`, and `watch.ended` (completed), and exited.
  - `ui_open` on a review in another project used to show "Review not found in project"
    (the command reached the view before the project switch); after holding the target
    until its project is active, the review opened. The scratch project was deleted and
    the host stopped.
- `npm run package` built the installer; a silent install replaced the per-user copy, and
  the installed CLI read the migrated reviews. `skill install --scope user --mcp --force`
  for codex and claude replaced the previous shipped skill files (identical to the last
  commit) and added `tool_timeout_sec = 1900` to the Codex entry; nothing else in either
  config changed.
- Gaps: Fit on art smaller than the stage (the samples were all larger), a Codex client
  holding a long `review_wait`, and the MCP resource watch were not exercised.
