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

export async function drawSpriteSheetCanvases(sourceCanvas, previewCanvas, animationCanvas, imageUrl, payload, selectedIndex) {
    const image = await loadImage(imageUrl);
    const layout = normalizeLayout(payload);
    drawSourceCanvas(sourceCanvas, image, layout, Number(selectedIndex) || 0);
    const output = renderOutputCanvas(image, layout);
    drawPreviewCanvas(previewCanvas, output.canvas, output.frames);
    startAnimation(animationCanvas, output.canvas, output.frames, layout);
}

export async function renderSpriteSheetOutput(imageUrl, payload) {
    const image = await loadImage(imageUrl);
    const layout = normalizeLayout(payload);
    const output = renderOutputCanvas(image, layout);
    return {
        DataUrl: output.canvas.toDataURL("image/png"),
        Width: output.canvas.width,
        Height: output.canvas.height,
        Frames: output.frames.map(frame => ({
            Index: frame.index,
            SourceRect: toDotNetRect(frame.sourceRect),
            CellRect: toDotNetRect(frame.cellRect),
            SpriteRect: toDotNetRect(frame.spriteRect),
            OffsetX: frame.offsetX,
            OffsetY: frame.offsetY,
            PivotX: 0.5,
            PivotY: 1.0,
            Duration: frame.duration,
        })),
    };
}

export function disposeSpriteSheetCanvases(animationCanvas) {
    stopAnimation(animationCanvas);
}

function renderOutputCanvas(image, layout) {
    const width = layout.columns * layout.cellWidth + Math.max(0, layout.columns - 1) * layout.gutter;
    const height = layout.rows * layout.cellHeight + Math.max(0, layout.rows - 1) * layout.gutter;
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
    const frameCount = Math.min(layout.frameCount, layout.frames.length, layout.rows * layout.columns);
    const duration = Number((1 / Math.max(1, layout.fps)).toFixed(6));
    for (let index = 0; index < frameCount; index++) {
        const frame = layout.frames[index];
        const sourceRect = clampRect(frame.sourceRect, image.naturalWidth || image.width, image.naturalHeight || image.height);
        const row = Math.floor(index / layout.columns);
        const column = index % layout.columns;
        const cellRect = {
            x: column * (layout.cellWidth + layout.gutter),
            y: row * (layout.cellHeight + layout.gutter),
            w: layout.cellWidth,
            h: layout.cellHeight,
        };
        const destX = Math.round(cellRect.x + ((layout.cellWidth - sourceRect.w) / 2) + frame.offsetX);
        const baseline = cellRect.y + layout.cellHeight - layout.padding;
        const destY = Math.round(baseline - sourceRect.h + frame.offsetY);
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
        frames.push({
            index,
            sourceRect,
            cellRect,
            spriteRect,
            offsetX: frame.offsetX,
            offsetY: frame.offsetY,
            duration,
        });
    }

    return { canvas, frames };
}

function drawSourceCanvas(canvas, image, layout, selectedIndex) {
    const ctx = resizeCanvas(canvas);
    drawChecker(ctx, canvas.width, canvas.height);
    const viewport = fitRect(canvas.width, canvas.height, image.naturalWidth || image.width, image.naturalHeight || image.height, 18 * deviceScale());
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(image, viewport.x, viewport.y, viewport.w, viewport.h);

    const scaleX = viewport.w / (image.naturalWidth || image.width);
    const scaleY = viewport.h / (image.naturalHeight || image.height);
    ctx.save();
    ctx.lineWidth = Math.max(2, 2 * deviceScale());
    ctx.font = `${Math.max(11, 11 * deviceScale())}px system-ui, sans-serif`;
    ctx.textBaseline = "top";
    for (const frame of layout.frames.slice(0, layout.frameCount)) {
        const rect = frame.sourceRect;
        const x = viewport.x + rect.x * scaleX;
        const y = viewport.y + rect.y * scaleY;
        const w = rect.w * scaleX;
        const h = rect.h * scaleY;
        const selected = frame.index === selectedIndex;
        ctx.strokeStyle = selected ? "#f59f00" : "#1f6feb";
        ctx.fillStyle = selected ? "rgba(245,159,0,0.16)" : "rgba(31,111,235,0.12)";
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);
        ctx.fillStyle = selected ? "#7a5300" : "#0f4db8";
        ctx.fillText(String(frame.index + 1), x + 4 * deviceScale(), y + 4 * deviceScale());
    }
    ctx.restore();
}

function drawPreviewCanvas(canvas, outputCanvas, frames) {
    const ctx = resizeCanvas(canvas);
    drawChecker(ctx, canvas.width, canvas.height);
    const viewport = fitRect(canvas.width, canvas.height, outputCanvas.width, outputCanvas.height, 18 * deviceScale());
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(outputCanvas, viewport.x, viewport.y, viewport.w, viewport.h);

    const scaleX = viewport.w / outputCanvas.width;
    const scaleY = viewport.h / outputCanvas.height;
    ctx.save();
    ctx.lineWidth = Math.max(1, deviceScale());
    ctx.strokeStyle = "rgba(31,111,235,0.7)";
    for (const frame of frames) {
        ctx.strokeRect(
            viewport.x + frame.cellRect.x * scaleX,
            viewport.y + frame.cellRect.y * scaleY,
            frame.cellRect.w * scaleX,
            frame.cellRect.h * scaleY);
    }
    ctx.restore();
}

function startAnimation(canvas, outputCanvas, frames, layout) {
    stopAnimation(canvas);
    if (!canvas || frames.length === 0) return;

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

function normalizeLayout(payload) {
    const rows = clampInt(read(payload, "Rows", "rows"), 1, 32, 1);
    let columns = clampInt(read(payload, "Columns", "columns"), 1, 64, 1);
    const frameCount = clampInt(read(payload, "FrameCount", "frameCount"), 1, 128, 1);
    if (rows * columns < frameCount) {
        columns = Math.max(1, Math.ceil(frameCount / rows));
    }

    const frames = (read(payload, "Frames", "frames") || []).map((frame, fallbackIndex) => {
        const rect = read(frame, "SourceRect", "sourceRect") || {};
        return {
            index: clampInt(read(frame, "Index", "index"), 0, 127, fallbackIndex),
            sourceRect: {
                x: clampInt(read(rect, "X", "x"), 0, 32767, 0),
                y: clampInt(read(rect, "Y", "y"), 0, 32767, 0),
                w: clampInt(read(rect, "Width", "width", "W", "w"), 1, 32767, 1),
                h: clampInt(read(rect, "Height", "height", "H", "h"), 1, 32767, 1),
            },
            offsetX: clampInt(read(frame, "OffsetX", "offsetX"), -32767, 32767, 0),
            offsetY: clampInt(read(frame, "OffsetY", "offsetY"), -32767, 32767, 0),
        };
    });

    frames.sort((left, right) => left.index - right.index);
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
    canvas.width = image.naturalWidth || image.width;
    canvas.height = image.naturalHeight || image.height;
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

function fitRect(canvasWidth, canvasHeight, imageWidth, imageHeight, padding) {
    const maxW = Math.max(1, canvasWidth - padding * 2);
    const maxH = Math.max(1, canvasHeight - padding * 2);
    const scale = Math.min(maxW / imageWidth, maxH / imageHeight);
    const w = imageWidth * scale;
    const h = imageHeight * scale;
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

async function loadImage(url) {
    if (!url) throw new Error("Image URL is required.");
    const image = new Image();
    image.decoding = "async";
    image.src = url;
    await image.decode();
    return image;
}
