const sourceStates = new WeakMap();
const frameStates = new WeakMap();
const editStates = new WeakMap();
const imageCache = new Map();

export async function loadSourceCanvas(canvas, dotNetRef, imageUrl, regions, selectedIds, tool) {
    if (!isCanvas(canvas)) return;
    const image = await loadImage(imageUrl);
    let state = sourceStates.get(canvas);
    if (!state) {
        state = {
            dotNetRef,
            image,
            imageUrl,
            regions: [],
            selectedIds: new Set(),
            tool: "select",
            view: null,
            drag: null,
            polygonDraft: [],
            hover: null,
        };
        sourceStates.set(canvas, state);
        attachSource(canvas, state);
    }

    state.dotNetRef = dotNetRef;
    state.image = image;
    if (state.imageUrl !== imageUrl) {
        state.imageUrl = imageUrl;
        state.view = null;
    }
    state.regions = normalizeRegions(regions);
    state.selectedIds = new Set((selectedIds || []).map(String));
    state.tool = tool || "select";
    renderSource(canvas, state);
}

export async function loadFrameCanvas(canvas, dotNetRef, imageUrl, frame, previousUrl, nextUrl, tool, showOnion) {
    if (!isCanvas(canvas)) return;
    const image = await loadImage(imageUrl);
    const previous = previousUrl ? await loadImage(previousUrl) : null;
    const next = nextUrl ? await loadImage(nextUrl) : null;
    let state = frameStates.get(canvas);
    if (!state) {
        state = {
            dotNetRef,
            image,
            imageUrl,
            previous,
            next,
            frame: null,
            tool: "content",
            showOnion: true,
            view: null,
            drag: null,
        };
        frameStates.set(canvas, state);
        attachFrame(canvas, state);
    }

    state.dotNetRef = dotNetRef;
    state.image = image;
    state.previous = previous;
    state.next = next;
    state.frame = normalizeFrame(frame);
    state.tool = tool || "content";
    state.showOnion = Boolean(showOnion);
    if (state.imageUrl !== imageUrl) {
        state.imageUrl = imageUrl;
        state.view = null;
    }
    renderFrame(canvas, state);
}

export async function loadEditCanvas(canvas, imageUrl, maskUrl, tool, brushSize, editSourceWidth, editSourceHeight, cropX, cropY, cropWidth, cropHeight) {
    if (!isCanvas(canvas)) return;
    const image = await loadImage(imageUrl);
    const imageW = imageWidth(image);
    const imageH = imageHeight(image);
    const sourceW = Math.max(1, number(editSourceWidth, 0) || imageW);
    const sourceH = Math.max(1, number(editSourceHeight, 0) || imageH);
    const target = {
        x: Math.max(0, number(cropX, 0)),
        y: Math.max(0, number(cropY, 0)),
        width: Math.max(1, number(cropWidth, 0) || imageW),
        height: Math.max(1, number(cropHeight, 0) || imageH),
    };

    const source = document.createElement("canvas");
    source.width = sourceW;
    source.height = sourceH;
    const sourceCtx = source.getContext("2d");
    sourceCtx.imageSmoothingEnabled = false;
    sourceCtx.fillStyle = "#ff00ff";
    sourceCtx.fillRect(0, 0, sourceW, sourceH);
    sourceCtx.drawImage(image, target.x, target.y, target.width, target.height);

    const paint = document.createElement("canvas");
    paint.width = sourceW;
    paint.height = sourceH;

    let state = editStates.get(canvas);
    if (!state) {
        state = {
            source,
            paint,
            target,
            tool: tool || "mask",
            brushSize: Number(brushSize) || 28,
            view: null,
            drag: null,
        };
        editStates.set(canvas, state);
        attachEdit(canvas, state);
    } else {
        state.source = source;
        state.paint = paint;
        state.target = target;
        state.tool = tool || "mask";
        state.brushSize = Number(brushSize) || 28;
        state.view = null;
        state.drag = null;
    }
    if (maskUrl) {
        await loadMaskIntoEditPaint(state, maskUrl);
    }
    renderEdit(canvas, state);
}

export function setEditTool(canvas, tool) {
    const state = editStates.get(canvas);
    if (state) state.tool = tool || "mask";
}

export function setEditBrushSize(canvas, brushSize) {
    const state = editStates.get(canvas);
    if (state) state.brushSize = Number(brushSize) || state.brushSize;
}

export function clearEditMask(canvas) {
    const state = editStates.get(canvas);
    if (!state) return;
    state.paint.getContext("2d").clearRect(0, 0, state.paint.width, state.paint.height);
    renderEdit(canvas, state);
}

export function hasEditMaskPaint(canvas) {
    const state = editStates.get(canvas);
    return state ? paintHasPixels(state.paint) : false;
}

export function exportEditSourcePng(canvas) {
    const state = editStates.get(canvas);
    if (!state) throw new Error("Edit canvas is not ready.");
    return state.source.toDataURL("image/png");
}

export function exportEditMaskPng(canvas) {
    const state = editStates.get(canvas);
    if (!state) throw new Error("Edit canvas is not ready.");
    const mask = document.createElement("canvas");
    mask.width = state.source.width;
    mask.height = state.source.height;
    const ctx = mask.getContext("2d");
    ctx.fillStyle = "rgba(0,0,0,1)";
    ctx.fillRect(0, 0, mask.width, mask.height);
    ctx.globalCompositeOperation = "destination-out";
    ctx.drawImage(state.paint, 0, 0);
    ctx.globalCompositeOperation = "source-over";
    return mask.toDataURL("image/png");
}

function attachSource(canvas, state) {
    canvas.addEventListener("wheel", event => onSourceWheel(canvas, state, event), { passive: false });
    canvas.addEventListener("pointerdown", event => onSourcePointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onSourcePointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onSourcePointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onSourcePointerUp(canvas, state, event));
    canvas.addEventListener("pointerleave", event => {
        if (state.drag) onSourcePointerUp(canvas, state, event);
    });
}

function isCanvas(value) {
    return value && typeof value.addEventListener === "function" && typeof value.getContext === "function";
}

function attachFrame(canvas, state) {
    canvas.addEventListener("wheel", event => onFrameWheel(canvas, state, event), { passive: false });
    canvas.addEventListener("pointerdown", event => onFramePointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onFramePointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onFramePointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onFramePointerUp(canvas, state, event));
    canvas.addEventListener("keydown", event => onFrameKeyDown(canvas, state, event));
}

function attachEdit(canvas, state) {
    canvas.addEventListener("wheel", event => onEditWheel(canvas, state, event), { passive: false });
    canvas.addEventListener("pointerdown", event => onEditPointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onEditPointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onEditPointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onEditPointerUp(canvas, state, event));
    canvas.addEventListener("pointerleave", event => {
        if (state.drag) onEditPointerUp(canvas, state, event);
    });
}

function onSourceWheel(canvas, state, event) {
    if (!state.image) return;
    event.preventDefault();
    ensureView(canvas, state, imageWidth(state.image), imageHeight(state.image));
    const point = canvasPoint(canvas, event);
    const before = screenToWorld(state.view, point.x, point.y);
    const factor = event.deltaY < 0 ? 1.12 : 0.88;
    state.view.scale = clamp(state.view.scale * factor, 0.05, 64);
    state.view.x = point.x - before.x * state.view.scale;
    state.view.y = point.y - before.y * state.view.scale;
    renderSource(canvas, state);
}

function onSourcePointerDown(canvas, state, event) {
    if (!state.image) return;
    ensureView(canvas, state, imageWidth(state.image), imageHeight(state.image));
    const world = sourcePoint(canvas, state, event);
    if (!world) return;
    event.preventDefault();
    canvas.setPointerCapture?.(event.pointerId);

    if (state.tool === "pan") {
        const point = canvasPoint(canvas, event);
        state.drag = { type: "pan", start: point, view: { ...state.view } };
        return;
    }

    if (state.tool === "draw") {
        const id = crypto.randomUUID();
        const region = {
            id,
            name: `Region ${state.regions.length + 1}`,
            x: world.x,
            y: world.y,
            width: 1,
            height: 1,
            shapePaths: [],
            regionType: "frame",
            order: state.regions.length,
        };
        state.regions.push(region);
        state.selectedIds = new Set([id]);
        state.drag = { type: "draw", id, start: world };
        renderSource(canvas, state);
        return;
    }

    if (state.tool === "polygon") {
        const draft = state.polygonDraft || [];
        if (draft.length >= 3 && distance(world, draft[0]) <= closeDistance(state)) {
            const id = crypto.randomUUID();
            const bounds = boundsFromPoints(draft, imageWidth(state.image), imageHeight(state.image));
            state.regions.push({
                id,
                name: `Region ${state.regions.length + 1}`,
                x: bounds.x,
                y: bounds.y,
                width: bounds.width,
                height: bounds.height,
                shapePaths: [{ points: draft.map(p => ({ x: p.x, y: p.y })) }],
                regionType: "frame",
                order: state.regions.length,
            });
            state.selectedIds = new Set([id]);
            state.polygonDraft = [];
            commitSource(state);
        } else {
            state.polygonDraft = [...draft, world];
        }
        renderSource(canvas, state);
        return;
    }

    const hit = hitRegion(state, world);
    if (!hit) {
        state.selectedIds.clear();
        state.dotNetRef?.invokeMethodAsync("OnSourceRegionSelected", null);
        renderSource(canvas, state);
        return;
    }

    state.selectedIds = new Set([hit.region.id]);
    state.dotNetRef?.invokeMethodAsync("OnSourceRegionSelected", hit.region.id);
    state.drag = {
        type: hit.handle ? "resize" : "move",
        id: hit.region.id,
        handle: hit.handle,
        start: world,
        original: { ...hit.region },
    };
    renderSource(canvas, state);
}

function onSourcePointerMove(canvas, state, event) {
    if (!state.image) return;
    const world = sourcePoint(canvas, state, event);
    if (state.tool === "polygon" && !state.drag) {
        state.hover = world;
        renderSource(canvas, state);
        return;
    }
    if (!state.drag) return;
    event.preventDefault();

    if (state.drag.type === "pan") {
        const point = canvasPoint(canvas, event);
        state.view.x = state.drag.view.x + point.x - state.drag.start.x;
        state.view.y = state.drag.view.y + point.y - state.drag.start.y;
        renderSource(canvas, state);
        return;
    }
    if (!world) return;

    const region = state.regions.find(r => r.id === state.drag.id);
    if (!region) return;
    if (state.drag.type === "draw") {
        Object.assign(region, rectFromPoints(state.drag.start, world, imageWidth(state.image), imageHeight(state.image)));
    } else if (state.drag.type === "move") {
        const dx = world.x - state.drag.start.x;
        const dy = world.y - state.drag.start.y;
        const moved = clampRect(state.drag.original.x + dx, state.drag.original.y + dy, state.drag.original.width, state.drag.original.height, imageWidth(state.image), imageHeight(state.image));
        Object.assign(region, moved);
        if (state.drag.original.shapePaths?.length) {
            region.shapePaths = state.drag.original.shapePaths.map(path => ({
                points: (path.points || []).map(point => ({
                    x: clamp(Math.round(point.x + dx), 0, imageWidth(state.image) - 1),
                    y: clamp(Math.round(point.y + dy), 0, imageHeight(state.image) - 1),
                })),
            }));
        }
    } else if (state.drag.type === "resize") {
        Object.assign(region, resizeRect(state.drag.original, state.drag.handle, world, imageWidth(state.image), imageHeight(state.image)));
        region.shapePaths = [];
    }
    renderSource(canvas, state);
}

function onSourcePointerUp(canvas, state, event) {
    if (!state.drag) return;
    event.preventDefault();
    const type = state.drag.type;
    state.drag = null;
    try { canvas.releasePointerCapture?.(event.pointerId); } catch { }
    renderSource(canvas, state);
    if (type !== "pan") commitSource(state);
}

function onFrameWheel(canvas, state, event) {
    if (!state.frame) return;
    event.preventDefault();
    const workspace = frameWorkspace(state.frame);
    ensureView(canvas, state, workspace.width, workspace.height, workspace.x, workspace.y);
    const point = canvasPoint(canvas, event);
    const before = screenToWorld(state.view, point.x, point.y);
    const factor = event.deltaY < 0 ? 1.12 : 0.88;
    state.view.scale = clamp(state.view.scale * factor, 0.05, 64);
    state.view.x = point.x - before.x * state.view.scale;
    state.view.y = point.y - before.y * state.view.scale;
    renderFrame(canvas, state);
}

function onFramePointerDown(canvas, state, event) {
    if (!state.frame) return;
    canvas.focus?.();
    const workspace = frameWorkspace(state.frame);
    ensureView(canvas, state, workspace.width, workspace.height, workspace.x, workspace.y);
    const world = framePoint(canvas, state, event);
    if (!world) return;
    event.preventDefault();
    canvas.setPointerCapture?.(event.pointerId);

    if (state.tool === "pan") {
        const point = canvasPoint(canvas, event);
        state.drag = { type: "pan", start: point, view: { ...state.view } };
        return;
    }

    const content = contentRect(state.frame);
    if (world.x >= content.x && world.x <= content.x + content.width && world.y >= content.y && world.y <= content.y + content.height) {
        state.drag = {
            type: "content",
            start: world,
            originalX: state.frame.contentOffsetX,
            originalY: state.frame.contentOffsetY,
        };
    }
}

function onFramePointerMove(canvas, state, event) {
    if (!state.drag) return;
    event.preventDefault();
    if (state.drag.type === "pan") {
        const point = canvasPoint(canvas, event);
        state.view.x = state.drag.view.x + point.x - state.drag.start.x;
        state.view.y = state.drag.view.y + point.y - state.drag.start.y;
        renderFrame(canvas, state);
        return;
    }
    const world = framePoint(canvas, state, event);
    if (!world) return;
    if (state.drag.type === "content") {
        state.frame.contentOffsetX = Math.round(state.drag.originalX + world.x - state.drag.start.x);
        state.frame.contentOffsetY = Math.round(state.drag.originalY + world.y - state.drag.start.y);
    }
    renderFrame(canvas, state);
}

function onFramePointerUp(canvas, state, event) {
    if (!state.drag) return;
    event.preventDefault();
    const type = state.drag.type;
    state.drag = null;
    try { canvas.releasePointerCapture?.(event.pointerId); } catch { }
    renderFrame(canvas, state);
    if (type === "content") {
        state.dotNetRef?.invokeMethodAsync("OnFrameContentOffsetChanged", state.frame.contentOffsetX, state.frame.contentOffsetY);
    }
}

function onFrameKeyDown(canvas, state, event) {
    if (!state.frame || state.tool !== "content") return;
    const step = event.shiftKey ? 8 : 1;
    let dx = 0;
    let dy = 0;
    if (event.key === "ArrowLeft") dx = -step;
    else if (event.key === "ArrowRight") dx = step;
    else if (event.key === "ArrowUp") dy = -step;
    else if (event.key === "ArrowDown") dy = step;
    else return;
    event.preventDefault();
    state.frame.contentOffsetX += dx;
    state.frame.contentOffsetY += dy;
    renderFrame(canvas, state);
    state.dotNetRef?.invokeMethodAsync("OnFrameContentNudged", dx, dy);
}

function onEditWheel(canvas, state, event) {
    if (!state.source) return;
    event.preventDefault();
    ensureView(canvas, state, state.source.width, state.source.height);
    const point = canvasPoint(canvas, event);
    const before = screenToWorld(state.view, point.x, point.y);
    const factor = event.deltaY < 0 ? 1.12 : 0.88;
    state.view.scale = clamp(state.view.scale * factor, 0.05, 64);
    state.view.x = point.x - before.x * state.view.scale;
    state.view.y = point.y - before.y * state.view.scale;
    renderEdit(canvas, state);
}

function onEditPointerDown(canvas, state, event) {
    if (!state.source) return;
    canvas.focus?.();
    ensureView(canvas, state, state.source.width, state.source.height);
    event.preventDefault();
    canvas.setPointerCapture?.(event.pointerId);
    if (state.tool === "pan") {
        const point = canvasPoint(canvas, event);
        state.drag = { type: "pan", start: point, view: { ...state.view } };
        return;
    }

    const world = editPoint(canvas, state, event);
    state.drag = { type: "mask" };
    paintEditMask(state, world, state.tool === "erase");
    renderEdit(canvas, state);
}

function onEditPointerMove(canvas, state, event) {
    if (!state.drag) return;
    event.preventDefault();
    if (state.drag.type === "pan") {
        const point = canvasPoint(canvas, event);
        state.view.x = state.drag.view.x + point.x - state.drag.start.x;
        state.view.y = state.drag.view.y + point.y - state.drag.start.y;
        renderEdit(canvas, state);
        return;
    }

    paintEditMask(state, editPoint(canvas, state, event), state.tool === "erase");
    renderEdit(canvas, state);
}

function onEditPointerUp(canvas, state, event) {
    if (!state.drag) return;
    event.preventDefault();
    state.drag = null;
    try { canvas.releasePointerCapture?.(event.pointerId); } catch { }
    renderEdit(canvas, state);
}

function renderSource(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawStageBackground(ctx, canvas.width, canvas.height);
    if (!state.image) return;
    ensureView(canvas, state, imageWidth(state.image), imageHeight(state.image));
    ctx.save();
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(state.view.scale, 0, 0, state.view.scale, state.view.x, state.view.y);
    ctx.drawImage(state.image, 0, 0);
    for (const region of state.regions) drawRegion(ctx, state, region, state.selectedIds.has(region.id));
    drawPolygonDraft(ctx, state);
    ctx.restore();
}

function renderEdit(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawStageBackground(ctx, canvas.width, canvas.height);
    if (!state.source) return;
    ensureView(canvas, state, state.source.width, state.source.height);
    ctx.save();
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(state.view.scale, 0, 0, state.view.scale, state.view.x, state.view.y);
    ctx.drawImage(state.source, 0, 0);
    ctx.drawImage(state.paint, 0, 0);
    ctx.strokeStyle = "#38bdf8";
    ctx.lineWidth = 2 / state.view.scale;
    ctx.strokeRect(state.target.x + 0.5, state.target.y + 0.5, state.target.width - 1, state.target.height - 1);
    ctx.restore();
}

function renderFrame(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawStageBackground(ctx, canvas.width, canvas.height);
    if (!state.image || !state.frame) return;
    const workspace = frameWorkspace(state.frame);
    ensureView(canvas, state, workspace.width, workspace.height, workspace.x, workspace.y);
    ctx.save();
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(state.view.scale, 0, 0, state.view.scale, state.view.x, state.view.y);
    ctx.fillStyle = "#ff00ff";
    ctx.fillRect(workspace.x, workspace.y, workspace.width, workspace.height);
    if (state.showOnion && state.previous) {
        ctx.globalAlpha = 0.28;
        ctx.drawImage(state.previous, 0, 0, state.frame.logicalWidth, state.frame.logicalHeight);
    }
    ctx.globalAlpha = 1;
    const content = contentRect(state.frame);
    ctx.drawImage(state.image, content.x, content.y, content.width, content.height);
    if (state.showOnion && state.next) {
        ctx.globalAlpha = 0.22;
        ctx.drawImage(state.next, 0, 0, state.frame.logicalWidth, state.frame.logicalHeight);
        ctx.globalAlpha = 1;
    }
    drawFrameOverlays(ctx, state);
    ctx.restore();
}

function drawRegion(ctx, state, region, selected) {
    ctx.save();
    if (region.shapePaths?.length) {
        ctx.beginPath();
        for (const path of region.shapePaths) {
            const points = path.points || [];
            if (points.length < 3) continue;
            ctx.moveTo(points[0].x, points[0].y);
            for (let i = 1; i < points.length; i++) ctx.lineTo(points[i].x, points[i].y);
            ctx.closePath();
        }
        ctx.fillStyle = selected ? "rgba(34,197,94,0.20)" : "rgba(34,197,94,0.10)";
        ctx.strokeStyle = selected ? "#f59e0b" : "#22c55e";
        ctx.lineWidth = selected ? 3 / state.view.scale : 2 / state.view.scale;
        ctx.fill("evenodd");
        ctx.stroke();
    } else {
        ctx.fillStyle = selected ? "rgba(245,158,11,0.18)" : "rgba(34,197,94,0.10)";
        ctx.strokeStyle = selected ? "#f59e0b" : "#22c55e";
        ctx.lineWidth = selected ? 3 / state.view.scale : 2 / state.view.scale;
        ctx.fillRect(region.x, region.y, region.width, region.height);
        ctx.strokeRect(region.x, region.y, region.width, region.height);
        if (selected) drawHandles(ctx, state, region);
    }
    ctx.fillStyle = "rgba(15,23,42,0.86)";
    ctx.font = `${Math.max(10, 12 / state.view.scale)}px sans-serif`;
    ctx.fillText(region.name || "Region", region.x + 4 / state.view.scale, region.y + 14 / state.view.scale);
    ctx.restore();
}

function drawHandles(ctx, state, region) {
    const size = 8 / state.view.scale;
    ctx.fillStyle = "#ffffff";
    ctx.strokeStyle = "#f59e0b";
    for (const [x, y] of [[region.x, region.y], [region.x + region.width, region.y], [region.x, region.y + region.height], [region.x + region.width, region.y + region.height]]) {
        ctx.fillRect(x - size / 2, y - size / 2, size, size);
        ctx.strokeRect(x - size / 2, y - size / 2, size, size);
    }
}

function drawPolygonDraft(ctx, state) {
    const points = state.polygonDraft || [];
    if (points.length === 0) return;
    const preview = state.hover ? [...points, state.hover] : points;
    ctx.save();
    ctx.beginPath();
    ctx.moveTo(preview[0].x, preview[0].y);
    for (let i = 1; i < preview.length; i++) ctx.lineTo(preview[i].x, preview[i].y);
    ctx.strokeStyle = "#f59e0b";
    ctx.lineWidth = 2 / state.view.scale;
    ctx.stroke();
    ctx.fillStyle = "#f59e0b";
    for (const point of points) {
        ctx.beginPath();
        ctx.arc(point.x, point.y, 4 / state.view.scale, 0, Math.PI * 2);
        ctx.fill();
    }
    ctx.restore();
}

function drawFrameOverlays(ctx, state) {
    const frame = state.frame;
    ctx.save();
    ctx.strokeStyle = "#38bdf8";
    ctx.lineWidth = 1.5;
    ctx.strokeRect(0.5, 0.5, frame.logicalWidth - 1, frame.logicalHeight - 1);
    const content = contentRect(frame);
    ctx.strokeStyle = "#f59e0b";
    ctx.setLineDash([4, 3]);
    ctx.strokeRect(content.x + 0.5, content.y + 0.5, content.width, content.height);
    ctx.setLineDash([]);
    ctx.fillStyle = "rgba(15,23,42,0.82)";
    ctx.font = "11px sans-serif";
    ctx.fillText(`${frame.name}  offset ${frame.contentOffsetX},${frame.contentOffsetY}`, 6, 14);
    ctx.restore();
}

function paintEditMask(state, point, erase) {
    const ctx = state.paint.getContext("2d");
    ctx.save();
    ctx.globalCompositeOperation = erase ? "destination-out" : "source-over";
    ctx.fillStyle = "rgba(31,111,235,0.46)";
    ctx.beginPath();
    ctx.arc(point.x, point.y, Math.max(1, state.brushSize / 2), 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
}

async function loadMaskIntoEditPaint(state, maskUrl) {
    const image = await loadImage(maskUrl);
    const maskWidth = imageWidth(image);
    const maskHeight = imageHeight(image);
    const mask = document.createElement("canvas");
    mask.width = maskWidth;
    mask.height = maskHeight;
    const maskCtx = mask.getContext("2d");
    maskCtx.drawImage(image, 0, 0, maskWidth, maskHeight);
    const maskPixels = maskCtx.getImageData(0, 0, maskWidth, maskHeight);
    const paintCtx = state.paint.getContext("2d");
    const paintPixels = paintCtx.getImageData(0, 0, state.paint.width, state.paint.height);
    const offsetX = maskWidth === state.paint.width && maskHeight === state.paint.height ? 0 : state.target.x;
    const offsetY = maskWidth === state.paint.width && maskHeight === state.paint.height ? 0 : state.target.y;

    for (let y = 0; y < maskHeight; y++) {
        const destY = offsetY + y;
        if (destY < 0 || destY >= state.paint.height) continue;
        for (let x = 0; x < maskWidth; x++) {
            const destX = offsetX + x;
            if (destX < 0 || destX >= state.paint.width) continue;
            const maskIndex = ((y * maskWidth) + x) * 4;
            const maskAlpha = maskPixels.data[maskIndex + 3];
            if (maskAlpha >= 255) continue;
            const destIndex = ((destY * state.paint.width) + destX) * 4;
            paintPixels.data[destIndex + 0] = 31;
            paintPixels.data[destIndex + 1] = 111;
            paintPixels.data[destIndex + 2] = 235;
            paintPixels.data[destIndex + 3] = 255 - maskAlpha;
        }
    }

    paintCtx.putImageData(paintPixels, 0, 0);
}

function paintHasPixels(paint) {
    const ctx = paint.getContext("2d", { willReadFrequently: true });
    const pixels = ctx.getImageData(0, 0, paint.width, paint.height).data;
    for (let i = 3; i < pixels.length; i += 4) {
        if (pixels[i] > 0) return true;
    }
    return false;
}

function commitSource(state) {
    state.regions.forEach((region, index) => region.order = index);
    state.dotNetRef?.invokeMethodAsync("OnSourceRegionsChanged", {
        Regions: state.regions.map(toDotNetRegion),
        SelectedIds: [...state.selectedIds],
    });
}

function toDotNetRegion(region) {
    return {
        Id: region.id,
        Name: region.name,
        X: Math.round(region.x),
        Y: Math.round(region.y),
        Width: Math.round(region.width),
        Height: Math.round(region.height),
        ShapePaths: (region.shapePaths || []).map(path => ({
            Points: (path.points || []).map(point => ({ X: Math.round(point.x), Y: Math.round(point.y) })),
        })),
        RegionType: region.regionType || "frame",
        Order: region.order || 0,
    };
}

function normalizeRegions(regions) {
    return (regions || []).map((region, index) => ({
        id: String(read(region, "id", "Id") || crypto.randomUUID()),
        name: String(read(region, "name", "Name") || `Region ${index + 1}`),
        x: number(read(region, "x", "X"), 0),
        y: number(read(region, "y", "Y"), 0),
        width: Math.max(1, number(read(region, "width", "Width"), 1)),
        height: Math.max(1, number(read(region, "height", "Height"), 1)),
        shapePaths: normalizeShapePaths(read(region, "shapePaths", "ShapePaths")),
        regionType: String(read(region, "regionType", "RegionType") || "frame"),
        order: number(read(region, "order", "Order"), index),
    }));
}

function normalizeShapePaths(paths) {
    return (paths || []).map(path => ({
        points: (read(path, "points", "Points") || []).map(point => ({
            x: number(read(point, "x", "X"), 0),
            y: number(read(point, "y", "Y"), 0),
        })),
    })).filter(path => path.points.length >= 3);
}

function normalizeFrame(frame) {
    return {
        id: String(read(frame, "id", "Id") || ""),
        index: number(read(frame, "index", "Index"), 0),
        name: String(read(frame, "name", "Name") || "Frame"),
        logicalWidth: Math.max(1, number(read(frame, "logicalWidth", "LogicalWidth"), 1)),
        logicalHeight: Math.max(1, number(read(frame, "logicalHeight", "LogicalHeight"), 1)),
        sourceWidth: Math.max(1, number(read(frame, "sourceWidth", "SourceWidth"), 1)),
        sourceHeight: Math.max(1, number(read(frame, "sourceHeight", "SourceHeight"), 1)),
        workingWidth: Math.max(0, number(read(frame, "workingWidth", "WorkingWidth"), 0)),
        workingHeight: Math.max(0, number(read(frame, "workingHeight", "WorkingHeight"), 0)),
        contentOffsetX: number(read(frame, "contentOffsetX", "ContentOffsetX"), 0),
        contentOffsetY: number(read(frame, "contentOffsetY", "ContentOffsetY"), 0),
        hasMask: Boolean(read(frame, "hasMask", "HasMask")),
    };
}

function hitRegion(state, point) {
    for (let i = state.regions.length - 1; i >= 0; i--) {
        const region = state.regions[i];
        const handle = hitHandle(state, region, point);
        if (handle) return { region, handle };
        if (region.shapePaths?.length) {
            if (pointInShapePaths(region.shapePaths, point.x, point.y)) return { region, handle: null };
        } else if (point.x >= region.x && point.x <= region.x + region.width && point.y >= region.y && point.y <= region.y + region.height) {
            return { region, handle: null };
        }
    }
    return null;
}

function hitHandle(state, region, point) {
    if (region.shapePaths?.length) return null;
    const size = 10 / state.view.scale;
    const handles = [
        ["nw", region.x, region.y],
        ["ne", region.x + region.width, region.y],
        ["sw", region.x, region.y + region.height],
        ["se", region.x + region.width, region.y + region.height],
    ];
    for (const [name, x, y] of handles) {
        if (Math.abs(point.x - x) <= size && Math.abs(point.y - y) <= size) return name;
    }
    return null;
}

function resizeRect(original, handle, point, width, height) {
    let x1 = original.x;
    let y1 = original.y;
    let x2 = original.x + original.width;
    let y2 = original.y + original.height;
    if (handle.includes("n")) y1 = point.y;
    if (handle.includes("s")) y2 = point.y;
    if (handle.includes("w")) x1 = point.x;
    if (handle.includes("e")) x2 = point.x;
    return clampRect(Math.min(x1, x2), Math.min(y1, y2), Math.abs(x2 - x1), Math.abs(y2 - y1), width, height);
}

function rectFromPoints(a, b, width, height) {
    return clampRect(Math.min(a.x, b.x), Math.min(a.y, b.y), Math.abs(b.x - a.x), Math.abs(b.y - a.y), width, height);
}

function clampRect(x, y, width, height, sourceWidth, sourceHeight) {
    const cx = clamp(Math.round(x), 0, Math.max(0, sourceWidth - 1));
    const cy = clamp(Math.round(y), 0, Math.max(0, sourceHeight - 1));
    return {
        x: cx,
        y: cy,
        width: clamp(Math.round(width), 1, Math.max(1, sourceWidth - cx)),
        height: clamp(Math.round(height), 1, Math.max(1, sourceHeight - cy)),
    };
}

function boundsFromPoints(points, width, height) {
    const xs = points.map(p => p.x);
    const ys = points.map(p => p.y);
    return clampRect(Math.min(...xs), Math.min(...ys), Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys), width, height);
}

function contentRect(frame) {
    return {
        x: frame.contentOffsetX,
        y: frame.contentOffsetY,
        width: frame.workingWidth > 0 ? frame.workingWidth : frame.sourceWidth,
        height: frame.workingHeight > 0 ? frame.workingHeight : frame.sourceHeight,
    };
}

function frameWorkspace(frame) {
    const margin = clamp(Math.max(64, Math.round(Math.max(frame.logicalWidth, frame.logicalHeight) * 0.5)), 64, 512);
    return {
        x: -margin,
        y: -margin,
        width: frame.logicalWidth + (margin * 2),
        height: frame.logicalHeight + (margin * 2),
    };
}

function ensureView(canvas, state, width, height, originX = 0, originY = 0) {
    if (state.view) return;
    const ctx = resizeCanvas(canvas);
    const padding = 28 * deviceScale();
    const scale = Math.min((canvas.width - padding * 2) / width, (canvas.height - padding * 2) / height);
    state.view = {
        scale: Math.max(0.05, scale),
        x: (canvas.width - width * scale) / 2 - originX * scale,
        y: (canvas.height - height * scale) / 2 - originY * scale,
    };
    ctx.setTransform(1, 0, 0, 1, 0, 0);
}

function sourcePoint(canvas, state, event) {
    const point = canvasPoint(canvas, event);
    const world = screenToWorld(state.view, point.x, point.y);
    return {
        x: Math.round(clamp(world.x, 0, imageWidth(state.image) - 1)),
        y: Math.round(clamp(world.y, 0, imageHeight(state.image) - 1)),
    };
}

function framePoint(canvas, state, event) {
    const point = canvasPoint(canvas, event);
    const world = screenToWorld(state.view, point.x, point.y);
    const workspace = frameWorkspace(state.frame);
    return {
        x: Math.round(clamp(world.x, workspace.x, workspace.x + workspace.width)),
        y: Math.round(clamp(world.y, workspace.y, workspace.y + workspace.height)),
    };
}

function editPoint(canvas, state, event) {
    const point = canvasPoint(canvas, event);
    const world = screenToWorld(state.view, point.x, point.y);
    return {
        x: Math.round(clamp(world.x, 0, state.source.width - 1)),
        y: Math.round(clamp(world.y, 0, state.source.height - 1)),
    };
}

function canvasPoint(canvas, event) {
    const rect = canvas.getBoundingClientRect();
    const dpr = deviceScale();
    return {
        x: (event.clientX - rect.left) * dpr,
        y: (event.clientY - rect.top) * dpr,
    };
}

function screenToWorld(view, x, y) {
    return {
        x: (x - view.x) / view.scale,
        y: (y - view.y) / view.scale,
    };
}

function resizeCanvas(canvas) {
    const rect = canvas.getBoundingClientRect();
    const dpr = deviceScale();
    const width = Math.max(320, Math.round(rect.width * dpr));
    const height = Math.max(240, Math.round(rect.height * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    return canvas.getContext("2d");
}

function drawStageBackground(ctx, width, height) {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = "#d7dde5";
    ctx.fillRect(0, 0, width, height);
    const size = 16 * deviceScale();
    ctx.fillStyle = "#cbd5df";
    for (let y = 0; y < height; y += size) {
        for (let x = 0; x < width; x += size) {
            if (((x / size) + (y / size)) % 2 < 1) ctx.fillRect(x, y, size, size);
        }
    }
}

function pointInShapePaths(paths, x, y) {
    let inside = false;
    for (const path of paths || []) {
        if (pointInPath(path.points || [], x, y)) inside = !inside;
    }
    return inside;
}

function pointInPath(points, x, y) {
    let inside = false;
    for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
        const a = points[i];
        const b = points[j];
        const intersects = ((a.y > y) !== (b.y > y)) && x < ((b.x - a.x) * (y - a.y)) / ((b.y - a.y) || 0.00001) + a.x;
        if (intersects) inside = !inside;
    }
    return inside;
}

function closeDistance(state) {
    return 12 / Math.max(0.001, state.view?.scale || 1);
}

function distance(a, b) {
    return Math.hypot(a.x - b.x, a.y - b.y);
}

function read(value, ...names) {
    if (!value) return undefined;
    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(value, name)) return value[name];
    }
    return undefined;
}

function number(value, fallback) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? Math.round(parsed) : fallback;
}

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}

function deviceScale() {
    return window.devicePixelRatio || 1;
}

function imageWidth(image) {
    return image?.naturalWidth || image?.width || 1;
}

function imageHeight(image) {
    return image?.naturalHeight || image?.height || 1;
}

async function loadImage(url) {
    if (!url) throw new Error("Image URL is required.");
    if (imageCache.has(url)) return imageCache.get(url);
    const image = new Image();
    image.decoding = "async";
    image.src = url;
    await image.decode();
    imageCache.set(url, image);
    return image;
}
