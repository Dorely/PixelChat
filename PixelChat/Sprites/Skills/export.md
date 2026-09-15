# Export and verification

When the user requests downloadable files, export from an explicit document revision. Preserve the editable native document, PNG frames or atlas, frame durations, clip direction/loop policy, logical dimensions, offsets, pivots and slices. Present artwork and animation previews in Current Review; do not create exports or append download links as a routine completion step.

Padding is transparent space inside each frame slot; gutter separates slots; outer margin surrounds the atlas. Verify pixel reconstruction and alpha against the source revision. Historical export artifacts remain immutable. Do not assume successful file creation proves animation quality; retain measured findings and frame-addressed artistic review separately.

Use sprite_export(documentId, revision, specification) with format bundle|atlas|frames|preview|metadata, columns (0 automatic), padding, gutter, outerMargin, and optional preview clip name. Artifacts are persisted and downloadable. bundle includes editable layers, cels, production rules, clips, selection, and provenance; atlas and frames flatten visible layers. sprite_create with bundleExportId imports a saved native/atlas/frames export with new document/frame identities.

GIF is a preview: colors and alpha are quantized, and durations round to centiseconds. PNG and JSON retain exact RGBA and timing. A native bundle is a portable revision snapshot; the originating document retains its full undo history.
