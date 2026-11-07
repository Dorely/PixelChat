# Review workflow

## Reviews

A review is an ordered board of exact versions with instructions. Versions are
immutable: an entry's `revisionId` names the bytes the user saw, and its decision
(`unreviewed`, `accepted`, `needs_changes`, or `rejected`) belongs to them. New art never
inherits a decision. When you make or rescan a new version, it is unreviewed until you put
it in front of the user: add it to an active review (`review_update` with `addItemIds` or
`addRevisionIds`; it sits beside the old entry) or create a new review. `newerVersions` on
an entry tells you the item has moved on since.

- A review is `standard` (the user decides on each entry) or a `contest` (`mode: "contest"`,
  at least two entries): the entries are candidates and the user picks the winners. `picks`
  sets how many (default 1, fewer than the candidates); set it when you know more than one
  should win. A pick is `accepted`; a pick past the limit clears the oldest. The other
  candidates stay `unreviewed` and need no decision: `needs_changes` and `rejected` are
  refused, so the user's feedback on them arrives as notes. Use a contest to let the user
  choose between variations, not to ask yes/no on each. Read the outcome from `review_get`:
  the entries with decision `accepted` are the winners; fewer than `picks` means the user has
  not finished choosing. Do not record a pick yourself unless asked, and `review_wait` until
  the review completes or the user picks.
- Reference images give context for the decision (a style target, the current in-game art, a
  sibling asset): pass `referenceItemIds` or `referenceRevisionIds` to `review_create`, or
  `addReferenceItemIds` / `referenceRevisionIds` to `review_update`. Entries have a `role` of
  `subject` or `reference`. References show in their own section, may carry notes, take no
  decision, and do not count toward progress. A version is one or the other.
- `review_get` returns entries and the note threads on their versions.
- `review_decide` names entries by `revisionId`.
- Title, instructions, entries, completion, and reopening require `expectedRevision`.
  On `REVISION_CONFLICT`, read the review again and reapply your change if it still
  applies.
- Completed reviews are frozen: decisions, entries, and new review notes are refused,
  and their items cannot be deleted. Replies and resolution still work. Do not reopen a
  review just to get around the lock; put new versions in a new review.
- Create a review (`review_create` with `itemIds` or a `search_items` filter) to put new
  or revised art in front of the user; it opens unless `open` is false. If the user is in
  another review, PixelChat only offers it (`offered`) and leaves them where they are: say
  it is ready rather than opening it again.

## Notes, pins, and drawings

Notes belong to an item and to the version they were written on; a review note is on one
of that review's versions (`note_add` defaults to the item's entry there). A root note may carry a
pin in that version's pixels: a point `{x, y}` or a box `{x, y, width, height}`, and a
drawing (`sketch`): strokes with flat `points` `[x1, y1, x2, y2, ...]`, a `size`, and a
`color`, where the user circled, crossed out, or sketched what they mean. Marks on another
version than the one you are looking at still describe the older pixels.

- Treat open root notes as the work list. `needs_changes` with notes is a change request;
  `rejected` usually means start over, not polish.
- `view_image` draws open pins and drawings numbered in note order (a drawing's number
  sits where it starts) and reports each one's pin or drawing bounds, so you can match a
  note to the region it means. Look at drawings; their shape (an arrow, an outline, a
  sketched part) is often the feedback itself. Use `revisionId` to see the version a note was written
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
