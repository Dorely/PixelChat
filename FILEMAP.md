# PixelChat - File Map

> **Auto-maintained reference.** Agents and contributors should update this file whenever files are added, removed, or significantly refactored.
> Read this file at the start of every session to understand the codebase layout.

---

## Root

| File | Description |
|------|-------------|
| `VISION.md` | High-level product vision, durable product principles, and AI-assisted sprite-workbench goals. |
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
| `PixelChat.csproj` | `net10.0` Blazor Web project with Electron.NET, EF Core SQLite, Microsoft.Extensions.AI, OpenAI SDK, ImageSharp image decoding, SharpGLTF motion-guide support, SQLitePCLRaw bundle pin, runtime IDs, and warnings-as-errors. |
| `Program.cs` | App host setup: Electron mode detection/window launch, Blazor Interactive Server, DI wiring, art/sprite services, EF migrations, OAuth/media endpoints, and routing. |
| `appsettings.json` / `appsettings.Development.json` | Configuration for logging, desktop binding, OAuth redirect URI, SQLite, Blazor hub size, agent/tool limits, image-generation defaults, sprite-animation defaults, and local background-removal sidecar/model defaults. |
| `Properties/launchSettings.json` | Local launch profiles for browser-hosted HTTP and Electron desktop mode on `localhost:1455`. |
| `Properties/electron-builder.json` | Electron/electron-builder packaging metadata for Windows, Linux, and macOS targets. |

### Auth/

| File | Description |
|------|-------------|
| `OpenAIAccountOAuthEndpoints.cs` | Minimal API endpoints for starting and completing OpenAI account OAuth, then redirecting back to provider settings. |

### Chat/

| File | Description |
|------|-------------|
| `IAssistantChatService.cs` / `AssistantChatService.cs` | Project-scoped assistant turn service with explicit image context, autonomous generation budget wiring/model-only outputs including sprite stabilization diagnostics, Shellmate-style tool streaming/execution, form drafts, and transcript replay. |
| `IWorkspaceChatRuntime.cs` / `WorkspaceChatRuntime.cs` | App-process chat runtime that keeps turns alive across renderer reloads, throttles streaming state notifications, commits finished turns, and applies workspace/form side effects. |
| `WorkspaceVisibleState.cs` | In-memory visible UI snapshot store and compact all-tab records including Activity, Review, sprite, asset, and recipe context used by assistant tools. |
| `AssistantPromptBuilder.cs` | Builds the workbench assistant system prompt around the Source -> Frames -> Sheet -> Export workflow, deterministic sprite tools, art/animation recipes, review, activity, and export guidance. |
| `AssistantToolModels.cs` | Persisted tool-call manifest records, form draft payloads, animation frame mark payloads, and per-turn autonomous generation budget state. |
| `AssistantToolRegistry.cs` | Tool registry for visible state, focused reads, recipes/guides, generation, greenfield source-region/frame-set/frame-mask/sheet operations, legacy sprite repair tools, Review sets, favorites, and exports. |
| `AssistantTurnUpdate.cs` | Streaming update records consumed by the workbench: text/tool deltas, completions, form drafts, workspace mutations, and errors. |

### Art/

| File | Description |
|------|-------------|
| `IArtWorkflowService.cs` / `ArtWorkflowService.cs` | Provider-agnostic workflow service for workbench loads, media reads, assets, Activity, Review sets, guide generation, sprite sheets/reviews/working-frame stabilization/split/reassembly, greenfield region-to-standalone-asset extraction, exports, art/animation recipes, masks, import, crop, and edits. |
| `ArtWorkflowModels.cs` | Request/result/view records for the workbench, Activity, lazy media URLs/binaries, animation-guide generation, sprite-sheet composition/provenance/stabilization/animation metadata, per-frame isolation/reassembly, standalone region extraction, art/animation recipe management, and assistant tools. |
| `IFrameSetService.cs` / `FrameSetService.cs` | Greenfield deterministic Source -> Frames -> Sheet service over SpriteRegion/FrameSet/Frame/Anchor/SheetLayout/BuiltSheet: detect/save regions, create/edit/order/align frames, manage frame masks, and build opaque sheets with linked manifests. |
| `FrameSetModels.cs` | View/request/result records for the greenfield source-region, frame-set, frame edit, mask, and build-sheet pipeline. |
| `AnimationGuideModels.cs` | Shared guide-rendering records for animation specs, frame specs, guide layouts, and per-frame slots without restoring the old animation job pipeline. |
| `SpriteAnimationOptions.cs` | Configuration record for sprite-animation defaults used by guide rendering and animation-generation workflow setup. |
| `SpriteFacing.cs` | Facing normalization, yaw conversion, left/right detection, and prompt phrasing helpers for animation guides. |
| `SpriteMotionArchetypes.cs` | Procedural motion archetype builder for unit, tower, projectile, and VFX animation guide frame specs. |
| `SpriteGuideRenderer.cs` | Procedural PNG renderer for lightweight animation guide sheets and diagnostic guide sheets. |
| `MotionClipCatalog.cs` | Motion clip manifest loader and resolver for external GLTF-backed animation guides. |
| `GltfMotionGuideRenderer.cs` | GLB sampler/renderer that produces mannequin motion guide sheets from cataloged Quaternius clips. |
| `ArtMediaEndpoints.cs` | Local HTTP media endpoints for lazy asset previews/full images, asset/frame masks, legacy sprite-frame previews, and greenfield frame-set frame previews. |
| `IImageGenerationRuntime.cs` / `ImageGenerationRuntime.cs` | App-process image batch runtime that owns atomic background generation/edit starts, awaitable completion, retries, per-output state, partial previews, and interrupted-batch reconciliation. |
| `IBackgroundRemovalService.cs` / `RembgBackgroundRemovalService.cs` | Export-only local AI background-removal service that provisions app-owned rembg/uv sidecars, prefers GPU with CPU fallback, and returns real-alpha PNG output. |
| `BackgroundRemovalOptions.cs` | Configurable local background-removal sidecar defaults for uv, Python, rembg, model list, acceleration, cache paths, alpha matting, and timeout. |
| `ImageProviderModels.cs` | Provider abstraction plus generation/edit request, result, streaming progress, structured error records, and pre-submit image size validation. |
| `OpenAIAccountImageProvider.cs` | OpenAI account Responses image provider using Codex-style auth headers, SSE parsing, partial image progress, references, and masked edit payloads. |
| `ImageGenerationOptions.cs` | Configurable image model, output, size, quality, count, parallelism, retry, timeout, partial previews, and reference defaults. |
| `DataUrl.cs` | Data URL parse/format helpers for stored BLOBs and model image inputs. |
| `ImageMetadataReader.cs` | Lightweight PNG/JPEG dimension reader for imported and generated assets. |
| `ImageRgbaDecoder.cs` | Shared RGBA decoder for PNG/JPEG source assets used by greenfield sprite region/frame operations and standalone region extraction. |
| `SpriteSheetImageAnalyzer.cs` | Server-side PNG analyzer for background-aware foreground bounds, connected sprite boxes/shape outlines, and animation motion metrics. |
| `SpriteSheetPngCodec.cs` | Minimal PNG RGBA decoder/encoder used by server-side sprite-sheet rendering. |
| `SpriteSheetServerRenderer.cs` | Server-side sprite-sheet preview/normalization/review renderer with irregular frame isolation, erase/keep cleanup, coordinate-grid and removed-vs-source overlays, working-frame stabilization diagnostics, outlier-aware reassembly, annotated sheet views, diffs, onion skins, and filmstrips. |

### Assets/MotionClips/

| File | Description |
|------|-------------|
| `manifest.json` | Motion clip catalog manifest for guide renderer lookup and default humanoid walk resolution. |
| `Quaternius/UAL2/UAL2_Standard.glb` | Restored CC0 Quaternius Universal Animation Library 2 GLB used for sampled walk-cycle guide rendering. |
| `Quaternius/UAL2/README.md` / `License.txt` | Source attribution and license notes for the restored Quaternius motion clip asset. |

### Components/

| File | Description |
|------|-------------|
| `App.razor` | Root HTML shell, static assets, Blazor script, and reconnect modal. |
| `Routes.razor` | Router setup using `MainLayout` and the NotFound page. |
| `_Imports.razor` | Shared Razor `@using` directives for components. |

### Components/Shared/

| File | Description |
|------|-------------|
| `LazyImage.razor` / `.razor.css` / `.razor.js` | IntersectionObserver-backed image component that reserves thumbnail space and assigns `src` only when near the viewport. |
| `SpriteAnimationPreview.razor` / `.razor.js` | Reusable canvas animation preview component that plays saved sprite-frame preview images without storing a GIF artifact. |

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
| `Home.razor` / `.razor.css` / `.razor.js` | Workbench route at `/` and `/chat`: local tab state, chat attachments, Activity log, Review UI, recipe-aware Generate/Edit, Sprites, art/animation recipe editors, guide-aware Assets tabs, canvas helpers, and exports. |
| `NotFound.razor` | 404 page wired through status-code re-execution. |
| `Error.razor` | Error page rendered by exception handler middleware. |
| `Settings/Providers.razor` / `.razor.css` | Provider settings page for OpenAI account OAuth, OpenAI-compatible endpoints, model tests, thinking modes, defaults, API-key updates, and child model rows. |

### Components/Sprites/

| File | Description |
|------|-------------|
| `SpriteSheetWorkspace.razor` / `.razor.css` / `.razor.js` | Canvas-first Sprites workspace bound to greenfield SpriteRegion/FrameSet/Frame services. Provides Source/Frames/Sheet/Export modes, source-region drawing/editing, frame content/mask canvas editing, contextual inspector, bottom region/frame strips, sheet build, export, and disabled history placeholders. |

### Llm/

| File | Description |
|------|-------------|
| `AgentOptions.cs` | Configurable agent/chat options for OpenAI account timeout, tool-loop iterations, model-facing tool result limits, and autonomous generation-round budgets. |
| `ChatClientFactory.cs` / `IChatClientFactory.cs` | Creates and tests Microsoft.Extensions.AI chat clients from persisted providers, credentials, and provider thinking-mode defaults. |
| `OpenAIAccountAuthService.cs` / `IOpenAIAccountAuthService.cs` | OpenAI account OAuth PKCE flow, token refresh, revocation, and token secret persistence. |
| `OpenAIAccountChatClient.cs` | Streaming `IChatClient` bridge to the OpenAI account Responses SSE endpoint with image inputs and function-call events. |
| `OpenAIAccountProvider.cs` | Constants and helpers for the OpenAI account provider and JWT account-id extraction. |
| `LlmProviderService.cs` / `ILlmProviderService.cs` | Provider CRUD, readiness snapshots, credential status, default-provider selection, and effective API-key/token resolution. |
| `ProviderThinkingModes.cs` | Thinking-mode normalization, OpenAI mode constants/dropdown options, endpoint detection, and chat-option mapping. |
| `SecretNames.cs` | Centralized secret key names for provider API keys and OAuth tokens. |
| `ToolCallStreamingContent.cs` | `AIContent` records for provider-level function-call start and argument-delta streaming. |
| `ToolCallArguments.cs` | Parser/normalizer for JSON and SDK tool-call arguments before `AIFunction` invocation. |
| `StreamingToolCallTracker.cs` | Normalizes provider function-call start/delta/final content into app-level streaming tool updates. |

### Tokens/

| File | Description |
|------|-------------|
| `ITokenCounter.cs` / `TiktokenTokenCounter.cs` / `CharEstimateTokenCounter.cs` / `CompositeTokenCounter.cs` | Local text token counting abstractions and tiktoken-first implementation with character fallback. |
| `TokenCountRequest.cs` / `TokenCountResult.cs` / `TokenCountingOptions.cs` | Token counting request/result records and model-to-encoding defaults. |
| `ImageTokenEstimator.cs` | Local image token estimator using OpenAI-style patch and tile formulas by model family. |
| `ChatTokenEstimator.cs` | Counts a model-facing `ChatMessage` context across text, tool calls/results, and image content. |

### Models/

| File | Description |
|------|-------------|
| `Project.cs` | EF entity for art workbench projects, active batch/sprite sheet/frame-set/workspace mode, and owned assets, sprite sheets, frame sets, recipes, and Activity records. |
| `ActivityRun.cs` | EF entity for user-visible workflow Activity runs with status, actor, workflow kind, primary artifact, and child steps/artifacts. |
| `ActivityStep.cs` | EF entity for ordered Activity steps shown in the workflow log. |
| `ActivityArtifact.cs` | EF entity for Activity artifacts referencing assets, batches, sprite sheets, frames, or other workflow outputs. |
| `AnimationRecipe.cs` | EF entity for reusable animation/motion recipes with guide asset, frame order, expected boxes, anchor strategy, prompt scaffold, export defaults, and primary example. |
| `AnimationRecipeVersion.cs` | EF entity for append-only animation recipe snapshots and change summaries. |
| `ArtAsset.cs` | EF entity for imported, generated, edited, cropped, sprite-guide, and sprite-sheet image BLOBs plus lineage, source recipe version, favorite flag, prompt, and metadata. |
| `BackgroundRemovalExportCache.cs` | EF entity for cached Local AI export PNGs keyed by source asset bytes, model, rembg version, and processing options. |
| `ExportStepCache.cs` | EF entity for persisted applied export-step PNGs per source asset and source image hash. |
| `GenerationBatch.cs` | EF entity for image generation/edit batches, provider metadata, inputs, masks, outputs, output errors, lineage, status, and stamped recipe version. |
| `PromptRecipe.cs` | EF entity backing art recipes: reusable prompt/style/production guides, avoid rules, one active example image, and preferred defaults. |
| `PromptRecipeVersion.cs` | EF entity for append-only art recipe snapshots including the active example image used by user/assistant saves and restore. |
| `SpriteSheetDefinition.cs` | EF entity for row-based sprite-sheet definitions linking immutable source assets to mutable working sprite-sheet assets plus layout, background fill, stabilization metadata, FPS, and loop defaults. |
| `SpriteSheetFrameRecord.cs` | EF entity for durable sprite frame records, source/output rectangles, per-frame source-image provenance, pivots/timing/root offsets, previews, hidden working-frame PNGs, labels, dimensions, and timestamps. (Legacy sprite model; being replaced by the greenfield FrameSet/Frame entities.) |
| `SpriteRegion.cs` | Greenfield EF entity for a source-image region (rect/polygon, type, order) that stays linked to source pixels and can be extracted as an asset or turned into frames. |
| `StandaloneAsset.cs` | Greenfield EF entity linking an extracted region to its output `ArtAsset` (kind `Extracted`) with logical size, content offset, source link, and a deferred bitmap-revision pointer. |
| `FrameSet.cs` | Greenfield EF entity (replaces `SpriteSheetDefinition` as the frame owner) holding ordered frames, default cell size, playback/alignment settings, and child sheet layouts. |
| `Frame.cs` | Greenfield EF entity (replaces `SpriteSheetFrameRecord`) with explicit coordinate spaces: source bounds, logical cell size, content offset, duration, working/preview bitmaps, and anchors. |
| `Anchor.cs` | Greenfield EF entity for a named per-frame alignment point (feet/root/center/custom) with confidence and detected/manual source. |
| `SheetLayout.cs` | Greenfield EF entity for deterministic sheet geometry (rows/columns/cell/padding/gutter/outer-margin/ordering) and playback/background defaults for a frame set. |
| `BuiltSheet.cs` | Greenfield EF entity for a reassembled opaque sheet asset retaining a per-frame placement manifest and links to the frames used, so the sheet stays rebuildable. |
| `HistoryTask.cs` | Greenfield EF entity (schema only; backend deferred) grouping a user/agent instruction's operations into one undoable task for the planned history system. |
| `ImageMask.cs` | EF entity for saved PNG mask BLOBs attached to assets or greenfield frames, including owner and coordinate-space metadata. |
| `ChatContextAttachment.cs` | EF entity for persistent visible chat attachments referencing assets, masks, crops, recipes, or batches. |
| `CompareReviewSet.cs` | EF entities backing the project-scoped Review set and ordered review items referencing assets, batches, sprite sheets, animations, or frames. |
| `AssistantConversation.cs` | EF entity for project-scoped persistent assistant conversations. |
| `AssistantMessage.cs` | EF entity and enums for transcript messages, tool roles, tool-call manifests, roles, statuses, and errors. |
| `AuthType.cs` | Enum for provider authentication modes: none, API key, or OAuth. |
| `LlmProvider.cs` | EF entity for chat endpoint/model rows, thinking mode, default selection, child model credential inheritance, and readiness snapshots. |
| `OAuthToken.cs` | EF entity for OAuth token metadata; token values are stored through `ISecretStore`. |
| `StoredSecret.cs` | EF entity backing the first SQLite implementation of `ISecretStore`. |

### Persistence/

| File | Description |
|------|-------------|
| `AppDbContext.cs` | EF Core context for providers, OAuth metadata, stored secrets, assistant transcripts, projects, assets, Activity, animation recipes, sprite sheets, export caches/step caches, batches, art recipes, masks, and context chips. |
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
| `20260611071606_PromptRecipeVersions.cs` / `.Designer.cs` | EF migration adding append-only prompt recipe version history and backfilling existing recipes as version 1. |
| `20260611181547_RecipeExampleImageAndVersionLinkage.cs` / `.Designer.cs` | EF migration replacing multi-example recipe JSON with one example image and adding recipe-version linkage to batches/assets. |
| `20260611222045_SpriteSheetBackgroundFill.cs` / `.Designer.cs` | EF migration adding nullable sprite-sheet background fill metadata. |
| `20260612071043_SpriteFrameWorkingImages.cs` / `.Designer.cs` | EF migration adding hidden per-frame working PNG state for sprite isolation, cleanup, and reassembly. |
| `20260612154126_ProviderThinkingMode.cs` / `.Designer.cs` | EF migration adding nullable provider thinking mode and last-tested thinking snapshot fields. |
| `20260622190840_CompareReviewSet.cs` / `.Designer.cs` | EF migration adding project-scoped Compare review sets and ordered review items. |
| `20260622205745_SpriteFrameSourceImageProvenance.cs` / `.Designer.cs` | EF migration adding nullable per-frame source image provenance columns and a SetNull asset reference. |
| `20260623044535_AssetAnimationPipeline.cs` / `.Designer.cs` | Historical EF migration that created the now-removed asset-animation pipeline tables and columns. |
| `20260623175122_AssetAnimationRunEvents.cs` / `.Designer.cs` | Historical EF migration that created now-removed asset-animation timeline events. |
| `20260624193635_SpriteWorkflowReset.cs` / `.Designer.cs` | EF migration adding Activity run/step/artifact tables and versioned AnimationRecipe tables. |
| `20260624194923_RemoveAssetAnimationPipeline.cs` / `.Designer.cs` | EF migration destructively dropping the superseded asset-animation pipeline tables and sprite-frame legacy source columns. |
| `20260625173222_SpriteSheetStabilization.cs` / `.Designer.cs` | EF migration adding saved sprite-sheet stabilization metadata JSON to sprite-sheet definitions. |
| `*_SpriteGreenfieldModel.cs` / `.Designer.cs` | EF migration adding the greenfield sprite tables (SpriteRegions, StandaloneAssets, FrameSets, Frames, Anchors, SheetLayouts, BuiltSheets, HistoryTasks), the new `Extracted` asset kind, and ImageMask owner/coordinate-space columns. |
| `20260625233000_ActiveFrameSetProjectState.cs` | Corrective EF migration that adds `Projects.ActiveFrameSetId` after the greenfield migration for databases that had already applied the earlier migration. |
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
