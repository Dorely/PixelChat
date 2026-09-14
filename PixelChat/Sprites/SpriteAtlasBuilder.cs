using PixelChat.Models;

namespace PixelChat.Sprites;

public sealed record SpriteExportSpec(string Format = "bundle", int Columns = 0, int Padding = 0, int Gutter = 0, int OuterMargin = 0,
    string? Clip = null, string HorizontalAnchor = "left", string VerticalAnchor = "top", string Ordering = "rowMajor");
public sealed record SpriteExportFrame(Guid Id, string Name, int Index, string FileName, SpriteRect Rect, SpriteRect Slot,
    int LogicalWidth, int LogicalHeight, SpritePoint Offset, int DurationMs, IReadOnlyDictionary<string, SpritePoint> Pivots, string PixelHash);
public sealed record SpriteAtlasManifest(string Format, int Version, Guid DocumentId, long Revision, string Name, string Image,
    int Width, int Height, int Rows, int Columns, SpriteExportSpec Specification, IReadOnlyList<SpriteExportFrame> Frames,
    IReadOnlyList<SpriteClip> Clips, IReadOnlyList<SpriteSlice> Slices);
public sealed record SpriteAtlasResult(SpriteBitmap? Bitmap, SpriteAtlasManifest Manifest);

public static class SpriteAtlasBuilder
{
    public static SpriteAtlasResult Build(SpriteSnapshot snapshot, IReadOnlyDictionary<string, SpriteBitmap> bitmaps, SpriteExportSpec spec, CancellationToken token = default)
    {
        if (spec.Padding is < 0 or > 4096 || spec.Gutter is < 0 or > 4096 || spec.OuterMargin is < 0 or > 4096) throw new InvalidOperationException("Padding, gutter, and margin must be 0–4096.");
        if (spec.HorizontalAnchor is not ("left" or "center" or "right") || spec.VerticalAnchor is not ("top" or "center" or "bottom") || spec.Ordering is not ("rowMajor" or "columnMajor")) throw new InvalidOperationException("Invalid atlas placement options.");
        var doc = snapshot.Document; var columns = spec.Columns == 0 ? (int)Math.Ceiling(Math.Sqrt(doc.Frames.Count)) : spec.Columns;
        if (columns < 1 || columns > doc.Frames.Count) throw new InvalidOperationException("Columns must be between 1 and the frame count, or zero for automatic layout.");
        var rows = (doc.Frames.Count + columns - 1) / columns;
        var cellW = doc.Frames.Max(f => f.Width); var cellH = doc.Frames.Max(f => f.Height);
        var slotW = checked(cellW + 2 * spec.Padding); var slotH = checked(cellH + 2 * spec.Padding);
        var width = checked(2 * spec.OuterMargin + columns * slotW + (columns - 1) * spec.Gutter);
        var height = checked(2 * spec.OuterMargin + rows * slotH + (rows - 1) * spec.Gutter);
        var atlas = spec.Format == "atlas" ? SpriteRaster.Blank(width, height) : null; var frames = new List<SpriteExportFrame>();
        foreach (var frame in doc.Frames)
        {
            token.ThrowIfCancellationRequested(); var i = frames.Count;
            var col = spec.Ordering == "columnMajor" ? i / rows : i % columns; var row = spec.Ordering == "columnMajor" ? i % rows : i / columns;
            var slot = new SpriteRect(spec.OuterMargin + col * (slotW + spec.Gutter), spec.OuterMargin + row * (slotH + spec.Gutter), slotW, slotH);
            var x = slot.X + spec.Padding + Align(cellW - frame.Width, spec.HorizontalAnchor);
            var y = slot.Y + spec.Padding + Align(cellH - frame.Height, spec.VerticalAnchor);
            var raster = SpriteRaster.Composite(doc, frame, h => bitmaps[h]);
            if (atlas is not null) for (var sy = 0; sy < frame.Height; sy++) for (var sx = 0; sx < frame.Width; sx++) atlas.Put(x + sx, y + sy, raster.Get(sx, sy));
            frames.Add(new(frame.Id, frame.Name, i, $"frames/{i:D4}-{frame.Id}.png", new(x,y,frame.Width,frame.Height), slot, frame.Width, frame.Height,
                new(0,0), frame.DurationMs, frame.Pivots, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(raster.Pixels))));
        }
        return new(atlas?.Encode(), new("pixelchat-atlas", 1, snapshot.DocumentId, snapshot.Revision, doc.Name, "atlas.png", width, height, rows, columns, spec, frames, doc.Clips, doc.Slices));
    }
    private static int Align(int available, string anchor) => anchor is "right" or "bottom" ? available : anchor == "center" ? available / 2 : 0;
}
