const editorStates = new WeakMap();
const animations = new WeakMap();

export async function detectSpriteSheetFrames(imageUrl, expectedFrames, layoutHint, backgroundMode) {
    const image = await loadImage(imageUrl);
    const pixels = imagePixels(image);
    const frames = detectFramesFromPixels(
        pixels.data,
        pixels.width,
        pixels.height,
        Number(expectedFrames) || null,
        backgroundMode || "auto");

    if (frames.length === 0) {
        return gridFallback(pixels.width, pixels.height, Number(expectedFrames) || 1, layoutHint);
    }

    const rowCount = countRows(frames);
    const columns = rowCount <= 1
        ? frames.length
        : Math.max(...groupFramesByRows(frames).map(row => row.length));
    return {
        ImageWidth: pixels.width,
        ImageHeight: pixels.height,
        Rows: Math.max(1, rowCount),
        Columns: Math.max(1, columns),
        Frames: frames.map((frame, index) => ({
            Index: index,
            SourceRect: {
                X: frame.x,
                Y: frame.y,
                Width: frame.w,
                Height: frame.h,
            },
        })),
    };
}

export async function drawSpriteBoxEditor(sourceCanvas, animationCanvas, dotNetRef, imageUrl, payload, selectedIndex, tool) {
    const image = await loadImage(imageUrl);
    const layout = normalizeLayout(payload);
    const state = ensureEditorState(sourceCanvas, dotNetRef);
    state.image = image;
    state.imageUrl = imageUrl;
    state.layout = layout;
    state.selectedIndex = Number.isFinite(Number(selectedIndex)) ? Number(selectedIndex) : -1;
    state.tool = String(tool || "select").toLowerCase();
    drawSourceCanvas(sourceCanvas, state);

    const output = renderOutputCanvas(image, layout);
    startAnimation(animationCanvas, output.canvas, output.frames, layout);
}

export async function renderSpriteSheetAutosave(imageUrl, payload) {
    const image = await loadImage(imageUrl);
    const layout = normalizeLayout(payload);
    const output = renderOutputCanvas(image, layout);
    return {
        DataUrl: output.canvas.toDataURL("image/png"),
        Width: output.canvas.width,
        Height: output.canvas.height,
        Frames: output.frames.map(frame => ({
            Index: frame.index,
            Label: frame.label,
            SourceRect: toDotNetRect(frame.rebasedSourceRect),
            CellRect: toDotNetRect(frame.cellRect),
            SpriteRect: toDotNetRect(frame.spriteRect),
            PreviewPngDataUrl: frame.previewCanvas.toDataURL("image/png"),
        })),
    };
}

export function disposeSpriteBoxEditor(sourceCanvas, animationCanvas) {
    stopAnimation(animationCanvas);
    editorStates.delete(sourceCanvas);
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
    };
    editorStates.set(canvas, state);

    canvas.addEventListener("pointerdown", event => onPointerDown(canvas, state, event));
    canvas.addEventListener("pointermove", event => onPointerMove(canvas, state, event));
    canvas.addEventListener("pointerup", event => onPointerUp(canvas, state, event));
    canvas.addEventListener("pointercancel", event => onPointerUp(canvas, state, event));
    canvas.addEventListener("pointerleave", event => {
        if (state.drag) onPointerUp(canvas, state, event);
    });
    return state;
}

function onPointerDown(canvas, state, event) {
    if (!state.image || !state.layout || !state.viewport) return;
    const point = eventToImagePoint(canvas, state, event);
    if (!point) return;
    event.preventDefault();
    canvas.setPointerCapture?.(event.pointerId);

    if (state.tool === "draw") {
        const index = state.layout.frames.length;
        const label = `Frame ${index + 1}`;
        const frame = {
            index,
            label,
            sourceRect: { x: point.x, y: point.y, w: 1, h: 1 },
        };
        state.layout.frames.push(frame);
        state.layout.frameCount = state.layout.frames.length;
        state.selectedIndex = index;
        state.drag = { type: "draw", index, start: point };
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
        type: hit.handle ? "resize" : "move",
        index: hit.index,
        handle: hit.handle,
        start: point,
        original: { ...frame.sourceRect },
    };
    drawSourceCanvas(canvas, state);
}

function onPointerMove(canvas, state, event) {
    if (!state.drag || !state.layout || !state.viewport) return;
    const point = eventToImagePoint(canvas, state, event);
    if (!point) return;
    event.preventDefault();

    const frame = state.layout.frames[state.drag.index];
    if (!frame) return;

    if (state.drag.type === "draw") {
        frame.sourceRect = rectFromPoints(state.drag.start, point);
    } else if (state.drag.type === "move") {
        const dx = point.x - state.drag.start.x;
        const dy = point.y - state.drag.start.y;
        frame.sourceRect = clampRect({
            x: Math.round(state.drag.original.x + dx),
            y: Math.round(state.drag.original.y + dy),
            w: state.drag.original.w,
            h: state.drag.original.h,
        }, imageWidth(state.image), imageHeight(state.image));
    } else if (state.drag.type === "resize") {
        frame.sourceRect = resizeRect(state.drag.original, state.drag.handle, point, imageWidth(state.image), imageHeight(state.image));
    }

    drawSourceCanvas(canvas, state);
}

function onPointerUp(canvas, state, event) {
    if (!state.drag || !state.layout) return;
    event.preventDefault();

    const frame = state.layout.frames[state.drag.index];
    if (frame) {
        frame.sourceRect = clampRect(frame.sourceRect, imageWidth(state.image), imageHeight(state.image));
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
    drawSourceCanvas(canvas, state);
    state.dotNetRef?.invokeMethodAsync("OnSpriteBoxesChanged", {
        SelectedIndex: selectedIndex,
        Frames: state.layout.frames.map(frame => ({
            Index: frame.index,
            Label: frame.label,
            SourceRect: toDotNetRect(frame.sourceRect),
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
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);

        const label = frame.label || `Frame ${frame.index + 1}`;
        const labelWidth = Math.min(ctx.measureText(label).width + 10 * deviceScale(), Math.max(28 * deviceScale(), w));
        ctx.fillStyle = selected ? "rgba(122,83,0,0.92)" : "rgba(15,77,184,0.9)";
        ctx.fillRect(x, y, labelWidth, 20 * deviceScale());
        ctx.fillStyle = "#ffffff";
        ctx.fillText(label, x + 5 * deviceScale(), y + 3 * deviceScale(), labelWidth - 8 * deviceScale());

        if (selected) drawHandles(ctx, x, y, w, h);
    }
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

function renderOutputCanvas(image, layout) {
    if (layout.frames.length === 0) {
        const canvas = document.createElement("canvas");
        canvas.width = imageWidth(image);
        canvas.height = imageHeight(image);
        const ctx = canvas.getContext("2d");
        ctx.imageSmoothingEnabled = false;
        ctx.drawImage(image, 0, 0);
        return { canvas, frames: [] };
    }

    const frameCount = Math.min(layout.frameCount, layout.frames.length, layout.rows * layout.columns);
    const rows = Math.max(layout.rows, Math.ceil(frameCount / layout.columns));
    const width = layout.columns * layout.cellWidth + Math.max(0, layout.columns - 1) * layout.gutter;
    const height = rows * layout.cellHeight + Math.max(0, rows - 1) * layout.gutter;
    if (width <= 0 || height <= 0 || width > 32767 || height > 32767 || width * height > 120_000_000) {
        throw new Error("Sprite sheet output is too large for browser rendering.");
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d");
    ctx.imageSmoothingEnabled = false;
    ctx.clearRect(0, 0, width, height);

    const frames = [];
    for (let index = 0; index < frameCount; index++) {
        const frame = layout.frames[index];
        const sourceRect = clampRect(frame.sourceRect, imageWidth(image), imageHeight(image));
        const row = Math.floor(index / layout.columns);
        const column = index % layout.columns;
        const cellRect = {
            x: column * (layout.cellWidth + layout.gutter),
            y: row * (layout.cellHeight + layout.gutter),
            w: layout.cellWidth,
            h: layout.cellHeight,
        };
        const destX = Math.round(cellRect.x + ((layout.cellWidth - sourceRect.w) / 2));
        const baseline = cellRect.y + layout.cellHeight - layout.padding;
        const destY = Math.round(baseline - sourceRect.h);
        const spriteRect = {
            x: destX,
            y: destY,
            w: sourceRect.w,
            h: sourceRect.h,
        };

        ctx.drawImage(
            image,
            sourceRect.x,
            sourceRect.y,
            sourceRect.w,
            sourceRect.h,
            destX,
            destY,
            sourceRect.w,
            sourceRect.h);

        const rebasedSourceRect = intersectRect(spriteRect, width, height);
        const previewCanvas = document.createElement("canvas");
        previewCanvas.width = cellRect.w;
        previewCanvas.height = cellRect.h;
        const previewCtx = previewCanvas.getContext("2d");
        previewCtx.imageSmoothingEnabled = false;
        previewCtx.drawImage(canvas, cellRect.x, cellRect.y, cellRect.w, cellRect.h, 0, 0, cellRect.w, cellRect.h);
        frames.push({
            index,
            label: frame.label || `Frame ${index + 1}`,
            sourceRect,
            cellRect,
            spriteRect,
            rebasedSourceRect,
            previewCanvas,
        });
    }

    return { canvas, frames };
}

function startAnimation(canvas, outputCanvas, frames, layout) {
    stopAnimation(canvas);
    if (!canvas || frames.length === 0) {
        const ctx = resizeCanvas(canvas);
        drawChecker(ctx, canvas.width, canvas.height);
        return;
    }

    const state = {
        outputCanvas,
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
    const viewport = fitRect(canvas.width, canvas.height, frame.cellRect.w, frame.cellRect.h, 12 * deviceScale());
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(
        state.outputCanvas,
        frame.cellRect.x,
        frame.cellRect.y,
        frame.cellRect.w,
        frame.cellRect.h,
        viewport.x,
        viewport.y,
        viewport.w,
        viewport.h);
}

function hitTest(state, point) {
    for (let index = state.layout.frames.length - 1; index >= 0; index--) {
        const frame = state.layout.frames[index];
        const handle = hitHandle(frame.sourceRect, point, state);
        if (handle) return { index, handle };
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
    if (!viewport || x < viewport.x || x > viewport.x + viewport.w || y < viewport.y || y > viewport.y + viewport.h) {
        return null;
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

function normalizeLayout(payload) {
    const rows = clampInt(read(payload, "Rows", "rows"), 1, 32, 1);
    let columns = clampInt(read(payload, "Columns", "columns"), 1, 64, 1);
    const frames = (read(payload, "Frames", "frames") || []).map((frame, fallbackIndex) => {
        const rect = read(frame, "SourceRect", "sourceRect") || {};
        return {
            index: clampInt(read(frame, "Index", "index"), 0, 127, fallbackIndex),
            label: String(read(frame, "Label", "label") || `Frame ${fallbackIndex + 1}`),
            sourceRect: {
                x: clampInt(read(rect, "X", "x"), 0, 32767, 0),
                y: clampInt(read(rect, "Y", "y"), 0, 32767, 0),
                w: clampInt(read(rect, "Width", "width", "W", "w"), 1, 32767, 1),
                h: clampInt(read(rect, "Height", "height", "H", "h"), 1, 32767, 1),
            },
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
        frames,
    };
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

function detectFramesFromPixels(data, width, height, expectedFrames, backgroundMode) {
    const foreground = new Uint8Array(width * height);
    let foregroundCount = 0;
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const offset = ((y * width) + x) * 4;
            const value = isForeground(data[offset], data[offset + 1], data[offset + 2], data[offset + 3], backgroundMode) ? 1 : 0;
            foreground[(y * width) + x] = value;
            foregroundCount += value;
        }
    }

    if (foregroundCount === 0) return [];

    const rowCounts = new Array(height).fill(0);
    for (let y = 0; y < height; y++) {
        let count = 0;
        const rowOffset = y * width;
        for (let x = 0; x < width; x++) count += foreground[rowOffset + x];
        rowCounts[y] = count;
    }

    const rowBands = buildBands(rowCounts, Math.max(2, Math.ceil(width * 0.01)), Math.max(4, Math.floor(height / 160)), Math.max(3, Math.floor(height / 160)));
    const frames = [];
    for (const rowBand of rowBands) {
        const bandHeight = rowBand.end - rowBand.start + 1;
        const columnCounts = new Array(width).fill(0);
        for (let x = 0; x < width; x++) {
            let count = 0;
            for (let y = rowBand.start; y <= rowBand.end; y++) count += foreground[(y * width) + x];
            columnCounts[x] = count;
        }

        const columnBands = buildBands(columnCounts, Math.max(2, Math.ceil(bandHeight * 0.01)), Math.max(4, Math.floor(width / 240)), Math.max(3, Math.floor(width / 256)));
        for (const columnBand of columnBands) {
            const rect = tightRect(foreground, width, rowBand, columnBand);
            if (rect) frames.push(rect);
        }
    }

    frames.sort((left, right) => left.y - right.y || left.x - right.x);
    let normalized = frames.map((frame, index) => ({ ...frame, index }));
    if (expectedFrames && normalized.length > expectedFrames) {
        normalized = normalized
            .sort((left, right) => (right.w * right.h) - (left.w * left.h))
            .slice(0, expectedFrames)
            .sort((left, right) => left.y - right.y || left.x - right.x)
            .map((frame, index) => ({ ...frame, index }));
    }
    return normalized;
}

function tightRect(foreground, width, rowBand, columnBand) {
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    for (let y = rowBand.start; y <= rowBand.end; y++) {
        for (let x = columnBand.start; x <= columnBand.end; x++) {
            if (!foreground[(y * width) + x]) continue;
            minX = Math.min(minX, x);
            minY = Math.min(minY, y);
            maxX = Math.max(maxX, x);
            maxY = Math.max(maxY, y);
        }
    }

    if (!Number.isFinite(minX)) return null;
    return { x: minX, y: minY, w: Math.max(1, maxX - minX + 1), h: Math.max(1, maxY - minY + 1) };
}

function buildBands(counts, threshold, minSize, gapTolerance) {
    const bands = [];
    let start = -1;
    let end = -1;
    let gap = 0;
    for (let index = 0; index < counts.length; index++) {
        if (counts[index] >= threshold) {
            if (start < 0) start = index;
            end = index;
            gap = 0;
        } else if (start >= 0) {
            gap++;
            if (gap > gapTolerance) {
                if (end - start + 1 >= minSize) bands.push({ start, end });
                start = -1;
                end = -1;
                gap = 0;
            }
        }
    }

    if (start >= 0 && end - start + 1 >= minSize) bands.push({ start, end });
    return bands;
}

function countRows(frames) {
    return groupFramesByRows(frames).length;
}

function groupFramesByRows(frames) {
    if (frames.length === 0) return [];
    const heights = frames.map(frame => frame.h).sort((left, right) => left - right);
    const tolerance = Math.max(8, Math.floor(heights[Math.floor(heights.length / 2)] / 3));
    const rows = [];
    for (const frame of [...frames].sort((left, right) => left.y - right.y)) {
        let row = rows.find(item => Math.abs(item.y - frame.y) <= tolerance);
        if (!row) {
            row = { y: frame.y, frames: [] };
            rows.push(row);
        }
        row.frames.push(frame);
    }

    return rows.map(row => row.frames);
}

function gridFallback(width, height, expectedFrames, layoutHint) {
    const count = Math.max(1, expectedFrames || 1);
    let rows = 1;
    let columns = count;
    if (layoutHint && String(layoutHint).toLowerCase().includes("multi") && count > 4) {
        rows = Math.max(1, Math.floor(Math.sqrt(count)));
        columns = Math.max(1, Math.ceil(count / rows));
    }

    const cellWidth = Math.max(1, Math.floor(width / columns));
    const cellHeight = Math.max(1, Math.floor(height / rows));
    const frames = [];
    for (let index = 0; index < count; index++) {
        const row = Math.floor(index / columns);
        const column = index % columns;
        frames.push({
            Index: index,
            SourceRect: {
                X: column * cellWidth,
                Y: row * cellHeight,
                Width: cellWidth,
                Height: cellHeight,
            },
        });
    }

    return { ImageWidth: width, ImageHeight: height, Rows: rows, Columns: columns, Frames: frames };
}

function isForeground(r, g, b, a, backgroundMode) {
    if (a <= 16) return false;
    const mode = String(backgroundMode || "auto").toLowerCase();
    if (mode === "alpha" || mode === "transparency") return true;
    return !(r >= 210 && b >= 210 && g <= 80);
}

function imagePixels(image) {
    const canvas = document.createElement("canvas");
    canvas.width = imageWidth(image);
    canvas.height = imageHeight(image);
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
    return {
        width: canvas.width,
        height: canvas.height,
        data: ctx.getImageData(0, 0, canvas.width, canvas.height).data,
    };
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

function intersectRect(rect, width, height) {
    const x1 = Math.max(0, Math.min(width, rect.x));
    const y1 = Math.max(0, Math.min(height, rect.y));
    const x2 = Math.max(0, Math.min(width, rect.x + rect.w));
    const y2 = Math.max(0, Math.min(height, rect.y + rect.h));
    return { x: x1, y: y1, w: Math.max(1, x2 - x1), h: Math.max(1, y2 - y1) };
}

function toDotNetRect(rect) {
    return {
        X: Math.round(rect.x),
        Y: Math.round(rect.y),
        Width: Math.round(rect.w),
        Height: Math.round(rect.h),
    };
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
