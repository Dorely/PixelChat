# Codex images

Dated 2026-10-29 from PixelChat's previous .NET implementation (live findings of
2026-09-14) and the ImageGenCli Codex provider. Revalidate before changing provider code.

## Endpoint and account

- `POST https://chatgpt.com/backend-api/codex/images/generations`, or `/edits` when any
  input image is sent. Bearer token from the Codex OAuth sign-in (the same client as
  ImageGenCli and SfxChat), `chatgpt-account-id` from the token's JWT claims.
- Each image draws on the ChatGPT plan's image allowance. Rate-limit and plan-limit
  headers are recorded with the result's usage.
- Body: `prompt`, `model`, `size` (`WIDTHxHEIGHT`), `background: "auto"`, optional
  `quality` (omitted for auto), and `images` as data URLs. The response carries
  `data[0].b64_json` (PNG), `generation_id`, and the size, quality, and background the
  backend actually served, which may differ from the request.

## Models

| Model | Qualities | Native alpha |
| --- | --- | --- |
| `gpt-image-2.5-flare` (PixelChat default) | auto, low, medium, high, xhigh, max | yes |
| `gpt-image-2.5-sunburst` | same | yes |
| `gpt-image-2` | auto, low, medium, high | no |

Up to four input images per request, including an edit source.

## Sizes

Rasters are multiples of 16, between 0.66 and 8.3 MP, edges up to 3840, aspect within
3:1. PixelChat resolves `WIDTHxHEIGHT` to the nearest valid raster and aspects or presets
to about 1.57 MP.

## Transparency

- On 2026-09-14 the endpoint rejected an explicit `background: "transparent"` for both
  2.5 models. `auto` plus explicit alpha instructions in the prompt produced real alpha
  for generation on both.
- Edits: Flare kept alpha; one Sunburst edit returned opaque pixels. PixelChat flags a
  result that is fully opaque when transparency was asked for, and never cleans up or
  retries.
- Ask for real transparency and forbid painted checkerboards, backdrops, and cast shadows;
  PixelChat appends that wording by code.

## Failure semantics

A 401 refreshes the token once. A response that arrived (any HTTP status) means the
outcome is known. A timeout, transport failure, or unreadable body may still have been
rendered and billed, so the job is recorded as uncertain and never replayed.
