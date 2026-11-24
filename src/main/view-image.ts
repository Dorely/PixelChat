import sharp from 'sharp';
import { WorkbenchError } from '../shared/workbench';
import type { MediaStore } from './media';
import type { Store } from './store';
import { strokeBounds, strokeStart } from '../shared/marks';

/** Small art is enlarged with whole-pixel scaling so a model can see it; large art is reduced. */
const MIN_VIEW_EDGE = 512;
const escapeXml = (value: string) => value.replace(/[<>&"']/g, char => `&#${char.charCodeAt(0)};`);

/**
 * Renders a revision for agent vision: flattened onto an opaque backdrop so transparent pixels read
 * as the backdrop, with open note pins and drawings drawn and numbered in the same order as the viewer.
 * Pins from another revision are dashed; their coordinates describe that revision's pixels. Animated versions show one frame.
 */
export async function viewImage(store: Store, media: MediaStore, projectId: string, itemId: string, options: { revisionId?: string; pins: boolean; background: string; maxSize: number; frame: number }) {
  const item = store.item(itemId, projectId);
  const revision = item.revisions.find(entry => entry.id === (options.revisionId ?? item.selectedRevisionId));
  const file = revision && store.revisionFile(revision.id, projectId);
  if (!revision || !file) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
  if (options.frame >= revision.frames) throw new WorkbenchError('INVALID_ARGUMENTS', `This version has ${revision.frames} ${revision.frames === 1 ? 'frame' : 'frames'}; frame is zero-based.`);
  const edge = Math.max(revision.width, revision.height);
  const scale = edge > options.maxSize ? options.maxSize / edge : edge < MIN_VIEW_EDGE ? Math.max(1, Math.min(Math.ceil(MIN_VIEW_EDGE / edge), Math.floor(options.maxSize / edge))) : 1;
  const width = Math.max(1, Math.round(revision.width * scale)), height = Math.max(1, Math.round(revision.height * scale));

  const marked = item.notes.filter(thread => (thread.pin || thread.sketch) && !thread.resolvedAt);
  const sizes = new Map(item.revisions.map(entry => [entry.id, entry]));
  const pins = options.pins ? marked.map((thread, index) => ({
    number: index + 1, noteId: thread.id, revisionId: thread.revisionId, onThisVersion: thread.revisionId === revision.id, pin: thread.pin,
    frame: thread.frame, drawing: thread.sketch ? { strokes: thread.sketch.length, colors: [...new Set(thread.sketch.map(stroke => stroke.color))], bounds: strokeBounds(thread.sketch) } : null,
    body: thread.body.length > 200 ? `${thread.body.slice(0, 200)}…` : thread.body,
  })).filter(pin => pin.frame === null || (pin.onThisVersion && pin.frame === options.frame)) : [];
  const marks = pins.map(({ number, onThisVersion, noteId }) => {
    const thread = marked.find(entry => entry.id === noteId)!, dash = onThisVersion ? '' : ' stroke-dasharray="8 5"';
    // Drawings and pins from another version are mapped proportionally onto this one.
    const noted = sizes.get(thread.revisionId) ?? revision;
    const sx = width / noted.width, sy = height / noted.height;
    let drawn = '', anchor = { x: 0, y: 0 };
    if (thread.sketch) {
      const lines = (outline: boolean) => thread.sketch!.map(stroke => {
        const size = Math.max(2, stroke.size * Math.min(sx, sy)) + (outline ? 3 : 0), color = outline ? '#000' : stroke.color;
        if (stroke.points.length === 2) return `<circle cx="${stroke.points[0]! * sx}" cy="${stroke.points[1]! * sy}" r="${size / 2}" fill="${color}"/>`;
        const points = stroke.points.map((value, i) => i % 2 ? value * sy : value * sx).join(' ');
        return `<polyline points="${points}" fill="none" stroke="${color}" stroke-width="${size}" stroke-linecap="round" stroke-linejoin="round"${dash}/>`;
      }).join('');
      drawn += lines(true) + lines(false);
      const start = strokeStart(thread.sketch);
      anchor = { x: start.x * sx, y: start.y * sy };
    }
    if (thread.pin) {
      const pin = thread.pin, x = pin.x * sx, y = pin.y * sy;
      const shape = pin.width && pin.height
        ? (stroke: string, size: number) => `<rect x="${x}" y="${y}" width="${pin.width! * sx}" height="${pin.height! * sy}" fill="none" stroke="${stroke}" stroke-width="${size}"${dash}/>`
        : (stroke: string, size: number) => `<circle cx="${x}" cy="${y}" r="9" fill="none" stroke="${stroke}" stroke-width="${size}"${dash}/>`;
      drawn += shape('#000', 5) + shape('#ff2bd6', 2.5);
      anchor = { x, y };
    }
    const labelX = Math.min(Math.max(anchor.x, 13), width - 13), labelY = Math.min(Math.max(anchor.y - 14, 13), height - 13);
    return `${drawn}<circle cx="${labelX}" cy="${labelY}" r="12" fill="#ff2bd6" stroke="#000" stroke-width="2"/>`
      + `<text x="${labelX}" y="${labelY + 5}" text-anchor="middle" font-family="Arial, sans-serif" font-size="14" font-weight="700" fill="#000">${escapeXml(String(number))}</text>`;
  });

  const path = revision.format === 'mp4' || revision.format === 'webm' ? media.playbackPath(revision.id) : media.path(file.file);
  let image = sharp(path, { page: options.frame }).resize(width, height, { fit: 'fill', kernel: scale >= 1 ? 'nearest' : 'lanczos3' }).flatten({ background: options.background });
  if (marks.length) {
    // Resize and flatten first so pins are drawn over final pixels at the output scale.
    image = sharp(await image.png().toBuffer()).composite([{ input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">${marks.join('')}</svg>`) }]);
  }
  const data = await image.png().toBuffer();
  return {
    media: { type: 'image', data: data.toString('base64'), mimeType: 'image/png' },
    itemId, revisionId: revision.id, selected: revision.id === item.selectedRevisionId, width: revision.width, height: revision.height, frame: options.frame, frames: revision.frames, scale, viewWidth: width, viewHeight: height,
    background: options.background, alpha: !revision.hasAlpha ? 'none' : revision.opaque ? 'channel, fully opaque' : 'transparent pixels shown as the background',
    pins,
  };
}
