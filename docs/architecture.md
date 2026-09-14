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
source regions, deterministic frame operations, and derived sheets. `ISpriteDocumentService`
owns native FrameSet revisions, shared immutable PNG content, atomic command
transactions, and persisted undo/redo. `SpriteCommandEngine` edits temporary
snapshots with layer locks, selection, palette, and allocation constraints.
Frame rows project document metadata and retain deleted identities and masks
for undo. Versioned document manifests contain layers, cels, clips, pivots,
selection, production specifications, and provenance. Stale expected revisions
fail without overwriting newer work. UI and assistant sprite
mutations should pass through `ISpriteWorkspaceActionService` when visible focus
and workspace synchronization must accompany the underlying mutation.

`IAssistantChatService` owns a scoped assistant turn and tool loop.
`IWorkspaceChatRuntime` is app-process state that allows a turn to continue
across renderer reloads, owns cancellable conversation compaction, and
broadcasts completed workspace effects. Compaction first removes persisted
assistant tool-call manifests, tool-result messages, and tool-linked chat
visuals. It adds an authoritative visible context notice that tells the model
to re-read current workspace state rather than infer removed tool results.
When the resulting next-request estimate remains above the configured
threshold, the default chat provider hierarchically summarizes token-bounded
text chunks and atomically replaces the old conversation with one structured
summary. Provider failure or cancellation leaves the original conversation
unchanged. Active chat attachments remain attached and continue to count
toward the estimate.
`IImageGenerationRuntime` similarly owns app-process generation/edit batches,
progress, retries, completion, and interrupted-batch reconciliation. Do not move
these lifetimes into a Razor component or make background work depend on a
single UI circuit.

Generation batches persist ordered `GenerationPrompt` and `GenerationOutput` rows.
Variants have one prompt with several outputs; assistant concept batches have
several distinct prompts with one output each. Recipe bulk batches accept ordered,
duplicate-preserving prompts, each with 1â€“4 outputs, without a fixed prompt-count
cap. The Generate workspace has Single/Bulk modes and art recipes offer Bulk
generate. Prompt entry, output cards, and pending review use pagination.

Submission captures recipe guidance/version, reference bytes/order, image model,
quality, format, and background. Recipe notes remain excluded. A forward migration
expands historical prompt/state/error JSON arrays into rows, preserves output
indexes and asset links, and snapshots available recipe/reference data. It removes
the superseded batch-array columns; per-output state/error payloads retain provider
diagnostics. Historical batch provider metadata remains readable; new metadata is
retained per asset. A downgrade requires restoring a database backup.

The app-process image runtime owns one active batch per project. It creates a
bounded worker set instead of a task for every prompt/output. `ImageRequestScheduler`
limits actual image provider requests across project image jobs to
`Images:MaxParallelRequests` (default four). Only awaited lifecycle transitions
write output state; live progress updates are serialized in memory and cannot
supersede saved success. Saving an output asset and its successful queue row is
one EF transaction. Completion reads asset metadata without loading image BLOBs;
queue collection reads use split queries to avoid prompt/output cross products.

Stop cancels in-flight work and leaves pending outputs for manual Resume. Resume
runs queued/cancelled slots; Retry failed runs only failures. Successful and deleted
outputs are never regenerated. Shutdown reconciliation marks unfinished work stopped
without dispatching requests. Transient transport/rate/timeout errors use bounded
retries, exponential backoff, and HTTP Retry-After; invalid requests, account access,
and quota failures remain actionable errors. Timeout covers response streaming as
well as headers. Partial preview images remain transient and are released when an
output finishes. UI generation refreshes are coalesced and reloads serialized.

Recipes are maintained current creative guidance rather than cumulative project
documentation. Their fields have distinct ownership:

- The prompt is concise model-facing guidance that should apply to every future
  use within the recipe's scope. Art prompts use only applicable visual
  language, subject-family, composition, and production-use blocks; animation
  prompts use only applicable motion, layout, continuity, and timing blocks.
- Notes are model-excluded current working memory for active direction,
  workflow preferences, reference-use instructions, and operational caveats.
  They may change as work evolves and should not retain superseded chronology.
- Attachments carry reusable visual evidence as ordered example or guide
  references.
- Version change summaries carry history. One-off subjects, candidate details,
  experiments, constraints, and output diagnoses belong to generation prompts,
  review state, or chat rather than the reusable recipe prompt.

Assistant recipe updates rewrite the current prompt and notes as coherent
snapshots: still-valid guidance is retained, changed rules are replaced, and
conflicts, duplication, and abandoned directions are removed. A recipe
revision is saved before a controlled generation test so batches retain exact
version provenance; disproven revisions are replaced or reverted rather than
extended with compatibility-style exceptions.

Provider-neutral contracts isolate chat and image workflows from transports.
Provider-specific OAuth, Responses streaming, tool-call parsing, readiness
checks, and image requests belong in the `Llm`, `Auth`, and provider adapter
implementations. New providers should extend those boundaries rather than add
provider conditionals throughout the UI or art services.

Asset and frame edit masks are provider guidance, not a local pixel lock.
Canvas preparation may create or transform a mask for localized edits and
outpainting, and the provider receives that mask when supported. After the
provider returns an image, canvas finalization may normalize provider
dimensions, restore logical scale, crop provider padding, and normalize
editable removable-background pixels. The complete provider result remains
authoritative: PixelChat never pastes protected source pixels back over it.

OpenAI account chat exposes Sol, Terra, Luna, and Astra through the shared OAuth
connection. Built-in models become ready from valid credentials without a separate
manual test; a selected unavailable model produces an error rather than fallback.
The chat selector persists model and effort (low, medium, high, xhigh, max).
Built-in requests have a 272,000-token context and 258,400-token input ceiling.
The estimate includes instructions, serialized tool schemas, conversation content,
and conservative image reserves. Each initial/continuation submission checks the
ceiling and compacts safely using the captured provider; an input still over the
ceiling is rejected. Local estimates are not a billing guarantee.

`ImageModelSelectionService` persists global model/quality preferences using
short-lived scopes. Sunburst is the default, with Flare and Image 2 alternatives.
Quality defaults to auto; 2.5 also offers xhigh/max, and changing to Image 2
visibly normalizes unsupported quality to auto. Batches snapshot model, quality,
format, and background. Image orchestration uses Sol independently of chat.

Native `transparent` differs from removable magenta. A forward migration converts
historical transparent aliases to removable before introducing the new meaning.
Generation and recipe preferences support native alpha on 2.5; edits default to
preserving the submitted source treatment, with explicit overrides independent
of recipe backgrounds. Native requests deliberately send image tool `background: auto`
and PNG, while injecting explicit empty-alpha and no-painted-checkerboard requirements
into Responses system instructions. Application background remains `transparent`;
provider metadata records requested and transport backgrounds separately. Background
and edit/mask instructions are selected by code, not conditional model-facing rules. Original provider output bytes remain separately inspectable/downloadable after
canvas finalization, including direct frame edit revisions. Native edit padding has
alpha zero; cropping, resizing, thumbnails, and exports preserve RGBA and bypass
magenta normalization. Returned provider pixels remain authoritative.

The shared transparency inspector measures the complete decoded raster and offers
checkerboard, white, black, and color preview backgrounds, plus pixel coordinates,
RGBA, and opacity. Preview backgrounds and overlays never enter asset bytes.
Opaque requested-alpha results show a warning and remain available for inspection;
mixed alpha is explicitly not proof that the entire background is transparent.
The September 14, 2026 account endpoint rejected the explicit transparent parameter
for both 2.5 IDs. The auto parameter plus alpha instructions produced real alpha for
generation on both models. With the wired system guidance, a Flare edit retained
alpha while a Sunburst edit returned opaque pixels. The inspector discloses
this route and retains measured-alpha warnings. There is no background retry or
cleanup fallback. Assistant system prompts inject only the selected image model's
applicable guidance, including native-alpha availability; static tool descriptions
refer to that guidance instead of presenting model-dependent branches. Initial,
continuation, compacted, and idle token-estimate prompts use the current global
image selection.

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
environment-variable overrides. Options records own the agent, chat-compaction
token threshold, image generation, sprite animation, background removal, token
counting, desktop host, OAuth redirect, and persistence settings. Avoid
hard-coding configuration in components or feature entities.

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

The user authorized automated tests and browser checks for the native sprite
release. Run `dotnet test PixelChat.Tests/PixelChat.Tests.csproj` for raster,
transaction, history, conflict, and migration fixtures. Native migration first
adds the document tables, materializes existing rendered cells, and only then
drops mutable bitmap columns. Failed materialization leaves those columns intact.
Cross-platform runtime identifiers and successful compilation
do not validate Electron packaging, OAuth, provider calls, image generation,
rembg provisioning or acceleration, or OS-specific behavior. Exercise the
relevant integration on the relevant platform before claiming it works, and
report anything not exercised.


Native sprite editor UI

`SpriteSheetWorkspace` manages source imports and the active document. `NativeSpriteEditor` sends complete pointer gestures as revision-checked command batches; its canvas displays server-rendered pixels and transient stroke previews. Rectangle/polygon/color selections, clipboard contents, layers, frame timing, clips, pivots, and history belong to document state. `SpriteDocumentEvents` triggers reloads after agent or manual commits. Revision-specific media URLs keep previews tied to the state they label. `SpriteTimeline` defines frame order for native playback and Review; the shared animation preview consumes saved durations and loop policy.

For the authorized browser smoke check, run a host against an isolated database in Development (unpublished builds need development static-web-assets resolution), then run `node PixelChat.Tests/sprite-editor.browser.cjs`. Supply `PIXELCHAT_PLAYWRIGHT_MODULE` if Playwright is outside local module resolution and `PIXELCHAT_TEST_URL` for a different host. The script closes its headless Edge instance; terminate the host afterward. The check creates a named fixture sprite in that isolated database. Optional `PIXELCHAT_TEST_SCREENSHOT` saves a screenshot.

Native agent editing and inspection

`SpriteToolRegistry` exposes native document reads, creation, command batches, scripts, renders, history, and progressively loaded embedded references through the existing function registry. `AssistantChatService` attaches actual PNG content from persisted `SpriteInspection` records to tool results; transcript visuals use the existing persistence path. Inspection renders are cached by document revision and request. Contact-sheet pagination does not imply numerical validation coverage. Color selections store a fixed mask resolved from the visible composite, so changing layers or pixels does not change selection membership.

Scripts run in a dedicated invocation of the application with `--sprite-script-worker`. Jint 4.16 exposes only pure JavaScript document data and an operation-queue API; CLR access and module loading are not enabled. Worker limits include two seconds of interpreter time, 250,000 statements, recursion depth 64, 32 MB interpreter allocation, 10,000 commands, and bounded input/output. The parent enforces an eight-second deadline, a 256 MB working-set ceiling, cancellation, and process-tree termination. The worker receives a scrubbed environment and no application services. Jint is not an operating-system security boundary. Commands are applied through the shared engine only after successful worker completion and a fresh revision check; scripts read the starting snapshot, not intermediate queued results.

Native AI jobs and diagnostics

`SpriteGenerationService` captures document revision, frame, layer, source pixels, selection/mask, and prepared logical/provider canvases. Preparations are bound to the actual native revision and layer. The existing app-process `IImageGenerationRuntime` starts and controls native jobs through `ArtWorkflowService.Sprites`; there is no second background runner or source-asset surrogate. GenerationBatch snapshots target metadata, reference roles and guide metadata, model/quality, recipe versions, and mask/canvas bytes. Provider output uses the existing authoritative finalizer and retains raw bytes on candidate assets. Applying a candidate sends a revision-checked command batch; source pixels are never pasted back over provider output. Strict palette/alpha violations are rejected instead of silently quantized. Stale candidates stay inspectable and require a new current-revision job. Native jobs keep the Sprites workspace visible when resumed.

`SpriteAiPanel` provides the same preparation/candidate/control loop manually using short-lived service scopes. `SpriteValidationService` measures every frame and actual clip sequence, including late frames, reverse/ping-pong timing, one-shots and loop seams. Alpha, palette, dimensions, duplication and pixel counts are facts; edge contact, centroid/area changes, palette drift and pixel discontinuities are heuristics. Artistic findings are stored explicitly as judgments with revision/frame references. Validation never changes pixels. Assessment and inspection records survive chat compaction.

The fixed sheet-first/normalize/center/single-row prompt itinerary and direct AI frame-edit route are removed. Operational references remain separate from art and animation recipes. The old SpriteEditSession runtime/table is retired; its target-linked provenance is migrated to assessments or source asset metadata while candidate assets and image batches remain intact. Unused generation-round/candidate-sheet/repair/snapshot animation settings are removed; active image runtime limits remain in Images configuration.


Native exports and release boundaries

`SpriteExportService` persists immutable revision/specification-addressed export artifacts in `SpriteExports`. Native ZIP bundles contain a versioned document manifest and hash-verified PNG bitmap content, including clipboard and provenance. They represent one portable snapshot; source document history remains in SQLite. Import remaps frame IDs to preserve global relational identity, records original IDs, and restores clips/selection references. Asset and region references are provenance when their original project is unavailable. Bounded ZIP decoding checks versions, entry sizes, duplicate names, hashes, dimensions, and reconstructed pixels without extracting files.

`SpriteAtlasBuilder` owns placement for both native exports and the existing `build_sheet` derived-asset path. Padding is transparent space inside every frame slot, gutter separates slots, and outer margin surrounds the atlas. Version 1 JSON contains actual pixel rectangles, slot rectangles, logical dimensions/offsets, frame order/durations, clips, pivots, slices, and pixel hashes. Native exports default to top-left placement. PNG frame exports and metadata do not allocate an atlas. GIF previews honor clip order and loop policy but quantize colors/alpha and round timing to centiseconds; native playback and PNG/JSON retain exact data. Historical exports are not rewritten.

`SpriteExportPanel` exposes exports, refresh/download history, and named slices. The import panel accepts native, atlas and frame ZIPs. `/media/projects/{projectId}/sprite-exports/{id}` enforces project ownership and supports inline GIF previews. The eleven native tools include generation/jobs/export and cached playback inspection. Typed translation replaces the obsolete absolute-offset tool; anchor matching uses materialized logical canvas coordinates. Relational import offsets remain provenance only. Frame pivots project into relational anchors. A forward migration supplies missing blank-frame identities for migrated empty sets.

Raster bounds are 8192 pixels per side, 16 megapixels per canvas, 64 megapixels of unique document cel content and per-batch raster allocation, plus a stroke-work budget. Rotation takes explicit nearest/smooth resampling; pixel mode permits nearest only. Existing nonconforming pixels require explicit conversion before enforcing strict rules. Script startup prefers the sibling native apphost, with dotnet assembly fallback. The worker is always terminated after cancellation/failure.

Run the fixed direct-drawing corpus with `PIXELCHAT_CORPUS_OUTPUT` set to an output directory and `dotnet test PixelChat.Tests/PixelChat.Tests.csproj --filter FullyQualifiedName~SpriteCorpusTests`. It writes labeled contact PNGs, GIFs, native bundles and measured results. These are deterministic engineering fixtures; artistic acceptance and unperformed provider comparisons are recorded in `docs/native-sprite-validation.md`.


Sprite viewport and inspector

Sprites uses a bounded flex/grid layout inside the sprite-specific workspace host. The document header and contextual options stay above the canvas; the tool rail, viewport, fixed-height frame timeline and tabbed inspector share the remaining space. The inspector scrolls independently and can be collapsed. AI, export and history panels remain mounted when hidden so switching tabs does not discard drafts or job state. References and recipes use named project selections rather than requiring raw IDs. Source imports and blank creation appear in bounded overlays.

`NativeSpriteEditor.razor.js` owns floating zoom, pan and fit state locally. Initial display and changes to frame dimensions fit the entire image with margins. Fit mode tracks viewport resizes; manual navigation keeps its zoom and center when space changes. Same-sized frame switches and document revisions retain navigation. Wheel zoom anchors the image coordinate under the pointer; right/middle-button pointer capture pans without issuing document commands. Pan limits retain a visible sliver of artwork. Fit and actual-size controls, plus a numeric percentage field, span 0.1–12,800% zoom. Navigation does not change authoritative raster dimensions or create history entries.

The canvas backing raster stays at logical dimensions and CSS transforms control its presentation. Coordinate conversion uses its transformed client rectangle for all drawing and selection tools. Pixel mode displays crisp edges; painted artwork uses smooth display below 100% and reveals pixels when enlarged. Image decoding is cached by revision/onion URLs across tool-only changes. Pointer previews render at most once per animation frame; eyedropper reads only one source pixel. Event listeners, pointer state and resize observers belong to the editor and are disposed with it. UI selection overlays receive polygon geometry rather than transmitting the full saved color-selection mask.

Run `node PixelChat.Tests/sprite-viewport.browser.cjs` against the same isolated development host used by `sprite-editor.browser.cjs`. It verifies tiny/portrait/4096px image fitting, fractional and high zoom, wheel anchoring, right-drag pan without edits, drawing after transforms, undo without losing navigation, responsive fit and inspector collapse, an actual 4096×2048 PNG import, and retained AI drafts. Optional `PIXELCHAT_TEST_SCREENSHOT` captures the resulting layout for review. Existing editor browser coverage follows the new inspector tabs. Browser tests close their own Edge instances; terminate the validation host afterward.
