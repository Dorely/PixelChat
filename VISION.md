# Vision

PixelChat is an AI-assisted 2D game art workbench for turning rough ideas, references, and generated images into usable game assets.

The goal is not to build a general-purpose image generator. The goal is to help game developers move from promptable art direction to production-ready sprites, icons, props, tiles, backgrounds, masks, and animation sprite sheets with less friction and more repeatability.

## Product Direction

PixelChat should feel like a sprite-editing environment with AI assistance woven into the creative workflow. The user should be able to start from a prompt, a reference, an imported asset, or an existing image, then iteratively shape the result into something that can be used in a game.

The app should make the path from idea to asset visible and controllable. AI should help generate options, diagnose problems, propose repairs, and preserve useful lessons, but the user remains able to inspect, override, refine, and finish the work manually.

## Creative Memory

PixelChat should help users preserve what worked.

Successful style direction, production constraints, prompt language, references, and motion/layout guidance should be reusable without forcing every future asset into the same mold. Visual style knowledge and animation/motion knowledge should remain conceptually distinct so a user can reuse either one independently.

Recipes are not just saved prompts. They are reusable creative guidance: a way to carry forward a style, constraint set, asset-production lesson, or animation approach that produced useful results.

## Core Capabilities

1. **Agentic creative guidance**
   A chat assistant helps users explore visual direction, generate and refine prompts, compare results, diagnose failures, carry out bounded repair work, and save reusable creative lessons.

2. **AI-powered image generation and editing**
   The system integrates with image generation APIs to create new assets, produce variations, and perform targeted edits on generated or imported images. The architecture should stay provider-agnostic.

3. **A focused sprite editor**
   The editor should provide practical game-art tools for isolating, aligning, cleaning, masking, previewing, and exporting sprites and animation frames. It should prioritize workflows that make assets usable in game engines, not just visually appealing in a gallery.

4. **Transparent collaboration**
   Users should be able to follow what the assistant is doing, see intermediate outputs, understand why a step was taken, and take over the work at any point.

## Human Control

Every important AI-assisted operation should have a visible manual counterpart when that is practical and safe. The assistant should operate through understandable actions, not hidden magic.

The user should be able to review outputs, inspect intermediate states, correct mistakes, delete unwanted results, and repeat successful workflows with different source material.

## Design Direction

PixelChat should borrow from proven sprite and pixel-art tools: timelines, onion skinning, transforms, selections, pivots, slices, preview playback, and sprite-sheet export. It should adapt those ideas for an AI-assisted workflow rather than copying a traditional editor wholesale.

The guiding principle is simple:

**Help game developers iterate from idea to usable 2D game art as quickly, consistently, and repeatably as possible.**
