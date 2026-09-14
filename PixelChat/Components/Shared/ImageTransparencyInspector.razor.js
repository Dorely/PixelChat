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

export async function mount(root, src, requestedBackground) {
    unmount(root);
    const controller = new AbortController();
    mounted.set(root, controller);
    const image = root.querySelector('[data-alpha-image]');
    const status = root.querySelector('[data-alpha-status]');
    const warning = root.querySelector('[data-alpha-warning]');
    status.textContent = 'Inspecting image pixels…';
    warning.hidden = true;
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
        status.textContent = alpha.opaque === alpha.total
            ? 'Opaque — no transparent pixels'
            : alpha.transparent === alpha.total
                ? 'Fully transparent — no visible pixels'
                : `Contains transparency — fully transparent ${percent(alpha.transparent)}%; partially transparent ${percent(alpha.partial)}%. This does not prove every background pixel is transparent.`;
        if (requestedBackground === 'transparent' && alpha.opaque === alpha.total) {
            warning.textContent = 'Transparency was requested, but this image is fully opaque. Any checkerboard visible in it is part of the image.';
            warning.hidden = false;
        }
        const inspect = event => {
            const bounds = image.getBoundingClientRect();
            const pixel = inspectPixel(pixels, canvas.width, canvas.height,
                (event.clientX - bounds.left) * canvas.width / bounds.width,
                (event.clientY - bounds.top) * canvas.height / bounds.height);
            root.querySelector('[data-alpha-pixel]').textContent =
                `Pixel (${pixel.x}, ${pixel.y}) · RGBA (${pixel.r}, ${pixel.g}, ${pixel.b}, ${pixel.a}) · Opacity ${(100 * pixel.a / 255).toFixed(2)}%`;
        };
        image.addEventListener('pointermove', inspect, { signal: controller.signal });
        image.addEventListener('pointerdown', inspect, { signal: controller.signal });
    } catch (error) {
        if (!controller.signal.aborted) status.textContent = `Alpha could not be inspected: ${error.message}`;
    }
}
