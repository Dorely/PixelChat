# PixelChat - File Map

> **Auto-maintained reference.** Agents and contributors should update this file whenever files are added, removed, or significantly refactored.
> Read this file at the start of every session to understand the codebase layout.

---

## Root

| File | Description |
|------|-------------|
| `VISION.md` | High-level project vision and success criteria. |
| `README.md` | Project purpose, first-slice status, requirements, build/run commands, packaging notes, and local-data notes. |
| `AGENTS.md` | Stable project guidance for agents and contributors. |
| `FILEMAP.md` | This file - concise map of source files and project structure. |
| `PixelChat.sln` | Solution file containing the `PixelChat` project. |
| `global.json` | Pins the .NET SDK version (`10.0.100`). |
| `.editorconfig` | C#/Razor formatting and naming rules generated from the .NET template. |
| `.gitignore` | Standard .NET ignore patterns plus PixelChat local SQLite database files. |
| `.vscode/launch.json` | VS Code debug configurations; the first/default F5 target launches PixelChat in Electron mode. |
| `.vscode/tasks.json` | VS Code build task used by debug launch configurations. |

## PixelChat/ - Blazor/Electron App

| File | Description |
|------|-------------|
| `PixelChat.csproj` | `net10.0` Blazor Web project with Electron.NET, EF Core SQLite, Microsoft.Extensions.AI, OpenAI SDK, runtime IDs, and warnings-as-errors. |
| `Program.cs` | App host setup: Electron mode detection/window launch, Blazor Interactive Server, DI wiring, EF migrations, OAuth endpoints, and routing. |
| `appsettings.json` / `appsettings.Development.json` | Configuration for logging, desktop binding, OAuth redirect URI, SQLite, Blazor hub size, agent/tool limits, image-generation defaults, and local background-removal sidecar/model defaults. |
| `Properties/launchSettings.json` | Local launch profiles for browser-hosted HTTP and Electron desktop mode on `localhost:1455`. |
| `Properties/electron-builder.json` | Electron/electron-builder packaging metadata for Windows, Linux, and macOS targets. |

### Auth/

| File | Description |
|------|-------------|
| `OpenAIAccountOAuthEndpoints.cs` | Minimal API endpoints for starting and completing OpenAI account OAuth, then redirecting back to provider settings. |

### Chat/

| File | Description |
|------|-------------|
| `IAssistantChatService.cs` / `AssistantChatService.cs` | Project-scoped assistant turn service with explicit asset/mask/sprite-frame image context, Shellmate-style tool-call streaming/execution, form-draft updates, and transcript replay. |
| `IWorkspaceChatRuntime.cs` / `WorkspaceChatRuntime.cs` | App-process chat runtime that keeps turns alive across renderer reloads, exposes live snapshots, finished-turn commits, and workspace/form side effects. |
| `AssistantPromptBuilder.cs` | Builds the workbench assistant system prompt for visible context, art guidance, form drafting, non-destructive sprite-sheet box edits, explicit normalization, and immediate visible tool use. |
| `AssistantToolModels.cs` | Persisted tool-call manifest records and form draft payloads used by chat/runtime/UI. |
| `AssistantToolRegistry.cs` | Tool registry for workspace state, form drafting, sprite-sheet detection/box update/normalization/reset/frame attachment, chat attachments, workspace mode switching, asset favorites/notes, and export actions. |
| `AssistantTurnUpdate.cs` | Streaming update records consumed by the workbench: text/tool deltas, completions, form drafts, workspace mutations, and errors. |

### Art/

| File | Description |
|------|-------------|
| `IArtWorkflowService.cs` / `ArtWorkflowService.cs` | Provider-agnostic workflow service for projects, assets, autosaved sprite-sheet working assets/frame records, export/step caches, streaming compare batches, masks, recipes, chat attachments, import, crop, prompt assembly, and masked edits. |
| `ArtWorkflowModels.cs` | Request/result/view records used by the art workbench, sprite-sheet editor, recipe management, and assistant tools. |
| `IImageGenerationRuntime.cs` / `ImageGenerationRuntime.cs` | App-process image batch runtime that owns background generation/edit batches, retries, per-output state, partial previews, and interrupted-batch reconciliation. |
| `IBackgroundRemovalService.cs` / `RembgBackgroundRemovalService.cs` | Export-only local AI background-removal service that provisions app-owned rembg/uv sidecars, prefers GPU with CPU fallback, and returns real-alpha PNG output. |
| `BackgroundRemovalOptions.cs` | Configurable local background-removal sidecar defaults for uv, Python, rembg, model list, acceleration, cache paths, alpha matting, and timeout. |
| `ImageProviderModels.cs` | Provider abstraction plus generation/edit request, result, streaming progress, and structured error records. |
| `OpenAIAccountImageProvider.cs` | OpenAI account Responses image provider using Codex-style auth headers, SSE parsing, partial image progress, references, and masked edit payloads. |
| `ImageGenerationOptions.cs` | Configurable image model, output, size, quality, count, parallelism, retry, timeout, partial previews, and reference defaults. |
| `DataUrl.cs` | Data URL parse/format helpers for stored BLOBs and model image inputs. |
| `ImageMetadataReader.cs` | Lightweight PNG/JPEG dimension reader for imported and generated assets. |
| `SpriteSheetImageAnalyzer.cs` | Server-side PNG foreground analyzer that detects connected sprite objects, row-major frame boxes, and shape outlines. |
| `SpriteSheetPngCodec.cs` | Minimal PNG RGBA decoder/encoder used by server-side sprite-sheet rendering. |
| `SpriteSheetServerRenderer.cs` | Server-side sprite-sheet preview/normalization renderer with shape-masked copying and sheet-wide alignment anchors. |

### Components/

| File | Description |
|------|-------------|
| `App.razor` | Root HTML shell, static assets, Blazor script, and reconnect modal. |
| `Routes.razor` | Router setup using `MainLayout` and the NotFound page. |
| `_Imports.razor` | Shared Razor `@using` directives for components. |

### Components/Chat/

| File | Description |
|------|-------------|
| `ChatModels.cs` | UI-only ordered chat parts, Shellmate-style compact tool chip state, live-turn state, and persisted tool-call helpers used by chat components. |
| `ChatSurface.razor` / `.razor.css` / `.razor.js` | Reusable chat shell for ordered text/tool transcript rendering, streaming state, composer autosize, enter-to-send, and scroll-follow behavior. |
| `ChatToolChipView.razor` / `.razor.css` | Expandable compact tool-call chip used for live and persisted assistant tool activity. |

### Components/Layout/

| File | Description |
|------|-------------|
| `MainLayout.razor` / `.razor.css` | Route-aware app layout that gives the workbench route a fixed viewport and lets other pages scroll internally. |
| `NavMenu.razor` / `.razor.css` | Sidebar navigation for Chat and Providers. |
| `ReconnectModal.razor` / `.razor.css` / `.razor.js` | Template reconnect UI shown when the SignalR circuit drops. |

### Components/Pages/

| File | Description |
|------|-------------|
| `Home.razor` / `.razor.css` / `.razor.js` | Workbench route at `/` and `/chat`: project top bar, chat attachments, Generate/Compare/Edit/Sprites/Recipes/Assets tabs, canvas helpers, and export step-stack processing with Local AI, key-color cleanup, fast cleanup, and sprite JSON sidecars. |
| `NotFound.razor` | 404 page wired through status-code re-execution. |
| `Error.razor` | Error page rendered by exception handler middleware. |
| `Settings/Providers.razor` / `.razor.css` | Provider settings page for OpenAI account OAuth, OpenAI-compatible endpoints, model tests, defaults, API-key updates, and child model rows. |

### Components/Sprites/

| File | Description |
|------|-------------|
| `SpriteSheetWorkspace.razor` / `.razor.css` / `.razor.js` | Sprites workspace with non-destructive box/outline editing, anchor controls, metadata autosave, explicit Normalize Sheet stitching, frame thumbnails/attachments, animation preview, reset, and export. |

### Llm/

| File | Description |
|------|-------------|
| `AgentOptions.cs` | Configurable agent/chat options for OpenAI account timeout, tool-loop iterations, and model-facing tool result limits. |
| `ChatClientFactory.cs` / `IChatClientFactory.cs` | Creates and tests Microsoft.Extensions.AI chat clients from persisted providers and effective credentials. |
| `OpenAIAccountAuthService.cs` / `IOpenAIAccountAuthService.cs` | OpenAI account OAuth PKCE flow, token refresh, revocation, and token secret persistence. |
| `OpenAIAccountChatClient.cs` | Streaming `IChatClient` bridge to the OpenAI account Responses SSE endpoint with image inputs and function-call events. |
| `OpenAIAccountProvider.cs` | Constants and helpers for the OpenAI account provider and JWT account-id extraction. |
| `LlmProviderService.cs` / `ILlmProviderService.cs` | Provider CRUD, readiness snapshots, credential status, default-provider selection, and effective API-key/token resolution. |
| `SecretNames.cs` | Centralized secret key names for provider API keys and OAuth tokens. |
| `ToolCallStreamingContent.cs` | `AIContent` records for provider-level function-call start and argument-delta streaming. |
| `ToolCallArguments.cs` | Parser/normalizer for JSON and SDK tool-call arguments before `AIFunction` invocation. |
| `StreamingToolCallTracker.cs` | Normalizes provider function-call start/delta/final content into app-level streaming tool updates. |

### Models/

| File | Description |
|------|-------------|
| `Project.cs` | EF entity for art workbench projects, active batch, active sprite sheet, and active workspace mode including the Sprites and Assets tabs. |
| `ArtAsset.cs` | EF entity for imported, generated, edited, cropped, and sprite-sheet image BLOBs plus lineage, favorite flag, prompt, and metadata. |
| `BackgroundRemovalExportCache.cs` | EF entity for cached Local AI export PNGs keyed by source asset bytes, model, rembg version, and processing options. |
| `ExportStepCache.cs` | EF entity for persisted applied export-step PNGs per source asset and source image hash. |
| `GenerationBatch.cs` | EF entity for image generation/edit batches, provider metadata, inputs, masks, outputs, output errors, lineage, and status. |
| `PromptRecipe.cs` | EF entity for reusable visible prompt templates, style/avoid rules, examples, and preferred defaults. |
| `SpriteSheetDefinition.cs` | EF entity for row-based sprite-sheet definitions linking immutable source assets to mutable working sprite-sheet assets plus layout, FPS, and loop defaults. |
| `SpriteSheetFrameRecord.cs` | EF entity for durable sprite frame records, current/source/cell/sprite rectangles, labels, previews, dimensions, and timestamps. |
| `ImageMask.cs` | EF entity for saved PNG mask BLOBs attached to assets. |
| `ChatContextAttachment.cs` | EF entity for persistent visible chat attachments referencing assets, masks, crops, recipes, or batches. |
| `AssistantConversation.cs` | EF entity for project-scoped persistent assistant conversations. |
| `AssistantMessage.cs` | EF entity and enums for transcript messages, tool roles, tool-call manifests, roles, statuses, and errors. |
| `AuthType.cs` | Enum for provider authentication modes: none, API key, or OAuth. |
| `LlmProvider.cs` | EF entity for chat endpoint/model rows, default selection, child model credential inheritance, and readiness snapshots. |
| `OAuthToken.cs` | EF entity for OAuth token metadata; token values are stored through `ISecretStore`. |
| `StoredSecret.cs` | EF entity backing the first SQLite implementation of `ISecretStore`. |

### Persistence/

| File | Description |
|------|-------------|
| `AppDbContext.cs` | EF Core context for providers, OAuth metadata, stored secrets, assistant transcripts, projects, assets, sprite sheets, export caches/step caches, batches, recipes, masks, and context chips. |
| `DatabaseMigrationBootstrapper.cs` | Migration bootstrapper that clears stale SQLite migration locks before running EF migrations. |
| `PersistenceServiceCollectionExtensions.cs` | DI extension that wires `AppDbContext` to SQLite from configuration. |
| `SqliteConnectionSettings.cs` | SQLite connection-string builder and PRAGMA setup for busy timeout and WAL mode. |

### Persistence/Migrations/

| File | Description |
|------|-------------|
| `20260604212229_InitialSchema.cs` / `.Designer.cs` | EF initial migration for providers, OAuth metadata, stored secrets, and assistant transcripts. |
| `20260604224321_ArtWorkbenchFirstSlice.cs` / `.Designer.cs` | EF migration adding art projects/assets/batches/recipes/masks/context chips and assistant tool-call columns. |
| `20260605053624_AssetAttachmentCompareStreaming.cs` / `.Designer.cs` | EF migration removing active-asset/reference/rejected columns and adding generation batch output-error storage. |
| `20260605194957_GenerationBatchBackground.cs` / `.Designer.cs` | EF migration adding generation batch background mode with `auto` as the existing-row default. |
| `20260605214431_GenerationOutputStates.cs` / `.Designer.cs` | EF migration adding per-output generation state JSON for progress, retries, and structured failures. |
| `20260608210339_BackgroundRemovalExportCache.cs` / `.Designer.cs` | EF migration adding persistent Local AI export PNG cache records and cache-key indexes. |
| `20260608232906_ExportStepCache.cs` / `.Designer.cs` | EF migration adding persisted applied export-step PNG cache records and indexes. |
| `20260609011241_EditBatchSourceSnapshots.cs` / `.Designer.cs` | EF migration adding nullable edit-source PNG snapshot columns to generation batches. |
| `20260609022859_SpriteSheetEditor.cs` / `.Designer.cs` | EF migration adding active sprite-sheet project state and persisted sprite-sheet definitions. |
| `20260610192432_SpriteSheetSecondPass.cs` / `.Designer.cs` | EF corrective migration adding durable sprite-sheet frame records for autosaved working sheets. |
| `20260610204025_SpriteSheetSmartSeparation.cs` / `.Designer.cs` | EF migration adding sprite frame shape JSON plus sheet-wide horizontal and vertical normalization anchors. |
| `AppDbContextModelSnapshot.cs` | EF model snapshot for the current migrated schema. |

### Persistence/Repositories/

| File | Description |
|------|-------------|
| `ILlmProviderRepository.cs` / `LlmProviderRepository.cs` | EF repository for provider CRUD, lookup, and default selection. |
| `IOAuthTokenRepository.cs` / `OAuthTokenRepository.cs` | EF repository for OAuth token metadata replacement, lookup, and deletion. |
| `IAssistantConversationRepository.cs` / `AssistantConversationRepository.cs` | EF repository for project-scoped assistant conversations, ordered transcript messages, and message lookup. |

### Secrets/

| File | Description |
|------|-------------|
| `ISecretStore.cs` | Abstraction for named local secrets. |
| `SqliteSecretStore.cs` | First implementation of `ISecretStore`, storing named secret values in SQLite. |

### wwwroot/

| File | Description |
|------|-------------|
| `app.css` | App-wide CSS from the Blazor template with PixelChat layout adjustments. |
| `favicon.png` | Site/app icon from the Blazor template. |
| `lib/bootstrap/` | Vendored Bootstrap distribution used by first-slice UI. |
