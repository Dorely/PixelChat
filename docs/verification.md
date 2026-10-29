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
