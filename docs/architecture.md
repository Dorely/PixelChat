# PixelChat Architecture

## Status and Scope

PixelChat is a desktop-first, local AI-assisted 2D game art workbench. Normal
user operation is through an Electron.NET desktop shell backed by a local
ASP.NET Core host. The same Blazor application can run directly in a browser for
local development and debugging.

The project declares Windows x64, Linux x64, macOS x64, and macOS arm64 runtime
identifiers. Declared targets preserve the intended cross-platform desktop
shape; they are not evidence that packaging or platform-specific behavior has
been validated on every operating system.

The implemented workbench includes project-scoped assistant chat, image
generation and editing, visual review and asset lifecycle management, reusable
art and animation recipes, source-region and frame-set sprite workflows,
deterministic sprite-sheet construction, animation guides, and export cleanup.
`VISION.md` remains the product direction and is not proof that every future
workflow is complete.

## Stack

- .NET 10 ASP.NET Core with Blazor Interactive Server
- Electron.NET for the desktop shell
- EF Core with SQLite for local persistence and migrations
- `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, and the OpenAI
  .NET SDK for chat abstraction and OpenAI-compatible provider support
- ImageSharp and local PNG helpers for image decoding and server-side processing
- SharpGLTF for sampled GLB motion guides
- Bootstrap and Three.js vendored under `PixelChat/wwwroot`
- An optional app-owned `uv`/Python/rembg sidecar for local export background
  removal

The solution currently contains one application project. `Program.cs` owns host
startup, dependency registration, middleware, local media and OAuth endpoints,
database migration, interrupted image-batch reconciliation, and Electron window
creation.

## Runtime and Ownership Boundaries

The workbench UI is composed primarily in `Home.razor`, shared chat and export
components, and `SpriteSheetWorkspace.razor`. Razor components own interaction
and presentation state; business behavior must remain in injected services so
the same operation can be used consistently by manual UI actions and assistant
tools.

The main runtime flow is:

1. Razor UI or an assistant tool requests an operation.
2. Shared action and workflow services validate the request and coordinate
   persisted state.
3. Provider adapters, image-processing services, or deterministic sprite
   services perform the specialized work.
4. Repositories and `AppDbContext` persist results, while visible-state stores
   and runtime notifications synchronize the active workbench.

`IArtWorkflowService` owns project, asset, generation, review, recipe, mask,
import, edit, and export workflows. `IFrameSetService` owns the deterministic
Source -> Frames -> Sheet model and bitmap operations. UI and assistant sprite
mutations should pass through `ISpriteWorkspaceActionService` when visible focus
and workspace synchronization must accompany the underlying mutation.

`IAssistantChatService` owns a scoped assistant turn and tool loop.
`IWorkspaceChatRuntime` is app-process state that allows a turn to continue
across renderer reloads and broadcasts completed workspace effects.
`IImageGenerationRuntime` similarly owns app-process generation/edit batches,
progress, retries, completion, and interrupted-batch reconciliation. Do not move
these lifetimes into a Razor component or make background work depend on a
single UI circuit.

Generation batches persist ordered prompt specifications rather than one
batch-level prompt. A variant batch has one prompt specification with multiple
outputs; a concept batch has multiple distinct prompt specifications with one
output each. Shared references, recipes, size, background, Avoid constraints,
and provider settings remain batch-wide. Runtime output indexes resolve to one
specific prompt before provider submission, retry, asset creation, and review.
The manual Generate form and edit tools create variant batches; the assistant
uses concept batches for ideation and alternate directions.

Provider-neutral contracts isolate chat and image workflows from transports.
Provider-specific OAuth, Responses streaming, tool-call parsing, readiness
checks, and image requests belong in the `Llm`, `Auth`, and provider adapter
implementations. New providers should extend those boundaries rather than add
provider conditionals throughout the UI or art services.

Local media endpoints serve persisted and transient images, masks, sprite
frames, chat visuals, and motion assets to the local workbench. JavaScript
modules are used for browser-only canvas, scrolling, lazy-image, animation, and
Three.js interactions; authoritative workflow and persistence decisions remain
server-side.

## Persistence, Configuration, and Security

PixelChat uses a local SQLite database through `AppDbContext`. It stores
projects, assets and image BLOBs, generation batches with ordered prompt
specifications, review decisions, recipes and versions, masks, frame sets,
frames and built sheets, export caches, assistant transcripts and visuals,
provider metadata, OAuth metadata, and named secret values. The host applies EF
Core migrations at startup and configures SQLite for a busy timeout and WAL
mode.

Applied migration files are immutable schema history. Never edit, reorder, or
delete an applied migration to make the migration directory resemble the
current model. Add a forward migration for schema changes and remove
superseded runtime models, services, and paths in the same feature. Historical
migrations that create structures later dropped by another migration are
expected and are not compatibility shims.

Configuration belongs in `appsettings.json`, environment-specific settings, and
environment-variable overrides. Options records own the agent, image
generation, sprite animation, background removal, token counting, desktop host,
OAuth redirect, and persistence settings. Avoid hard-coding configuration in
components or feature entities.

Credentials, API keys, and OAuth token values must pass through `ISecretStore`.
The current `SqliteSecretStore` implementation stores those values in the local
SQLite database; it is an abstraction boundary, not an operating-system secure
credential vault or a claim of encryption at rest. Never log secrets,
authorization codes, access tokens, refresh tokens, or sensitive provider
payloads.

The desktop and browser development profiles use `localhost:1455`. OpenAI
account OAuth currently depends on the registered
`http://localhost:1455/auth/callback` redirect. Port changes must update both
desktop binding and an accepted OAuth redirect configuration. Keep the desktop
host local-only unless a deliberate architecture and security change expands
its exposure.

## Build and Validation

Requirements:

- .NET 10 SDK, pinned by `global.json`
- Node.js 22 or later for Electron.NET desktop builds and packaging

Build and start the browser-hosted development app:

```powershell
dotnet build PixelChat.sln
dotnet run --project PixelChat
```

Start the Electron desktop shell:

```powershell
dotnet run --project PixelChat -- --electron
```

Create the current Windows x64 folder publish:

```powershell
dotnet publish PixelChat/PixelChat.csproj -c Release -r win-x64 --self-contained
```

Normal source changes require a successful solution build followed by a
browser-host startup check with no startup exceptions. Always terminate the
host after validation. Electron startup, browser UI checks, screenshots,
Playwright, and manual UI validation are performed only when explicitly
requested. Documentation-only work should still validate every referenced path
and command and should run broader checks when the documentation asserts that
those checks work.

There are currently no automated test projects. Do not add one without explicit
user direction. Cross-platform runtime identifiers and successful compilation
do not validate Electron packaging, OAuth, provider calls, image generation,
rembg provisioning or acceleration, or OS-specific behavior. Exercise the
relevant integration on the relevant platform before claiming it works, and
report anything not exercised.
