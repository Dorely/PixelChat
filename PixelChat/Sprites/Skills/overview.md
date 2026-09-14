# Native sprite workflow

Read the current document and production rules. Choose direct drawing, procedural animation, or image-assisted poses for the task. Preserve identity references, intended motion, logical dimensions, and art mode. Use expectedRevision on every mutation. A conflict means read current state and rebase; do not blindly retry old operations.

Use sprite_help to load only the needed reference: commands, scripting, drawing, poses, animation, or export. Use typed batches for local repairs and scripts for coherent drawing or repetition. Inspect actual PNG evidence with sprite_render after a meaningful batch. Paginate contact sheets to see every frame. Numerical validation and artistic judgments are separate. Task IDs group only contiguous edits; manual work closes the group.

Completion means the editable document, all frames and timing, visual inspection, repair findings, and reproducible exports satisfy the user's task. Image-provider outputs are proposals until applied through the revision-checked document engine.
