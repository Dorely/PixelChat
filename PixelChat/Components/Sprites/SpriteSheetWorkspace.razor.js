const editorStates = new WeakMap();
const animations = new WeakMap();
const frameWorkStates = new WeakMap();

export async function drawSpriteBoxEditor(sourceCanvas, animationCanvas, dotNetRef, imageUrl, payload, selectedIndex, tool) {
    const image = await loadImage(imageUrl);
    const layout = normalizeLayout(payload);
    const state = ensureEditorState(sourceCanvas, dotNetRef);
    state.image = image;
    state.imageUrl = imageUrl;
    state.layout = layout;
    state.selectedIndex = Number.isFinite(Number(selectedIndex)) ? Number(selectedIndex) : -1;
    state.tool = String(tool || "select").toLowerCase();
    const drawToken = {};
    state.drawToken = drawToken;
    if (state.tool !== "outline") {
        state.outlineDraft = [];
        state.hoverPoint = null;
    }
    updateCanvasToolClasses(sourceCanvas, state);
    drawSourceCanvas(sourceCanvas, state);

    const previews = await buildFramePreviewCanvases(image, layout);
    if (state.drawToken === drawToken) {
        startAnimation(animationCanvas, previews.frames, layout);
    }
}

export function disposeSpriteBoxEditor(sourceCanvas, animationCanvas) {
    stopAnimation(animationCanvas);
    editorStates.delete(sourceCanvas);
}

export async function drawFrameWorkCanvas(canvas, dotNetRef, imageUrl, eraseMode) {
    if (!canvas) return;

    const state = ensureFrameWorkState(canvas, dotNetRef);
    state.dotNetRef = dotNetRef;
    const nextImageUrl = String(imageUrl || "");
    const nextEraseMode = normalizeFrameWorkEraseMode(eraseMode);
    if (state.imageUrl !== nextImageUrl || state.eraseMode !== nextEraseMode) {
        resetFrameWorkDraft(state);
    }

    state.imageUrl = nextImageUrl;
    state.eraseMode = nextEraseMode;
    state.image = state.imageUrl ? await loadImage(state.imageUrl) : null;
    drawFrameWork(canvas, state);
}

function normalizeFrameWorkEraseMode(value) {
    const mode = String(value || "none").toLowerCase();
    if (mode === "rect" || mode === "polygon" || mode === "lasso") return mode;
    return "none";
}

function resetFrameWorkDraft(state) {
    state.drag = null;
    state.polygonDraft = [];
    state.hoverPoint = null;
    state.lassoPoints = [];
}

function ensureEditorState(canvas, dotNetRef) {
    let state = editorStates.get(canvas);
    if (state) {
        state.dotNetRef = dotNetRef;
        return state;
    }

    state = {
        dotNetRef,
        image: null,
        imageUrl: "",
        layout: null,
        selectedIndex: -1,
        tool: "select",
        viewport: null,
        drag: null,
        outlineDraft: [],
        hoverPoint: null,
    };
    editorStates.set(canvas, state);

    canvas.addEventListener("pointerdown", event => onPointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onPointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onPointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onPointerUp(canvas, state, event));
    canvas.addEventListener("pointerleave", event => {
        if (state.drag) {
            onPointerUp(canvas, state, event);
        } else if (state.hoverPoint) {
            state.hoverPoint = null;
            drawSourceCanvas(canvas, state);
        }
    });
    return state;
}

function ensureFrameWorkState(canvas, dotNetRef) {
    let state = frameWorkStates.get(canvas);
    if (state) {
        state.dotNetRef = dotNetRef;
        return state;
    }

    state = {
        dotNetRef,
        image: null,
        imageUrl: "",
        eraseMode: "none",
        viewport: null,
        drag: null,
        polygonDraft: [],
        hoverPoint: null,
        lassoPoints: [],
    };
    frameWorkStates.set(canvas, state);

    canvas.addEventListener("pointerdown", event => onFrameWorkPointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onFrameWorkPointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onFrameWorkPointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onFrameWorkPointerUp(canvas, state, event));
    canvas.addEventListener("pointerleave", event => {
        if (state.drag) {
            onFrameWorkPointerUp(canvas, state, event);
        } else if (state.eraseMode === "polygon" && state.hoverPoint) {
            state.hoverPoint = null;
            drawFrameWork(canvas, state);
        }
    });
    return state;
}

function onFrameWorkPointerDown(canvas, state, event) {
    if (state.eraseMode === "none" || !state.image || !state.viewport) return;
    const point = eventToFrameWorkPoint(canvas, state, event);
    if (!point) return;

    event.preventDefault();
    if (state.eraseMode === "rect") {
        canvas.setPointerCapture?.(event.pointerId);
        state.drag = { type: "rect", start: point, current: point };
        drawFrameWork(canvas, state);
        return;
    }

    if (state.eraseMode === "polygon") {
        const draft = state.polygonDraft || [];
        if (draft.length >= 3 && distance(point, draft[0]) <= frameWorkCloseDistance(state)) {
            invokeFrameWorkPolygonErase(state, draft);
            resetFrameWorkDraft(state);
        } else {
            state.polygonDraft = [...draft, point];
            state.hoverPoint = null;
        }

        drawFrameWork(canvas, state);
        return;
    }

    if (state.eraseMode === "lasso") {
        canvas.setPointerCapture?.(event.pointerId);
        state.drag = { type: "lasso" };
        state.lassoPoints = [point];
        drawFrameWork(canvas, state);
    }
}

function onFrameWorkPointerMove(canvas, state, event) {
    if (state.eraseMode === "none" || !state.image || !state.viewport) return;
    const point = eventToFrameWorkPoint(canvas, state, event);
    if (state.eraseMode === "polygon" && !state.drag) {
        if (!samePoint(state.hoverPoint, point)) {
            state.hoverPoint = point;
            drawFrameWork(canvas, state);
        }
        return;
    }

    if (!state.drag || !point) return;

    event.preventDefault();
    if (state.drag.type === "rect") {
        state.drag.current = point;
    } else if (state.drag.type === "lasso") {
        const points = state.lassoPoints || [];
        const previous = points[points.length - 1];
        if (!previous || distance(previous, point) >= 1.5) {
            points.push(point);
            state.lassoPoints = points;
        }
    }

    drawFrameWork(canvas, state);
}

function onFrameWorkPointerUp(canvas, state, event) {
    if (!state.drag) return;

    event.preventDefault();
    const drag = state.drag;
    state.drag = null;
    if (drag.type === "rect") {
        const rect = rectFromPoints(drag.start, drag.current);
        drawFrameWork(canvas, state);
        if (rect.w < 1 || rect.h < 1) return;

        state.dotNetRef?.invokeMethodAsync("OnFrameWorkEraseRect", {
            X: rect.x,
            Y: rect.y,
            Width: rect.w,
            Height: rect.h,
        });
        return;
    }

    if (drag.type === "lasso") {
        const polygon = simplifyLassoPoints(state.lassoPoints || []);
        state.lassoPoints = [];
        drawFrameWork(canvas, state);
        if (polygon.length >= 3) {
            invokeFrameWorkPolygonErase(state, polygon);
        }
    }
}

function drawFrameWork(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawChecker(ctx, canvas.width, canvas.height);
    canvas.classList.toggle("is-erase-mode", state.eraseMode !== "none");
    canvas.classList.toggle("is-polygon-mode", state.eraseMode === "polygon");
    canvas.classList.toggle("is-lasso-mode", state.eraseMode === "lasso");
    canvas.classList.toggle("is-dragging-erase", Boolean(state.drag));
    if (!state.image) {
        state.viewport = null;
        return;
    }

    const width = imageWidth(state.image);
    const height = imageHeight(state.image);
    const viewport = fitRect(canvas.width, canvas.height, width, height, 16 * deviceScale());
    state.viewport = viewport;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(state.image, viewport.x, viewport.y, viewport.w, viewport.h);

    const scaleX = viewport.w / width;
    const scaleY = viewport.h / height;
    if (state.drag?.type === "rect") {
        drawFrameWorkRectDraft(ctx, state.drag.start, state.drag.current, viewport, scaleX, scaleY);
    }

    if (state.eraseMode === "polygon" && (state.polygonDraft?.length || 0) > 0) {
        drawFrameWorkPolygonDraft(ctx, state, viewport, scaleX, scaleY);
    }

    if (state.drag?.type === "lasso" && (state.lassoPoints?.length || 0) > 0) {
        drawFrameWorkLassoDraft(ctx, state.lassoPoints, viewport, scaleX, scaleY);
    }
}

function drawFrameWorkRectDraft(ctx, start, current, viewport, scaleX, scaleY) {
    const rect = rectFromPoints(start, current);
    ctx.save();
    ctx.fillStyle = "rgba(220, 53, 69, 0.22)";
    ctx.strokeStyle = "rgba(220, 53, 69, 0.92)";
    ctx.lineWidth = Math.max(2, 2 * deviceScale());
    const x = viewport.x + rect.x * scaleX;
    const y = viewport.y + rect.y * scaleY;
    const w = rect.w * scaleX;
    const h = rect.h * scaleY;
    ctx.fillRect(x, y, w, h);
    ctx.strokeRect(x, y, w, h);
    ctx.restore();
}

function drawFrameWorkPolygonDraft(ctx, state, viewport, scaleX, scaleY) {
    const draft = state.polygonDraft || [];
    const hover = state.hoverPoint;
    const canClose = hover && draft.length >= 3 && distance(hover, draft[0]) <= frameWorkCloseDistance(state);
    const preview = hover && !canClose ? [...draft, hover] : draft;
    drawFrameWorkPath(ctx, preview, viewport, scaleX, scaleY, draft.length >= 3, canClose);

    ctx.save();
    for (let index = 0; index < draft.length; index++) {
        const point = draft[index];
        const x = viewport.x + point.x * scaleX;
        const y = viewport.y + point.y * scaleY;
        const radius = (canClose && index === 0 ? 6.5 : 4.5) * deviceScale();
        ctx.beginPath();
        ctx.arc(x, y, radius, 0, Math.PI * 2);
        ctx.fillStyle = canClose && index === 0 ? "#dc3545" : "#ffffff";
        ctx.strokeStyle = "#dc3545";
        ctx.lineWidth = Math.max(1.5, 1.5 * deviceScale());
        ctx.fill();
        ctx.stroke();
    }
    ctx.restore();
}

function drawFrameWorkLassoDraft(ctx, points, viewport, scaleX, scaleY) {
    drawFrameWorkPath(ctx, points, viewport, scaleX, scaleY, points.length >= 3, false);
}

function drawFrameWorkPath(ctx, points, viewport, scaleX, scaleY, fill, highlighted) {
    if (!points || points.length === 0) return;

    ctx.save();
    ctx.beginPath();
    ctx.moveTo(viewport.x + points[0].x * scaleX, viewport.y + points[0].y * scaleY);
    for (let index = 1; index < points.length; index++) {
        ctx.lineTo(viewport.x + points[index].x * scaleX, viewport.y + points[index].y * scaleY);
    }
    if (fill) {
        ctx.closePath();
        ctx.fillStyle = highlighted ? "rgba(220, 53, 69, 0.28)" : "rgba(220, 53, 69, 0.18)";
        ctx.fill();
    }

    ctx.lineWidth = Math.max(2, 2 * deviceScale());
    ctx.strokeStyle = highlighted ? "#a61e2d" : "#dc3545";
    ctx.stroke();
    ctx.restore();
}

function invokeFrameWorkPolygonErase(state, points) {
    if (Math.abs(polygonArea(points || [])) < 1) return;

    const payload = (points || [])
        .filter(Boolean)
        .map(point => ({
            X: Math.round(point.x),
            Y: Math.round(point.y),
        }));
    if (payload.length < 3) return;

    state.dotNetRef?.invokeMethodAsync("OnFrameWorkErasePolygon", payload);
}

function simplifyLassoPoints(points) {
    if (!Array.isArray(points) || points.length < 3) return [];

    const compact = [];
    for (const point of points) {
        const previous = compact[compact.length - 1];
        if (!previous || distance(previous, point) >= 2) {
            compact.push(point);
        }
    }

    if (compact.length > 3 && distance(compact[0], compact[compact.length - 1]) < 2) {
        compact.pop();
    }
    if (Math.abs(polygonArea(compact)) < 4) return [];

    const maxPoints = 96;
    if (compact.length <= maxPoints) return compact;

    const step = Math.ceil(compact.length / maxPoints);
    const sampled = [];
    for (let index = 0; index < compact.length; index += step) {
        sampled.push(compact[index]);
    }
    if (!samePoint(sampled[sampled.length - 1], compact[compact.length - 1])) {
        sampled.push(compact[compact.length - 1]);
    }
    return sampled;
}

function polygonArea(points) {
    if (!Array.isArray(points) || points.length < 3) return 0;

    let area = 0;
    for (let index = 0; index < points.length; index++) {
        const current = points[index];
        const next = points[(index + 1) % points.length];
        area += current.x * next.y - next.x * current.y;
    }
    return area / 2;
}

function frameWorkCloseDistance(state) {
    if (!state.viewport || !state.image) return 8;
    return 12 / Math.max(0.001, state.viewport.w / imageWidth(state.image));
}

function eventToFrameWorkPoint(canvas, state, event) {
    if (!state.viewport || !state.image) return null;
    const rect = canvas.getBoundingClientRect();
    const dpr = deviceScale();
    const x = (event.clientX - rect.left) * dpr;
    const y = (event.clientY - rect.top) * dpr;
    const viewport = state.viewport;
    if (!state.drag) {
        const slack = 24 * dpr;
        if (x < viewport.x - slack || x > viewport.x + viewport.w + slack || y < viewport.y - slack || y > viewport.y + viewport.h + slack) {
            return null;
        }
    }

    return {
        x: Math.round((x - viewport.x) / viewport.w * imageWidth(state.image)),
        y: Math.round((y - viewport.y) / viewport.h * imageHeight(state.image)),
    };
}

function updateCanvasToolClasses(canvas, state) {
    if (!canvas) return;

    canvas.classList.toggle("is-select-tool", state.tool === "select");
    canvas.classList.toggle("is-draw-tool", state.tool === "draw");
    canvas.classList.toggle("is-outline-tool", state.tool === "outline");
    canvas.classList.toggle("is-dragging-frame", Boolean(state.drag));
}

function onPointerDown(canvas, state, event) {
    if (!state.image || !state.layout || !state.viewport) return;
    const point = eventToImagePoint(canvas, state, event);
    if (!point) return;
    event.preventDefault();
    canvas.setPointerCapture?.(event.pointerId);

    if (state.tool === "outline") {
        const draft = state.outlineDraft || [];
        if (draft.length >= 3 && distance(point, draft[0]) <= outlineCloseDistance(state)) {
            const shapePaths = [{ points: draft.map(p => ({ x: p.x, y: p.y })) }];
            const sourceRect = rectFromShapePaths(shapePaths, imageWidth(state.image), imageHeight(state.image));
            const index = state.layout.frames.length;
            const label = `Frame ${index + 1}`;
            state.layout.frames.push({ index, label, sourceRect, shapePaths });
            state.layout.frameCount = state.layout.frames.length;
            state.selectedIndex = index;
            state.outlineDraft = [];
            state.hoverPoint = null;
            reindexLayoutFrames(state.layout);
            drawSourceCanvas(canvas, state);
            state.dotNetRef?.invokeMethodAsync("OnSpriteBoxesChanged", {
                SelectedIndex: state.selectedIndex,
                Frames: state.layout.frames.map(frame => ({
                    Index: frame.index,
                    Label: frame.label,
                    SourceRect: toDotNetRect(frame.sourceRect),
                    ShapePaths: toDotNetShapePaths(frame.shapePaths),
                })),
            });
            return;
        }

        state.outlineDraft = [...draft, point];
        state.hoverPoint = null;
        drawSourceCanvas(canvas, state);
        return;
    }

    if (state.tool === "draw") {
        const index = state.layout.frames.length;
        const label = `Frame ${index + 1}`;
        const frame = {
            index,
            label,
            sourceRect: { x: point.x, y: point.y, w: 1, h: 1 },
            shapePaths: [],
        };
        state.layout.frames.push(frame);
        state.layout.frameCount = state.layout.frames.length;
        state.selectedIndex = index;
        state.drag = { type: "draw", index, start: point };
        updateCanvasToolClasses(canvas, state);
        drawSourceCanvas(canvas, state);
        return;
    }

    const hit = hitTest(state, point);
    if (!hit) {
        state.selectedIndex = -1;
        state.dotNetRef?.invokeMethodAsync("OnSpriteBoxSelected", -1);
        drawSourceCanvas(canvas, state);
        return;
    }

    state.selectedIndex = hit.index;
    state.dotNetRef?.invokeMethodAsync("OnSpriteBoxSelected", hit.index);
    const frame = state.layout.frames[hit.index];
    state.drag = {
        type: hit.vertex ? "vertex" : (hit.handle ? "resize" : "move"),
        index: hit.index,
        handle: hit.handle,
        vertex: hit.vertex,
        start: point,
        original: { ...frame.sourceRect },
        originalShapePaths: cloneShapePaths(frame.shapePaths),
    };
    updateCanvasToolClasses(canvas, state);
    drawSourceCanvas(canvas, state);
}

function onPointerMove(canvas, state, event) {
    if (!state.layout || !state.viewport) return;
    const point = eventToImagePoint(canvas, state, event);
    if (!state.drag) {
        if (state.tool === "outline") {
            if (!samePoint(state.hoverPoint, point)) {
                state.hoverPoint = point;
                drawSourceCanvas(canvas, state);
            }
        } else if (state.hoverPoint) {
            state.hoverPoint = null;
            drawSourceCanvas(canvas, state);
        }
        return;
    }

    if (!point) return;
    event.preventDefault();

    const frame = state.layout.frames[state.drag.index];
    if (!frame) return;

    if (state.drag.type === "draw") {
        frame.sourceRect = rectFromPoints(state.drag.start, point);
    } else if (state.drag.type === "move") {
        const dx = point.x - state.drag.start.x;
        const dy = point.y - state.drag.start.y;
        if (frame.shapePaths?.length) {
            frame.shapePaths = offsetShapePaths(state.drag.originalShapePaths, dx, dy, imageWidth(state.image), imageHeight(state.image));
            frame.sourceRect = rectFromShapePaths(frame.shapePaths, imageWidth(state.image), imageHeight(state.image));
        } else {
            frame.sourceRect = clampRect({
                x: Math.round(state.drag.original.x + dx),
                y: Math.round(state.drag.original.y + dy),
                w: state.drag.original.w,
                h: state.drag.original.h,
            }, imageWidth(state.image), imageHeight(state.image));
        }
    } else if (state.drag.type === "resize") {
        frame.sourceRect = resizeRect(state.drag.original, state.drag.handle, point, imageWidth(state.image), imageHeight(state.image));
    } else if (state.drag.type === "vertex") {
        frame.shapePaths = moveShapeVertex(
            state.drag.originalShapePaths,
            state.drag.vertex,
            point,
            imageWidth(state.image),
            imageHeight(state.image));
        frame.sourceRect = rectFromShapePaths(frame.shapePaths, imageWidth(state.image), imageHeight(state.image));
    }

    drawSourceCanvas(canvas, state);
}

function onPointerUp(canvas, state, event) {
    if (!state.drag || !state.layout) return;
    event.preventDefault();

    const frame = state.layout.frames[state.drag.index];
    if (frame) {
        if (frame.shapePaths?.length) {
            frame.shapePaths = normalizeShapePaths(frame.shapePaths, imageWidth(state.image), imageHeight(state.image));
            frame.sourceRect = rectFromShapePaths(frame.shapePaths, imageWidth(state.image), imageHeight(state.image));
        } else {
            frame.sourceRect = clampRect(frame.sourceRect, imageWidth(state.image), imageHeight(state.image));
        }
        if (frame.sourceRect.w < 3 || frame.sourceRect.h < 3) {
            state.layout.frames.splice(state.drag.index, 1);
            state.selectedIndex = -1;
        }
    }

    reindexLayoutFrames(state.layout);
    state.layout.frameCount = state.layout.frames.length;
    const selectedIndex = state.selectedIndex >= 0 && state.selectedIndex < state.layout.frames.length
        ? state.selectedIndex
        : (state.layout.frames.length > 0 ? 0 : -1);
    state.selectedIndex = selectedIndex;
    state.drag = null;
    updateCanvasToolClasses(canvas, state);
    drawSourceCanvas(canvas, state);
    state.dotNetRef?.invokeMethodAsync("OnSpriteBoxesChanged", {
        SelectedIndex: selectedIndex,
        Frames: state.layout.frames.map(frame => ({
            Index: frame.index,
            Label: frame.label,
            SourceRect: toDotNetRect(frame.sourceRect),
            ShapePaths: toDotNetShapePaths(frame.shapePaths),
        })),
    });
}

function drawSourceCanvas(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawChecker(ctx, canvas.width, canvas.height);
    if (!state.image || !state.layout) return;

    const width = imageWidth(state.image);
    const height = imageHeight(state.image);
    const viewport = fitRect(canvas.width, canvas.height, width, height, 18 * deviceScale());
    state.viewport = viewport;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(state.image, viewport.x, viewport.y, viewport.w, viewport.h);

    const scaleX = viewport.w / width;
    const scaleY = viewport.h / height;
    ctx.save();
    ctx.lineWidth = Math.max(2, 2 * deviceScale());
    ctx.font = `${Math.max(12, 12 * deviceScale())}px system-ui, sans-serif`;
    ctx.textBaseline = "top";
    for (const frame of state.layout.frames) {
        const rect = frame.sourceRect;
        const x = viewport.x + rect.x * scaleX;
        const y = viewport.y + rect.y * scaleY;
        const w = rect.w * scaleX;
        const h = rect.h * scaleY;
        const selected = frame.index === state.selectedIndex;
        ctx.strokeStyle = selected ? "#f59f00" : "#1f6feb";
        ctx.fillStyle = selected ? "rgba(245,159,0,0.16)" : "rgba(31,111,235,0.12)";
        if (frame.shapePaths?.length) {
            drawShapeOverlay(ctx, frame.shapePaths, viewport, scaleX, scaleY, selected);
            ctx.setLineDash([5 * deviceScale(), 4 * deviceScale()]);
            ctx.strokeRect(x, y, w, h);
            ctx.setLineDash([]);
        } else {
            ctx.fillRect(x, y, w, h);
            ctx.strokeRect(x, y, w, h);
        }

        const label = frame.label || `Frame ${frame.index + 1}`;
        const labelWidth = Math.min(ctx.measureText(label).width + 10 * deviceScale(), Math.max(28 * deviceScale(), w));
        ctx.fillStyle = selected ? "rgba(122,83,0,0.92)" : "rgba(15,77,184,0.9)";
        ctx.fillRect(x, y, labelWidth, 20 * deviceScale());
        ctx.fillStyle = "#ffffff";
        ctx.fillText(label, x + 5 * deviceScale(), y + 3 * deviceScale(), labelWidth - 8 * deviceScale());

        if (selected) {
            if (frame.shapePaths?.length) {
                drawVertexHandles(ctx, frame.shapePaths, viewport, scaleX, scaleY);
            } else {
                drawHandles(ctx, x, y, w, h);
            }
        }
    }
    drawOutlineDraft(ctx, state, viewport, scaleX, scaleY);
    ctx.restore();
}

function drawHandles(ctx, x, y, w, h) {
    const size = 8 * deviceScale();
    const half = size / 2;
    ctx.fillStyle = "#ffffff";
    ctx.strokeStyle = "#f59f00";
    for (const point of [
        [x, y],
        [x + w, y],
        [x, y + h],
        [x + w, y + h],
    ]) {
        ctx.fillRect(point[0] - half, point[1] - half, size, size);
        ctx.strokeRect(point[0] - half, point[1] - half, size, size);
    }
}

function drawShapeOverlay(ctx, shapePaths, viewport, scaleX, scaleY, selected) {
    ctx.save();
    ctx.lineWidth = Math.max(selected ? 3 : 2, (selected ? 3 : 2) * deviceScale());
    ctx.beginPath();
    for (const path of shapePaths) {
        const points = path.points || [];
        if (points.length < 3) continue;
        ctx.moveTo(viewport.x + points[0].x * scaleX, viewport.y + points[0].y * scaleY);
        for (let index = 1; index < points.length; index++) {
            ctx.lineTo(viewport.x + points[index].x * scaleX, viewport.y + points[index].y * scaleY);
        }
        ctx.closePath();
    }

    ctx.fillStyle = selected ? "rgba(22,163,74,0.18)" : "rgba(22,163,74,0.11)";
    ctx.strokeStyle = "#16a34a";
    ctx.fill("evenodd");
    ctx.stroke();
    ctx.restore();
}

function drawVertexHandles(ctx, shapePaths, viewport, scaleX, scaleY) {
    const points = shapePaths.flatMap(path => path.points || []);

    const radius = 4.5 * deviceScale();
    ctx.lineWidth = Math.max(1.5, 1.5 * deviceScale());
    for (const point of points) {
        const x = viewport.x + point.x * scaleX;
        const y = viewport.y + point.y * scaleY;
        ctx.beginPath();
        ctx.arc(x, y, radius, 0, Math.PI * 2);
        ctx.fillStyle = "#ffffff";
        ctx.strokeStyle = "#f59f00";
        ctx.fill();
        ctx.stroke();
    }
}

function drawOutlineDraft(ctx, state, viewport, scaleX, scaleY) {
    const draft = state.outlineDraft || [];
    if (state.tool !== "outline" || draft.length === 0) return;
    const hover = state.hoverPoint;
    const canClose = hover && draft.length >= 3 && distance(hover, draft[0]) <= outlineCloseDistance(state);

    ctx.save();
    ctx.lineWidth = Math.max(2, 2 * deviceScale());
    ctx.beginPath();
    ctx.moveTo(viewport.x + draft[0].x * scaleX, viewport.y + draft[0].y * scaleY);
    for (let index = 1; index < draft.length; index++) {
        ctx.lineTo(viewport.x + draft[index].x * scaleX, viewport.y + draft[index].y * scaleY);
    }
    if (hover) {
        const target = canClose ? draft[0] : hover;
        ctx.lineTo(viewport.x + target.x * scaleX, viewport.y + target.y * scaleY);
    }
    if (canClose) {
        ctx.closePath();
        ctx.fillStyle = "rgba(245,159,0,0.14)";
        ctx.fill();
    }
    ctx.strokeStyle = canClose ? "#f59f00" : "#16a34a";
    ctx.stroke();

    for (let index = 0; index < draft.length; index++) {
        const point = draft[index];
        const x = viewport.x + point.x * scaleX;
        const y = viewport.y + point.y * scaleY;
        const radius = (canClose && index === 0 ? 6.5 : 4.5) * deviceScale();
        ctx.beginPath();
        ctx.arc(x, y, radius, 0, Math.PI * 2);
        ctx.fillStyle = canClose && index === 0 ? "#f59f00" : "#16a34a";
        ctx.strokeStyle = "#ffffff";
        ctx.fill();
        ctx.stroke();
    }

    if (hover && !canClose) {
        const x = viewport.x + hover.x * scaleX;
        const y = viewport.y + hover.y * scaleY;
        ctx.beginPath();
        ctx.arc(x, y, 3.5 * deviceScale(), 0, Math.PI * 2);
        ctx.fillStyle = "rgba(22,163,74,0.42)";
        ctx.fill();
    }
    ctx.restore();
}

async function buildFramePreviewCanvases(image, layout) {
    const frames = [];
    const frameCount = Math.min(layout.frameCount, layout.frames.length, layout.rows * layout.columns);
    for (let index = 0; index < frameCount; index++) {
        const frame = layout.frames[index];
        const sourceRect = clampRect(frame.sourceRect, imageWidth(image), imageHeight(image));
        const shapePaths = normalizeShapePaths(frame.shapePaths, imageWidth(image), imageHeight(image));
        const previewCanvas = await loadPreviewCanvas(frame.previewDataUrl, image, sourceRect);
        frames.push({
            index,
            label: frame.label || `Frame ${index + 1}`,
            sourceRect,
            shapePaths,
            previewCanvas,
        });
    }

    return { frames };
}

async function loadPreviewCanvas(previewDataUrl, image, sourceRect) {
    if (previewDataUrl) {
        try {
            const preview = await loadImage(previewDataUrl);
            const canvas = document.createElement("canvas");
            canvas.width = imageWidth(preview);
            canvas.height = imageHeight(preview);
            const ctx = canvas.getContext("2d");
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(preview, 0, 0);
            return canvas;
        } catch {
        }
    }

    return renderPlainCrop(image, sourceRect);
}

function renderPlainCrop(image, sourceRect) {
    const canvas = document.createElement("canvas");
    canvas.width = sourceRect.w;
    canvas.height = sourceRect.h;
    const ctx = canvas.getContext("2d");
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(
        image,
        sourceRect.x,
        sourceRect.y,
        sourceRect.w,
        sourceRect.h,
        0,
        0,
        sourceRect.w,
        sourceRect.h);
    return canvas;
}

function startAnimation(canvas, frames, layout) {
    stopAnimation(canvas);
    if (!canvas || frames.length === 0) {
        const ctx = resizeCanvas(canvas);
        drawChecker(ctx, canvas.width, canvas.height);
        return;
    }

    const state = {
        frames,
        layout,
        index: 0,
        timer: null,
    };
    const interval = Math.max(16, Math.round(1000 / Math.max(1, layout.fps)));
    state.timer = window.setInterval(() => {
        drawAnimationFrame(canvas, state);
        if (state.index < frames.length - 1) {
            state.index++;
        } else if (layout.loop) {
            state.index = 0;
        }
    }, interval);
    animations.set(canvas, state);
    drawAnimationFrame(canvas, state);
}

function stopAnimation(canvas) {
    const state = animations.get(canvas);
    if (state?.timer) window.clearInterval(state.timer);
    animations.delete(canvas);
}

function drawAnimationFrame(canvas, state) {
    const ctx = resizeCanvas(canvas);
    drawChecker(ctx, canvas.width, canvas.height);
    const frame = state.frames[state.index] || state.frames[0];
    const viewport = fitRect(canvas.width, canvas.height, frame.previewCanvas.width, frame.previewCanvas.height, 12 * deviceScale());
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(
        frame.previewCanvas,
        0,
        0,
        frame.previewCanvas.width,
        frame.previewCanvas.height,
        viewport.x,
        viewport.y,
        viewport.w,
        viewport.h);
}

function hitTest(state, point) {
    for (let index = state.layout.frames.length - 1; index >= 0; index--) {
        const frame = state.layout.frames[index];
        const vertex = hitShapeVertex(frame.shapePaths, point, state);
        if (vertex) return { index, vertex };
        const handle = frame.shapePaths?.length ? null : hitHandle(frame.sourceRect, point, state);
        if (handle) return { index, handle };
        if (frame.shapePaths?.length && pointInShapePaths(frame.shapePaths, point.x, point.y)) {
            return { index, handle: null };
        }

        const rect = frame.sourceRect;
        if (point.x >= rect.x && point.x <= rect.x + rect.w && point.y >= rect.y && point.y <= rect.y + rect.h) {
            return { index, handle: null };
        }
    }

    return null;
}

function hitHandle(rect, point, state) {
    if (!state.viewport || !state.image) return null;
    const handleSize = 10 / Math.max(0.001, state.viewport.w / imageWidth(state.image));
    const handles = [
        ["nw", rect.x, rect.y],
        ["ne", rect.x + rect.w, rect.y],
        ["sw", rect.x, rect.y + rect.h],
        ["se", rect.x + rect.w, rect.y + rect.h],
    ];
    for (const [name, x, y] of handles) {
        if (Math.abs(point.x - x) <= handleSize && Math.abs(point.y - y) <= handleSize) return name;
    }
    return null;
}

function resizeRect(original, handle, point, width, height) {
    let x1 = original.x;
    let y1 = original.y;
    let x2 = original.x + original.w;
    let y2 = original.y + original.h;
    if (handle.includes("n")) y1 = point.y;
    if (handle.includes("s")) y2 = point.y;
    if (handle.includes("w")) x1 = point.x;
    if (handle.includes("e")) x2 = point.x;
    return clampRect({
        x: Math.min(x1, x2),
        y: Math.min(y1, y2),
        w: Math.abs(x2 - x1),
        h: Math.abs(y2 - y1),
    }, width, height);
}

function eventToImagePoint(canvas, state, event) {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / Math.max(1, rect.width);
    const scaleY = canvas.height / Math.max(1, rect.height);
    const x = (event.clientX - rect.left) * scaleX;
    const y = (event.clientY - rect.top) * scaleY;
    const viewport = state.viewport;
    if (!viewport) return null;
    if (!state.drag) {
        const slack = 24 * deviceScale();
        if (x < viewport.x - slack || x > viewport.x + viewport.w + slack || y < viewport.y - slack || y > viewport.y + viewport.h + slack) {
            return null;
        }
    }

    return {
        x: Math.round((x - viewport.x) / (viewport.w / imageWidth(state.image))),
        y: Math.round((y - viewport.y) / (viewport.h / imageHeight(state.image))),
    };
}

function rectFromPoints(a, b) {
    return {
        x: Math.min(a.x, b.x),
        y: Math.min(a.y, b.y),
        w: Math.max(1, Math.abs(b.x - a.x)),
        h: Math.max(1, Math.abs(b.y - a.y)),
    };
}

function rectFromShapePaths(shapePaths, width, height) {
    const points = (shapePaths || []).flatMap(path => path.points || []);
    if (points.length === 0) return { x: 0, y: 0, w: 1, h: 1 };

    const minX = Math.max(0, Math.min(...points.map(point => point.x)));
    const minY = Math.max(0, Math.min(...points.map(point => point.y)));
    const maxX = Math.min(width, Math.max(...points.map(point => point.x)));
    const maxY = Math.min(height, Math.max(...points.map(point => point.y)));
    return clampRect({
        x: minX,
        y: minY,
        w: Math.max(1, maxX - minX),
        h: Math.max(1, maxY - minY),
    }, width, height);
}

function normalizeShapePaths(value, width, height) {
    const paths = Array.isArray(value) ? value : [];
    return paths
        .map(path => {
            const points = read(path, "Points", "points") || [];
            return {
                points: (Array.isArray(points) ? points : [])
                    .map(point => ({
                        x: clampInt(read(point, "X", "x"), 0, width, 0),
                        y: clampInt(read(point, "Y", "y"), 0, height, 0),
                    })),
            };
        })
        .filter(path => path.points.length >= 3);
}

function cloneShapePaths(shapePaths) {
    return (shapePaths || []).map(path => ({
        points: (path.points || []).map(point => ({ x: point.x, y: point.y })),
    }));
}

function offsetShapePaths(shapePaths, dx, dy, width, height) {
    return normalizeShapePaths((shapePaths || []).map(path => ({
        points: (path.points || []).map(point => ({
            x: point.x + dx,
            y: point.y + dy,
        })),
    })), width, height);
}

function moveShapeVertex(shapePaths, vertex, point, width, height) {
    const moved = cloneShapePaths(shapePaths);
    const path = moved[vertex.pathIndex];
    if (!path?.points?.[vertex.pointIndex]) return moved;

    path.points[vertex.pointIndex] = {
        x: clampInt(point.x, 0, width, 0),
        y: clampInt(point.y, 0, height, 0),
    };
    return normalizeShapePaths(moved, width, height);
}

function hitShapeVertex(shapePaths, point, state) {
    const totalPoints = (shapePaths || []).reduce((sum, path) => sum + (path.points?.length || 0), 0);
    if (totalPoints === 0 || !state.viewport || !state.image) return null;

    const handleSize = 10 / Math.max(0.001, state.viewport.w / imageWidth(state.image));
    for (let pathIndex = 0; pathIndex < shapePaths.length; pathIndex++) {
        const points = shapePaths[pathIndex].points || [];
        for (let pointIndex = 0; pointIndex < points.length; pointIndex++) {
            const candidate = points[pointIndex];
            if (Math.abs(point.x - candidate.x) <= handleSize && Math.abs(point.y - candidate.y) <= handleSize) {
                return { pathIndex, pointIndex };
            }
        }
    }

    return null;
}

function pointInShapePaths(shapePaths, x, y) {
    let inside = false;
    for (const path of shapePaths || []) {
        if (pointInPath(path.points || [], x, y)) inside = !inside;
    }
    return inside;
}

function pointInPath(points, x, y) {
    if (points.length < 3) return false;

    let inside = false;
    let previous = points.length - 1;
    for (let current = 0; current < points.length; current++) {
        const a = points[current];
        const b = points[previous];
        const denominator = b.y - a.y;
        if (Math.abs(denominator) > 0.0001
            && (a.y > y) !== (b.y > y)
            && x < ((b.x - a.x) * (y - a.y) / denominator) + a.x) {
            inside = !inside;
        }
        previous = current;
    }

    return inside;
}

function outlineCloseDistance(state) {
    if (!state.viewport || !state.image) return 8;
    return 12 / Math.max(0.001, state.viewport.w / imageWidth(state.image));
}

function distance(a, b) {
    return Math.hypot(a.x - b.x, a.y - b.y);
}

function samePoint(a, b) {
    if (!a && !b) return true;
    if (!a || !b) return false;
    return a.x === b.x && a.y === b.y;
}

function normalizeLayout(payload) {
    const rows = clampInt(read(payload, "Rows", "rows"), 1, 32, 1);
    let columns = clampInt(read(payload, "Columns", "columns"), 1, 64, 1);
    const frames = (read(payload, "Frames", "frames") || []).map((frame, fallbackIndex) => {
        const rect = read(frame, "SourceRect", "sourceRect") || {};
        const shapePaths = normalizeShapePaths(read(frame, "ShapePaths", "shapePaths"), 32767, 32767);
        return {
            index: clampInt(read(frame, "Index", "index"), 0, 127, fallbackIndex),
            label: String(read(frame, "Label", "label") || `Frame ${fallbackIndex + 1}`),
            previewDataUrl: String(read(frame, "PreviewDataUrl", "previewDataUrl") || ""),
            sourceRect: {
                x: clampInt(read(rect, "X", "x"), -32768, 32767, 0),
                y: clampInt(read(rect, "Y", "y"), -32768, 32767, 0),
                w: clampInt(read(rect, "Width", "width", "W", "w"), 1, 32767, 1),
                h: clampInt(read(rect, "Height", "height", "H", "h"), 1, 32767, 1),
            },
            shapePaths,
        };
    });

    frames.sort((left, right) => left.index - right.index);
    const frameCount = frames.length;
    if (frameCount > 0 && rows * columns < frameCount) {
        columns = Math.max(1, Math.ceil(frameCount / rows));
    }

    reindexFrames(frames);
    return {
        rows,
        columns,
        frameCount,
        cellWidth: clampInt(read(payload, "CellWidth", "cellWidth"), 1, 8192, 128),
        cellHeight: clampInt(read(payload, "CellHeight", "cellHeight"), 1, 8192, 128),
        padding: clampInt(read(payload, "Padding", "padding"), 0, 2048, 8),
        gutter: clampInt(read(payload, "Gutter", "gutter"), 0, 2048, 16),
        fps: clampInt(read(payload, "Fps", "fps"), 1, 60, 8),
        loop: Boolean(read(payload, "Loop", "loop") ?? true),
        horizontalAnchor: normalizeHorizontalAnchor(read(payload, "HorizontalAnchor", "horizontalAnchor")),
        verticalAnchor: normalizeVerticalAnchor(read(payload, "VerticalAnchor", "verticalAnchor")),
        frames,
    };
}

function normalizeHorizontalAnchor(value) {
    const token = String(value || "center").toLowerCase();
    if (token === "left" || token === "right") return token;
    return "center";
}

function normalizeVerticalAnchor(value) {
    const token = String(value || "bottom").toLowerCase();
    if (token === "top") return "top";
    if (token === "middle" || token === "center") return "middle";
    return "bottom";
}

function reindexLayoutFrames(layout) {
    reindexFrames(layout.frames);
}

function reindexFrames(frames) {
    frames.forEach((frame, index) => {
        frame.index = index;
        if (!frame.label) frame.label = `Frame ${index + 1}`;
    });
}

function resizeCanvas(canvas) {
    const rect = canvas.getBoundingClientRect();
    const dpr = deviceScale();
    const width = Math.max(260, Math.round(rect.width * dpr));
    const height = Math.max(220, Math.round(rect.height * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    return canvas.getContext("2d");
}

function drawChecker(ctx, width, height) {
    const size = 16 * deviceScale();
    ctx.fillStyle = "#dce3eb";
    ctx.fillRect(0, 0, width, height);
    ctx.fillStyle = "#cbd5e1";
    for (let y = 0; y < height; y += size) {
        for (let x = 0; x < width; x += size) {
            if (((x / size) + (y / size)) % 2 < 1) ctx.fillRect(x, y, size, size);
        }
    }
}

function fitRect(canvasWidth, canvasHeight, imageWidthValue, imageHeightValue, padding) {
    const maxW = Math.max(1, canvasWidth - padding * 2);
    const maxH = Math.max(1, canvasHeight - padding * 2);
    const scale = Math.min(maxW / imageWidthValue, maxH / imageHeightValue);
    const w = imageWidthValue * scale;
    const h = imageHeightValue * scale;
    return { x: (canvasWidth - w) / 2, y: (canvasHeight - h) / 2, w, h };
}

function clampRect(rect, width, height) {
    const x = clampInt(rect.x, 0, Math.max(0, width - 1), 0);
    const y = clampInt(rect.y, 0, Math.max(0, height - 1), 0);
    return {
        x,
        y,
        w: clampInt(rect.w, 1, Math.max(1, width - x), 1),
        h: clampInt(rect.h, 1, Math.max(1, height - y), 1),
    };
}

function toDotNetRect(rect) {
    return {
        X: Math.round(rect.x),
        Y: Math.round(rect.y),
        Width: Math.round(rect.w),
        Height: Math.round(rect.h),
    };
}

function toDotNetShapePaths(shapePaths) {
    return (shapePaths || [])
        .filter(path => (path.points || []).length >= 3)
        .map(path => ({
            Points: path.points.map(point => ({
                X: Math.round(point.x),
                Y: Math.round(point.y),
            })),
        }));
}

function read(value, ...names) {
    if (!value) return undefined;
    for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(value, name)) return value[name];
    }
    return undefined;
}

function clampInt(value, min, max, fallback) {
    const number = Number(value);
    if (!Number.isFinite(number)) return fallback;
    return Math.max(min, Math.min(max, Math.round(number)));
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
    const image = new Image();
    image.decoding = "async";
    image.src = url;
    await image.decode();
    return image;
}
