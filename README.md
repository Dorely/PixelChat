# PixelChat

A Windows desktop workspace for agent-aided art creation, editing, and review.
[MIT license](LICENSE).

PixelChat is built on the SfxChat stack (Electron, React/TypeScript, SQLite): an art
review workspace with an inline assistant and a CLI/MCP interface for external agents.
See [VISION](VISION.md) for direction and [architecture](docs/architecture.md) for what is
implemented. The earlier .NET/Blazor sprite workbench was removed; its history remains in git.

## Development

Requires Windows x64 and Node.js 22.

```powershell
npm install
npm run typecheck
npm run build
npm run dev
npm run dev:browser
```

`npm run dev:browser` serves the built app at a printed loopback URL for browser-based checks.

`npm run build` also bundles the agent CLI, skill, and native helper (needs CMake or Visual
Studio C++ build tools). `npm run package` builds the unsigned NSIS installer into `release/`.
See [agent workbench](docs/agent-workbench.md) for the CLI, MCP server, and skill install.

The app stores its library under `%APPDATA%\PixelChat`.
