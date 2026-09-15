# Native sprite workflow

Read the current document and production rules. Choose direct drawing, procedural animation, or image-assisted poses for the task. Preserve identity references, intended motion, logical dimensions, and art mode. Use expectedRevision on every mutation. A conflict means read current state and rebase; do not blindly retry old operations.

Use sprite_help to load only the needed reference: commands, scripting, drawing, poses, animation, or export. Use typed batches for local repairs and scripts for coherent drawing or repetition. Inspect actual PNG evidence with sprite_render after a meaningful batch. Paginate contact sheets to see every frame. Numerical validation and artistic judgments are separate. Task IDs group only contiguous edits; manual work closes the group.

Completion means the editable document, all frames and timing, visual inspection, and repair findings satisfy the user's task. Present changed artwork in Current Review with set_compare_review_set before replying, including animation previews for motion work. Refresh its title, summary, and items to show the latest work. Create exports only when the user requests downloadable files; do not append unsolicited preview or download links. Image-provider outputs are proposals until applied through the revision-checked document engine.
