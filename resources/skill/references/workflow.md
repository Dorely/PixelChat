# Review workflow

## Reviews

A review is an ordered board of items with instructions. Each entry has a decision:
`unreviewed`, `accepted`, `needs_changes`, or `rejected`, recorded against the item's
selected version at the time. An entry is `stale` when the selected version changed after
the decision, so the user sees revised art again.

- `review_get` returns entries and the note threads on their items. Pass `afterRevision`
  with `wait` (seconds) to block until the review changes.
- Title, instructions, entries, completion, and reopening require `expectedRevision`.
  On `REVISION_CONFLICT`, read the review again and reapply your change if it still
  applies.
- Completed reviews lock decisions and new review notes. Replies and resolution still
  work. Do not reopen a review just to get around the lock.
- Create a review (`review_create` with `itemIds` or a `search_items` filter) to put new
  or revised art in front of the user, then `ui_open` it.

## Notes and pins

Notes belong to an item and to the version they were written on. A root note may carry a
pin in that version's pixels: a point `{x, y}` or a box `{x, y, width, height}`. Pins on
another version than the one you are looking at still describe the older pixels.

- Treat open root notes as the work list. `needs_changes` with notes is a change request;
  `rejected` usually means start over, not polish.
- `view_image` draws open pins numbered in note order with their boxes, so you can match
  a note to the region it means. Use `revisionId` to see the version a note was written
  on.
- Reply with `note_add` and `parentId`. Resolve with `note_update { resolved: true }`
  after the change exists, not when you start. You may edit or delete only your own notes.

## Following the user

`activity_get` lists attributed changes in order: decisions, notes, selections, exports,
and jobs. Keep the returned `cursor` and pass it as `afterSeq` with `wait` to block until
something changes. Over MCP, subscribe to `pixelchat://projects/{projectId}/pending-exports`
or a review resource for change notifications; `activity_get` works without them.
