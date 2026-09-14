# Image-assisted poses

Use image generation for artwork and pose proposals when direct drawing is inefficient. Specify reference roles (identity, style, previous pose, guide), what changes, what must remain, logical dimensions, facing and required contacts. Guides are optional. Art and animation recipes remain separate reusable inputs.

Prepare edits from a captured document/frame revision and explicit target layer or canvas. Retain provider bytes. Inspect alpha and the entire result; provider masks are advisory and do not lock pixels. Compare candidates to the captured target, then apply through the document engine. A newer manual edit must cause a conflict. Use deterministic selection/layer commands for exact repairs. Never paste original pixels over an AI result without an explicit deterministic editing operation.
