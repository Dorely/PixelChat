# Restricted JavaScript

sprite_script receives documentId, expectedRevision, script, label, optional taskId and returnPreview. JavaScript runs in a dedicated process. `document` is a copy of the starting document; `revision` is its revision. `sprite.apply(operation)` appends one typed command; `sprite.batch(operations)` appends several. Snapshot reads do not reflect queued commands. Do not mutate the snapshot and expect it to save. Emit commands instead.

No CLR, imports, require, filesystem, network, shell, environment, or host objects are exposed. Bounds: 256K source characters, 4M message characters, 10,000 commands, 250,000 statements, recursion64, 32MB interpreter allocations, 2s interpreter time; parent enforces an8s deadline, GC heap cap and256MB working-set ceiling. Raster batches have a64-megapixel allocation budget. Errors, cancellation and worker failure commit nothing.

```javascript
const f = document.frames[0].id;
const l = document.layers[0].id;
sprite.apply({op:'rectangle',frameId:f,layerId:l,x:12,y:12,width:24,height:24,color:'#6040a0ff',filled:true});
for(let x=14;x<34;x+=4) sprite.apply({op:'line',frameId:f,layerId:l,x,y:14,x2:x,y2:32,color:'#b080e0ff'});
```

Build coherent shapes in a single script. Inspect the resulting PNG, then make a bounded repair. Avoid emitting one command per pixel when lines, shapes, fills, stamping or a few stroke paths suffice.
