const editors = new WeakMap();
const MAX_ZOOM = 128;
const MIN_ZOOM = .001;

export async function attach(canvas, dotnet, options) {
    let s = editors.get(canvas);
    if (!s) {
        const viewport = canvas.closest('.canvas-viewport');
        const pane = viewport.closest('.canvas-pane');
        s = { canvas, viewport, pane, dotnet, options, stroke: null, pan: null, polygon: [], disposed: false, generation: 0, scale: 1, x: 0, y: 0, fitMode: true, drawRequest: null, imageKey: '', listeners: [] };
        editors.set(canvas, s);
        const listen = (target, event, handler, settings) => { target.addEventListener(event, handler, settings); s.listeners.push([target, event, handler, settings]); };
        listen(viewport, 'pointerdown', e => down(s, e));
        listen(viewport, 'pointermove', e => move(s, e));
        listen(viewport, 'pointerup', e => up(s, e));
        listen(viewport, 'pointercancel', e => up(s, e));
        listen(viewport, 'lostpointercapture', () => { s.pan = null; s.stroke = null; viewport.classList.remove('panning'); queueDraw(s); });
        listen(viewport, 'contextmenu', e => { if (!e.target.closest('.playback-overlay')) e.preventDefault(); });
        listen(viewport, 'wheel', e => {
            if (e.target.closest('.playback-overlay')) return;
            e.preventDefault();
            if (s.stroke || s.pan) return;
            const r = viewport.getBoundingClientRect();
            const delta = e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? viewport.clientHeight : 1);
            zoom(s, s.scale * Math.exp(-Math.max(-600, Math.min(600, delta)) * .0025), e.clientX - r.left, e.clientY - r.top);
        }, { passive: false });
        listen(viewport, 'keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); finishPolygon(s); }
            if (e.key === 'Escape') { s.polygon = []; s.stroke = null; s.pan = null; viewport.classList.remove('panning'); queueDraw(s); }
        });
        listen(canvas, 'dblclick', () => { if (s.options.tool === 'polygon') finishPolygon(s); });
        for (const button of pane.querySelectorAll('[data-view-action]')) listen(button, 'click', () => {
            if (s.stroke || s.pan) return;
            const action = button.dataset.viewAction;
            if (action === 'fit') fit(s);
            else zoom(s, action === 'actual' ? 1 : s.scale * (action === 'in' ? 1.25 : .8));
        });
        s.zoomInput = pane.querySelector('[data-zoom-input]');
        const enterZoom = () => { const value = Number(s.zoomInput.value.replace('%', '').trim()); if (Number.isFinite(value) && value > 0) zoom(s, value / 100); else transform(s); };
        listen(s.zoomInput, 'change', enterZoom);
        listen(s.zoomInput, 'keydown', e => { if (e.key === 'Enter') { enterZoom(); s.zoomInput.blur(); } });
        s.observer = new ResizeObserver(() => {
            if (viewport.clientWidth < 1 || viewport.clientHeight < 1) return;
            if (s.fitMode) fit(s);
            else {
                s.x += (viewport.clientWidth - (s.viewWidth ?? viewport.clientWidth)) / 2;
                s.y += (viewport.clientHeight - (s.viewHeight ?? viewport.clientHeight)) / 2;
                transform(s);
            }
            s.viewWidth = viewport.clientWidth; s.viewHeight = viewport.clientHeight;
        });
        s.observer.observe(viewport);
    }
    const geometryChanged = s.options.width !== options.width || s.options.height !== options.height;
    const frameChanged = s.options.frameId !== options.frameId;
    if (frameChanged) { s.stroke = null; s.polygon = []; }
    s.options = options; s.dotnet = dotnet;
    if (geometryChanged) { s.fitMode = true; fit(s); }
    const urls = [options.url, ...options.onions];
    const imageKey = JSON.stringify(urls);
    // Tool/color/busy changes must not decode a large image again or reset navigation.
    if (s.imageKey === imageKey && s.images) { queueDraw(s); return; }
    const generation = ++s.generation;
    const images = await Promise.all(urls.map(async url => { const image = new Image(); image.src = url; await image.decode(); return image; }));
    if (s.disposed || generation !== s.generation) return;
    s.imageKey = imageKey; s.images = images;
    canvas.width = options.width; canvas.height = options.height;
    canvas.style.width = `${options.width}px`; canvas.style.height = `${options.height}px`;
    if (s.fitMode) fit(s); else transform(s);
    draw(s);
    canvas.dataset.renderRevision = String(options.revision);
}

function fit(s) {
    const width = s.viewport.clientWidth, height = s.viewport.clientHeight;
    if (width < 1 || height < 1) return;
    const padding = Math.min(32, width / 8, height / 8);
    s.scale = Math.min(MAX_ZOOM, (width - 2 * padding) / s.options.width, (height - 2 * padding) / s.options.height);
    s.x = (width - s.options.width * s.scale) / 2;
    s.y = (height - s.options.height * s.scale) / 2;
    s.fitMode = true; transform(s);
}
function zoom(s, value, x = s.viewport.clientWidth / 2, y = s.viewport.clientHeight / 2) {
    const scale = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, value));
    s.x = x - (x - s.x) * scale / s.scale;
    s.y = y - (y - s.y) * scale / s.scale;
    s.scale = scale; s.fitMode = false; transform(s);
}
function transform(s) {
    const w = s.viewport.clientWidth, h = s.viewport.clientHeight;
    // Retain a visible sliver while allowing artwork edges to reach the viewport center.
    const visibleX = Math.min(24, s.options.width * s.scale, w / 2);
    const visibleY = Math.min(24, s.options.height * s.scale, h / 2);
    s.x = Math.max(visibleX - s.options.width * s.scale, Math.min(w - visibleX, s.x));
    s.y = Math.max(visibleY - s.options.height * s.scale, Math.min(h - visibleY, s.y));
    s.canvas.style.imageRendering = s.options.artMode === 'pixel' || s.scale >= 1 ? 'pixelated' : 'auto';
    s.canvas.style.transform = `translate(${s.x}px, ${s.y}px) scale(${s.scale})`;
    s.canvas.style.backgroundSize = `${16 / s.scale}px ${16 / s.scale}px`;
    s.viewport.dataset.zoom = String(s.scale);
    s.viewport.dataset.panX = String(s.x); s.viewport.dataset.panY = String(s.y);
    s.viewport.dataset.fit = String(s.fitMode);
    if (s.zoomInput) s.zoomInput.value = Number((s.scale * 100).toFixed(2)).toString();
}
function queueDraw(s) {
    if (s.drawRequest !== null || s.disposed) return;
    s.drawRequest = requestAnimationFrame(() => { s.drawRequest = null; if (!s.disposed) draw(s); });
}
function draw(s) {
    const c = s.canvas.getContext('2d'); c.clearRect(0, 0, s.canvas.width, s.canvas.height); c.imageSmoothingEnabled = false;
    if (!s.images) return;
    c.globalAlpha = .22; for (const image of s.images.slice(1)) c.drawImage(image, 0, 0); c.globalAlpha = 1; c.drawImage(s.images[0], 0, 0);
    const selection = s.options.selection;
    if (selection?.frameId === s.options.frameId && selection.polygon?.length) polygon(c, selection.polygon, '#ffde77');
    if (s.polygon.length) polygon(c, s.polygon, '#ffde77');
    c.strokeStyle = '#72dce8'; c.lineWidth = 1;
    for (const p of Object.values(s.options.pivots || {})) { c.beginPath(); c.moveTo(p.x - 3, p.y); c.lineTo(p.x + 3, p.y); c.moveTo(p.x, p.y - 3); c.lineTo(p.x, p.y + 3); c.stroke(); }
    if (s.stroke) {
        const { points, options: o } = s.stroke; const a = points[0], b = points.at(-1);
        c.strokeStyle = o.tool === 'erase' ? '#ffb0bd' : o.color; c.fillStyle = o.color; c.lineWidth = o.size;
        c.beginPath();
        if (['select', 'rectangle', 'ellipse'].includes(o.tool)) {
            const x = Math.min(a.x,b.x), y = Math.min(a.y,b.y), w = Math.abs(b.x-a.x)+1, h = Math.abs(b.y-a.y)+1;
            if (o.tool === 'select') { c.strokeStyle = '#ffde77'; c.lineWidth = 1; }
            if (o.tool === 'ellipse') c.ellipse(x+w/2,y+h/2,w/2,h/2,0,0,2*Math.PI); else c.rect(x,y,w,h);
            if (o.filled && o.tool !== 'select') c.fill(); else c.stroke();
        } else if (['pencil','brush','erase','line'].includes(o.tool)) {
            const path = o.tool === 'line' ? [a,b] : points;
            if (path.length === 1) c.fillRect(a.x,a.y,o.size,o.size);
            else { path.forEach((p,i) => i ? c.lineTo(p.x+.5,p.y+.5) : c.moveTo(p.x+.5,p.y+.5)); c.stroke(); }
        }
    }
}
function polygon(c, points, color) { c.strokeStyle = color; c.lineWidth = .5; c.beginPath(); points.forEach((p, i) => i ? c.lineTo(p.x, p.y) : c.moveTo(p.x, p.y)); c.closePath(); c.stroke(); }
export function coordinates(canvas, clientX, clientY) {
    const r = canvas.getBoundingClientRect();
    return { x: Math.floor((clientX - r.left) * canvas.width / r.width), y: Math.floor((clientY - r.top) * canvas.height / r.height) };
}
function colorAt(s, p) {
    const c = document.createElement('canvas'); c.width = 1; c.height = 1;
    const ctx = c.getContext('2d'); ctx.drawImage(s.images[0], p.x, p.y, 1, 1, 0, 0, 1, 1);
    return '#' + [...ctx.getImageData(0, 0, 1, 1).data].map(n => n.toString(16).padStart(2, '0')).join('');
}
function down(s, e) {
    if (e.target.closest('.playback-overlay') || s.pan || s.stroke) return;
    if (e.button === 2 || e.button === 1) {
        e.preventDefault(); s.viewport.focus({ preventScroll: true });
        s.pan = { pointerId: e.pointerId, x: e.clientX, y: e.clientY, startX: s.x, startY: s.y };
        s.viewport.setPointerCapture(e.pointerId); s.viewport.classList.add('panning'); s.fitMode = false; return;
    }
    if (s.options.busy || !s.images || e.button !== 0 || e.target !== s.canvas) return;
    e.preventDefault(); s.canvas.focus({ preventScroll: true }); const p = coordinates(s.canvas, e.clientX, e.clientY);
    if (p.x < 0 || p.y < 0 || p.x >= s.options.width || p.y >= s.options.height) return;
    if (s.options.tool === 'polygon') { s.polygon.push(p); queueDraw(s); return; }
    if (s.options.tool === 'eyedropper') { s.dotnet.invokeMethodAsync('PickColor', colorAt(s, p)); return; }
    s.viewport.setPointerCapture(e.pointerId);
    s.stroke = { pointerId: e.pointerId, points: [p], options: { ...s.options }, color: s.options.tool === 'selectColor' ? colorAt(s, p) : s.options.color };
    queueDraw(s);
}
function move(s, e) {
    if (s.pan) {
        if (e.pointerId !== s.pan.pointerId) return;
        s.x = s.pan.startX + e.clientX - s.pan.x; s.y = s.pan.startY + e.clientY - s.pan.y;
        transform(s); return;
    }
    const p = coordinates(s.canvas, e.clientX, e.clientY);
    const label = s.pane.querySelector('[data-pointer-status]');
    if (label) label.textContent = p.x >= 0 && p.y >= 0 && p.x < s.options.width && p.y < s.options.height ? `${p.x}, ${p.y}` : '—';
    if (!s.stroke || s.stroke.pointerId !== e.pointerId) return;
    const points = s.stroke.points; const last = points.at(-1);
    if (p.x === last.x && p.y === last.y || points.length >= 10000) return;
    points.push(p); queueDraw(s);
}
function release(s, e) { if (s.viewport.hasPointerCapture(e.pointerId)) s.viewport.releasePointerCapture(e.pointerId); }
async function up(s, e) {
    if (s.pan) { s.pan = null; s.viewport.classList.remove('panning'); release(s, e); return; }
    if (!s.stroke || e.pointerId !== s.stroke.pointerId) return;
    const stroke = s.stroke; s.stroke = null;
    release(s, e);
    if (e.type === 'pointercancel') { draw(s); return; }
    const o = stroke.options; const a = stroke.points[0]; const b = coordinates(s.canvas, e.clientX, e.clientY);
    const command = { op: o.tool, frameId: o.frameId, layerId: o.layerId, color: o.color, size: o.size, filled: o.filled };
    if (['pencil', 'brush', 'erase'].includes(o.tool)) command.points = [...stroke.points, b];
    else if (o.tool === 'line') Object.assign(command, { x: a.x, y: a.y, x2: b.x, y2: b.y });
    else if (['rectangle', 'ellipse', 'select'].includes(o.tool)) Object.assign(command, { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(b.x - a.x) + 1, height: Math.abs(b.y - a.y) + 1 });
    else if (o.tool === 'selectColor') Object.assign(command, { op: 'select', x: 0, y: 0, width: o.width, height: o.height, color: stroke.color });
    else if (o.tool === 'pivot') Object.assign(command, { op: 'setPivot', name: 'root', x: b.x, y: b.y });
    else Object.assign(command, { x: b.x, y: b.y });
    await s.dotnet.invokeMethodAsync('SetPointer', b.x, b.y);
    await s.dotnet.invokeMethodAsync('CanvasCommand', command, o.revision);
}
async function finishPolygon(s) {
    if (s.polygon.length < 3 || s.options.busy) return;
    const points = s.polygon; s.polygon = [];
    await s.dotnet.invokeMethodAsync('CanvasCommand', { op: 'select', frameId: s.options.frameId, polygon: points }, s.options.revision);
}

export function dispose(canvas) {
    const s = editors.get(canvas); if (!s) return;
    s.disposed = true; s.observer.disconnect();
    if (s.drawRequest !== null) cancelAnimationFrame(s.drawRequest);
    for (const [target, event, handler, settings] of s.listeners) target.removeEventListener(event, handler, settings);
    editors.delete(canvas);
}
