# Native sprite editor implementation

The approved release promotes FrameSet into the native sprite document. It has five ordered implementation stages, each verified and committed separately.

1. Document foundation: immutable content-addressed PNGs; versioned FrameSet document state; a shared atomic command engine; persisted undo/redo; optimistic revision conflicts; blank documents; forward migration of rendered frames. Relational Frame rows retain identities for masks and references and are projections of document state. Deletion keeps those identities for undo. This stage removes mutable frame bitmap ownership and the unused HistoryTask runtime model.
2. Manual editor: native drawing/selection/transform controls, layers, timeline, clips, pivots, onion skin, history, and shared timing-aware playback.
3. Agent surface: compact sprite tools, model-visible PNG evidence, progressively loaded workflow references, and a resource-bounded Jint worker process.
4. AI and diagnostics: revision-bound preparations/candidates/application, complete animation measurements and review, job controls, and removal of the fixed sheet-generation itinerary.
5. Export and verification: deterministic atlases, frames, timing metadata, animated previews, portable document bundles, reconstruction checks, browser verification, and recorded creative evaluations.

Impact crosses Models, Persistence and forward migrations, Art and sprite services, dependency registration, chat tools and image-result handling, Razor/JavaScript workspace controls, media endpoints, image jobs, and documentation. Image-provider transport and raw provider outputs remain owned by the existing image workflow. Source regions and generated assets remain provenance/import inputs; built sheets remain derived artifacts.

Verification uses `dotnet test PixelChat.Tests/PixelChat.Tests.csproj`, `dotnet build PixelChat.sln`, and a bounded host startup check. Migration fixtures run against old schema boundaries before destructive column removal. Browser and live provider checks must be recorded separately; a successful build is not evidence of either.

All five implementation stages are implemented. The release verification record is in [native-sprite-validation.md](native-sprite-validation.md). Automated and browser checks cover the local editor loop; the recorded direct-drawing corpus includes explicit artistic judgments. Live image-assisted versus generated-sheet quality comparisons and Electron packaging were not exercised in this implementation run.
