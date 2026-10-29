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
| [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), [third-party/node-LICENSE.txt](third-party/node-LICENSE.txt) | Notices for the redistributed Node runtime. |
| [FILEMAP.md](FILEMAP.md) | This map. |
| [.gitignore](.gitignore), [.gitattributes](.gitattributes) | Ignored local data/credentials/build output; LF conventions. |
| [.vscode/launch.json](.vscode/launch.json) | F5 source-mapped Electron main and renderer debugging. |
| [.github/workflows/windows.yml](.github/workflows/windows.yml) | Credential-free Windows typecheck and installer artifact build. |

## Documentation

| File | Responsibility |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Implemented runtime ownership, persistence, providers, and agent interfaces. |
| [docs/agent-workbench.md](docs/agent-workbench.md) | CLI, MCP tools and resources, skill installation, bridge security, and agent build. |
| [docs/verification.md](docs/verification.md) | Dated manual, live, and build evidence with explicit gaps. |
| [docs/knowledge/README.md](docs/knowledge/README.md) | Image-provider knowledge navigation and revalidation caveat. |
| [docs/knowledge/codex-images.md](docs/knowledge/codex-images.md), [gemini-images.md](docs/knowledge/gemini-images.md) | Dated provider endpoint, model, size, and transparency notes. |

## Application foundation

| File | Responsibility |
| --- | --- |
| [package.json](package.json), [package-lock.json](package-lock.json) | Pinned dependencies and build/package commands. |
| [tsconfig.json](tsconfig.json) | Strict TypeScript configuration. |
| [electron.vite.config.ts](electron.vite.config.ts), [electron-builder.yml](electron-builder.yml) | Desktop build and unsigned Windows NSIS packaging. |
| [resources/icon.svg](resources/icon.svg), [icon.png](resources/icon.png), [icon.ico](resources/icon.ico) | App icon master and rasters. |
| [scripts/dev-browser.mjs](scripts/dev-browser.mjs) | Runs the built app as a loopback browser host and prints its URL. |
| [scripts/build-agent.mjs](scripts/build-agent.mjs) | Bundles the CLI and skill, fetches the pinned Node runtime, and builds the native helper. |
| [native/helper.cpp](native/helper.cpp), [CMakeLists.txt](native/CMakeLists.txt) | `pixelchat-helper.exe`: DPAPI discovery protection, private directory ACL, installer PATH entry. |
| [installer/agent.nsh](installer/agent.nsh) | NSIS hooks adding and removing the agent PATH entry. |
| [resources/skill/SKILL.md](resources/skill/SKILL.md), [references/](resources/skill/references) | Agent skill and its workflow, library/export, and image-editing references. |

## Main process

| File | Responsibility |
| --- | --- |
| [src/main/index.ts](src/main/index.ts) | Electron lifecycle, hardened window, fixed IPC command schemas, `pixel-media` protocol, bridge and browser-mode startup. |
| [src/main/bridge.ts](src/main/bridge.ts) | Token-authenticated named-pipe bridge for the CLI with DPAPI-protected discovery. |
| [src/main/view-image.ts](src/main/view-image.ts) | Renders a revision as a PNG for agent vision: backdrop, scaling, numbered note pins. |
| [src/main/store.ts](src/main/store.ts) | SQLite schema and repository for settings, projects, sessions, sources, items, revisions, reviews, notes, image jobs, and the activity log. |
| [src/main/media.ts](src/main/media.ts) | Stores exact original bytes (plus provider originals of converted results) and WebP thumbnails per revision; reads image facts with sharp. |
| [src/main/library.ts](src/main/library.ts) | Source folders, background scans and rescans, glob filters, loose-file imports, and conflict-checked export. |
| [src/main/operations.ts](src/main/operations.ts) | Shared zod-validated operation registry for UI, assistant, CLI, and MCP; error envelopes. |
| [src/main/images.ts](src/main/images.ts) | Image job admission, bounded scheduler, rate-limit retry, and result revisions with provenance. |
| [src/main/ui-port.ts](src/main/ui-port.ts) | Acknowledged navigation commands from operations to the renderer. |
| [src/main/browser-host.ts](src/main/browser-host.ts) | Loopback HTTP host (`/rpc`, `/events`, `/media/`) for browser development. |
| [src/main/diagnostics.ts](src/main/diagnostics.ts) | Redacted JSONL diagnostic log with references for user-facing errors. |
| [src/main/providers/secrets.ts](src/main/providers/secrets.ts) | OS-backed secure storage (`safeStorage`) for credentials. |
| [src/main/providers/auth.ts](src/main/providers/auth.ts) | Codex OAuth sign-in (PKCE loopback), token refresh, and sign-out. |
| [src/main/providers/image-provider.ts](src/main/providers/image-provider.ts) | Provider contract, `ImageProviderError` with answered/unanswered semantics, bounded fetch. |
| [src/main/providers/image-size.ts](src/main/providers/image-size.ts) | Resolves size requests to Codex rasters or Gemini ratio and tier. |
| [src/main/providers/codex-images.ts](src/main/providers/codex-images.ts) | Codex images generation and edits over the ChatGPT sign-in. |
| [src/main/providers/gemini-images.ts](src/main/providers/gemini-images.ts) | Gemini Interactions image generation and edits with an API key. |

## Shared contracts

| File | Responsibility |
| --- | --- |
| [src/shared/types.ts](src/shared/types.ts) | Domain records, renderer snapshot, and the `window.pixel` API type. |
| [src/shared/workbench.ts](src/shared/workbench.ts) | Operation origins, envelopes, UI commands, `WorkbenchError`, and the installation schema. |
| [src/shared/image-models.ts](src/shared/image-models.ts) | Image model catalog (provider, qualities, alpha, input limits), size presets, and defaults. |
| [src/shared/api.ts](src/shared/api.ts) | Builds the fixed renderer API over a transport (IPC or browser host). |
| [src/shared/guidance.ts](src/shared/guidance.ts) | Embedded skill references, their hash, and topic selection. |
| [src/shared/install.ts](src/shared/install.ts) | Skill and MCP installation for Codex and Claude Code: preflight, backups, status, uninstall. |
| [src/shared/native.ts](src/shared/native.ts) | Runs the native helper with bounded input, output, and time. |

## Agent CLI

| File | Responsibility |
| --- | --- |
| [src/cli/index.ts](src/cli/index.ts) | `pixelchat` command: app discovery and start, operation calls, skill install, and the stdio MCP server with watched resources. |

## Renderer

| File | Responsibility |
| --- | --- |
| [src/preload/index.ts](src/preload/index.ts) | Isolated bridge exposing `window.pixel` over IPC. |
| [src/renderer/index.html](src/renderer/index.html), [main.tsx](src/renderer/main.tsx) | Renderer entrypoint, content security policy, and API selection. |
| [src/renderer/App.tsx](src/renderer/App.tsx) | App shell: chat sidebar, project switcher, workspace views, project and settings dialogs, theme picker. |
| [src/renderer/Dialogs.tsx](src/renderer/Dialogs.tsx) | Shared modal shell and destructive-action confirmation. |
| [src/renderer/LibraryView.tsx](src/renderer/LibraryView.tsx) | Library grid with search and filters, source management, and add-folder dialog. |
| [src/renderer/ItemCard.tsx](src/renderer/ItemCard.tsx) | Grid card and thumbnail shared by Library and review boards; decision labels. |
| [src/renderer/ItemViewer.tsx](src/renderer/ItemViewer.tsx) | Full-size item inspection: zoom, backdrops, versions, notes, edits, export, and keyboard review decisions. |
| [src/renderer/EditPanel.tsx](src/renderer/EditPanel.tsx) | Viewer edit prompt, model options, open-notes insertion, and the item's job list. |
| [src/renderer/SettingsDialog.tsx](src/renderer/SettingsDialog.tsx) | Codex sign-in and Gemini key settings; provider-readiness context. |
| [src/renderer/AgentSettings.tsx](src/renderer/AgentSettings.tsx) | Settings section to install, preview, check, and remove the agent skill and MCP registration. |
| [src/renderer/NotesPanel.tsx](src/renderer/NotesPanel.tsx) | Note threads, composer, and the pin overlay that turns clicks and drags into region pins. |
| [src/renderer/ReviewsView.tsx](src/renderer/ReviewsView.tsx) | Review list, new-review dialog, and review boards with decision filters. |
| [src/renderer/browser-api.ts](src/renderer/browser-api.ts) | Browser-host transport and host-path dialog for development. |
| [src/renderer/ops.ts](src/renderer/ops.ts) | Calls registry operations and unwraps envelopes into errors that keep their code. |
| [src/renderer/Icon.tsx](src/renderer/Icon.tsx) | Inline SVG icon set. |
| [src/renderer/theme.ts](src/renderer/theme.ts), [themes.css](src/renderer/themes.css) | Theme catalog, persistence, and color tokens. |
| [src/renderer/styles.css](src/renderer/styles.css) | Layout and component styles. |
| [src/renderer/env.d.ts](src/renderer/env.d.ts) | Declares `window.pixel`. |
