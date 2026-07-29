# PixelChat

PixelChat is a desktop-first, local Blazor/Electron application for building an AI-assisted 2D game art workbench. The long-term product vision is a workspace that helps game developers move from rough ideas to consistent, reusable, game-ready 2D assets.

See [VISION.md](VISION.md) for the product direction and [docs/architecture.md](docs/architecture.md) for the current technical boundaries and validation guidance.

## Current Capabilities

- Project-scoped assistant chat with streaming tool execution, visible intermediate results, persisted transcripts, image context, and intent-aware concept batches that generate one distinct prompt per output.
- Image generation with same-prompt variant or multi-prompt concept batches, imported-image editing, masks, outpainting-aware canvases, batch progress, visual review, and kept/rejected asset management.
- Reusable versioned art and animation recipes with example and guide attachments.
- A Source -> Frames -> Sheet sprite workflow with region extraction, frame ordering, alignment, cleanup, masked edits, animation preview, and deterministic sprite-sheet builds.
- Procedural and GLB-backed animation guides plus PNG/JSON export workflows and optional local AI background removal.
- OpenAI account OAuth and configurable OpenAI-compatible chat providers with local SQLite persistence.

## Requirements

- .NET 10 SDK.
- Node.js 22 or later for Electron.NET desktop builds.

## Motion Guide Assets

PixelChat vendors the CC0 Quaternius Universal Animation Library 2 Standard GLB for sampled mannequin animation guides. The catalog manifest exposes all 43 animations in that GLB for assistant guide generation, including the legacy walk-cycle alias used by the first proof of concept.

## Build

```bash
dotnet build PixelChat.sln
```

## Browser Development

Run the local browser-hosted app:

```bash
dotnet run --project PixelChat
```

The HTTP launch profile is pinned to `http://localhost:1455` so OpenAI account OAuth callbacks can use the same local redirect URI as the desktop shell.

## Desktop Development

Run the Electron.NET desktop shell:

```bash
dotnet run --project PixelChat -- --electron
```

In VS Code, press F5 with the default `PixelChat Electron` launch configuration to build and debug the Electron desktop shell.

Desktop binding is configured in `PixelChat/appsettings.json` under `Desktop:BindHost` and `Desktop:HttpPort`. Override the port in PowerShell with:

```powershell
$env:Desktop__HttpPort = '1456'
dotnet run --project PixelChat -- --electron
```

Changing the port can break OpenAI account OAuth unless `Auth:OpenAIAccount:RedirectUri` is also changed to an accepted redirect URI.

## Packaging

Electron package metadata lives in `PixelChat/Properties/electron-builder.json`. A local Windows folder publish can be produced with:

```bash
dotnet publish PixelChat/PixelChat.csproj -c Release -r win-x64 --self-contained
```

Cross-platform package creation may require building on the target OS depending on Electron/electron-builder support.

## Local Data

The local SQLite database stores projects, assets and image data, generation batches, review decisions, recipes and versions, masks, frame sets and built sheets, export caches, assistant transcripts and visuals, provider metadata, OAuth metadata, and named secret values.

API keys and OAuth token values are currently stored through the SQLite-backed `ISecretStore`; this is not an operating-system credential vault. Local database files are ignored by git.
