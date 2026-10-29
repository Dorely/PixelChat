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
| [src/main/index.ts](src/main/index.ts) | Electron lifecycle and window. |
| [src/preload/index.ts](src/preload/index.ts) | Isolated renderer bridge. |
| [src/renderer/index.html](src/renderer/index.html), [main.tsx](src/renderer/main.tsx) | Renderer entrypoint and content security policy. |
