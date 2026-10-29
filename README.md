# PixelChat

A Windows desktop workspace for agent-aided art creation, editing, and review.
[MIT license](LICENSE).

PixelChat is being rebuilt on the SfxChat stack (Electron, React/TypeScript, SQLite) as an
art review workspace with an inline assistant and a CLI/MCP interface for external agents.
See [VISION](VISION.md) for direction and [architecture](docs/architecture.md) for what is
implemented. The earlier .NET/Blazor sprite workbench was removed; its history remains in git.

## Development

Requires Windows x64 and Node.js 22.

```powershell
npm install
npm run typecheck
npm run build
npm run dev
```

`npm run package` builds the unsigned NSIS installer into `release/`.

The app stores its library under `%APPDATA%\PixelChat`.
