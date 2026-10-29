# Image editing

## Requests

- `edit_image` edits an item's selected version (or `revisionId`); `generate_images`
  makes new items, or new versions of `itemId`. Both return queued jobs, one per image;
  follow them with `jobs_get` (`batchId` or `jobIds`, optional `wait`).
- Pass a fresh `requestId` per request and keep it. Repeating it with identical arguments
  returns the same jobs; different arguments return `REQUEST_CONFLICT`.
- Job states: `queued`, `running`, `completed`, `failed`, `uncertain`, `cancelled`.
  `uncertain` means the provider may have rendered and billed without PixelChat
  receiving the result. It is never replayed; tell the user rather than resubmitting.
  Only queued jobs can be cancelled.
- `status` lists models with their qualities, transparency, and input limits, and which
  providers are signed in.

## Models

The model selects the provider. Codex models bill the user's ChatGPT plan; Gemini models
bill the Gemini API key's project.

- `gpt-image-2.5-flare` (default): fast iteration and edits; native alpha.
- `gpt-image-2.5-sunburst`: demanding detail, text, or subject preservation; native
  alpha, though one observed edit returned opaque pixels.
- `gpt-image-2`: no native alpha.
- `gemini-nano-banana-2.1`, `gemini-3-pro-image`: no alpha; quality is always auto.

## Size, format, background

- Edits default to the source's dimensions; providers return their nearest supported
  raster. Sizes: `auto`, `square`, `portrait`, `landscape`, `wide`, an aspect like
  `16:9`, or `WIDTHxHEIGHT`.
- Format defaults to the source file's format so the result can be exported in place.
- `background: auto` keeps a transparent source transparent (needs a native-alpha model
  and PNG or WebP). `transparent` and `opaque` force a treatment. PixelChat appends the
  background instructions itself; do not repeat them in the prompt.
- A job finishes with a warning when transparency was asked for but the result is fully
  opaque. Inspect with `view_image` before presenting it.

## Prompting

- Write the prompt as the change to make, then what must stay: character identity,
  proportions, palette, line weight, framing, and anything the notes did not mention.
- Turn pinned notes into concrete regional instructions ("the lure stem at the upper
  left: thicker, same color"). Pin coordinates mean nothing to the model.
- Keep edits focused; one change per request preserves the rest better than a list.
- Use `referenceItemIds` for style or character references; the edit source counts
  toward the model's input limit.
- Compare each result with `view_image` against the source version before reporting.
