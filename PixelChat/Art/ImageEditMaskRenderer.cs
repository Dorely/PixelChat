namespace PixelChat.Art;

public static class ImageEditMaskRenderer
{
    public static byte[] RenderPng(
        int width,
        int height,
        IReadOnlyList<SpriteSheetRect>? rects,
        IReadOnlyList<SpriteSheetShapePath>? polygons)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Edit mask dimensions must be positive.");

        var normalizedRects = (rects ?? [])
            .Where(rect => rect.Width > 0 && rect.Height > 0)
            .Select(rect => NormalizeRect(rect, width, height))
            .Where(rect => rect.Width > 0 && rect.Height > 0)
            .ToList();
        var normalizedPolygons = (polygons ?? [])
            .Select(path => new SpriteSheetShapePath(
                (path.Points ?? [])
                    .Select(point => new SpriteSheetPoint(
                        Math.Clamp(point.X, 0, width),
                        Math.Clamp(point.Y, 0, height)))
                    .ToList()))
            .Where(path => path.Points.Count >= 3)
            .ToList();

        if (normalizedRects.Count == 0 && normalizedPolygons.Count == 0)
            throw new InvalidOperationException("An edit mask requires at least one rectangle or polygon that overlaps the image.");

        var rgba = new byte[checked(width * height * 4)];
        for (var alpha = 3; alpha < rgba.Length; alpha += 4)
            rgba[alpha] = byte.MaxValue;

        var editablePixels = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var insideRect = normalizedRects.Any(rect =>
                    x >= rect.X && x < rect.X + rect.Width
                    && y >= rect.Y && y < rect.Y + rect.Height);
                var insidePolygon = normalizedPolygons.Any(path => PointInPath(path.Points, x + 0.5d, y + 0.5d));
                if (!insideRect && !insidePolygon)
                    continue;

                rgba[((y * width) + x) * 4 + 3] = 0;
                editablePixels++;
            }
        }

        if (editablePixels == 0)
            throw new InvalidOperationException("Edit mask regions must select at least one image pixel.");

        return SpriteSheetPngCodec.EncodeRgba(width, height, rgba);
    }

    private static SpriteSheetRect NormalizeRect(SpriteSheetRect rect, int width, int height)
    {
        var x0 = Clamp(rect.X, width);
        var y0 = Clamp(rect.Y, height);
        var x1 = Clamp((long)rect.X + rect.Width, width);
        var y1 = Clamp((long)rect.Y + rect.Height, height);
        return new SpriteSheetRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static int Clamp(long value, int maximum) =>
        (int)Math.Clamp(value, 0L, maximum);

    private static bool PointInPath(IReadOnlyList<SpriteSheetPoint> points, double x, double y)
    {
        var inside = false;
        var previous = points.Count - 1;
        for (var current = 0; current < points.Count; current++)
        {
            var currentPoint = points[current];
            var previousPoint = points[previous];
            var denominator = previousPoint.Y - currentPoint.Y;
            if (Math.Abs(denominator) > 0.0001d
                && (currentPoint.Y > y) != (previousPoint.Y > y)
                && x < ((previousPoint.X - currentPoint.X) * (y - currentPoint.Y) / denominator) + currentPoint.X)
            {
                inside = !inside;
            }

            previous = current;
        }

        return inside;
    }
}
