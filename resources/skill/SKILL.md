---
name: pixelchat
description: Review, annotate, edit, and export game art through the PixelChat workspace. Read the user's review decisions and region-pinned notes, look at the art, answer and resolve notes, run image edits the user asked for, and export chosen versions back to the source files.
---

# PixelChat

Work through the installed workspace. PixelChat owns the library, original bytes, reviews,
notes, image providers, credentials, and jobs. Do not call image providers directly, copy
its database, or build a separate review page.

1. Read `guidance` and compare its hash with `guidance.json` beside this skill; use the
   returned documents if they differ. Discover tools through MCP or `pixelchat tools`;
   CLI calls use `pixelchat call <operation> --input <request.json>`. Choose the project
   explicitly with `projects_list`; the project visible in the window is not your context.
2. Find the work: `review_list` and `review_get` give decisions and note threads with pins;
   `search_items` with status `open_notes` or `pending_export` finds items across reviews.
   Read [review workflow](references/workflow.md) for notes, pins, and following changes.
3. Look before you judge. `view_image` returns the image with open pins numbered and
   drawn; use it whenever a note refers to what the art looks like. Size and alpha facts
   are not a visual review, and file names are not evidence of content.
4. Decisions are the user's. Record `review_decide` only when the user asks. Reply to a
   note with `note_add` and `parentId`; resolve the thread with `note_update` once the
   change exists, and say what changed. To respond as the user works, loop on
   `review_wait` (or run `pixelchat watch-review`) as described in the workflow reference.
5. Image edits and generation are billed to the user's account: run them only for the
   user's request, at the requested count. Read [image editing](references/image-editing.md)
   first. Keep the `requestId` and job ids; retry only with the same `requestId` and
   arguments. Never resubmit an `uncertain` job on your own.
6. Results are new versions. The user chooses one ("Use this version"), which makes the
   item pending export. Set `selectedRevisionId` yourself only when asked. Export with
   `export_item` as described in [library and export](references/library.md); never
   pass `force` without the user's explicit instruction.
7. Art you make outside PixelChat belongs in the library before review: rescan its source
   or use `import_files` (with `itemId` for a new version of an item). Every version is a
   new, unreviewed asset; add it to a review and show it with `ui_open`. A completed review
   is final; never expect new art to appear in it.

Never ask the user for credentials; providers are configured in PixelChat's Settings.
