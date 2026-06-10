const states = new WeakMap();

export async function loadEditor(canvas, imageUrl, brushSize, tool, maskUrl, dotNetRef) {
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
        drawingTool: null,
        maskStrokeDirty: false,
        cropStart: null,
        cropRect: null,
        imageUrl,
        dotNetRef,
        resizeHandler: null,
    };

    detach(canvas);
    if (maskUrl) {
        await loadMaskIntoPaint(state, maskUrl);
    }
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
    state.maskStrokeDirty = false;
    render(canvas, state);
}

export function hasMaskPaint(canvas) {
    const state = states.get(canvas);
    return state ? paintHasPixels(state) : false;
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

export async function prepareExportPreview(imageUrl, tolerance, removeBackground, removalMethod, cleanKeyColor) {
    const image = await loadImage(imageUrl);
    const canvas = document.createElement("canvas");
    canvas.width = image.naturalWidth || image.width;
    canvas.height = image.naturalHeight || image.height;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);

    const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    const analysis = analyzeExportBackground(imageData.data, canvas.width, canvas.height);
    const keyAnalysis = analyzeKeyColorBackground(imageData.data, canvas.width, canvas.height);
    const method = normalizeRemovalMethod(removalMethod);
    const shouldRemove = removeBackground === null || removeBackground === undefined
        ? analysis.shouldRemove || analysis.checkerboard.shouldRemove
        : Boolean(removeBackground);
    const shouldCleanKey = shouldRemove
        && (cleanKeyColor === null || cleanKeyColor === undefined
            ? keyAnalysis.detected
            : Boolean(cleanKeyColor));

    let result = { removedPixels: 0, method: "", detail: "" };
    if (shouldRemove) {
        result = removeExportBackground(imageData, analysis, normalizeTolerance(tolerance), method);
    }

    let keyResult = { removedPixels: 0, softenedPixels: 0 };
    if (shouldCleanKey) {
        keyResult = removeKeyColorBackground(imageData);
    }

    ctx.putImageData(imageData, 0, 0);
    const stats = alphaStats(imageData.data);
    return {
        RemoveBackground: shouldRemove,
        DataUrl: canvas.toDataURL("image/png"),
        Message: exportPreviewMessage(shouldRemove, analysis, result, stats, keyResult),
        Method: mergeExportMethods(result.method, keyResult.removedPixels > 0 ? "key-color" : ""),
        TransparentPixels: stats.transparent,
        SemiTransparentPixels: stats.semiTransparent,
        OpaquePixels: stats.opaque,
        KeyColorDetected: keyAnalysis.detected,
        KeyColorPixels: keyAnalysis.candidatePixels,
        KeyColorRemovedPixels: keyResult.removedPixels,
        Width: canvas.width,
        Height: canvas.height,
    };
}

export async function analyzePngAlpha(imageUrl) {
    const image = await loadImage(imageUrl);
    const canvas = document.createElement("canvas");
    canvas.width = image.naturalWidth || image.width;
    canvas.height = image.naturalHeight || image.height;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);

    const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    const stats = alphaStats(imageData.data);
    return {
        TransparentPixels: stats.transparent,
        SemiTransparentPixels: stats.semiTransparent,
        OpaquePixels: stats.opaque,
        Width: canvas.width,
        Height: canvas.height,
    };
}

export async function prepareKeyColorCleanupPreview(imageUrl, cleanKeyColor) {
    const image = await loadImage(imageUrl);
    const canvas = document.createElement("canvas");
    canvas.width = image.naturalWidth || image.width;
    canvas.height = image.naturalHeight || image.height;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);

    const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    const keyAnalysis = analyzeKeyColorBackground(imageData.data, canvas.width, canvas.height);
    const keyResult = cleanKeyColor ? removeKeyColorBackground(imageData) : { removedPixels: 0, softenedPixels: 0 };
    ctx.putImageData(imageData, 0, 0);

    const stats = alphaStats(imageData.data);
    return {
        DataUrl: canvas.toDataURL("image/png"),
        Message: keyResult.removedPixels > 0
            ? `Key color cleanup removed ${keyResult.removedPixels.toLocaleString()} magenta px.`
            : keyAnalysis.detected
                ? "Magenta key color detected."
                : "No magenta key color detected.",
        Method: keyResult.removedPixels > 0 ? "key-color" : "",
        TransparentPixels: stats.transparent,
        SemiTransparentPixels: stats.semiTransparent,
        OpaquePixels: stats.opaque,
        KeyColorDetected: keyAnalysis.detected,
        KeyColorPixels: keyAnalysis.candidatePixels,
        KeyColorRemovedPixels: keyResult.removedPixels,
        Width: canvas.width,
        Height: canvas.height,
    };
}

export function downloadExportPng(dataUrl, fileName) {
    if (!dataUrl || !dataUrl.startsWith("data:image/png;base64,")) {
        throw new Error("Export preview is not a PNG.");
    }

    const link = document.createElement("a");
    link.href = dataUrl;
    link.download = normalizeExportFileName(fileName);
    document.body.appendChild(link);
    link.click();
    link.remove();
}

export function downloadExportBundle(dataUrl, fileName, manifestJson, manifestFileName) {
    downloadExportPng(dataUrl, fileName);
    if (!manifestJson) return;

    const blob = new Blob([manifestJson], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = normalizeJsonFileName(manifestFileName || "sprite-sheet.sprite.json");
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

async function loadMaskIntoPaint(state, maskUrl) {
    const image = await loadImage(maskUrl);
    const width = image.naturalWidth || image.width;
    const height = image.naturalHeight || image.height;
    if (width !== state.source.width || height !== state.source.height) return;

    const mask = document.createElement("canvas");
    mask.width = width;
    mask.height = height;
    const maskCtx = mask.getContext("2d");
    maskCtx.drawImage(image, 0, 0, width, height);
    const maskPixels = maskCtx.getImageData(0, 0, width, height);
    const paintCtx = state.paint.getContext("2d");
    const paintPixels = paintCtx.createImageData(width, height);

    for (let i = 0; i < maskPixels.data.length; i += 4) {
        const maskAlpha = maskPixels.data[i + 3];
        if (maskAlpha >= 255) continue;
        paintPixels.data[i] = 31;
        paintPixels.data[i + 1] = 111;
        paintPixels.data[i + 2] = 235;
        paintPixels.data[i + 3] = 255 - maskAlpha;
    }

    paintCtx.putImageData(paintPixels, 0, 0);
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
    state.drawingTool = state.tool;
    state.maskStrokeDirty = false;
    if (state.tool === "crop") {
        state.cropStart = point;
        state.cropRect = { x: point.x, y: point.y, w: 0, h: 0 };
    } else {
        paintAt(state, point, state.tool === "erase");
        state.maskStrokeDirty = true;
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
        state.maskStrokeDirty = true;
    }
    render(canvas, state);
}

function onPointerUp(event) {
    const canvas = event.currentTarget;
    const state = states.get(canvas);
    if (!state) return;
    const shouldNotify = state.drawing && state.drawingTool !== "crop" && state.maskStrokeDirty;
    state.drawing = false;
    state.drawingTool = null;
    state.maskStrokeDirty = false;
    try { canvas.releasePointerCapture(event.pointerId); } catch { }
    render(canvas, state);
    if (shouldNotify) notifyMaskChanged(state);
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
    }
    ctx.fill();
    ctx.restore();
}

function notifyMaskChanged(state) {
    if (!state.dotNetRef) return;
    state.dotNetRef.invokeMethodAsync("OnEditorMaskChanged").catch(() => { });
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

function paintHasPixels(state) {
    const pixels = state.paint.getContext("2d").getImageData(0, 0, state.paint.width, state.paint.height);
    for (let i = 3; i < pixels.data.length; i += 4) {
        if (pixels.data[i] > 0) return true;
    }
    return false;
}

function analyzeExportBackground(data, width, height) {
    const edgeBand = Math.max(2, Math.min(48, Math.floor(Math.min(width, height) * 0.035)));
    const sampleStep = 1;
    const clusterTolerance = 24;
    const clusters = [];
    const samples = [];
    let opaqueSamples = 0;
    let transparentSamples = 0;

    const addSample = (x, y) => {
        const offset = ((y * width) + x) * 4;
        const alpha = data[offset + 3];
        if (alpha < 245) {
            transparentSamples++;
            return;
        }

        opaqueSamples++;
        const color = {
            x,
            y,
            r: data[offset],
            g: data[offset + 1],
            b: data[offset + 2],
        };
        color.luma = luma(color.r, color.g, color.b);
        color.saturation = saturation(color.r, color.g, color.b);
        samples.push(color);
        addColorCluster(
            clusters,
            color.r,
            color.g,
            color.b,
            clusterTolerance);
    };

    for (let y = 0; y < edgeBand; y += sampleStep) {
        for (let x = 0; x < width; x += sampleStep) {
            addSample(x, y);
            addSample(x, height - 1 - y);
        }
    }

    for (let y = edgeBand; y < height - edgeBand; y += sampleStep) {
        for (let x = 0; x < edgeBand; x += sampleStep) {
            addSample(x, y);
            addSample(width - 1 - x, y);
        }
    }

    clusters.sort((left, right) => right.count - left.count);
    const paletteClusters = clusters.slice(0, 5);
    const representedSamples = paletteClusters.reduce((sum, cluster) => sum + cluster.count, 0);
    const opaqueCoverage = opaqueSamples > 0 ? representedSamples / opaqueSamples : 0;
    const dominantShare = opaqueSamples > 0 && paletteClusters.length > 0
        ? paletteClusters[0].count / opaqueSamples
        : 0;
    const transparentShare = opaqueSamples + transparentSamples > 0
        ? transparentSamples / (opaqueSamples + transparentSamples)
        : 0;
    const hasExistingTransparency = transparentShare > 0.12;
    const shouldRemove = !hasExistingTransparency
        && opaqueSamples > 0
        && dominantShare >= 0.28
        && opaqueCoverage >= 0.72
        && paletteClusters.length <= 5;

    const analysis = {
        shouldRemove,
        hasExistingTransparency,
        opaqueCoverage,
        palette: shouldRemove || opaqueCoverage >= 0.58
            ? paletteClusters.map(cluster => ({
                r: Math.round(cluster.r),
                g: Math.round(cluster.g),
                b: Math.round(cluster.b),
            }))
            : [],
    };
    analysis.checkerboard = analyzeCheckerboardBackground(data, samples, width, height, edgeBand);
    if (analysis.hasExistingTransparency) {
        analysis.checkerboard.shouldRemove = false;
    }
    return analysis;
}

function analyzeCheckerboardBackground(data, samples, width, height, edgeBand) {
    const backgroundSamples = samples.filter(sample => sample.saturation <= 42 && sample.luma >= 190);
    if (backgroundSamples.length < Math.max(80, samples.length * 0.38)) {
        return emptyCheckerboardAnalysis();
    }

    const lumaModel = splitLumaFamilies(backgroundSamples);
    if (!lumaModel || Math.abs(lumaModel.light.luma - lumaModel.dark.luma) < 7) {
        return emptyCheckerboardAnalysis();
    }

    const referenceRow = chooseCheckerboardLine(data, width, height, "row", edgeBand, lumaModel.mid);
    const referenceColumn = chooseCheckerboardLine(data, width, height, "column", edgeBand, lumaModel.mid);
    if (referenceRow < 0 || referenceColumn < 0) {
        return emptyCheckerboardAnalysis();
    }

    const xBits = lineBits(data, width, height, "row", referenceRow, lumaModel.mid);
    const yBits = lineBits(data, width, height, "column", referenceColumn, lumaModel.mid);
    smoothBits(xBits);
    smoothBits(yBits);

    const parityOffset = xBits[referenceColumn] ? 1 : 0;
    const paritySamples = [[], []];
    let matches = 0;
    for (const sample of backgroundSamples) {
        const parity = checkerboardStripeParity(modelForParity(xBits, yBits, parityOffset), sample.x, sample.y);
        paritySamples[parity].push(sample);
        const expectedLight = parity === 1;
        const actualLight = sample.luma > lumaModel.mid;
        if (expectedLight === actualLight) matches++;
    }

    if (paritySamples[0].length === 0 || paritySamples[1].length === 0) {
        return emptyCheckerboardAnalysis();
    }

    const colors = [colorMean(paritySamples[0]), colorMean(paritySamples[1])];
    const rawMatchShare = matches / backgroundSamples.length;
    const parityScore = Math.max(rawMatchShare, 1 - rawMatchShare);
    if (luma(colors[0].r, colors[0].g, colors[0].b) > luma(colors[1].r, colors[1].g, colors[1].b)) {
        invertBits(xBits);
        colors.reverse();
    }

    const model = {
        xBits,
        yBits,
        parityOffset,
        colors,
    };
    const runs = stripeRuns(xBits).concat(stripeRuns(yBits)).filter(run => run >= 8 && run <= 64);
    const tileSize = runs.length > 0 ? Math.round(quantile(runs, 0.5)) : 16;
    const distances = backgroundSamples.map(sample => {
        const color = checkerboardColorAt(model, sample.x, sample.y);
        return Math.sqrt(colorDistanceSquared(sample.r, sample.g, sample.b, color.r, color.g, color.b));
    });
    const noiseTolerance = Math.max(12, quantile(distances, 0.86));
    const confidence = Math.min(1, parityScore * 0.82 + Math.min(1, backgroundSamples.length / Math.max(1, samples.length)) * 0.18);

    return {
        detected: confidence >= 0.72,
        shouldRemove: confidence >= 0.72,
        confidence,
        tileSize,
        xBits,
        yBits,
        parityOffset,
        colors,
        midLuma: lumaModel.mid,
        noiseTolerance,
    };
}

function emptyCheckerboardAnalysis() {
    return {
        detected: false,
        shouldRemove: false,
        confidence: 0,
        tileSize: 0,
        xBits: null,
        yBits: null,
        parityOffset: 0,
        colors: [],
        midLuma: 0,
        noiseTolerance: 0,
    };
}

function splitLumaFamilies(samples) {
    if (samples.length === 0) return null;

    let darkCenter = quantile(samples.map(sample => sample.luma), 0.25);
    let lightCenter = quantile(samples.map(sample => sample.luma), 0.75);
    if (!Number.isFinite(darkCenter) || !Number.isFinite(lightCenter)) return null;

    for (let iteration = 0; iteration < 8; iteration++) {
        const dark = [];
        const light = [];
        for (const sample of samples) {
            if (Math.abs(sample.luma - darkCenter) <= Math.abs(sample.luma - lightCenter)) dark.push(sample);
            else light.push(sample);
        }

        if (dark.length === 0 || light.length === 0) break;
        darkCenter = dark.reduce((sum, sample) => sum + sample.luma, 0) / dark.length;
        lightCenter = light.reduce((sum, sample) => sum + sample.luma, 0) / light.length;
    }

    const mid = (darkCenter + lightCenter) / 2;
    const darkSamples = samples.filter(sample => sample.luma <= mid);
    const lightSamples = samples.filter(sample => sample.luma > mid);
    if (darkSamples.length === 0 || lightSamples.length === 0) return null;

    return {
        mid,
        dark: { luma: darkCenter, samples: darkSamples },
        light: { luma: lightCenter, samples: lightSamples },
    };
}

function chooseCheckerboardLine(data, width, height, axis, edgeBand, midLuma) {
    const limit = axis === "row" ? height : width;
    const candidates = [
        ...Array.from({ length: edgeBand }, (_, index) => index),
        ...Array.from({ length: edgeBand }, (_, index) => limit - edgeBand + index),
    ].filter(value => value >= 0 && value < limit);
    let best = -1;
    let bestScore = 0;

    for (const coordinate of candidates) {
        const bits = lineBits(data, width, height, axis, coordinate, midLuma);
        const quality = checkerLineQuality(data, width, height, axis, coordinate, bits);
        if (quality > bestScore) {
            best = coordinate;
            bestScore = quality;
        }
    }

    return bestScore >= 0.58 ? best : -1;
}

function lineBits(data, width, height, axis, coordinate, midLuma) {
    const length = axis === "row" ? width : height;
    const bits = new Uint8Array(length);
    for (let i = 0; i < length; i++) {
        const x = axis === "row" ? i : coordinate;
        const y = axis === "row" ? coordinate : i;
        const offset = ((y * width) + x) * 4;
        bits[i] = luma(data[offset], data[offset + 1], data[offset + 2]) > midLuma ? 1 : 0;
    }

    return bits;
}

function checkerLineQuality(data, width, height, axis, coordinate, bits) {
    let clean = 0;
    let transitions = 0;
    for (let i = 0; i < bits.length; i++) {
        const x = axis === "row" ? i : coordinate;
        const y = axis === "row" ? coordinate : i;
        const offset = ((y * width) + x) * 4;
        const lightness = luma(data[offset], data[offset + 1], data[offset + 2]);
        const chroma = saturation(data[offset], data[offset + 1], data[offset + 2]);
        if (data[offset + 3] >= 245 && lightness >= 205 && chroma <= 32) clean++;
        if (i > 0 && bits[i] !== bits[i - 1]) transitions++;
    }

    const cleanShare = clean / bits.length;
    const transitionShare = transitions / bits.length;
    return cleanShare * 0.82 + Math.min(1, transitionShare * 8) * 0.18;
}

function smoothBits(bits) {
    const copy = Uint8Array.from(bits);
    for (let i = 2; i < bits.length - 2; i++) {
        bits[i] = copy[i - 2] + copy[i - 1] + copy[i] + copy[i + 1] + copy[i + 2] >= 3 ? 1 : 0;
    }
}

function invertBits(bits) {
    for (let i = 0; i < bits.length; i++) bits[i] = bits[i] ? 0 : 1;
}

function modelForParity(xBits, yBits, parityOffset) {
    return { xBits, yBits, parityOffset };
}

function checkerboardStripeParity(model, x, y) {
    return (model.xBits[x] ^ model.yBits[y] ^ model.parityOffset) & 1;
}

function stripeRuns(bits) {
    const runs = [];
    let start = 0;
    for (let i = 1; i < bits.length; i++) {
        if (bits[i] === bits[i - 1]) continue;
        runs.push(i - start);
        start = i;
    }
    runs.push(bits.length - start);
    return runs;
}

function colorMean(samples) {
    if (samples.length === 0) return { r: 0, g: 0, b: 0 };
    const sum = samples.reduce((total, sample) => {
        total.r += sample.r;
        total.g += sample.g;
        total.b += sample.b;
        return total;
    }, { r: 0, g: 0, b: 0 });
    return {
        r: Math.round(sum.r / samples.length),
        g: Math.round(sum.g / samples.length),
        b: Math.round(sum.b / samples.length),
    };
}

function removeExportBackground(imageData, analysis, tolerance, method) {
    const checkerboard = analysis.checkerboard ?? emptyCheckerboardAnalysis();
    const wantsCheckerboard = method === "checkerboard" || (method === "auto" && checkerboard.shouldRemove);

    if (wantsCheckerboard && checkerboard.detected) {
        const result = checkerboardRemoval(imageData, checkerboard, tolerance);
        if (result.removedPixels > 0 || method === "checkerboard") {
            return {
                removedPixels: result.removedPixels,
                method: "checkerboard",
                detail: result.softenedPixels > 0 ? `${result.softenedPixels.toLocaleString()} soft edge px` : "",
            };
        }
    }

    const edgeResult = edgeColorRemoval(imageData, analysis.palette, tolerance);
    return {
        removedPixels: edgeResult.removedPixels,
        method: edgeResult.removedPixels > 0 ? "edge" : "",
        detail: wantsCheckerboard && !checkerboard.detected ? "checkerboard fallback" : "",
    };
}

function addColorCluster(clusters, r, g, b, tolerance) {
    let closest = null;
    let closestDistance = Number.POSITIVE_INFINITY;
    const threshold = tolerance * tolerance * 3;

    for (const cluster of clusters) {
        const distance = colorDistanceSquared(r, g, b, cluster.r, cluster.g, cluster.b);
        if (distance <= threshold && distance < closestDistance) {
            closest = cluster;
            closestDistance = distance;
        }
    }

    if (!closest) {
        clusters.push({ r, g, b, count: 1 });
        return;
    }

    closest.r = ((closest.r * closest.count) + r) / (closest.count + 1);
    closest.g = ((closest.g * closest.count) + g) / (closest.count + 1);
    closest.b = ((closest.b * closest.count) + b) / (closest.count + 1);
    closest.count++;
}

function checkerboardRemoval(imageData, model, tolerance) {
    const { data, width, height } = imageData;
    const pixelCount = width * height;
    const strictCandidate = new Uint8Array(pixelCount);
    const softCandidate = new Uint8Array(pixelCount);
    const scores = new Float32Array(pixelCount);

    for (let pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++) {
        const offset = pixelIndex * 4;
        const x = pixelIndex % width;
        const y = Math.floor(pixelIndex / width);
        const softScore = checkerboardPixelScore(data, offset, x, y, model, tolerance, 26);
        if (softScore > 0) {
            softCandidate[pixelIndex] = 1;
            const strictScore = checkerboardPixelScore(data, offset, x, y, model, tolerance, 0);
            if (strictScore > 0) {
                strictCandidate[pixelIndex] = 1;
                scores[pixelIndex] = strictScore;
            }
        }
    }

    const mask = checkerboardComponentMask(strictCandidate, scores, width, height, model.tileSize);
    closeConstrainedMask(mask, softCandidate, width, height);
    removeTinyMaskComponents(mask, width, height, Math.max(8, Math.round(model.tileSize * 1.25)));
    const removedPixels = applyAlphaMask(imageData, mask);
    const softenedPixels = softenKnownBackgroundBoundary(imageData, mask, (x, y) => checkerboardColorAt(model, x, y), tolerance);
    return { removedPixels, softenedPixels };
}

function checkerboardPixelScore(data, offset, x, y, model, tolerance, extraTolerance) {
    const alpha = data[offset + 3];
    if (alpha < 8) return 1;

    const predicted = checkerboardColorAt(model, x, y);
    const baseTolerance = checkerboardTolerance(model, tolerance) + extraTolerance;
    const colorThreshold = baseTolerance * baseTolerance * 3;
    const distance = colorDistanceSquared(data[offset], data[offset + 1], data[offset + 2], predicted.r, predicted.g, predicted.b);
    if (distance > colorThreshold) return 0;

    const pixelLuma = luma(data[offset], data[offset + 1], data[offset + 2]);
    const predictedLuma = luma(predicted.r, predicted.g, predicted.b);
    if (Math.abs(pixelLuma - predictedLuma) > baseTolerance * 1.45) return 0;

    const pixelSaturation = saturation(data[offset], data[offset + 1], data[offset + 2]);
    const predictedSaturation = saturation(predicted.r, predicted.g, predicted.b);
    if (pixelSaturation > Math.max(52, predictedSaturation + 42 + extraTolerance)) return 0;

    return Math.max(0.01, 1 - Math.sqrt(distance) / Math.max(1, Math.sqrt(colorThreshold)));
}

function checkerboardTolerance(model, tolerance) {
    return Math.max(16, Math.min(96, model.noiseTolerance + tolerance * 0.78));
}

function checkerboardColorAt(model, x, y) {
    return model.colors[checkerboardStripeParity(model, x, y)] ?? model.colors[0];
}

function checkerboardComponentMask(candidate, scores, width, height, tileSize) {
    const pixelCount = width * height;
    const mask = new Uint8Array(pixelCount);
    const visited = new Uint8Array(pixelCount);
    const queue = new Int32Array(pixelCount);
    const minimumInteriorArea = Math.max(96, Math.round(tileSize * tileSize * 0.45));

    for (let start = 0; start < pixelCount; start++) {
        if (!candidate[start] || visited[start]) continue;

        let head = 0;
        let tail = 0;
        let touchesEdge = false;
        let scoreSum = 0;
        visited[start] = 1;
        queue[tail++] = start;

        while (head < tail) {
            const pixelIndex = queue[head++];
            const x = pixelIndex % width;
            const y = Math.floor(pixelIndex / width);
            if (x === 0 || y === 0 || x === width - 1 || y === height - 1) touchesEdge = true;
            scoreSum += scores[pixelIndex];

            const add = (next) => {
                if (next < 0 || next >= pixelCount || visited[next] || !candidate[next]) return;
                const nextX = next % width;
                if (Math.abs(nextX - x) > 1) return;
                visited[next] = 1;
                queue[tail++] = next;
            };

            if (x > 0) add(pixelIndex - 1);
            if (x < width - 1) add(pixelIndex + 1);
            if (pixelIndex >= width) add(pixelIndex - width);
            if (pixelIndex < pixelCount - width) add(pixelIndex + width);
        }

        const averageScore = scoreSum / Math.max(1, tail);
        const remove = touchesEdge || (tail >= minimumInteriorArea && averageScore >= 0.28);
        if (!remove) continue;
        for (let i = 0; i < tail; i++) mask[queue[i]] = 1;
    }

    return mask;
}

function closeConstrainedMask(mask, constraint, width, height) {
    const additions = [];
    for (let y = 1; y < height - 1; y++) {
        let pixelIndex = y * width + 1;
        for (let x = 1; x < width - 1; x++, pixelIndex++) {
            if (mask[pixelIndex] || !constraint[pixelIndex]) continue;
            const neighbors =
                mask[pixelIndex - 1] + mask[pixelIndex + 1] +
                mask[pixelIndex - width] + mask[pixelIndex + width] +
                mask[pixelIndex - width - 1] + mask[pixelIndex - width + 1] +
                mask[pixelIndex + width - 1] + mask[pixelIndex + width + 1];
            if (neighbors >= 5) additions.push(pixelIndex);
        }
    }

    for (const pixelIndex of additions) {
        mask[pixelIndex] = 1;
    }
}

function removeTinyMaskComponents(mask, width, height, minimumSize) {
    const pixelCount = width * height;
    const visited = new Uint8Array(pixelCount);
    const queue = new Int32Array(pixelCount);

    for (let start = 0; start < pixelCount; start++) {
        if (!mask[start] || visited[start]) continue;

        let head = 0;
        let tail = 0;
        visited[start] = 1;
        queue[tail++] = start;

        while (head < tail) {
            const pixelIndex = queue[head++];
            const x = pixelIndex % width;
            const add = (next) => {
                if (next < 0 || next >= pixelCount || visited[next] || !mask[next]) return;
                visited[next] = 1;
                queue[tail++] = next;
            };

            if (x > 0) add(pixelIndex - 1);
            if (x < width - 1) add(pixelIndex + 1);
            if (pixelIndex >= width) add(pixelIndex - width);
            if (pixelIndex < pixelCount - width) add(pixelIndex + width);
        }

        if (tail >= minimumSize) continue;
        for (let i = 0; i < tail; i++) {
            mask[queue[i]] = 0;
        }
    }
}

function applyAlphaMask(imageData, mask) {
    const { data } = imageData;
    let removedPixels = 0;
    for (let pixelIndex = 0; pixelIndex < mask.length; pixelIndex++) {
        if (!mask[pixelIndex]) continue;
        const alphaOffset = pixelIndex * 4 + 3;
        if (data[alphaOffset] !== 0) {
            data[alphaOffset] = 0;
            removedPixels++;
        }
    }

    return removedPixels;
}

function softenKnownBackgroundBoundary(imageData, mask, colorAt, tolerance) {
    const { data, width, height } = imageData;
    let softenedPixels = 0;
    const maxDistance = Math.max(42, tolerance + 36);

    for (let y = 0; y < height; y++) {
        let pixelIndex = y * width;
        for (let x = 0; x < width; x++, pixelIndex++) {
            if (mask[pixelIndex]) continue;
            const alphaOffset = pixelIndex * 4 + 3;
            if (data[alphaOffset] <= 0) continue;
            if (!hasNearbyMask(mask, width, height, x, y, 2)) continue;
            const offset = pixelIndex * 4;
            const background = colorAt(x, y);
            const distance = Math.sqrt(colorDistanceSquared(data[offset], data[offset + 1], data[offset + 2], background.r, background.g, background.b));
            if (distance > maxDistance || luma(data[offset], data[offset + 1], data[offset + 2]) < 150) continue;

            const targetAlpha = Math.max(48, Math.min(232, Math.round(255 * (distance / maxDistance))));
            if (data[alphaOffset] > targetAlpha) {
                decontaminatePixel(data, offset, background, targetAlpha / 255);
                data[alphaOffset] = targetAlpha;
                softenedPixels++;
            }
        }
    }

    return softenedPixels;
}

function decontaminatePixel(data, offset, background, alpha) {
    const safeAlpha = Math.max(0.08, Math.min(1, alpha));
    data[offset] = clamp(Math.round((data[offset] - background.r * (1 - safeAlpha)) / safeAlpha), 0, 255);
    data[offset + 1] = clamp(Math.round((data[offset + 1] - background.g * (1 - safeAlpha)) / safeAlpha), 0, 255);
    data[offset + 2] = clamp(Math.round((data[offset + 2] - background.b * (1 - safeAlpha)) / safeAlpha), 0, 255);
}

function hasNearbyMask(mask, width, height, x, y, radius) {
    const minY = Math.max(0, y - radius);
    const maxY = Math.min(height - 1, y + radius);
    const minX = Math.max(0, x - radius);
    const maxX = Math.min(width - 1, x + radius);

    for (let ny = minY; ny <= maxY; ny++) {
        let pixelIndex = ny * width + minX;
        for (let nx = minX; nx <= maxX; nx++, pixelIndex++) {
            if (mask[pixelIndex]) return true;
        }
    }

    return false;
}

function edgeColorRemoval(imageData, palette, tolerance) {
    if (!palette.length) return { removedPixels: 0, softenedPixels: 0 };

    const { data, width, height } = imageData;
    const pixelCount = width * height;
    const visited = new Uint8Array(pixelCount);
    const mask = new Uint8Array(pixelCount);
    const queue = new Int32Array(pixelCount);
    let head = 0;
    let tail = 0;

    const enqueue = (pixelIndex) => {
        if (visited[pixelIndex]) return;
        const offset = pixelIndex * 4;
        if (!isEdgeBackgroundPixel(data, offset, palette, tolerance)) return;
        visited[pixelIndex] = 1;
        queue[tail++] = pixelIndex;
    };

    for (let x = 0; x < width; x++) {
        enqueue(x);
        enqueue((height - 1) * width + x);
    }
    for (let y = 1; y < height - 1; y++) {
        enqueue(y * width);
        enqueue(y * width + width - 1);
    }

    while (head < tail) {
        const pixelIndex = queue[head++];
        mask[pixelIndex] = 1;

        const x = pixelIndex % width;
        if (x > 0) enqueue(pixelIndex - 1);
        if (x < width - 1) enqueue(pixelIndex + 1);
        if (pixelIndex >= width) enqueue(pixelIndex - width);
        if (pixelIndex < pixelCount - width) enqueue(pixelIndex + width);
    }

    const removedPixels = applyAlphaMask(imageData, mask);
    const softenedPixels = softenKnownBackgroundBoundary(
        imageData,
        mask,
        (x, y) => nearestPaletteColor(data, ((y * width) + x) * 4, palette),
        tolerance);
    return { removedPixels, softenedPixels };
}

function isEdgeBackgroundPixel(data, offset, palette, tolerance) {
    const alpha = data[offset + 3];
    if (alpha < 8) return true;

    const threshold = tolerance * tolerance * 3;
    const r = data[offset];
    const g = data[offset + 1];
    const b = data[offset + 2];
    return palette.some(color => colorDistanceSquared(r, g, b, color.r, color.g, color.b) <= threshold);
}

function nearestPaletteColor(data, offset, palette) {
    let closest = palette[0];
    let closestDistance = Number.POSITIVE_INFINITY;
    for (const color of palette) {
        const distance = colorDistanceSquared(data[offset], data[offset + 1], data[offset + 2], color.r, color.g, color.b);
        if (distance < closestDistance) {
            closest = color;
            closestDistance = distance;
        }
    }

    return closest;
}

const keyBackgroundColor = { r: 255, g: 0, b: 255 };

function analyzeKeyColorBackground(data, width, height) {
    const edgeBand = Math.max(2, Math.min(36, Math.floor(Math.min(width, height) * 0.025)));
    let edgeSamples = 0;
    let edgeCandidates = 0;
    let candidatePixels = 0;

    for (let pixelIndex = 0; pixelIndex < width * height; pixelIndex++) {
        const offset = pixelIndex * 4;
        if (isKeyColorCandidate(data, offset, 20)) {
            candidatePixels++;
        }
    }

    const sample = (x, y) => {
        const offset = ((y * width) + x) * 4;
        if (data[offset + 3] < 8) return;
        edgeSamples++;
        if (isKeyColorCandidate(data, offset, 20)) {
            edgeCandidates++;
        }
    };

    for (let y = 0; y < edgeBand; y++) {
        for (let x = 0; x < width; x++) {
            sample(x, y);
            sample(x, height - 1 - y);
        }
    }

    for (let y = edgeBand; y < height - edgeBand; y++) {
        for (let x = 0; x < edgeBand; x++) {
            sample(x, y);
            sample(width - 1 - x, y);
        }
    }

    const edgeShare = edgeSamples > 0 ? edgeCandidates / edgeSamples : 0;
    const imageShare = candidatePixels / Math.max(1, width * height);
    return {
        detected: edgeShare >= 0.08 || (edgeShare >= 0.035 && imageShare >= 0.025),
        candidatePixels,
        edgeCandidates,
        edgeShare,
    };
}

function removeKeyColorBackground(imageData) {
    const { data, width, height } = imageData;
    const pixelCount = width * height;
    const mask = new Uint8Array(pixelCount);

    for (let pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++) {
        const offset = pixelIndex * 4;
        if (isKeyColorCandidate(data, offset, 0)) {
            mask[pixelIndex] = 1;
        }
    }

    removeTinyMaskComponents(mask, width, height, 4);
    const removedPixels = applyAlphaMask(imageData, mask);
    const softenedPixels = softenKeyColorBoundary(imageData, mask);
    return { removedPixels, softenedPixels };
}

function isKeyColorCandidate(data, offset, extraTolerance) {
    const alpha = data[offset + 3];
    if (alpha < 8) return false;

    const r = data[offset];
    const g = data[offset + 1];
    const b = data[offset + 2];
    const distance = Math.sqrt(colorDistanceSquared(
        r,
        g,
        b,
        keyBackgroundColor.r,
        keyBackgroundColor.g,
        keyBackgroundColor.b));
    const tolerance = 108 + extraTolerance;
    const magentaDominance = Math.min(r, b) - g;
    return distance <= tolerance
        && r >= 150 - extraTolerance
        && b >= 150 - extraTolerance
        && g <= 125 + extraTolerance
        && magentaDominance >= 55 - extraTolerance;
}

function softenKeyColorBoundary(imageData, mask) {
    const { data, width, height } = imageData;
    let softenedPixels = 0;
    const maxDistance = 164;

    for (let y = 0; y < height; y++) {
        let pixelIndex = y * width;
        for (let x = 0; x < width; x++, pixelIndex++) {
            if (mask[pixelIndex]) continue;
            const alphaOffset = pixelIndex * 4 + 3;
            if (data[alphaOffset] <= 0) continue;
            if (!hasNearbyMask(mask, width, height, x, y, 2)) continue;

            const offset = pixelIndex * 4;
            const r = data[offset];
            const g = data[offset + 1];
            const b = data[offset + 2];
            const magentaDominance = Math.min(r, b) - g;
            if (magentaDominance < 24 || r < 110 || b < 110 || g > 170) continue;

            const distance = Math.sqrt(colorDistanceSquared(r, g, b, keyBackgroundColor.r, keyBackgroundColor.g, keyBackgroundColor.b));
            if (distance > maxDistance) continue;

            const targetAlpha = Math.max(24, Math.min(230, Math.round(255 * (distance / maxDistance))));
            if (data[alphaOffset] > targetAlpha) {
                decontaminatePixel(data, offset, keyBackgroundColor, targetAlpha / 255);
                data[alphaOffset] = targetAlpha;
                softenedPixels++;
            }
        }
    }

    return softenedPixels;
}

function alphaStats(data) {
    let transparent = 0;
    let semiTransparent = 0;
    let opaque = 0;
    for (let offset = 3; offset < data.length; offset += 4) {
        const alpha = data[offset];
        if (alpha === 0) transparent++;
        else if (alpha === 255) opaque++;
        else semiTransparent++;
    }

    return { transparent, semiTransparent, opaque };
}

function colorDistanceSquared(r1, g1, b1, r2, g2, b2) {
    const dr = r1 - r2;
    const dg = g1 - g2;
    const db = b1 - b2;
    return dr * dr + dg * dg + db * db;
}

function exportPreviewMessage(removeBackground, analysis, result, stats, keyResult) {
    const keySuffix = keyResult?.removedPixels > 0
        ? ` Key color cleanup removed ${keyResult.removedPixels.toLocaleString()} magenta px.`
        : "";

    if (removeBackground) {
        if (result.removedPixels > 0) {
            const transparent = stats.transparent.toLocaleString();
            const soft = stats.semiTransparent > 0
                ? `, ${stats.semiTransparent.toLocaleString()} soft edge px`
                : "";
            return result.method === "checkerboard"
                ? `Patterned checkerboard removed: ${transparent} transparent px${soft}.${keySuffix}`
                : `Edge-color background removed: ${transparent} transparent px${soft}.${keySuffix}`;
        }

        if (keySuffix) {
            return `Key color cleanup removed ${keyResult.removedPixels.toLocaleString()} magenta px.`;
        }

        if (result.detail === "checkerboard fallback") {
            return "No stable checkerboard grid found; edge-color cleanup found nothing removable.";
        }

        return analysis.checkerboard.detected
            ? "Checkerboard detected, but no removable connected background pixels matched."
            : "No removable edge background detected.";
    }

    if (analysis.hasExistingTransparency) {
        return "Existing transparency preserved.";
    }

    if (analysis.checkerboard.shouldRemove) {
        return "Patterned checkerboard background detected.";
    }

    return analysis.shouldRemove ? "Background detected." : "PNG preview ready.";
}

function mergeExportMethods(primary, secondary) {
    const methods = [primary, secondary].filter(value => value && String(value).trim().length > 0);
    return methods.join("+");
}

function normalizeRemovalMethod(value) {
    const normalized = String(value ?? "").trim().toLowerCase();
    if (normalized === "checkerboard" || normalized === "patternedcheckerboard" || normalized === "patterned-checkerboard") {
        return "checkerboard";
    }

    if (normalized === "edge" || normalized === "edgecolors" || normalized === "edge-colors") {
        return "edge";
    }

    return "auto";
}

function luma(r, g, b) {
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function saturation(r, g, b) {
    return Math.max(r, g, b) - Math.min(r, g, b);
}

function quantile(values, amount) {
    if (!values.length) return 0;
    const sorted = [...values].sort((left, right) => left - right);
    const index = clamp((sorted.length - 1) * amount, 0, sorted.length - 1);
    const lower = Math.floor(index);
    const upper = Math.ceil(index);
    if (lower === upper) return sorted[lower];
    const ratio = index - lower;
    return sorted[lower] * (1 - ratio) + sorted[upper] * ratio;
}

function normalizeTolerance(value) {
    const number = Number(value);
    if (!Number.isFinite(number)) return 24;
    return Math.max(0, Math.min(80, Math.round(number)));
}

function normalizeExportFileName(value) {
    const fallback = "asset.png";
    if (!value) return fallback;

    const cleaned = String(value).trim().replace(/[\\/:*?"<>|]+/g, "-");
    const withoutExtension = cleaned.replace(/\.[^.]*$/, "");
    return `${withoutExtension || "asset"}.png`;
}

function normalizeJsonFileName(value) {
    const fallback = "sprite-sheet.sprite.json";
    if (!value) return fallback;

    const cleaned = String(value).trim().replace(/[\\/:*?"<>|]+/g, "-");
    const withoutExtension = cleaned
        .replace(/\.sprite\.json$/i, "")
        .replace(/\.json$/i, "")
        .replace(/\.[^.]*$/, "");
    return `${withoutExtension || "sprite-sheet"}.sprite.json`;
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
