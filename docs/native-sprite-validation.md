# Native sprite release verification

Recorded September 14, 2026 on Windows with .NET 10 and headless Microsoft Edge. This records local implementation checks, not a claim that an image provider or model produces production-quality animation.

## Local checks

- `dotnet test PixelChat.Tests/PixelChat.Tests.csproj`: 42 tests passed. Coverage includes exact pixels, fill/clipping, selection preservation, palette/alpha rules, compositing, arbitrary nearest rotation, history across context restart, manual/agent interleaving, stale revisions and atomic failure.
- Migration fixtures preserve source-derived and edited pixels, hidden RGB under transparent alpha, source geometry, IDs, saved masks, and edit provenance. Differently sized content and initially empty frame sets are covered.
- Worker checks exercise real subprocess execution, timeout, recursion, allocation, forbidden access, cancellation, and a forcibly terminated worker. Failures do not commit document state.
- All-frame diagnostics find a defect in frame 32. Jumping and squash/stretch produce advisory findings without modifying motion. Timing checks include unequal durations, reverse, ping-pong, one-shot and looping sequences.
- AI workflow tests use a deterministic fixture provider. They check native target/preparation binding, source-independent edits, preservation of raw provider bytes, authoritative masked output, undo and stale-result rejection. These are not live provider integration tests.
- Native bundle, PNG atlas and PNG frame round trips reconstruct exact RGBA and metadata. Padding/gutter/margin affect both pixels and rectangles. The existing derived-sheet route shares the same geometry. Tampered bundle data is rejected before document creation.
- Headless browser checks pass for pointer coordinates, visible pixels, undo/redo, layer visibility, frame selection/duplication, duration editing, history/reopen, all-frame diagnostics, preparation PNGs, reverse one-shot playback and native bundle export/reimport. Tool tests verify actual PNG model content and revision caching through the same renderer used by the workspace.
- Solution build succeeds with zero warnings/errors. The isolated development host starts and migrates without startup exceptions. Validation hosts and headless browser instances are terminated afterward.

## Fixed direct-drawing corpus

`PixelChat.Tests/sprite-corpus.js` is the reusable 64×64 source. The test executes each fixture in a real worker, inspects all frames, runs numerical validation and reconstructs its native bundle. Each uses one script call, one contact-sheet render, one validation call and two export calls (native bundle and GIF). No retries, manual interventions or image-provider calls occurred. Timings below include these local operations and exclude authoring the script and subsequent visual review; they are single-run observations, not model latency benchmarks. Model token/usage data is unavailable for this deterministic run.

| Fixture | Frames | Commands | Elapsed | Visual judgment from the contact sheet |
| --- | ---: | ---: | ---: | --- |
| Strict-pixel prop | 1 | 40 | 579 ms | Readable crystal silhouette and stable palette; accepted as a simple fixture. |
| Humanoid walk | 8 | 113 | 417 ms | Character identity remains readable, but contact alternation and foot planting are ambiguous; not accepted as a finished walk. |
| Nonhumanoid idle | 8 | 65 | 371 ms | Readable slime breathing/squash sequence; accepted as a simple fixture, with intentional area/centroid changes retained. |
| Impact effect | 8 | 94 | 410 ms | Expanding rays are readable, but disappearance of the core around frames 5–6 is abrupt; needs artistic refinement. |
| Painted character | 8 | 153 | 420 ms | Exercises continuous-alpha layers and body bobbing; proportions and limb motion are schematic, not accepted as polished painted animation. |

Structural and reconstruction checks passed for every fixture. These visual judgments are separate from measured validation and do not certify temporal quality from playback. The fixtures intentionally remain reproducible baselines rather than being edited until every example looks successful.

To reproduce artifacts:

```powershell
$env:PIXELCHAT_CORPUS_OUTPUT = Join-Path $env:TEMP 'pixelchat-native-corpus'
dotnet test PixelChat.Tests/PixelChat.Tests.csproj --filter FullyQualifiedName~SpriteCorpusTests
```

The output directory contains contact PNGs, GIFs, editable bundles and `results.json` with operations, timings and findings.

## Remaining integration and quality evaluation

Live Astra chat orchestration, Sunburst/Flare image-assisted poses, generated-sheet quality comparisons, provider cost/retry comparisons, OAuth, background-removal acceleration and Electron packaging were not exercised in this implementation run. Existing reports elsewhere in the repository are not evidence for the new native workflow. The three-way creative comparison therefore remains unverified; no winning generation method or production-quality acceptance is claimed.

For that evaluation, run the same five briefs through direct drawing, image-assisted poses and generated sheets where applicable. Preserve approved references, logical size/palette/motion requirements and each returned candidate. Record the selected provider model, actual calls/retries/usage, elapsed generation and review time, interventions, frame-addressed judgments and accepted revision. Use native exports to compare exact output and retain rejected candidates. GIF previews quantize color/alpha and timing; use native playback and PNG/JSON for exact inspection.

The Jint worker has resource limits and no exposed system APIs; it is not an operating-system security boundary. Native bundles contain one portable revision snapshot; persistent undo history stays with the originating document in the local database.


Account assistant schema regression (September 14, 2026)

The initial editor browser suites exercised manual controls but did not send an actual assistant turn to the account API. A user-reported 400 exposed the untyped `sprite_apply.operations.items` schema under forced strict mode. The corrected request retains typed operation objects and uses non-strict transport only for schemas with explicitly open properties; the engine still validates and commits batches atomically.

A live headless Edge run against the signed-in local app used gpt-6-astra at the saved high effort. All 65 tool definitions were accepted (HTTP 200). Astra called workspace state, sprite_read, sprite_help, sprite_apply, and sprite_render, then completed with ACCOUNT_TOOL_OK. In project `Account tool validation 1789446336538`, one batch created exactly the requested red pixel at (2,3) and changed timing to 125ms. The browser checked authoritative canvas RGBA and the duration field, and the resulting contact/frame PNGs appeared in chat. The prior project/model selection was restored and the browser and validation host were terminated. This verifies this account assistant path; it does not extend prior image-generation, packaging, or platform claims.


Alpha-aware vision regression (September 14, 2026)

The assistant now receives source-alpha measurements beside every image, with an actual opaque inspection composite. Asset/frame/native viewing tools accept an explicit contrasting background. Source/provider artwork is unchanged. All 58 xUnit tests pass, including hidden RGB at alpha zero, exact partial-alpha blending, original-byte preservation, opaque JPEGs, animated-input measurement scope, invalid colors, native artifact cache behavior, account image/metadata serialization, and readable inspection captions.

The normal solution output was locked by the user's active PixelChat debugger. The solution built with zero warnings/errors using `-p:BaseOutputPath=C:/Users/jonth/AppData/Local/Temp/pixelchat-alpha-build/`; the documented `dotnet run --project PixelChat` command with the same output property started successfully on port 1465. The user's debug session was left running.

A live Astra/high browser turn inspected a byte-identical copy of the reported 1536x1024 spritesheet. Default gray and explicit magenta asset reads reported 951,572 fully transparent pixels, 621,292 partially transparent pixels, zero opaque pixels, and alpha range 0–254. Astra also called inspect_frame on white and sprite_render on magenta, distinguished their resized/labeled measurement scopes, and concluded that background-removal regeneration was not justified: visible green belonged to characters/spell effects, while open background areas were clean. No image generation or cleanup was performed. The transcript remains in `Alpha vision validation 1789447930045`.

Browser assertions and a viewed screenshot verified the persisted magenta pixels, source PNG hash, reload persistence, and the corrected inspection modal: a readable source-alpha summary, whole-image fit, visible Close control, and no misleading source-transparency controls on the opaque preview. These are checks of the actual account assistant path and visible results; they do not establish universal artistic judgment quality or verify new image-provider calls.

The final complete `sprite-alpha-live.browser.cjs` run passed against the synthetic 64x64 fixture in `Alpha vision validation 1789448596113`: 3,072 fully transparent pixels with hidden green RGB, 124 partially transparent edge pixels, and 900 opaque pixels. All three viewing tools, exact stored composite pixels, byte-identical source, modal bounds/controls, and reload persistence passed. Astra again judged regeneration unjustified. Validation browsers and the separate host were terminated; the existing user debugger was not stopped. Provider image generation and other provider integrations were not exercised by this change.

After the normal output lock was released, the final unmodified `dotnet build PixelChat.sln` also passed with zero warnings/errors. `dotnet run --project PixelChat --no-build --no-launch-profile -- --environment Development --urls http://127.0.0.1:1465` then started from the normal output without startup exceptions and was terminated after the smoke check.
