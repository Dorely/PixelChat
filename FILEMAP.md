# PixelChat file map

Map of authored project files. Generated output (`out/`, `release/`, `node_modules/`) is excluded.

## Root

| File | Responsibility |
| --- | --- |
| [AGENTS.md](AGENTS.md) | Durable agent workflow, security, evidence, documentation, and verification practices. |
| [CLAUDE.md](CLAUDE.md) | Imports AGENTS.md so every harness reads one source. |
| [VISION.md](VISION.md) | Product intent for art review, agent collaboration, and image creation/editing. |
| [README.md](README.md) | Current status and development entry point. |
| [LICENSE](LICENSE) | MIT license. |
| [FILEMAP.md](FILEMAP.md) | This map. |
| [.gitignore](.gitignore), [.gitattributes](.gitattributes) | Ignored local data/credentials/build output; LF conventions. |
| [.vscode/launch.json](.vscode/launch.json) | F5 source-mapped Electron main and renderer debugging. |
| [.github/workflows/windows.yml](.github/workflows/windows.yml) | Credential-free Windows typecheck and installer artifact build. |

## Documentation

| File | Responsibility |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Implemented runtime ownership, persistence, providers, and agent interfaces. |
| [docs/verification.md](docs/verification.md) | Dated manual, live, and build evidence with explicit gaps. |
| [docs/knowledge/README.md](docs/knowledge/README.md) | Image-provider knowledge navigation and revalidation caveat. |

## Application foundation

| File | Responsibility |
| --- | --- |
| [package.json](package.json), [package-lock.json](package-lock.json) | Pinned dependencies and build/package commands. |
| [tsconfig.json](tsconfig.json) | Strict TypeScript configuration. |
| [electron.vite.config.ts](electron.vite.config.ts), [electron-builder.yml](electron-builder.yml) | Desktop build and unsigned Windows NSIS packaging. |
| [resources/icon.svg](resources/icon.svg), [icon.png](resources/icon.png), [icon.ico](resources/icon.ico) | App icon master and rasters. |
| [scripts/dev-browser.mjs](scripts/dev-browser.mjs) | Runs the built app as a loopback browser host and prints its URL. |

## Main process

| File | Responsibility |
| --- | --- |
| [src/main/index.ts](src/main/index.ts) | Electron lifecycle, hardened window, fixed IPC command schemas, `pixel-media` protocol, browser-mode startup. |
| [src/main/store.ts](src/main/store.ts) | SQLite schema and repository for settings, projects, and sessions; signals changes. |
| [src/main/operations.ts](src/main/operations.ts) | Shared zod-validated operation registry for UI, assistant, CLI, and MCP; error envelopes. |
| [src/main/ui-port.ts](src/main/ui-port.ts) | Acknowledged navigation commands from operations to the renderer. |
| [src/main/browser-host.ts](src/main/browser-host.ts) | Loopback HTTP host (`/rpc`, `/events`, `/media/`) for browser development. |
| [src/main/diagnostics.ts](src/main/diagnostics.ts) | Redacted JSONL diagnostic log with references for user-facing errors. |
| [src/main/providers/secrets.ts](src/main/providers/secrets.ts) | OS-backed secure storage (`safeStorage`) for credentials. |

## Shared contracts

| File | Responsibility |
| --- | --- |
| [src/shared/types.ts](src/shared/types.ts) | Domain records, renderer snapshot, and the `window.pixel` API type. |
| [src/shared/workbench.ts](src/shared/workbench.ts) | Operation origins, envelopes, UI commands, and `WorkbenchError`. |
| [src/shared/api.ts](src/shared/api.ts) | Builds the fixed renderer API over a transport (IPC or browser host). |

## Renderer

| File | Responsibility |
| --- | --- |
| [src/preload/index.ts](src/preload/index.ts) | Isolated bridge exposing `window.pixel` over IPC. |
| [src/renderer/index.html](src/renderer/index.html), [main.tsx](src/renderer/main.tsx) | Renderer entrypoint, content security policy, and API selection. |
| [src/renderer/App.tsx](src/renderer/App.tsx) | App shell: chat sidebar, project switcher, workspace views, dialogs, theme picker. |
| [src/renderer/browser-api.ts](src/renderer/browser-api.ts) | Browser-host transport and host-path dialog for development. |
| [src/renderer/ops.ts](src/renderer/ops.ts) | Calls registry operations and unwraps envelopes. |
| [src/renderer/Icon.tsx](src/renderer/Icon.tsx) | Inline SVG icon set. |
| [src/renderer/theme.ts](src/renderer/theme.ts), [themes.css](src/renderer/themes.css) | Theme catalog, persistence, and color tokens. |
| [src/renderer/styles.css](src/renderer/styles.css) | Layout and component styles. |
| [src/renderer/env.d.ts](src/renderer/env.d.ts) | Declares `window.pixel`. |
