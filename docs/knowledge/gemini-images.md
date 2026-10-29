# Gemini images

Dated 2026-10-29, condensed from ImageGenCli's Gemini research (verified against Google's
documentation on 2026-10-14). Revalidate before changing provider code.

## Endpoint and key

- `POST https://generativelanguage.googleapis.com/v1beta/interactions` with
  `x-goog-api-key`, `store: false`, and `stream: false`, so no conversation state is kept.
- Input is a text block plus base64 image blocks. `response_format` is
  `{ type: "image", aspect_ratio, image_size }`. The documented `delivery: "inline"` field
  returned HTTP 400 live on 2026-10-14 and is omitted.
- Inline requests are bounded at 20 MB. Billing is to the key's Google AI Studio project.

## Models

| Model | Ratios | Tiers |
| --- | --- | --- |
| `gemini-nano-banana-2.1` (Google's recommendation) | 14 documented | 1K, 2K, 4K |
| `gemini-3-pro-image` | 10 (no 1:4, 4:1, 1:8, 8:1) | 1K, 2K, 4K |

No quality or mask field exists. PixelChat allows five input images per request.

## Output

Read the final `model_output` steps of the completed interaction, skip thought steps and
thought-marked parts, and take the last image. Formats are checked against decoded
metadata and converted without resizing only when they differ. Results carry SynthID.
Error bodies are not echoed, to avoid disclosing keys or payloads.

## Transparency

The legacy image guide states transparent backgrounds are unsupported and no current page
establishes native alpha. PixelChat refuses transparent requests on Gemini models rather
than promising alpha; format conversion never creates transparency.

## Prompting

Describe the scene specifically with context and intent, state omissions positively, and
iterate with focused edits that say what must stay.
