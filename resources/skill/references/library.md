# Library and export

## Items and versions

An item is the lineage of one artwork; its versions (revisions) are immutable images,
never changed in place, with provenance: imported, generated, or edited, the parent
version, and for image jobs the exact prompt, model, and request ids. `selectedRevisionId` is the user's chosen version.
`syncedRevisionId` is the version that matches the source file on disk.

An item is **pending export** when its selected version differs from the synced one:
`search_items { status: "pending_export" }` lists them.

## Sources

`source_add` links an absolute folder and imports PNG, WebP, and JPEG files (dot-folders
skipped; include and exclude globs use forward-slash relative paths). `source_rescan`
reports added, changed, missing, and unchanged files; a changed file becomes a new synced
version in the item's lineage, unreviewed (reviews keep the version they showed). To bring
in reworked art without touching the source file, `import_files` with `itemId` adds each
file as a new version of that item. Follow scans with `source_get` and `wait`. PixelChat never modifies source files
except through `export_item`.

## Export

`export_item` writes the selected version's exact bytes to the linked source file and
makes it the synced version, or to an absolute `destination`. It refuses with:

- `SOURCE_CHANGED`: the file on disk differs from the synced version, or is missing.
  Someone changed it outside PixelChat. Rescan to bring that change in as a version, or
  ask the user. Do not overwrite their file without an explicit instruction.
- `FORMAT_MISMATCH`: the version's format does not match the file extension. Make a
  version in the right format (edits take a `format`).
- `DESTINATION_EXISTS`: the destination file exists.

`force` overrides `SOURCE_CHANGED` and `DESTINATION_EXISTS`, only on the user's explicit
instruction. Exports are written atomically.
