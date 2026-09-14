const editors = new WeakMap();
export async function attach(canvas, dotnet, options) {
    let state = editors.get(canvas);
    if (!state) {
        state = { canvas, dotnet, options, stroke: null, polygon: [], disposed: false, generation: 0 };
        editors.set(canvas, state);
        state.down = e => down(state, e); state.move = e => move(state, e); state.up = e => up(state, e);
        state.key = e => { if (e.key === 'Enter') finishPolygon(state); if (e.key === 'Escape') { state.polygon = []; state.stroke = null; draw(state); } };
        state.double = () => finishPolygon(state);
        canvas.addEventListener('pointerdown', state.down); canvas.addEventListener('pointermove', state.move); canvas.addEventListener('pointerup', state.up);
        canvas.addEventListener('pointercancel', state.up); canvas.addEventListener('keydown', state.key); canvas.addEventListener('dblclick', state.double);
    }
    state.options = options; state.dotnet = dotnet;
    const generation = ++state.generation;
    const urls = [options.url, ...options.onions];
    const images = await Promise.all(urls.map(async url => { const image = new Image(); image.src = url; await image.decode(); return image; }));
    if (state.disposed || generation !== state.generation) return;
    state.images = images; canvas.width = options.width; canvas.height = options.height;
    draw(state);
    canvas.dataset.renderRevision = String(options.revision);
}
function draw(s) {
    const c = s.canvas.getContext('2d'); c.clearRect(0, 0, s.canvas.width, s.canvas.height); c.imageSmoothingEnabled = false;
    if (!s.images) return;
    c.globalAlpha = .22; for (const image of s.images.slice(1)) c.drawImage(image, 0, 0); c.globalAlpha = 1; c.drawImage(s.images[0], 0, 0);
    const selection = s.options.selection;
    if (selection?.frameId === s.options.frameId && selection.polygon?.length) polygon(c, selection.polygon, '#ffff00');
    if (s.polygon.length) polygon(c, s.polygon, '#ffff00');
    c.strokeStyle = '#00ffff'; c.lineWidth = 1;
    for (const p of Object.values(s.options.pivots || {})) { c.beginPath(); c.moveTo(p.x - 3, p.y); c.lineTo(p.x + 3, p.y); c.moveTo(p.x, p.y - 3); c.lineTo(p.x, p.y + 3); c.stroke(); }
}
function polygon(c, points, color) { c.strokeStyle = color; c.lineWidth = .5; c.beginPath(); points.forEach((p, i) => i ? c.lineTo(p.x, p.y) : c.moveTo(p.x, p.y)); c.closePath(); c.stroke(); }
export function coordinates(canvas, clientX, clientY) {
    const r = canvas.getBoundingClientRect();
    return { x: Math.floor((clientX - r.left) * canvas.width / r.width), y: Math.floor((clientY - r.top) * canvas.height / r.height) };
}
function colorAt(s, p) {
    const c = document.createElement('canvas'); c.width = s.options.width; c.height = s.options.height;
    const ctx = c.getContext('2d'); ctx.drawImage(s.images[0], 0, 0);
    return '#' + [...ctx.getImageData(p.x, p.y, 1, 1).data].map(n => n.toString(16).padStart(2, '0')).join('');
}
function down(s, e) {
    if (s.options.busy || !s.images || e.button !== 0) return;
    e.preventDefault(); s.canvas.focus(); const p = coordinates(s.canvas, e.clientX, e.clientY);
    if (s.options.tool === 'polygon') { s.polygon.push(p); draw(s); return; }
    s.canvas.setPointerCapture(e.pointerId);
    s.stroke = { points: [p], options: { ...s.options }, color: colorAt(s, p) };
    if (s.options.tool === 'eyedropper') { s.dotnet.invokeMethodAsync('PickColor', s.stroke.color); s.stroke = null; }
}
function move(s, e) {
    if (!s.stroke) return;
    const p = coordinates(s.canvas, e.clientX, e.clientY); const points = s.stroke.points; const last = points[points.length - 1];
    if (p.x === last.x && p.y === last.y || points.length >= 10000) return;
    points.push(p); draw(s);
    const c = s.canvas.getContext('2d'); c.strokeStyle = s.stroke.options.color; c.lineWidth = s.stroke.options.size;
    c.beginPath(); points.forEach((p, i) => i ? c.lineTo(p.x + .5, p.y + .5) : c.moveTo(p.x + .5, p.y + .5)); c.stroke();
}
async function up(s, e) {
    if (!s.stroke) return;
    const stroke = s.stroke; s.stroke = null;
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
    if (s.polygon.length < 3) return;
    const points = s.polygon; s.polygon = [];
    await s.dotnet.invokeMethodAsync('CanvasCommand', { op: 'select', frameId: s.options.frameId, polygon: points }, s.options.revision);
}
export function dispose(canvas) {
    const s = editors.get(canvas); if (!s) return; s.disposed = true;
    for (const [event, handler] of [['pointerdown',s.down],['pointermove',s.move],['pointerup',s.up],['pointercancel',s.up],['keydown',s.key],['dblclick',s.double]]) canvas.removeEventListener(event, handler);
    editors.delete(canvas);
}
