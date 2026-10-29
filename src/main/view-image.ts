import sharp from 'sharp';
import { WorkbenchError } from '../shared/workbench';
import type { MediaStore } from './media';
import type { Store } from './store';

/** Small art is enlarged with whole-pixel scaling so a model can see it; large art is reduced. */
const MIN_VIEW_EDGE = 512;
const escapeXml = (value: string) => value.replace(/[<>&"']/g, char => `&#${char.charCodeAt(0)};`);

/**
 * Renders a revision for agent vision: flattened onto an opaque backdrop so transparent pixels read
 * as the backdrop, with open note pins drawn and numbered in the same order as the viewer.
 * Pins from another revision are dashed; their coordinates describe that revision's pixels.
 */
export async function viewImage(store: Store, media: MediaStore, projectId: string, itemId: string, options: { revisionId?: string; pins: boolean; background: string; maxSize: number }) {
  const item = store.item(itemId, projectId);
  const revision = item.revisions.find(entry => entry.id === (options.revisionId ?? item.selectedRevisionId));
  const file = revision && store.revisionFile(revision.id, projectId);
  if (!revision || !file) throw new WorkbenchError('NOT_FOUND', 'Revision not found on this item.');
  const edge = Math.max(revision.width, revision.height);
  const scale = edge > options.maxSize ? options.maxSize / edge : edge < MIN_VIEW_EDGE ? Math.max(1, Math.min(Math.ceil(MIN_VIEW_EDGE / edge), Math.floor(options.maxSize / edge))) : 1;
  const width = Math.max(1, Math.round(revision.width * scale)), height = Math.max(1, Math.round(revision.height * scale));

  const pinned = item.notes.filter(thread => thread.pin && !thread.resolvedAt);
  const pins = options.pins ? pinned.map((thread, index) => ({
    number: index + 1, noteId: thread.id, revisionId: thread.revisionId, onThisVersion: thread.revisionId === revision.id, pin: thread.pin!,
    body: thread.body.length > 200 ? `${thread.body.slice(0, 200)}…` : thread.body,
  })) : [];
  const marks = pins.map(({ number, onThisVersion, pin }) => {
    const x = pin.x * scale, y = pin.y * scale, dash = onThisVersion ? '' : ' stroke-dasharray="8 5"';
    const shape = pin.width && pin.height
      ? (stroke: string, size: number) => `<rect x="${x}" y="${y}" width="${pin.width! * scale}" height="${pin.height! * scale}" fill="none" stroke="${stroke}" stroke-width="${size}"${dash}/>`
      : (stroke: string, size: number) => `<circle cx="${x}" cy="${y}" r="9" fill="none" stroke="${stroke}" stroke-width="${size}"${dash}/>`;
    const labelX = Math.min(Math.max(x, 13), width - 13), labelY = Math.min(Math.max(y - 14, 13), height - 13);
    return `${shape('#000', 5)}${shape('#ff2bd6', 2.5)}<circle cx="${labelX}" cy="${labelY}" r="12" fill="#ff2bd6" stroke="#000" stroke-width="2"/>`
      + `<text x="${labelX}" y="${labelY + 5}" text-anchor="middle" font-family="Arial, sans-serif" font-size="14" font-weight="700" fill="#000">${escapeXml(String(number))}</text>`;
  });

  let image = sharp(await media.read(file.file)).resize(width, height, { fit: 'fill', kernel: scale >= 1 ? 'nearest' : 'lanczos3' }).flatten({ background: options.background });
  if (marks.length) {
    // Resize and flatten first so pins are drawn over final pixels at the output scale.
    image = sharp(await image.png().toBuffer()).composite([{ input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}">${marks.join('')}</svg>`) }]);
  }
  const data = await image.png().toBuffer();
  return {
    media: { type: 'image', data: data.toString('base64'), mimeType: 'image/png' },
    itemId, revisionId: revision.id, selected: revision.id === item.selectedRevisionId, width: revision.width, height: revision.height, scale, viewWidth: width, viewHeight: height,
    background: options.background, alpha: !revision.hasAlpha ? 'none' : revision.opaque ? 'channel, fully opaque' : 'transparent pixels shown as the background',
    pins,
  };
}
