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

## 2026-10-30 — Review switcher and return on completion

- `npm run typecheck` and `npm run build` succeeded.
- With the user's installed app closed, `npm run dev:browser` on the real profile, in a
  scratch project with three active reviews (copies of two stored images): the board header
  menu listed the other active reviews with decided counts; choosing one opened it, and its
  count updated after a card decision. Complete returned to the review list, which no
  longer showed that review among the active ones. Opened from completed, the menu showed
  it as "(completed)" above the active reviews. The scratch project was deleted and the
  host stopped.
- `npm run package` and a silent reinstall succeeded; the installed CLI answered.

## 2026-10-30 — Agent navigation never leaves an open review

- `npm run typecheck` and `npm run build` succeeded.
- With the user's installed app closed, `npm run dev:browser` on the real profile, in a
  scratch project (copies of two stored images):
  - With "First pass" open, `review_create` "Second pass" returned `opened: false,
    offered: true`; the board stayed on First pass and a prompt read "The agent opened
    review Second pass." with Open. `ui_open` for a review in another project also returned
    `offered` and named the project. Open switched project and showed that review.
  - From the review list, `review_create` returned `opened: true` and the board opened.
  - The banners' icon took `flex: 1` and split the width with the text (also on the error
    banner); fixed and rechecked.
  - The scratch project was deleted and the host stopped.
- `npm run package` and a silent reinstall succeeded; the installed app restarted on the
  new build and its CLI answered.

## 2026-10-30 — GIF and animated versions in reviews

- `npm run typecheck` and `npm run build` succeeded.
- The user's installed app was running, so the browser host ran from a scratch Electron
  entry (outside the repository) that pointed `appData` at a scratch profile; the real
  library stayed at schema 3. The fresh profile migrated through schema 4.
- Imported a scratch folder with an 8-frame and a 6-frame transparent GIF, a one-frame
  GIF, and a PNG: four items, frames 8, 6, 1, 1, all with transparent pixels. A review of
  all four rendered on the board; the two animated GIFs' thumbnails were animated WebP
  (`ANMF` chunks), the stills were not, and cards read "gif · 8 frames". The viewer loaded
  the original as `image/gif` and listed "GIF · 8 frames"; its Edit panel read "Image
  edits need a still PNG, WebP, or JPEG version."
- `view_image` returned frame 0 of 8 by default and different pixels for `frame: 4`;
  `frame: 8` was refused. `edit_image` on the animation returned `UNSUPPORTED_IMAGE`.
- Not checked: watching playback in a screenshot (the pane could not capture while the
  window was hidden), the native desktop window, animated WebP imports, and large GIFs'
  import time. The scratch host was shut down and its profile and entry deleted.

## 2026-10-31 — Contest reviews and reference entries

- `npm run typecheck` and `npm run build` succeeded.
- The user's installed app was running, so the browser host ran from a scratch Electron
  entry (outside the repository) with a scratch `appData`; the fresh profile migrated to
  schema 5. Five scratch PNGs were imported.
- Through `window.pixel.operation`: a contest of three with one reference was created
  (counts covered the three candidates); a one-candidate contest and a decision on a
  reference were refused; accepting art1 then art2 left only art2 accepted; accepting two
  in one call was refused. A standard review took references at creation and by
  `addReferenceItemIds`; `referenceRevisionIds: []` cleared them and `mode` switched.
- The board showed the Contest badge, Pick buttons, a "Picked" badge, "Picked: art2 · 1
  reference", and a Reference section whose card has no decision buttons.
- Not checked: the viewer's reference state and keys, the new-review Contest checkbox,
  agent calls over MCP/CLI, and the native window. The scratch host was shut down and its
  profile deleted.
- Follow-up check on a fresh scratch host: the viewer on a reference entry showed a
  "Reference" badge, no decision buttons, and the hint "Reference · N note · click or
  drag to pin"; A on the reference did nothing, and A on a candidate picked it and moved
  on. The new-review dialog's Contest checkbox was stacked and centered; fixed
  (`.check-row` specificity) and rechecked. Scratch host and profile removed.
- `npm run package` built the installer. A silent install from the sandboxed shell wrote
  its Start Menu shortcut into Claude's virtualized `%APPDATA%` and the Finish page failed
  to find it; re-running it unsandboxed installed cleanly (shortcut present, no wizard).
  `skill install --scope user --target claude --target codex --mcp --force` refreshed the
  workflow, library, and image-editing references; the installed app answered `status`.
- Skill text now teaches contests and references (SKILL.md step 8, workflow reference on
  reading the winner). `npm run package`, an unsandboxed silent reinstall, and
  `skill install --scope user --target claude --target codex --mcp --force` followed; all 12
  managed files (Claude and Codex skills, `.claude.json`, `config.toml`) read up-to-date and
  both installed SKILL.md files mention contests. Agent behavior with the new text was not
  exercised.
- Regression found after the install: `SCHEMA_VERSION` was still 4, so the first launch
  migrated the real library to 5 and every later launch refused it ("created by a newer
  PixelChat version"; `app.start_failed` in the diagnostic log). The scratch checks never
  restarted a migrated profile, so they missed it. Set it to 5, repackaged, and reinstalled
  unsandboxed; the app started on the existing library and `status` answered ready.

## 2026-11-02 — Edit masks, note drawings, and cursor zoom

- `npm run typecheck` and `npm run build` succeeded.
- Scratch Electron host with its own `appData` (the real library stays at schema 5 for the
  installed app), `npm run dev:browser`-style loopback renderer in the built-in browser,
  scratch art (800×600 grid, 48×48 blob):
  - Fit opened at 40%. Ctrl+wheel (synthetic `WheelEvent`s with `ctrlKey`) zoomed with the
    image point under the cursor fixed horizontally within 0.6 px; vertically it recentered
    while the image was shorter than the stage, then held. The event was default-prevented.
    Space+drag panned without painting.
  - Draw: a line drawn, Ctrl+Enter added the note with its `sketch`; it showed numbered at
    the stroke start and as "Drawing x, y · w×h" in the thread.
  - Mask: a brush stroke and a Shift+drag box registered; the Edit panel showed
    "Mask · 2 strokes"; Ctrl+Z removed one; Escape returned to Pin.
  - Admission: mask with a Gemini model refused; empty mask (and one fully erased) refused;
    `generate_images` with `mask` refused as an unrecognized key; too many inputs refused
    with "including the edit source and the mask". A Codex masked edit queued and failed
    with "Codex is not signed in." (nothing billed), recording the instructions and mask.
  - `view_image` drew the drawing outlined with label 1 and reported its bounds.
- Rasterizer (an esbuild bundle of `mask.ts` outside the repo): a round stroke, an erase box
  cut out of it, and a single-point dot came out as transparent black in an opaque white PNG
  at the source size.
- Not checked: a real billed Codex masked edit, the native desktop window, physical mouse
  Ctrl+wheel and middle-drag (synthetic events only), and an agent reading drawings over
  MCP/CLI. Scratch host shut down and scratch files removed.
- 2026-11-03: `npm run package` built the installer; the running installed app was closed
  and an unsandboxed silent install replaced it. `skill install --scope user --target claude
  --target codex --mcp --force` updated SKILL.md, the workflow and image-editing references,
  and guidance.json for Claude and Codex (MCP configs already current). The installed app
  migrated the real library and answered `status` (Codex models report `mask: true`, Gemini
  `false`); after closing it, a fresh start answered `projects_list` with the user's three
  projects, so the schema 6 library reopens. Masks and drawings were not exercised in the
  installed app.

## 2026-11-06 — Pick-only contests, hideable chat, remembered layout

- `npm run typecheck` and `npm run build` passed.
- Scratch Electron host (a temporary wrapper outside the repo setting `appData` before loading
  the built main module; the real library stayed at schema 6), scratch 64×64 PNGs. The new
  profile opened at schema 7. Browser host in the built-in browser, through real operations:
  - `review_create`: `picks: 4` of 4 candidates and `picks` on a standard review refused.
  - `review_decide` on a contest: `rejected`, `needs_changes`, and two accepts in one call with
    one pick refused. Accepting another candidate moved the pick; with `picks: 2`, a third
    pick cleared the oldest. A standard review still took `rejected`.
  - `review_update`: lowering `picks` below the current picks, and `picks` at the candidate
    count, refused.
  - Board: cards show only Pick; filters All, Picked, Not picked; "Picked 2 of 2: …"; list and
    switcher show picks against the limit; Review again once the picks are filled.
  - Viewer: only Pick; R and C did nothing, A picked and stayed on the candidate, U cleared.
  - New review dialog: "Winners to pick" appears with Contest, capped below the item count.
  - Chat toggle: from the top bar and viewer header it removed the sidebar and handle and took
    the viewer to the window edge; a composer draft survived hiding; arrow keys changed the
    width; hidden state and view survived a reload.
- Native desktop window on the scratch profile, restarted with a temporary driver outside the
  repo: chat hidden and view returned after a restart, and shown again after the next. Size and
  position (1234×777 at 60,50) returned after closing through `CloseMainWindow`. A renderer
  `window.close()` skips the main-process `close` event and does not save them; the app never
  calls it.
- Not checked: restoring a maximized window, a saved position on a disconnected display, mouse
  dragging of the width, a contest made before schema 7 with recorded rejections, and an agent
  creating a contest with `picks` over MCP/CLI. Scratch host shut down and scratch files removed.
- `npm run package` built the installer; with no image jobs active, the running installed app
  was closed and an unsandboxed silent install replaced it. `skill install --scope user --target
  claude --target codex --mcp --force` updated SKILL.md, the workflow reference, and
  guidance.json for Claude and Codex (MCP configs already current), and all 12 managed files read
  up-to-date. The installed app migrated the real library to schema 7 and answered `status` and
  `projects_list` (three projects); `tools` lists `picks`. After closing it, a fresh start
  answered both again, so the schema 7 library reopens. It was left running. Picks and the chat
  toggle were not exercised in the installed app.

## 2026-11-14 — Chat hidden until a chat model is configured

- `npm run typecheck` and `npm run build` passed.
- Scratch browser host (a temporary wrapper outside the repo setting `appData` to a scratch
  folder; the installed app kept the real profile), in the built-in browser:
  - Fresh profile, Codex not signed in, no custom models: sidebar and resize handle hidden, no
    chat toggle in the top bar; the stored layout kept `chatOpen: true`.
  - A placeholder custom chat model written into the scratch settings (no endpoint called),
    then a reload: sidebar shown with the top-bar toggle. Hiding stored `chatOpen: false` and
    survived a reload with the toggle reading "Show chat"; showing again stored `true`.
  - Removing the model from Settings hid the sidebar and toggle without a reload; the layout
    still read `chatOpen: true`.
  - Viewer on a scratch 64×64 PNG: its header carried "Hide chat" with a model configured;
    deleting the model through `deleteChatModel` removed the toggle and the viewer moved to the
    window edge (left 0).
- Not checked: the Codex sign-in path (a scratch profile cannot sign in; `configured` reads
  `codex.status.ready`), the native desktop window, and the installed app. Scratch host shut
  down and scratch files removed; no virtualized profile copy was created.
- `npm run package` built the installer. With no active image jobs in any of the three
  projects, the running installed app was closed through `CloseMainWindow` and an unsandboxed
  silent install (`/S`, exit 0) replaced it; the Start Menu shortcut is present and the
  installed `app.asar` carries the `configured` check. The installed CLI started the app and it
  answered `status` (Codex ready, so the chat counts as configured). `skill status` read all 12
  managed files up-to-date, so no skill reinstall was needed. The chat's visibility was not
  looked at in the installed window.

## 2026-11-14 — Linux builds and published releases

- `npm run typecheck` passed. `npm run package` on Windows built
  `PixelChat-Setup-0.3.0.exe` (helper, runtime, and app signing steps are no-ops without a
  certificate; the installer is unsigned).
- Windows bridge refactor: the new `pixelchat.cmd` from `win-unpacked` read the running
  installed app's DPAPI discovery and answered `status` and `tools` (42). The installed app was
  not replaced and the new app was not launched (single instance). A temporary script outside
  the repo ran `discovery.ts` against a scratch profile with the built helper: the pipe name
  had the `\.\pipe\pixelchat-<64 hex>` shape, `readDiscovery` round-tripped it, the bridge
  directory ACL listed only SYSTEM and the user, a client connected over the pipe, and
  `removeDiscovery` left the directory empty.
- Linux, WSL Ubuntu 24.04 with WSLg, a scratch copy under `/tmp` with Node 22.23.5:
  `npm ci`, `npm run typecheck`, and `npm run package` built
  `PixelChat-0.3.0-x86_64.AppImage` and `pixelchat_0.3.0_amd64.deb`. The pinned Node v24.21.0
  linux-x64 archive matched nodejs.org's SHASUMS256 and the extracted `bin/node` its pin.
  - The .deb holds `/opt/PixelChat/pixelchat`, `resources/agent/{pixelchat,pixelchat-runtime}`
    (0755), `cli.cjs`, the skill, the AppArmor profile, and `pixelchat.desktop`
    (`StartupWMClass=pixelchat` once `desktopName` was set). Its postinst and postrm are the
    electron-builder templates with the `/usr/bin/pixelchat` alternative pointed at the CLI.
  - With the app closed, a symlink to `resources/agent/pixelchat` (as `/usr/bin` would hold)
    ran `status`: the CLI started `linux-unpacked/pixelchat` and answered, reporting secure
    storage unavailable (WSL has no Secret Service keyring, so the `basic_text` guard applied).
    `bridge` was 0700, `endpoint.json` 0600, the socket inside it. `tools` listed 42;
    `projects_list` returned the new `Default` project; `import_files` imported a PNG (sharp
    and better-sqlite3 load on Linux); `ui_open` returned `opened` from the renderer;
    `view_image` returned a PNG. SIGTERM quit the app and left `bridge` empty.
  - The AppImage (`--appimage-extract-and-run`; WSL lacks `libfuse2`) started, served
    `projects_list` to the CLI over its socket, quit on SIGTERM, and cleaned up.
- Not checked: `dpkg -i` and the maintainer scripts (no sudo in WSL), a Linux desktop with
  AppArmor's user-namespace restriction (Ubuntu 23.10+), a real keyring, the AppImage MCP
  refusal in Settings, and the GitHub release run (recorded with the first release below).
  WSL scratch copy, Node, and the WSL test profile were removed afterward.
