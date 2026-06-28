# PixelChat

PixelChat is a desktop-first, local Blazor/Electron application for building an AI-assisted 2D game art workbench. The long-term product vision is a workspace that helps game developers move from rough ideas to consistent, reusable, game-ready 2D assets.

## First Slice Status

Implemented now:

- .NET 10 Blazor Interactive Server app hosted by Electron.NET.
- Local SQLite persistence using EF Core migrations.
- Provider configuration for OpenAI account OAuth and arbitrary OpenAI-compatible chat endpoints.
- SQLite-backed `ISecretStore` abstraction for API keys and OAuth tokens.
- Persistent global chat transcript with streaming responses, stop/cancel, reset, and provider readiness gating.
- PixelChat-specific chat prompt focused on 2D game art direction, prompt design, and asset planning.

Not implemented yet:

- Image generation or editing.
- Asset import/export, sprite sheets, masks, selections, or editor tools.
- Prompt recipe libraries, generation history, or project-specific style memory.
- Automated tests.

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

The local SQLite database stores provider metadata, chat transcripts, OAuth token metadata, and first-slice secret values through `ISecretStore`. API keys and OAuth tokens are stored as secrets in the local database. Database files are ignored by git.
