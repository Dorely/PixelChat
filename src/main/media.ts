import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import sharp from 'sharp';
import { WorkbenchError } from '../shared/workbench';
import type { Revision } from '../shared/types';
import type { RevisionInput } from './store';

const THUMBNAIL_SIZE = 384;
const EXTENSIONS: Record<Revision['format'], string> = { png: 'png', webp: 'webp', jpeg: 'jpg' };
export const MEDIA_TYPES: Record<Revision['format'], string> = { png: 'image/png', webp: 'image/webp', jpeg: 'image/jpeg' };
/** Imported file extensions; GIF and other formats are not reviewed yet. */
export const IMAGE_EXTENSIONS = new Set(['.png', '.webp', '.jpg', '.jpeg']);

/** Owns revision bytes under the profile: exact originals plus derived thumbnails. */
export class MediaStore {
  constructor(readonly root: string) {}
  async init() { await Promise.all(['originals', 'thumbnails'].map(name => mkdir(join(this.root, name), { recursive: true }))); }
  path(file: string) { return join(this.root, file); }
  thumbnailPath(revisionId: string) { return join(this.root, 'thumbnails', `${revisionId}.webp`); }

  /**
   * Stores exact bytes as a new revision file and returns its facts. Nothing is re-encoded.
   * When the bytes were converted from a provider's output, the provider original is kept beside it.
   */
  async ingest(bytes: Buffer, providerOriginal?: { data: Buffer; format: Revision['format'] }): Promise<Omit<RevisionInput, 'origin' | 'parentId' | 'provenance'> & { providerFile?: string }> {
    const metadata = await imageFacts(bytes);
    const format = metadata.format as Revision['format'];
    const hasAlpha = !!metadata.hasAlpha;
    const opaque = hasAlpha ? (await sharp(bytes).stats()).isOpaque : true;
    const id = randomUUID(), file = `originals/${id}.${EXTENSIONS[format]}`;
    const thumbnail = await sharp(bytes).resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE, { fit: 'inside', withoutEnlargement: true }).webp({ quality: 82, alphaQuality: 90 }).toBuffer();
    await this.write(this.path(file), bytes);
    await this.write(this.thumbnailPath(id), thumbnail);
    const providerFile = providerOriginal ? `originals/${id}.provider.${EXTENSIONS[providerOriginal.format]}` : undefined;
    if (providerOriginal && providerFile) await this.write(this.path(providerFile), providerOriginal.data);
    return { ...(providerFile ? { providerFile } : {}), id, file, sha256: sha256(bytes), bytes: bytes.length, width: metadata.width, height: metadata.height, format, hasAlpha, opaque };
  }
  read(file: string) { return readFile(this.path(file)); }
  /** Best-effort removal of revision files whose records were deleted. */
  async remove(files: string[]) {
    for (const file of files) {
      const id = file.slice('originals/'.length).replace(/\.[a-z]+$/, '');
      const providerFiles = Object.values(EXTENSIONS).map(extension => `originals/${id}.provider.${extension}`);
      await Promise.all([file, ...providerFiles].map(path => rm(this.path(path), { force: true })).concat(rm(this.thumbnailPath(id), { force: true }))).catch(() => undefined);
    }
  }
  private async write(path: string, bytes: Buffer) {
    const temp = `${path}.partial`;
    await writeFile(temp, bytes, { flag: 'wx' });
    await rename(temp, path);
  }
}

/** Decoded facts for PNG, WebP, or JPEG bytes; anything else is unsupported. */
export async function imageFacts(bytes: Buffer): Promise<sharp.Metadata & { format: Revision['format']; width: number; height: number }> {
  let metadata: sharp.Metadata;
  try { metadata = await sharp(bytes).metadata(); } catch { throw new WorkbenchError('UNSUPPORTED_IMAGE', 'The bytes are not a readable PNG, WebP, or JPEG image.'); }
  if (!EXTENSIONS[metadata.format as Revision['format']] || !metadata.width || !metadata.height) throw new WorkbenchError('UNSUPPORTED_IMAGE', `Unsupported image format: ${metadata.format ?? 'unknown'}.`);
  return metadata as sharp.Metadata & { format: Revision['format']; width: number; height: number };
}
export const sha256 = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');
