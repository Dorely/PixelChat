import sharp from 'sharp';
import { WorkbenchError } from '../shared/workbench';
import type { MaskShape } from '../shared/types';

/** Appended after the prompt when a mask is sent; the mask image is always the last input. */
export const MASK_INSTRUCTION =
  'The last image is an edit mask, not content to reproduce. Apply the requested change only where the mask is transparent. Keep every area where the mask is opaque exactly as it is in the first image.';

const svgShape = (shape: MaskShape) => {
  const fill = shape.erase ? '#000' : '#fff';
  if (shape.kind === 'box') return `<rect x="${shape.x}" y="${shape.y}" width="${shape.width}" height="${shape.height}" fill="${fill}"/>`;
  const [x, y] = shape.points;
  // A single point is a dot of the brush size.
  if (shape.points.length === 2) return `<circle cx="${x}" cy="${y}" r="${shape.size / 2}" fill="${fill}"/>`;
  return `<polyline points="${shape.points.join(' ')}" fill="none" stroke="${fill}" stroke-width="${shape.size}" stroke-linecap="round" stroke-linejoin="round"/>`;
};

/**
 * Rasterizes mask shapes at the source size as the provider mask: opaque white to keep, transparent
 * black where the edit applies, so it still reads if a model drops alpha. Refuses a mask that covers nothing.
 */
export async function renderMask(shapes: MaskShape[], width: number, height: number): Promise<Buffer> {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"><rect width="${width}" height="${height}" fill="#000"/>${shapes.map(svgShape).join('')}</svg>`;
  const painted = await sharp(Buffer.from(svg)).resize(width, height, { fit: 'fill' }).extractChannel(0).raw().toBuffer();
  const pixels = Buffer.alloc(width * height * 4);
  let editable = false;
  for (let i = 0; i < width * height; i++) {
    const edit = painted[i]! >= 128;
    editable ||= edit;
    pixels.fill(edit ? 0 : 255, i * 4, i * 4 + 4);
  }
  if (!editable) throw new WorkbenchError('INVALID_ARGUMENTS', 'The mask covers no pixels of the source. Paint the area to edit.');
  return sharp(pixels, { raw: { width, height, channels: 4 } }).png().toBuffer();
}
