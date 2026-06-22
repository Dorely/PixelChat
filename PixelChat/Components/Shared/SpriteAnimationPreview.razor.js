const animations = new WeakMap();

export async function start(canvas, frames, fps, loop) {
    dispose(canvas);

    const context = canvas?.getContext?.("2d");
    if (!context || !Array.isArray(frames) || frames.length === 0) {
        clear(canvas, context);
        return;
    }

    const state = {
        canvas,
        context,
        images: [],
        frameIndex: 0,
        frameMs: Math.max(16, Math.round(1000 / Math.max(1, Number(fps) || 8))),
        loop: Boolean(loop),
        timer: null,
        resizeObserver: null,
        resizeHandler: null,
        renderRequest: null,
        disposed: false,
    };

    animations.set(canvas, state);
    state.images = (await Promise.all(frames.map(loadFrame))).filter(Boolean);

    if (state.disposed || animations.get(canvas) !== state) {
        return;
    }

    if (state.images.length === 0) {
        clear(canvas, context);
        dispose(canvas);
        return;
    }

    bindResize(state);
    queueRender(state);
    scheduleNextFrame(state);
}

export function dispose(canvas) {
    const state = animations.get(canvas);
    if (state) {
        stop(state);
        animations.delete(canvas);
    }
}

async function loadFrame(frame) {
    const image = new Image();
    image.decoding = "async";
    image.src = frame.previewImageUrl ?? frame.PreviewImageUrl ?? "";

    try {
        if (typeof image.decode === "function") {
            await image.decode();
        } else {
            await waitForImage(image);
        }
    } catch {
        try {
            await waitForImage(image);
        } catch {
            return null;
        }
    }

    return {
        image,
        duration: Number(frame.duration ?? frame.Duration ?? 0),
    };
}

function waitForImage(image) {
    return new Promise((resolve, reject) => {
        if (image.complete) {
            if (image.naturalWidth || image.width) {
                resolve();
            } else {
                reject();
            }
            return;
        }

        image.onload = resolve;
        image.onerror = reject;
    });
}

function bindResize(state) {
    if (typeof ResizeObserver === "function") {
        state.resizeObserver = new ResizeObserver(() => queueRender(state));
        state.resizeObserver.observe(state.canvas);
        return;
    }

    state.resizeHandler = () => queueRender(state);
    window.addEventListener("resize", state.resizeHandler);
}

function scheduleNextFrame(state) {
    if (state.disposed || state.images.length <= 1) {
        return;
    }

    const item = state.images[state.frameIndex] ?? state.images[0];
    const delay = Math.max(16, Math.round((item.duration || 0) * 1000) || state.frameMs);
    state.timer = window.setTimeout(() => {
        state.timer = null;
        if (state.disposed) {
            return;
        }

        if (state.frameIndex < state.images.length - 1) {
            state.frameIndex += 1;
        } else if (state.loop) {
            state.frameIndex = 0;
        } else {
            return;
        }

        queueRender(state);
        scheduleNextFrame(state);
    }, delay);
}

function queueRender(state) {
    if (state.disposed || state.renderRequest !== null) {
        return;
    }

    state.renderRequest = window.requestAnimationFrame(() => {
        state.renderRequest = null;
        draw(state);
    });
}

function draw(state) {
    if (state.disposed) {
        return;
    }

    const item = state.images[state.frameIndex] ?? state.images[0];
    if (!item) {
        clear(state.canvas, state.context);
        return;
    }

    const { width, height } = resizeCanvas(state.canvas);
    state.context.clearRect(0, 0, width, height);
    state.context.imageSmoothingEnabled = false;

    const imageWidth = item.image.naturalWidth || item.image.width || 1;
    const imageHeight = item.image.naturalHeight || item.image.height || 1;
    const viewport = fitRect(width, height, imageWidth, imageHeight);
    state.context.drawImage(item.image, viewport.x, viewport.y, viewport.w, viewport.h);
}

function resizeCanvas(canvas) {
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(1, Math.round((rect.width || canvas.clientWidth || 1) * dpr));
    const height = Math.max(1, Math.round((rect.height || canvas.clientHeight || 1) * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }

    return { width, height };
}

function fitRect(canvasWidth, canvasHeight, imageWidth, imageHeight) {
    const scale = Math.min(canvasWidth / imageWidth, canvasHeight / imageHeight);
    const w = imageWidth * scale;
    const h = imageHeight * scale;
    return {
        x: Math.round((canvasWidth - w) / 2),
        y: Math.round((canvasHeight - h) / 2),
        w: Math.max(1, Math.round(w)),
        h: Math.max(1, Math.round(h)),
    };
}

function stop(state) {
    state.disposed = true;

    if (state.timer !== null) {
        window.clearTimeout(state.timer);
        state.timer = null;
    }

    if (state.renderRequest !== null) {
        window.cancelAnimationFrame(state.renderRequest);
        state.renderRequest = null;
    }

    if (state.resizeObserver) {
        state.resizeObserver.disconnect();
        state.resizeObserver = null;
    }

    if (state.resizeHandler) {
        window.removeEventListener("resize", state.resizeHandler);
        state.resizeHandler = null;
    }
}

function clear(canvas, context) {
    if (!canvas || !context) {
        return;
    }
    canvas.width = 1;
    canvas.height = 1;
    context.clearRect(0, 0, 1, 1);
}
