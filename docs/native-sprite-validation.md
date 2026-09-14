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
