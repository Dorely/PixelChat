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

