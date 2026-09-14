# PixelChat

The Sprites workspace includes a native drawing canvas, layers, selections, transforms, clips, pivots, timeline, and undo/redo. Create a blank pixel-art or painted sprite, or import artwork. Native sprite documents store content-addressed cels and persistent revisions
on FrameSet. Existing frames migrate from rendered pixels into painted-mode
documents. Export editable native bundles, PNG frames/atlases with versioned JSON, or GIF previews from a saved revision. Reimport native bundles to retain layers, timing, pivots, and production rules. Agent commands and bounded scripts share the manual editor's undo history.
See [the implementation record](docs/native-sprite-editor-implementation.md) and [verification results](docs/native-sprite-validation.md).
Run its regression suite with `dotnet test PixelChat.Tests/PixelChat.Tests.csproj`.

PixelChat is a desktop-first, local Blazor/Electron application for building an AI-assisted 2D game art workbench. The long-term product vision is a workspace that helps game developers move from rough ideas to consistent, reusable, game-ready 2D assets.

See [VISION.md](VISION.md) for the product direction and [docs/architecture.md](docs/architecture.md) for the current technical boundaries and validation guidance.

## Current Capabilities

- Project-scoped assistant chat with streaming tool execution, visible intermediate results, persisted transcripts, image context, confirmed tool-history compaction with lookup-safe context notices and threshold-based summaries, and intent-aware concept batches that generate one distinct prompt per output.
- Recipe bulk generation: paste one prompt per line, edit/remove preview rows, and request 1â€“4 images per prompt with shared settings/references. Order and duplicates are preserved, with paginated output/review and no fixed prompt-count cap. Stop, Resume, and Retry failed preserve saved successes; interrupted queues require manual resume.
- Image generation with same-prompt variant or multi-prompt concept batches, imported-image editing, provider-guidance masks, outpainting-aware canvases whose complete provider output remains authoritative, batch progress, visual review, and kept/rejected asset management.
- Reusable versioned art and animation recipes with example and guide attachments.
- A native sprite workflow with source-region and bundle imports, drawing, layers, selections, mode-aware transforms, saved frame timing, clips, AI candidates, reversible edits, complete animation diagnostics, and reproducible native/PNG/JSON exports.
- Procedural and GLB-backed animation guides plus PNG/JSON export workflows and optional local AI background removal.
- OpenAI account OAuth with Sol, Terra, Luna, and Astra chat selections and low through max effort; new account connections default to Sol / medium. Built-in models use a 272,000-token context and a 258,400-token input budget, including tools and images, with automatic safe compaction.
- Persistent app-wide chat/effort and image/quality selectors. Image choices are Image 2, Image 2.5 Flare, and Image 2.5 Sunburst (default); Sol independently orchestrates images.
- Native-alpha requests for the two 2.5 image models, source-preserving edit backgrounds, and image inspection with switchable preview backgrounds, decoded alpha statistics, opaque-output warnings, and pixel RGBA values. A painted checkerboard is never treated as transparency.
- Configurable OpenAI-compatible chat providers and local SQLite persistence.

Native transparency on both 2.5 models uses `background: "auto"`, PNG output, and explicit alpha instructions injected into the image system prompt. PixelChat keeps the requested `transparent` setting separate from the provider setting and checks returned pixels. September 14, 2026 account tests of the wired system-prompt route produced genuine alpha for generation on both models and a Flare edit; the Sunburst edit returned fully opaque pixels. The explicit `background: "transparent"` parameter was rejected. Opaque results remain available with a warning, without automatic cleanup or retries. The assistant receives only the selected image model's guidance in its system prompt.

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

Native sprite editing supports agent command batches and bounded JavaScript drawing scripts. Agents can inspect exact pixel regions, labeled revision PNGs, onion skins, contact sheets, and differences. Inspection artifacts and undo history persist independently of chat. Use `sprite_help` in the assistant workflow for the compact command and scripting references.

The local SQLite database stores projects, assets and image data, generation batches, review decisions, recipes and versions, masks, frame sets and built sheets, export caches, assistant transcripts and visuals, provider metadata, OAuth metadata, and named secret values.

API keys and OAuth token values are currently stored through the SQLite-backed `ISecretStore`; this is not an operating-system credential vault. Local database files are ignored by git.
