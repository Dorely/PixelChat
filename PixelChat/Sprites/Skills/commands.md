# Typed sprite commands

sprite_apply takes documentId, expectedRevision, label, operations[], optional taskId and returnPreview. Every operation uses logical integer coordinates; IDs are GUIDs from sprite_read. Layers are ordered bottom to top. The whole batch is atomic.

- pencil / brush / erase: frameId, layerId, points:[{x,y},...], size (1-512), color (#RRGGBB or #RRGGBBAA). A single x,y is also accepted. Brush size1 is exact. Painted brush edges support fractional alpha.
- line: frameId, layerId, x,y,x2,y2,size,color.
- rectangle / ellipse: frameId,layerId,x,y,width,height,color,filled.
- fill: frameId,layerId,x,y,color (connected exact-color fill).
- select: frameId and x,y,width,height OR polygon:[{x,y},...]; optional color resolves a fixed mask from visible composite pixels. clearSelection removes the selection.
- copy / cut / clear: frameId,layerId, constrained by selection. Clipboard pixels are immutable. paste: frameId,layerId,x,y.
- stamp: source:{frameId,layerId}, target:{frameId,layerId}, sourceRect:{x,y,width,height}, destination:{x,y}.
- translate: frameId,layerId,dx,dy. flip adds axis horizontal|vertical. rotate adds integer degrees and resampling nearest|smooth; pixel mode requires nearest. Selection limits destination and source pixels.
- crop / resize: frameId,layerId,width,height; crop adds x,y; resize requires resampling nearest|smooth. Changes all layers in that frame; clear selection and unlock layers first. Pixel mode requires nearest.
- addLayer: optional id,name. setLayer: layerId, optional name,visible,locked,opacity(0-1). duplicateLayer: layerId, optional id,name. reorderLayer: layerId,index. mergeLayer merges downward. deleteLayer requires another layer.
- addFrame: optional id,name,index,durationMs. duplicateFrame: frameId, optional id,name. deleteFrame requires another frame. reorderFrame: frameId,index. setDuration: frameId,durationMs(1-60000). setFrame: frameId,name,hideFromOnionSkin.
- setClip: clip:{name,frameIds:[GUIDs],direction:forward|reverse|pingpong,loop:true|false}. deleteClip: name.
- setPivot: frameId,name,x,y. deletePivot: frameId,name. Attachment points are named pivots.
- setSlice: name,x,y,width,height. deleteSlice: name.
- setSpecification: specification:{artMode:pixel|painted,width,height,enforcePalette,binaryAlpha,palette:[hex],identityReferences:[assetId],motionRequirements}. convert:true explicitly quantizes existing pixels; imports never silently convert.
- replaceCel: frameId,layerId,bitmapHash for a prepared native bitmap matching frame dimensions; selection applies. AI candidate application additionally checks its captured target revision.

Locked layers reject raster operations. Transparent canvas clips drawing. Cropping and translation can intentionally discard pixels; undo retains the earlier revision.

setProvenance accepts key (1–200 characters) and value (up to 100,000 characters). Document and batch raster budgets are 64 megapixels; each canvas is at most 16 megapixels, with sides at most 8192. Stroke work is also bounded. Split excessive operations into coherent revision-checked batches.
