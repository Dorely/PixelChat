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

