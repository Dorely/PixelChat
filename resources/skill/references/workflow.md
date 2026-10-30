# Review workflow

## Reviews

A review is an ordered board of exact versions with instructions. Versions are
immutable: an entry's `revisionId` names the bytes the user saw, and its decision
(`unreviewed`, `accepted`, `needs_changes`, or `rejected`) belongs to them. New art never
inherits a decision. When you make or rescan a new version, it is unreviewed until you put
it in front of the user: add it to an active review (`review_update` with `addItemIds` or
`addRevisionIds`; it sits beside the old entry) or create a new review. `newerVersions` on
an entry tells you the item has moved on since.

- `review_get` returns entries and the note threads on their versions.
- `review_decide` names entries by `revisionId`.
- Title, instructions, entries, completion, and reopening require `expectedRevision`.
  On `REVISION_CONFLICT`, read the review again and reapply your change if it still
  applies.
- Completed reviews are frozen: decisions, entries, and new review notes are refused,
  and their items cannot be deleted. Replies and resolution still work. Do not reopen a
  review just to get around the lock; put new versions in a new review.
- Create a review (`review_create` with `itemIds` or a `search_items` filter) to put new
  or revised art in front of the user, then `ui_open` it.

## Notes and pins

Notes belong to an item and to the version they were written on; a review note is on one
of that review's versions (`note_add` defaults to the item's entry there). A root note may carry a
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

To respond while the user reviews, wait on the review:

- `review_wait { projectId, reviewId }` blocks until the user decides, writes or resolves a
  note, changes entries, completes, or reopens the review, then returns those `events`,
  the review summary, and a `cursor`. Pass the cursor as `afterSeq` next time so nothing
  is missed, act on the events (read notes with `review_get`, look with `view_image`,
  reply), and wait again. `until: "completed"` waits for completion instead. On
  `timedOut`, just wait again. Waits default to 50 seconds; pass a longer `wait` (up to
  1800) only if your client allows long tool calls.
- From a shell, `pixelchat watch-review <projectId> <reviewId>` holds one connection and
  prints each event as a JSON line until the review is completed. Run it in the background
  and react to each line.

`activity_get` lists all attributed changes in order: decisions, notes, selections,
exports, and jobs, with the same `cursor`/`afterSeq` and `wait`. Over MCP, subscribe to
`pixelchat://projects/{projectId}/pending-exports` or a review resource for change
notifications; the waits work without them.
