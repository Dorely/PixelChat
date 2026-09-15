# PixelChat - File Map

> **Auto-maintained reference.** Agents and contributors should update this file whenever files are added, removed, or significantly refactored.
> Read this file at the start of every session to understand the codebase layout.

---

## Root

| File | Description |
|------|-------------|
| `VISION.md` | High-level product vision, durable product principles, and AI-assisted sprite-workbench goals. |
| `README.md` | Project purpose, current capability overview, requirements, build/run commands, packaging notes, and local-data behavior. |
| `AGENTS.md` | Stable project guidance for agents and contributors. |
| `docs/architecture.md` | Current technical architecture, ownership boundaries, persistence/security constraints, platform scope, and validation commands. |
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
| `Program.cs` | App host setup: Electron mode detection/window launch, Blazor Interactive Server, DI wiring, art/sprite services, EF migrations, static files/assets, OAuth/media endpoints, and routing. |
| `appsettings.json` / `appsettings.Development.json` | Configuration for logging, desktop binding, OAuth redirect URI, SQLite, Blazor hub size, agent/tool and chat-compaction limits, image-generation defaults, sprite-animation defaults, and local background-removal sidecar/model defaults. |
| `Properties/launchSettings.json` | Local launch profiles for browser-hosted HTTP and Electron desktop mode on `localhost:1455`. |
| `Properties/electron-builder.json` | Electron/electron-builder packaging metadata for Windows, Linux, and macOS targets. |

### Auth/

| File | Description |
|------|-------------|
| `OpenAIAccountOAuthEndpoints.cs` | Minimal API endpoints for starting and completing OpenAI account OAuth, then redirecting back to provider settings. |

### Chat/

| File | Description |
|------|-------------|
| `IAssistantChatService.cs` / `AssistantChatService.cs` | Project-scoped assistant turn service with explicit image context, chat-visual persistence, tool streaming/execution and replay, lookup-safe tool-history pruning, threshold-based hierarchical summaries, and model-only visual outputs. |
| `ModelImageInspection.cs` | Shared assistant image factory: measured source alpha, validated inspection backgrounds, opaque PNG composites, and readable inspection captions. |
| `IWorkspaceChatRuntime.cs` / `WorkspaceChatRuntime.cs` | App-process chat runtime that keeps turns and cancellable compaction alive across renderer reloads, throttles state notifications, commits finished turns with visuals, and broadcasts workspace side effects. |
| `WorkspaceVisibleState.cs` | In-memory visible UI snapshot store and compact workspace records for Review, live sprite focus/agent status, asset, and recipe context used by assistant tools. |
| `AssistantPromptBuilder.cs` | Builds the assistant system prompt from selected-image-model guidance and `AgentOptions` budget limits, including living-recipe maintenance, intent-based concept-vs-variant generation, model-vs-user visibility, direct generation/edit execution, provider-guided mask/outpaint guidance, Review presentation, Keep/Reject triage, and concise native sprite objectives with progressive workflow help. |
| `AssistantToolModels.cs` | Persisted tool-call manifest records, concept-batch prompt items, explicit display titles, animation frame mark payloads, and per-turn autonomous generation budget state. |
| `AssistantToolRegistry.cs` | Tool registry for visible state, focused reads, recipes/guides, same-prompt generation rounds, distinct-prompt concept batches, provider-guided preview-locked directional edits, greenfield Source/Frames/Sheet tools, visual Review sets, batch triage/finalization, exports, and `displayTitle` metadata. |
| `AssistantTurnUpdate.cs` | Streaming update records consumed by the workbench: text/tool deltas, explicit display title metadata, visual metadata, completions, workspace mutations, and errors. |

### Art/

| File | Description |
|------|-------------|
| `IArtWorkflowService.cs` / `ArtWorkflowService.cs` | Provider-agnostic workflow service for workbench loads, asset lifecycle/review decisions, visual Review sets, media, generation, transient canvas previews, logical/provider-aware edits with authoritative provider output, sprite work, exports, recipes, masks, import, and crop. |
| `ArtWorkflowModels.cs` | Request/result/view records for the workbench, ordered generation prompt specifications and batch modes, edit-canvas options/transforms/finalization/previews, lazy media, animation guides, sprite-sheet metadata, region extraction, recipes, and assistant tools. |
| `IFrameSetService.cs` / `FrameSetService.cs` | Source regions/import, deterministic frame cleanup/alignment, native frame projections, masks, and derived sheets; AI jobs use the native generation service. |
| `ISpriteWorkspaceActionService.cs` / `SpriteWorkspaceActionService.cs` | Shared Sprites action layer used by UI clicks and assistant tools to wrap greenfield mutations including optional scale normalization, update persisted sprite focus, and keep the visible workspace synchronized. |
| `FrameSetModels.cs` | Source/frame/sheet contracts, including sprite summary revisions and first-frame identities for thumbnail selection. |
| `AnimationGuideModels.cs` | Shared guide-rendering records for animation specs, frame specs, guide layouts, and per-frame slots without restoring the old animation job pipeline. |
| `SpriteAnimationOptions.cs` | Configuration record for sprite-animation defaults used by guide rendering and animation-generation workflow setup. |
| `SpriteFacing.cs` | Facing normalization, yaw conversion, left/right detection, and prompt phrasing helpers for animation guides. |
| `SpriteMotionArchetypes.cs` | Procedural motion archetype builder for unit, tower, projectile, and VFX animation guide frame specs. |
| `SpriteGuideRenderer.cs` | Procedural PNG renderer for lightweight animation guide sheets and diagnostic guide sheets. |
| `MotionClipCatalog.cs` | Motion clip manifest loader/resolver with shared defaults, discovery metadata, and GLTF-backed animation guide validation. |
| `GltfMotionGuideRenderer.cs` | GLB sampler/renderer that produces yaw/pitch-adjustable mannequin motion guide sheets from cataloged Quaternius clips. |
| `ArtMediaEndpoints.cs` | Local HTTP media endpoints for lazy asset previews/full images, chat visual previews/full images, asset/frame masks, motion-clip GLB assets, native revision/frame renders, persisted inspections, and downloadable sprite exports. |
| `IImageGenerationRuntime.cs` / `ImageGenerationRuntime.cs` | App-process image batch runtime that owns atomic background generation/edit starts, bounded variant/concept/bulk workers, Stop/Resume/Retry failed, awaitable completion, serialized state, transient previews, and manual shutdown recovery. |
| `IBackgroundRemovalService.cs` / `RembgBackgroundRemovalService.cs` | Export-only local AI background-removal service that provisions app-owned rembg/uv sidecars, prefers GPU with CPU fallback, and returns real-alpha PNG output. |
| `BackgroundRemovalOptions.cs` | Configurable local background-removal sidecar defaults for uv, Python, rembg, model list, acceleration, cache paths, alpha matting, and timeout. |
| `ImageProviderModels.cs` | Provider abstraction plus generation/edit request, result, streaming progress, structured errors, public size constraints, optional transport-specific reliable edit budgets, and pre-submit validation. |
| `OpenAIAccountImageProvider.cs` | OpenAI account Responses image provider using Codex-style auth headers, SSE parsing, partial image progress, references, masked edit payloads, its configurable reliable edit pixel budget, and selected request system guidance with native-alpha auto transport. |
| `ImageEditCanvasService.cs` | Shared edit/outpaint pipeline that prepares logical/provider canvases, masks, and previews; normalizes removable logical-source backgrounds; dilates semantic boundaries; and restores provider output to logical dimensions without overwriting returned pixels from the source. |
| `EditCanvasPreparationStore.cs` | Fifteen-minute bounded in-memory store for preview-locked asset/frame canvas preparations, limited to four entries per project and validated against source revisions. |
| `ImageModelSelectionService.cs` | App-wide persisted image model/quality selection and model capability validation. |
| `GenerationQueueState.cs` | Builds ordered persisted prompt/output/reference snapshots and maps per-output lifecycle state and errors. |
| `ImageRequestScheduler.cs` | App-wide provider request semaphore, shared by batch workers and direct frame edits. |
| `ImageGenerationOptions.cs` | Configurable image model, output, size, quality, count, parallelism, retry, timeout, partial previews, reference defaults, and OpenAI-account reliable edit pixel budget. |
| `ImageBackgroundModes.cs` | Shared generation background modes and recipe-preference normalization/resolution for natural, opaque, removable-magenta, and distinct native-alpha output. |
| `DataUrl.cs` | Data URL parse/format helpers for stored BLOBs and model image inputs. |
| `ImageMetadataReader.cs` | Lightweight PNG/JPEG dimension reader for imported and generated assets. |
| `ImageRgbaDecoder.cs` | Shared RGBA decoder for PNG/JPEG source assets used by greenfield sprite region/frame operations and standalone region extraction. |
| `ImageEditMaskRenderer.cs` | Rasterizes assistant rectangle/polygon selections into full-size PNG edit masks with opaque-preserve and transparent-edit semantics. |
| `SpriteSheetImageAnalyzer.cs` | Background-aware foreground bounds, connected source-region detection, shape outlines, and extraction-quality measurements. |
| `SpriteSheetPngCodec.cs` | Minimal PNG RGBA decoder/encoder used by server-side sprite-sheet rendering. |
| `SpriteSheetServerRenderer.cs` | Deterministic source-sheet rendering, frame isolation, cleanup, annotated views, and reassembly. Native revision inspections own animation views. |

### Assets/MotionClips/

| File | Description |
|------|-------------|
| `manifest.json` | Motion clip catalog manifest exposing vendored Quaternius UAL1/UAL2 animations with shared and per-pack defaults plus legacy walk aliasing. |
| `Quaternius/UAL1/AnimationLibrary_Godot_Standard.glb` | CC0 Quaternius Universal Animation Library Standard GLB used for default sampled mannequin motion-guide rendering. |
| `Quaternius/UAL1/README.md` / `License.txt` | Source attribution, coverage, checksum, and license notes for the vendored UAL1 motion clip asset. |
| `Quaternius/UAL2/UAL2_Standard.glb` | Restored CC0 Quaternius Universal Animation Library 2 GLB used for sampled mannequin motion-guide rendering. |
| `Quaternius/UAL2/README.md` / `License.txt` | Source attribution, coverage, and license notes for the restored Quaternius motion clip asset. |

### Components/

| File | Description |
|------|-------------|
| `App.razor` | Root HTML shell, static assets, Blazor script, and reconnect modal. |
| `Routes.razor` | Router setup using `MainLayout` and the NotFound page. |
| `_Imports.razor` | Shared Razor `@using` directives for components. |

### Components/Shared/

| File | Description |
|------|-------------|
| `BulkPromptEditor.razor` | Paginated editable one-prompt-per-line bulk input preserving order and duplicates. |
| `ImageTransparencyInspector.razor` / `.razor.css` / `.razor.js` | Fitted image preview with background controls, compact full-raster alpha statistics, and pixel inspection. |
| `ExportPanel.razor` / `.razor.css` | Assets PNG export modal with cleanup steps, local AI removal, preview backgrounds, and reset; sprite bundles use SpriteExportPanel. |
| `AnimationGuideBuilderModal.razor` / `.razor.css` | Shared Assets > Guides modal for configuring guide grids, previewing GLB motion clips in 3D with yaw/pitch drag, rendering guide previews, and saving SpriteGuide assets. |
| `LazyImage.razor` / `.razor.css` / `.razor.js` | IntersectionObserver-backed image component that reserves thumbnail space and assigns `src` only when near the viewport. |
| `SpriteAnimationPreview.razor` / `.razor.js` | Reusable canvas animation preview component that plays any ordered image sequence from a neutral `AnimationPreviewFrame` list (url/label/durationMs) plus fps/loop, with no GIF artifact. |

### Components/Chat/

| File | Description |
|------|-------------|
| `ChatModels.cs` | UI-only ordered chat text/tool/image parts, compaction notice and summary presentation, compact tool chip state with explicit display titles and visuals, live-turn state, and persisted tool-call helpers. |
| `ChatModelSelector.razor` | Persistent global chat model and effort selection with built-in account context budgets. |
| `ChatSurface.razor` / `.razor.css` / `.razor.js` | Reusable chat shell for ordered text/tool/image/context transcript rendering, visual preview clicks, streaming state, composer autosize, enter-to-send, and scroll-follow behavior. |
| `ChatToolChipView.razor` / `.razor.css` | Expandable compact tool-call chip used for live and persisted assistant tool timeline entries. |

### Components/Layout/

| File | Description |
|------|-------------|
| `MainLayout.razor` / `.razor.css` | Route-aware app layout that gives the workbench route a fixed viewport and lets other pages scroll internally. |
| `NavMenu.razor` / `.razor.css` | Sidebar navigation for Chat and Providers. |
| `ReconnectModal.razor` / `.razor.css` / `.razor.js` | Template reconnect UI shown when the SignalR circuit drops. |

### Components/Pages/

| File | Description |
|------|-------------|
| `Home.razor` / `.razor.css` / `.razor.js` | Workbench route at `/` and `/chat`: Generate/Batches/Review/Edit/Sprites/Recipes/Assets workspaces, variant/concept batch and per-output prompt presentation, preview-locked outpaint controls, pending and agent-completed review, asset management, chat, alpha-aware inspection modals, virtualized grids, canvas helpers, and exports. |
| `NotFound.razor` | 404 page wired through status-code re-execution. |
| `Error.razor` | Error page rendered by exception handler middleware. |
| `Settings/Providers.razor` / `.razor.css` | Provider settings page for OpenAI account OAuth, OpenAI-compatible endpoints, model tests, thinking modes, defaults, API-key updates, and child model rows. |

### Components/Sprites/

| File | Description |
|------|-------------|
| `SpriteSheetWorkspace.razor` / `.razor.css` | Bounded sprite workspace, thumbnail picker integration, dismissible create/import panels, and visible-state integration. |

### Llm/

| File | Description |
|------|-------------|
| `AgentOptions.cs` | Configurable agent/chat options for OpenAI account timeout, tool-loop iterations, model-facing tool result limits, autonomous generation-round budgets, and the conversation-compaction token threshold. |
| `OpenAIModelCatalog.cs` | Built-in account model IDs, effort choices, and default Codex context/input budgets. |
| `ChatClientFactory.cs` / `IChatClientFactory.cs` | Creates and tests Microsoft.Extensions.AI chat clients from persisted providers, credentials, and provider thinking-mode defaults. |
| `OpenAIAccountAuthService.cs` / `IOpenAIAccountAuthService.cs` | OpenAI account OAuth PKCE flow, token refresh, revocation, and token secret persistence. |
| `OpenAIAccountChatClient.cs` | Account Responses SSE bridge with image inputs, function-call events, and schema-aware strict tool serialization. |
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
| `ChatTokenEstimator.cs` | Counts a model-facing `ChatMessage` context across instructions, tool schemas/calls/results, text, and image content. |

### Models/

| File | Description |
|------|-------------|
| `Project.cs` | EF entity for art workbench projects, active batch/sprite sheet/frame-set/workspace/sprite focus state, and owned assets, sprite sheets, frame sets, and recipes. |
| `AnimationRecipe.cs` | EF entity for reusable animation/motion recipe prompts with private notes, current version, and ordered asset attachments. |
| `AnimationRecipeVersion.cs` | EF entity for append-only animation recipe name/prompt/notes snapshots and change summaries. |
| `ArtAsset.cs` | EF entity for image BLOBs plus lineage, recipe versions, manual favorite state, prompt/metadata, and Pending/Kept/Rejected lifecycle status. |
| `AssetReviewDecision.cs` | Append-only user/assistant Keep, Reject, and Clear decisions with reasons, source-batch provenance, and timestamps. |
| `BackgroundRemovalExportCache.cs` | EF entity for cached Local AI export PNGs keyed by source asset bytes, model, rembg version, and processing options. |
| `ExportStepCache.cs` | EF entity for persisted applied export-step PNGs per source asset and source image hash. |
| `WorkbenchPreferences.cs` | Singleton persisted app-wide image model/quality preferences. |
| `GenerationQueue.cs` | Prompt/output rows and immutable reference-byte snapshots for generation queues. |
| `GenerationBatch.cs` | EF entity for image generation/edit batches with ordered queue rows, recipe snapshots, provider metadata, lineage, edit transforms, and review completion provenance. |
| `PromptRecipe.cs` | EF entity backing reusable art recipe prompts with a generation-background preference, private notes, version history, and ordered example/guide attachments. |
| `PromptRecipeVersion.cs` | EF entity for append-only art recipe name/prompt/notes/background-preference snapshots used by user/assistant saves and restore. |
| `RecipeAssetAttachment.cs` | EF entity for ordered art/animation recipe asset attachments with example or guide roles. |
| `SpriteRegion.cs` | Greenfield EF entity for a source-image region (rect/polygon, type, order) that stays linked to source pixels and can be extracted as an asset or turned into frames. |
| `StandaloneAsset.cs` | Greenfield EF entity linking an extracted region to its output `ArtAsset` (kind `Extracted`) with logical size, content offset, source link, and a deferred bitmap-revision pointer. |
| `FrameSet.cs` | Greenfield EF entity (replaces `SpriteSheetDefinition` as the frame owner) owning versioned document state, revision number, undo/redo stacks, frame projections, and derived sheet layouts. |
| `Frame.cs` | Greenfield EF entity (replaces `SpriteSheetFrameRecord`) with explicit coordinate spaces, duration, onion-skin visibility, relational source geometry projected from the native document and retained identities for undo. |
| `Anchor.cs` | Greenfield EF entity for a named per-frame alignment point (feet/root/center/custom) with confidence and detected/manual source. |
| `SheetLayout.cs` | Greenfield EF entity for deterministic sheet geometry (rows/columns/cell/padding/gutter/outer-margin/ordering) and playback/background defaults for a frame set. |
| `BuiltSheet.cs` | Greenfield EF entity for a reassembled RGBA sheet asset retaining a per-frame placement manifest and links to the frames used, so the sheet stays rebuildable. |
| `ImageMask.cs` | EF entity for saved PNG mask BLOBs attached to assets or greenfield frames, including owner and coordinate-space metadata. |
| `ChatContextAttachment.cs` | EF entity for persistent visible chat attachments referencing assets, masks, crops, recipes, or batches. |
| `CompareReviewSet.cs` | EF entities backing the project-scoped curated visual Review set with assets, greenfield frames, and FrameSet animations. |
| `AssistantConversation.cs` | EF entity for project-scoped persistent assistant conversations. |
| `AssistantMessage.cs` | EF entity and enums for transcript messages, tool calls, authoritative compaction notices, structured summaries, visual attachments, statuses, and errors. |
| `AssistantMessageVisual.cs` | EF entity for transcript-linked user/tool visuals with source references, optional image bytes/thumbnails, metadata, and ordering. |
| `AuthType.cs` | Enum for provider authentication modes: none, API key, or OAuth. |
| `LlmProvider.cs` | EF entity for chat endpoint/model rows, thinking mode, default selection, child model credential inheritance, and readiness snapshots. |
| `OAuthToken.cs` | EF entity for OAuth token metadata; token values are stored through `ISecretStore`. |
| `StoredSecret.cs` | EF entity backing the first SQLite implementation of `ISecretStore`. |

### Persistence/

| File | Description |
|------|-------------|
| `AppDbContext.cs` | EF Core context for providers, OAuth metadata, stored secrets, assistant transcripts and visuals, projects, assets, animation recipes, sprite sheets, export caches/step caches, batches, art recipes, masks, and context chips. |
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
| `20260624193635_SpriteWorkflowReset.cs` / `.Designer.cs` | Historical EF migration that added now-removed Activity run/step/artifact tables and versioned AnimationRecipe tables. |
| `20260624194923_RemoveAssetAnimationPipeline.cs` / `.Designer.cs` | EF migration destructively dropping the superseded asset-animation pipeline tables and sprite-frame legacy source columns. |
| `20260625173222_SpriteSheetStabilization.cs` / `.Designer.cs` | EF migration adding saved sprite-sheet stabilization metadata JSON to sprite-sheet definitions. |
| `*_SpriteGreenfieldModel.cs` / `.Designer.cs` | EF migration adding the greenfield sprite tables (SpriteRegions, StandaloneAssets, FrameSets, Frames, Anchors, SheetLayouts, BuiltSheets, HistoryTasks), the new `Extracted` asset kind, and ImageMask owner/coordinate-space columns. |
| `20260625233000_ActiveFrameSetProjectState.cs` | Corrective EF migration that adds `Projects.ActiveFrameSetId` after the greenfield migration for databases that had already applied the earlier migration. |
| `20260626000000_SpriteEditSessions.cs` | EF migration adding pending Sprites edit modal sessions with target, batch, mask, candidate, output-state, prompt/count, and crop persistence. |
| `20260626001500_FrameOnionSkinVisibility.cs` | EF migration adding per-frame onion-skin visibility metadata for the greenfield frame model. |
| `20260626003000_SpriteWorkspaceFocusState.cs` | EF migration adding persisted Sprites mode/source/frame/region focus state to projects for UI and assistant synchronization. |
| `20260626234709_ChatMessageVisualsRemoveActivity.cs` / `.Designer.cs` | EF migration adding assistant message visuals, dropping Activity tables, and normalizing old `Runs` workspace mode values. |
| `20260627061347_AnimationRecipeGenerationUsage.cs` / `.Designer.cs` | EF migration adding nullable animation recipe/version provenance to generation batches and generated/derived assets. |
| `20260628063511_SimplifyRecipesAndAttachments.cs` / `.Designer.cs` | EF migration simplifying art/animation recipes to named prompts and notes, adding typed recipe asset attachments, and backfilling old example/guide references. |
| `20260701072209_SpriteSheetLegacyRemovalAndReviewRework.cs` / `.Designer.cs` | EF migration dropping the legacy `SpriteSheetDefinitions`/`SpriteSheetFrameRecords` tables and `Projects.ActiveSpriteSheetId`, renaming the persisted `Compare` workspace mode to `Review`, and deleting review items of removed kinds. |
| `20260712000000_AssetReviewWorkflow.cs` | EF migration adding asset lifecycle/review decisions and batch review provenance while removing recipe items from curated Review sets. |
| `20260715000000_OutpaintAwareImageEditing.cs` | EF migration adding edit-canvas transform provenance to batches/frames and persisted canvas options to pending sprite-edit sessions. |
| `20260716002245_ReliableOutpaintFinalization.cs` / `.Designer.cs` | EF migration adding logical edit source/mask snapshots, frame finalization provenance, and pending sprite-edit canvas preparation identity/expiry. |
| `20260727212737_GenerationOnlyBackgroundPreferences.cs` / `.Designer.cs` | EF migration adding versioned generation-background preferences to art recipes, defaulting existing recipes to the current Generate selection. |
| `20260729043245_MultiPromptGenerationBatches.cs` / `.Designer.cs` | EF migration replacing the batch-level prompt with ordered prompt specifications and backfilling existing generation/edit batches as same-prompt variants. |
| `20260730010111_RemoveProtectedPixelPasteback.cs` / `.Designer.cs` | EF migration removing obsolete logical-source snapshots after edit finalization stopped pasting protected source pixels over provider output. |
| `20260914181503_OpenAIModelsAndNativeTransparency.cs` / `.Designer.cs` | Forward migration for global preferences, batch/edit snapshots, and old transparency aliases. |
| `20260914182535_PreserveRawProviderImages.cs` / `.Designer.cs` | Retains original provider image bytes separately from finalized assets. |
| `20260914183811_PersistRecipeBulkQueue.cs` / `.Designer.cs` | Expands batch JSON into ordered queue rows and snapshots available recipe/reference data before removing old columns. |
| `AppDbContextModelSnapshot.cs` | EF model snapshot for the current migrated schema. |

### Persistence/Repositories/

| File | Description |
|------|-------------|
| `ILlmProviderRepository.cs` / `LlmProviderRepository.cs` | EF repository for provider CRUD, lookup, and default selection. |
| `IOAuthTokenRepository.cs` / `OAuthTokenRepository.cs` | EF repository for OAuth token metadata replacement, lookup, and deletion. |
| `IAssistantConversationRepository.cs` / `AssistantConversationRepository.cs` | EF repository for project-scoped assistant conversations, ordered transcript messages with visuals, visual inserts, and message lookup. |

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
| `js/animation-guide-builder.js` | Stable static ES module for the animation guide builder's Three.js GLB viewer and yaw/pitch drag interaction. |
| `lib/bootstrap/` | Vendored Bootstrap distribution used by first-slice UI. |
| `lib/three/` | Vendored Three.js runtime modules, including split `three.module.js`/`three.core.js`, used by the animation guide builder's local GLB viewer. |

## Native sprite documents

| Path | Responsibility |
| --- | --- |
| `PixelChat/Sprites/SpriteDocument.cs` | Versioned document, layer/cel/frame/clip/specification contracts and revision conflicts. |
| `PixelChat/Sprites/SpriteRaster.cs` | Bounded RGBA operations, compositing, resampling, and content-addressed PNGs. |
| `PixelChat/Sprites/SpriteCommandEngine.cs` | Typed operations on temporary state with selection, locking, palette, and allocation constraints. |
| `PixelChat/Sprites/SpriteDocumentService.cs` | Atomic persistence, revision checks, frame identity projections, and durable undo/redo. |
| `PixelChat/Art/FrameSetService.Native.cs` | Source import and native revision integration for existing frame workflows. |
| `PixelChat/Models/SpriteBitmap.cs` / `SpriteRevision.cs` | Shared immutable PNG content and independent document history records. |
| `PixelChat/Persistence/NativeSpriteDataMigration.cs` | Rendered-cell materialization between additive and destructive schema migrations. |
| `PixelChat.Tests/SpriteDocumentTests.cs` / `SpriteMigrationTests.cs` | Pixel, transaction, history, conflict, and migration regression fixtures. |
| `PixelChat.Tests/PixelChat.Tests.csproj` | Authorized xUnit test project with corpus fixtures copied to output for isolated builds. |
| `docs/native-sprite-editor-implementation.md` | Release stages and cross-layer impact plan. |
| `20260914205129_NativeSpriteDocuments.cs` / `.Designer.cs` | Forward native document schema transition. |
| `20260914205804_RetireMutableFrameBitmaps.cs` / `.Designer.cs` | Forward native document schema transition. |
| `20260914210308_RemoveDeferredSpriteHistory.cs` / `.Designer.cs` | Forward native document schema transition. |

| `PixelChat/Components/Sprites/NativeSpriteEditor.razor` / `.razor.css` / `.razor.js` | Canvas with fit/wheel zoom/right-drag pan, tool rail, fixed timeline, tabbed inspector, and revision-checked manual commands. |
| `PixelChat/Sprites/SpriteTimeline.cs` | Shared clip ordering and duration-aware forward/reverse/ping-pong timing. |
| `PixelChat/Sprites/SpriteDocumentEvents.cs` | App-process document revision notifications for live manual/agent editing. |
| `PixelChat.Tests/SpriteTimelineTests.cs` | Unequal timing, one-shot, reverse, and ping-pong regression checks. |
| `PixelChat.Tests/sprite-editor.browser.cjs` | Headless Edge checks for drawing, layers, timing, history, diagnostics/preparations, playback, and export/reimport. |
| `20260914212522_NativeClipPlayback.cs` / `.Designer.cs` | Removes unused playback/alignment settings now represented by native document clips. |
| `PixelChat/Sprites/SpriteToolRegistry.cs` | Native tools with typed operation schemas, revision checks, and alpha-aware PNG views with optional inspection backgrounds. |
| `PixelChat/Sprites/SpriteScriptService.cs` / `SpriteScriptWorker.cs` | Parent-enforced worker limits and restricted Jint command generation with atomic application. |
| `PixelChat/Sprites/SpriteInspectionService.cs` / `PixelChat/Models/SpriteInspection.cs` | Cached, labeled revision renders persisted independently of chat. |
| `PixelChat/Sprites/Skills/*.md` | Embedded, progressively loaded command references and drawing, pose, animation, and export workflows. |
| `PixelChat.Tests/SpriteScriptTests.cs` / `SpriteToolTests.cs` | Real worker isolation/failure tests and native tool image-content checks. |
| `20260914213826_SpriteInspectionArtifacts.cs` / `.Designer.cs` | Persists revision-addressed inspection artifacts with shared bitmap content. |
| `PixelChat/Sprites/SpriteGenerationService.cs` | Revision/layer-bound preparations and candidate inspection/application over persisted image jobs. |
| `PixelChat/Art/ArtWorkflowService.Sprites.cs` | Native generation batch submission with captured references, model/recipe selections, provider canvases, and source-independent edits. |
| `PixelChat/Sprites/SpriteValidationService.cs` / `PixelChat/Models/SpriteAssessment.cs` | Every-frame measurements, clip/pair diagnostics, and separate persisted artistic judgments. |
| `PixelChat/Components/Sprites/SpriteAiPanel.razor` / `.razor.css` | Inspector AI prompt, named artwork/recipe references, preparation, candidates, jobs, and validation. |
| `PixelChat.Tests/SpriteWorkflowTests.cs` | Late-frame diagnostics, intended motion, target preparation, stale AI results, reversible application, and a fixture provider pipeline. |
| `20260914220038_NativeSpriteJobsAndAssessments.cs` / `.Designer.cs` | Adds native job snapshots/assessments and preserves edit-session provenance before retiring the obsolete session table. |

| `PixelChat/Sprites/SpriteAtlasBuilder.cs` | Shared deterministic slot placement and versioned frame/clip/pivot/slice export metadata. |
| `PixelChat/Sprites/SpriteExportService.cs` / `PixelChat/Models/SpriteExport.cs` | Cached immutable native/PNG/JSON/GIF artifacts and bounded hash-verified bundle reimport. |
| `PixelChat/Components/Sprites/SpriteExportPanel.razor` / `.razor.css` | Inspector export controls, revision-specific downloads, GIF previews, and named slices. |
| `20260914221808_NativeSpriteExports.cs` / `.Designer.cs` | Persists native export artifacts and projects blank-frame identities for migrated empty sets. |
| `PixelChat.Tests/SpriteExportTests.cs` | Exact RGBA reconstruction, timing/pivots/slices, padding placement, GIF timing, and tamper rejection. |
| `PixelChat.Tests/SpriteCorpusTests.cs` / `sprite-corpus.js` | Five direct-drawing fixtures with script, render, validation, export, and usage records; the script is copied to test output. |
| `docs/native-sprite-validation.md` | Local release checks, measured corpus results, artistic judgments, and unverified integrations. |

| `PixelChat/Components/Sprites/SpriteToolIcon.razor` | Accessible-label companion SVG icons for the native drawing tool rail. |
| `PixelChat.Tests/sprite-viewport.browser.cjs` | Canvas navigation, large-image import, inspector state, import dismissal, and thumbnail picker interaction checks. |

| `PixelChat/Components/Sprites/SpritePickerModal.razor` / `.razor.css` / `.razor.js` | Searchable revision-specific sprite thumbnail dialog with native modal focus and dismissal. |

| `PixelChat.Tests/ChatToolSchemaTests.cs` | Captures account requests to check tool schemas, strict constraints, references, and alpha-aware image payloads. |
| `PixelChat.Tests/sprite-assistant-live.browser.cjs` | Explicitly enabled real-account browser check for Astra reads, sprite_apply, rendered evidence, and pixel/timing verification. |

| `PixelChat.Tests/ModelImageInspectionTests.cs` | Hidden-RGB/source preservation, source alpha counts, partial blending, opaque/animated inputs, and background validation. |
| `PixelChat.Tests/sprite-alpha-live.browser.cjs` | Opt-in live Astra/browser alpha regression across asset/frame/native views, stored composite pixels, and unchanged source bytes. |
