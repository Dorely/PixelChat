import type { Pin, Stroke } from './types';

/** The whole-pixel box around a drawing's strokes, including their width. */
export function strokeBounds(strokes: Stroke[]): Required<Pin> {
  let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
  for (const stroke of strokes) {
    const half = stroke.size / 2;
    for (let i = 0; i + 1 < stroke.points.length; i += 2) {
      left = Math.min(left, stroke.points[i]! - half); right = Math.max(right, stroke.points[i]! + half);
      top = Math.min(top, stroke.points[i + 1]! - half); bottom = Math.max(bottom, stroke.points[i + 1]! + half);
    }
  }
  if (left === Infinity) return { x: 0, y: 0, width: 0, height: 0 };
  const x = Math.max(0, Math.floor(left)), y = Math.max(0, Math.floor(top));
  return { x, y, width: Math.ceil(right) - x, height: Math.ceil(bottom) - y };
}

/** The first point of a drawing, where its number label goes. */
export const strokeStart = (strokes: Stroke[]) => ({ x: strokes[0]?.points[0] ?? 0, y: strokes[0]?.points[1] ?? 0 });
