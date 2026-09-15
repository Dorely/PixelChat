const mounted = new WeakMap();

export function analyzeRgba(data) {
    let transparent = 0, partial = 0;
    const total = data.length / 4;
    for (let i = 3; i < data.length; i += 4) {
        if (data[i] === 0) transparent++;
        else if (data[i] < 255) partial++;
    }
    return { total, transparent, partial, opaque: total - transparent - partial };
}

export function inspectPixel(data, width, height, x, y) {
    x = Math.max(0, Math.min(width - 1, Math.floor(x)));
    y = Math.max(0, Math.min(height - 1, Math.floor(y)));
    const i = (y * width + x) * 4;
    return { x, y, r: data[i], g: data[i + 1], b: data[i + 2], a: data[i + 3] };
}

export function unmount(root) {
    const previous = mounted.get(root);
    if (previous) previous.abort();
    mounted.delete(root);
}

export async function mount(root, src) {
    unmount(root);
    const controller = new AbortController();
    mounted.set(root, controller);
    const image = root.querySelector('[data-alpha-image]');
    const status = root.querySelector('[data-alpha-status]');
    status.textContent = 'Inspecting image pixels…';
    root.querySelector('[data-alpha-pixel]').textContent = 'Pixel: — · RGBA: — · Opacity: —';
    image.src = src;
    try {
        await image.decode();
        if (controller.signal.aborted) return;
        // This canvas has no preview background: it contains decoded image pixels only.
        const canvas = document.createElement('canvas');
        canvas.width = image.naturalWidth;
        canvas.height = image.naturalHeight;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        const alpha = analyzeRgba(pixels);
        const percent = count => (100 * count / alpha.total).toFixed(3);
        status.textContent = `${canvas.width} × ${canvas.height} · Fully transparent ${percent(alpha.transparent)}% · Partially transparent ${percent(alpha.partial)}% · Opaque ${percent(alpha.opaque)}%`;
        const inspect = event => {
            const bounds = image.getBoundingClientRect();
            const scale = Math.min(bounds.width / canvas.width, bounds.height / canvas.height);
            if (scale <= 0) return;
            const x = (event.clientX - bounds.left - (bounds.width - canvas.width * scale) / 2) / scale;
            const y = (event.clientY - bounds.top - (bounds.height - canvas.height * scale) / 2) / scale;
            if (x < 0 || y < 0 || x >= canvas.width || y >= canvas.height) return;
            const pixel = inspectPixel(pixels, canvas.width, canvas.height, x, y);
            root.querySelector('[data-alpha-pixel]').textContent =
                `Pixel (${pixel.x}, ${pixel.y}) · RGBA (${pixel.r}, ${pixel.g}, ${pixel.b}, ${pixel.a}) · Opacity ${(100 * pixel.a / 255).toFixed(2)}%`;
        };
        image.addEventListener('pointermove', inspect, { signal: controller.signal });
        image.addEventListener('pointerdown', inspect, { signal: controller.signal });
    } catch (error) {
        if (!controller.signal.aborted) status.textContent = `Alpha could not be inspected: ${error.message}`;
    }
}
