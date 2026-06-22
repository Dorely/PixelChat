const animations = new WeakMap();

export async function start(canvas, frames, fps, loop) {
    dispose(canvas);

    const context = canvas.getContext("2d");
    if (!context || !Array.isArray(frames) || frames.length === 0) {
        clear(canvas, context);
        return;
    }

    const images = await Promise.all(frames.map(async frame => {
        const image = new Image();
        image.decoding = "async";
        image.src = frame.previewImageUrl ?? frame.PreviewImageUrl ?? "";
        await image.decode().catch(() => new Promise((resolve, reject) => {
            image.onload = resolve;
            image.onerror = reject;
        }));
        return {
            image,
            duration: Number(frame.duration ?? frame.Duration ?? 0)
        };
    }));

    const width = Math.max(1, ...images.map(item => item.image.naturalWidth || item.image.width || 1));
    const height = Math.max(1, ...images.map(item => item.image.naturalHeight || item.image.height || 1));
    canvas.width = width;
    canvas.height = height;

    let index = 0;
    let disposed = false;
    const frameMs = Math.max(16, Math.round(1000 / Math.max(1, Number(fps) || 8)));

    const draw = () => {
        if (disposed) {
            return;
        }

        const item = images[index];
        context.clearRect(0, 0, width, height);
        context.imageSmoothingEnabled = false;
        const imageWidth = item.image.naturalWidth || item.image.width || width;
        const imageHeight = item.image.naturalHeight || item.image.height || height;
        const x = Math.round((width - imageWidth) / 2);
        const y = Math.round((height - imageHeight) / 2);
        context.drawImage(item.image, x, y, imageWidth, imageHeight);

        if (images.length <= 1) {
            return;
        }

        const delay = Math.max(16, Math.round((item.duration || 0) * 1000) || frameMs);
        const handle = window.setTimeout(() => {
            if (disposed) {
                return;
            }
            if (index < images.length - 1) {
                index += 1;
            } else if (loop) {
                index = 0;
            } else {
                return;
            }
            draw();
        }, delay);
        animations.set(canvas, { stop: () => { disposed = true; window.clearTimeout(handle); } });
    };

    animations.set(canvas, { stop: () => { disposed = true; } });
    draw();
}

export function dispose(canvas) {
    const state = animations.get(canvas);
    if (state) {
        state.stop();
        animations.delete(canvas);
    }
}

function clear(canvas, context) {
    if (!context) {
        return;
    }
    canvas.width = 1;
    canvas.height = 1;
    context.clearRect(0, 0, 1, 1);
}
