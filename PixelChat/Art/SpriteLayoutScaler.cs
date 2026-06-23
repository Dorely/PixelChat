namespace PixelChat.Art;

internal static class SpriteLayoutScaler
{
    public static SpriteLayoutScaleResult ScaleToSource(LayoutSpec layout, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException("Candidate image dimensions are invalid.");
        if (layout.CanvasWidth <= 0 || layout.CanvasHeight <= 0)
            throw new InvalidOperationException("Layout canvas dimensions are invalid.");

        var scaleX = sourceWidth / (double)layout.CanvasWidth;
        var scaleY = sourceHeight / (double)layout.CanvasHeight;
        var scaled = !NearlyEqual(scaleX, 1d) || !NearlyEqual(scaleY, 1d);
        var slots = layout.Slots
            .Select(slot =>
            {
                var rect = ScaleAndClamp(slot.Rect, layout.CanvasWidth, layout.CanvasHeight, sourceWidth, sourceHeight);
                var safe = Intersect(
                    ScaleAndClamp(slot.SafeRect, layout.CanvasWidth, layout.CanvasHeight, sourceWidth, sourceHeight),
                    rect);
                if (safe.Width <= 0 || safe.Height <= 0)
                    safe = rect;

                var root = ScalePoint(slot.Root, layout.CanvasWidth, layout.CanvasHeight, sourceWidth, sourceHeight);
                root = new SpriteSheetPoint(
                    Math.Clamp(root.X, rect.X, Math.Max(rect.X, rect.X + rect.Width - 1)),
                    Math.Clamp(root.Y, rect.Y, Math.Max(rect.Y, rect.Y + rect.Height - 1)));
                var baseline = Math.Clamp(
                    ScaleY(slot.BaselineY, layout.CanvasHeight, sourceHeight),
                    rect.Y,
                    Math.Max(rect.Y, rect.Y + rect.Height - 1));
                return new SlotSpec(slot.FrameIndex, rect, root, baseline, safe);
            })
            .ToList();

        var sourceLayout = layout with
        {
            CanvasWidth = sourceWidth,
            CanvasHeight = sourceHeight,
            GuideCellWidth = Math.Max(1, (int)Math.Round(layout.GuideCellWidth * scaleX)),
            GuideCellHeight = Math.Max(1, (int)Math.Round(layout.GuideCellHeight * scaleY)),
            Slots = slots,
        };
        var warning = scaled
            ? $"candidate size {sourceWidth}x{sourceHeight} differs from planned layout {layout.CanvasWidth}x{layout.CanvasHeight}; slots scaled by {scaleX:0.###}x{scaleY:0.###}"
            : string.Empty;
        return new SpriteLayoutScaleResult(sourceLayout, scaleX, scaleY, scaled, warning);
    }

    private static SpriteSheetRect ScaleAndClamp(SpriteSheetRect rect, int plannedWidth, int plannedHeight, int sourceWidth, int sourceHeight)
    {
        var x0 = ScaleX(rect.X, plannedWidth, sourceWidth);
        var y0 = ScaleY(rect.Y, plannedHeight, sourceHeight);
        var x1 = ScaleX(rect.X + rect.Width, plannedWidth, sourceWidth);
        var y1 = ScaleY(rect.Y + rect.Height, plannedHeight, sourceHeight);
        x0 = Math.Clamp(x0, 0, sourceWidth);
        y0 = Math.Clamp(y0, 0, sourceHeight);
        x1 = Math.Clamp(x1, 0, sourceWidth);
        y1 = Math.Clamp(y1, 0, sourceHeight);
        return new SpriteSheetRect(
            Math.Min(x0, x1),
            Math.Min(y0, y1),
            Math.Abs(x1 - x0),
            Math.Abs(y1 - y0));
    }

    private static SpriteSheetPoint ScalePoint(SpriteSheetPoint point, int plannedWidth, int plannedHeight, int sourceWidth, int sourceHeight) =>
        new(
            Math.Clamp(ScaleX(point.X, plannedWidth, sourceWidth), 0, Math.Max(0, sourceWidth - 1)),
            Math.Clamp(ScaleY(point.Y, plannedHeight, sourceHeight), 0, Math.Max(0, sourceHeight - 1)));

    private static int ScaleX(int x, int plannedWidth, int sourceWidth) =>
        (int)Math.Round(x * (sourceWidth / (double)plannedWidth));

    private static int ScaleY(int y, int plannedHeight, int sourceHeight) =>
        (int)Math.Round(y * (sourceHeight / (double)plannedHeight));

    private static SpriteSheetRect Intersect(SpriteSheetRect a, SpriteSheetRect b)
    {
        var x0 = Math.Max(a.X, b.X);
        var y0 = Math.Max(a.Y, b.Y);
        var x1 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return new SpriteSheetRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) < 0.0001d;
}

internal sealed record SpriteLayoutScaleResult(
    LayoutSpec Layout,
    double ScaleX,
    double ScaleY,
    bool Scaled,
    string Warning);
