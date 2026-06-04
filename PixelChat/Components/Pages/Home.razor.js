const states = new WeakMap();

export async function loadEditor(canvas, imageUrl, brushSize, tool) {
    const image = await loadImage(imageUrl);
    const source = document.createElement("canvas");
    source.width = image.naturalWidth || image.width;
    source.height = image.naturalHeight || image.height;
    source.getContext("2d").drawImage(image, 0, 0, source.width, source.height);

    const paint = document.createElement("canvas");
    paint.width = source.width;
    paint.height = source.height;

    const state = {
        source,
        paint,
        brushSize: Number(brushSize) || 36,
        tool: tool || "mask",
        drawing: false,
        cropStart: null,
        cropRect: null,
        hasPaint: false,
        imageUrl,
        resizeHandler: null,
    };

    detach(canvas);
    states.set(canvas, state);
    attach(canvas, state);
    resize(canvas, state);
    render(canvas, state);
}

export function setTool(canvas, tool) {
    const state = states.get(canvas);
    if (!state) return;
    state.tool = tool || "mask";
}

export function setBrushSize(canvas, brushSize) {
    const state = states.get(canvas);
    if (!state) return;
    state.brushSize = Number(brushSize) || state.brushSize;
}

export function clearMask(canvas) {
    const state = states.get(canvas);
    if (!state) return;
    state.paint.getContext("2d").clearRect(0, 0, state.paint.width, state.paint.height);
    state.hasPaint = false;
    render(canvas, state);
}

export function hasMaskPaint(canvas) {
    const state = states.get(canvas);
    return !!state?.hasPaint;
}

export function exportSourcePng(canvas) {
    const state = states.get(canvas);
    if (!state) throw new Error("Editor is not ready.");
    return state.source.toDataURL("image/png");
}

export function exportMaskPng(canvas) {
    const state = states.get(canvas);
    if (!state) throw new Error("Editor is not ready.");

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

export function exportCropPng(canvas) {
    const state = states.get(canvas);
    if (!state || !state.cropRect) return null;

    const rect = normalizeRect(state.cropRect);
    if (rect.w < 4 || rect.h < 4) return null;

    const output = document.createElement("canvas");
    output.width = Math.round(rect.w);
    output.height = Math.round(rect.h);
    output.getContext("2d").drawImage(
        state.source,
        rect.x,
        rect.y,
        rect.w,
        rect.h,
        0,
        0,
        output.width,
        output.height);
    return output.toDataURL("image/png");
}

function attach(canvas, state) {
    canvas.addEventListener("pointerdown", onPointerDown);
    canvas.addEventListener("pointermove", onPointerMove);
    canvas.addEventListener("pointerup", onPointerUp);
    canvas.addEventListener("pointercancel", onPointerUp);
    canvas.addEventListener("pointerleave", onPointerUp);
    state.resizeHandler = () => {
        resize(canvas, state);
        render(canvas, state);
    };
    window.addEventListener("resize", state.resizeHandler);
}

function detach(canvas) {
    const state = states.get(canvas);
    if (state?.resizeHandler) window.removeEventListener("resize", state.resizeHandler);
    canvas.removeEventListener("pointerdown", onPointerDown);
    canvas.removeEventListener("pointermove", onPointerMove);
    canvas.removeEventListener("pointerup", onPointerUp);
    canvas.removeEventListener("pointercancel", onPointerUp);
    canvas.removeEventListener("pointerleave", onPointerUp);
}

function onPointerDown(event) {
    const canvas = event.currentTarget;
    const state = states.get(canvas);
    if (!state) return;
    const point = toImagePoint(canvas, state, event);
    if (!point) return;

    canvas.setPointerCapture(event.pointerId);
    state.drawing = true;
    if (state.tool === "crop") {
        state.cropStart = point;
        state.cropRect = { x: point.x, y: point.y, w: 0, h: 0 };
    } else {
        paintAt(state, point, state.tool === "erase");
    }
    render(canvas, state);
}

function onPointerMove(event) {
    const canvas = event.currentTarget;
    const state = states.get(canvas);
    if (!state || !state.drawing) return;
    const point = toImagePoint(canvas, state, event);
    if (!point) return;

    if (state.tool === "crop" && state.cropStart) {
        state.cropRect = {
            x: state.cropStart.x,
            y: state.cropStart.y,
            w: point.x - state.cropStart.x,
            h: point.y - state.cropStart.y,
        };
    } else {
        paintAt(state, point, state.tool === "erase");
    }
    render(canvas, state);
}

function onPointerUp(event) {
    const canvas = event.currentTarget;
    const state = states.get(canvas);
    if (!state) return;
    state.drawing = false;
    try { canvas.releasePointerCapture(event.pointerId); } catch { }
    render(canvas, state);
}

function paintAt(state, point, erase) {
    const ctx = state.paint.getContext("2d");
    ctx.save();
    ctx.beginPath();
    ctx.arc(point.x, point.y, state.brushSize / 2, 0, Math.PI * 2);
    if (erase) {
        ctx.globalCompositeOperation = "destination-out";
        ctx.fillStyle = "rgba(0,0,0,1)";
    } else {
        ctx.globalCompositeOperation = "source-over";
        ctx.fillStyle = "rgba(31,111,235,0.42)";
        state.hasPaint = true;
    }
    ctx.fill();
    ctx.restore();
}

function resize(canvas, state) {
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(320, Math.round(rect.width * dpr));
    const height = Math.max(320, Math.round(rect.height * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    state.dpr = dpr;
}

function render(canvas, state) {
    resize(canvas, state);
    const ctx = canvas.getContext("2d");
    const viewport = imageViewport(canvas, state);

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    drawChecker(ctx, canvas.width, canvas.height);
    ctx.drawImage(state.source, viewport.x, viewport.y, viewport.w, viewport.h);
    ctx.drawImage(state.paint, viewport.x, viewport.y, viewport.w, viewport.h);

    if (state.cropRect) {
        const rect = normalizeRect(state.cropRect);
        const scaleX = viewport.w / state.source.width;
        const scaleY = viewport.h / state.source.height;
        ctx.save();
        ctx.strokeStyle = "#f59f00";
        ctx.lineWidth = Math.max(2, state.dpr * 2);
        ctx.setLineDash([8 * state.dpr, 5 * state.dpr]);
        ctx.strokeRect(
            viewport.x + rect.x * scaleX,
            viewport.y + rect.y * scaleY,
            rect.w * scaleX,
            rect.h * scaleY);
        ctx.restore();
    }
}

function drawChecker(ctx, width, height) {
    const size = 18;
    ctx.fillStyle = "#dce3eb";
    ctx.fillRect(0, 0, width, height);
    ctx.fillStyle = "#cbd5e1";
    for (let y = 0; y < height; y += size) {
        for (let x = 0; x < width; x += size) {
            if (((x / size) + (y / size)) % 2 === 0) {
                ctx.fillRect(x, y, size, size);
            }
        }
    }
}

function toImagePoint(canvas, state, event) {
    const rect = canvas.getBoundingClientRect();
    const viewport = imageViewport(canvas, state);
    const x = (event.clientX - rect.left) * (canvas.width / rect.width);
    const y = (event.clientY - rect.top) * (canvas.height / rect.height);
    if (x < viewport.x || y < viewport.y || x > viewport.x + viewport.w || y > viewport.y + viewport.h) return null;
    return {
        x: clamp(((x - viewport.x) / viewport.w) * state.source.width, 0, state.source.width),
        y: clamp(((y - viewport.y) / viewport.h) * state.source.height, 0, state.source.height),
    };
}

function imageViewport(canvas, state) {
    const padding = 18 * (state.dpr || 1);
    const maxW = Math.max(1, canvas.width - padding * 2);
    const maxH = Math.max(1, canvas.height - padding * 2);
    const scale = Math.min(maxW / state.source.width, maxH / state.source.height);
    const w = state.source.width * scale;
    const h = state.source.height * scale;
    return {
        x: (canvas.width - w) / 2,
        y: (canvas.height - h) / 2,
        w,
        h,
    };
}

function normalizeRect(rect) {
    const x = rect.w < 0 ? rect.x + rect.w : rect.x;
    const y = rect.h < 0 ? rect.y + rect.h : rect.y;
    return {
        x: Math.max(0, x),
        y: Math.max(0, y),
        w: Math.abs(rect.w),
        h: Math.abs(rect.h),
    };
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}
