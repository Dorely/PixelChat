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
| `appsettings.json` / `appsettings.Development.json` | Configuration for logging, desktop binding, OAuth redirect URI, SQLite, Blazor hub size, agent/tool limits, and image-generation defaults. |
| `Properties/launchSettings.json` | Local launch profiles for browser-hosted HTTP and Electron desktop mode on `localhost:1455`. |
| `Properties/electron-builder.json` | Electron/electron-builder packaging metadata for Windows, Linux, and macOS targets. |

### Auth/

| File | Description |
|------|-------------|
| `OpenAIAccountOAuthEndpoints.cs` | Minimal API endpoints for starting and completing OpenAI account OAuth, then redirecting back to provider settings. |

### Chat/

| File | Description |
|------|-------------|
| `IAssistantChatService.cs` / `AssistantChatService.cs` | Project-scoped assistant turn service with explicit chat attachment image context, Shellmate-style tool-call streaming/execution, form-draft updates, and transcript replay. |
| `IWorkspaceChatRuntime.cs` / `WorkspaceChatRuntime.cs` | App-process chat runtime that keeps turns alive across renderer reloads, exposes live snapshots, finished-turn commits, and workspace/form side effects. |
| `AssistantPromptBuilder.cs` | Builds the workbench assistant system prompt for visible context, art guidance, form drafting, and immediate visible tool use. |
| `AssistantToolModels.cs` | Persisted tool-call manifest records and form-draft payloads used by chat/runtime/UI. |
| `AssistantToolRegistry.cs` | Tool registry for workspace state, form drafting, chat attachments, workspace mode switching, asset favorites/notes, and export actions. |
| `AssistantTurnUpdate.cs` | Streaming update records consumed by the workbench: text/tool deltas, completions, form drafts, workspace mutations, and errors. |

### Art/

| File | Description |
|------|-------------|
| `IArtWorkflowService.cs` / `ArtWorkflowService.cs` | Provider-agnostic workflow service for projects, assets, streaming compare batches, masks, recipe CRUD, chat attachments, import, crop, generation, and masked edits. |
| `ArtWorkflowModels.cs` | Request/result/view records used by the art workbench, recipe management, and assistant tools. |
| `ImageProviderModels.cs` | Provider abstraction plus generation/edit request and result records. |
| `OpenAIAccountImageProvider.cs` | OpenAI account Responses image provider using Codex-style auth headers, SSE parsing, references, and masked edit payloads. |
| `ImageGenerationOptions.cs` | Configurable image model, output, size, quality, count, parallelism, timeout, and reference defaults. |
| `DataUrl.cs` | Data URL parse/format helpers for stored BLOBs and model image inputs. |
| `ImageMetadataReader.cs` | Lightweight PNG/JPEG dimension reader for imported and generated assets. |

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
| `Home.razor` / `.razor.css` / `.razor.js` | Workbench route at `/` and `/chat`: project top bar, chat with attachments, Generate/Compare/Edit/Recipes/Assets tabs, live compare results, and canvas editor helpers. |
| `NotFound.razor` | 404 page wired through status-code re-execution. |
| `Error.razor` | Error page rendered by exception handler middleware. |
| `Settings/Providers.razor` / `.razor.css` | Provider settings page for OpenAI account OAuth, OpenAI-compatible endpoints, model tests, defaults, API-key updates, and child model rows. |

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
| `Project.cs` | EF entity for art workbench projects, active batch, and active workspace mode including the Assets tab. |
| `ArtAsset.cs` | EF entity for imported, generated, edited, and cropped image BLOBs plus lineage, favorite flag, prompt, and metadata. |
| `GenerationBatch.cs` | EF entity for image generation/edit batches, provider metadata, inputs, masks, outputs, output errors, lineage, and status. |
| `PromptRecipe.cs` | EF entity for reusable visible prompt templates, style/avoid rules, examples, and preferred defaults. |
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
| `AppDbContext.cs` | EF Core context for providers, OAuth metadata, stored secrets, assistant transcripts, projects, assets, batches, recipes, masks, and context chips. |
| `DatabaseMigrationBootstrapper.cs` | Migration bootstrapper that clears stale SQLite migration locks before running EF migrations. |
| `PersistenceServiceCollectionExtensions.cs` | DI extension that wires `AppDbContext` to SQLite from configuration. |
| `SqliteConnectionSettings.cs` | SQLite connection-string builder and PRAGMA setup for busy timeout and WAL mode. |

### Persistence/Migrations/

| File | Description |
|------|-------------|
| `20260604212229_InitialSchema.cs` / `.Designer.cs` | EF initial migration for providers, OAuth metadata, stored secrets, and assistant transcripts. |
| `20260604224321_ArtWorkbenchFirstSlice.cs` / `.Designer.cs` | EF migration adding art projects/assets/batches/recipes/masks/context chips and assistant tool-call columns. |
| `20260605053624_AssetAttachmentCompareStreaming.cs` / `.Designer.cs` | EF migration removing active-asset/reference/rejected columns and adding generation batch output-error storage. |
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
