namespace PixelChat.Art;

internal static class SpriteSheetServerRenderer
{
    public static SpriteSheetServerPreviewResult BuildFramePreviews(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int padding,
        int gutter,
        int fps,
        string? horizontalAnchor,
        string? verticalAnchor,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames)
    {
        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        cellWidth = Math.Clamp(cellWidth, 1, 8192);
        cellHeight = Math.Clamp(cellHeight, 1, 8192);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        _ = Math.Clamp(fps, 1, 60);
        var maxFrames = checked(rows * columns);
        var frames = inputFrames
            .Where(frame => frame.Index >= 0 && frame.Index < maxFrames)
            .OrderBy(frame => frame.Index)
            .Take(maxFrames)
            .ToList();
        if (frames.Count == 0)
            return new SpriteSheetServerPreviewResult([]);

        var savedFrames = new List<SpriteSheetFrameView>();
        foreach (var input in frames)
        {
            var sourceRect = ClampRect(input.SourceRect, sourceWidth, sourceHeight);
            var shapePaths = NormalizeShapePaths(input.ShapePaths, sourceWidth, sourceHeight);
            var row = input.Index / columns;
            var column = input.Index % columns;
            var cellRect = new SpriteSheetRect(
                column * (cellWidth + gutter),
                row * (cellHeight + gutter),
                cellWidth,
                cellHeight);
            var (destX, destY) = AlignedDestination(cellRect, sourceRect, padding, horizontalAnchor, verticalAnchor);
            var spriteRect = new SpriteSheetRect(destX, destY, sourceRect.Width, sourceRect.Height);
            var previewRgba = CropRect(sourceRgba, sourceWidth, sourceHeight, sourceRect, shapePaths);
            var previewDataUrl = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(sourceRect.Width, sourceRect.Height, previewRgba));
            savedFrames.Add(new SpriteSheetFrameView(
                input.Index,
                string.IsNullOrWhiteSpace(input.Label) ? $"Frame {input.Index + 1}" : input.Label.Trim(),
                sourceRect,
                shapePaths,
                cellRect,
                spriteRect,
                previewDataUrl));
        }

        return new SpriteSheetServerPreviewResult(savedFrames);
    }

    public static SpriteSheetServerRenderResult Render(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int padding,
        int gutter,
        int fps,
        string? horizontalAnchor,
        string? verticalAnchor,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames)
    {
        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        cellWidth = Math.Clamp(cellWidth, 1, 8192);
        cellHeight = Math.Clamp(cellHeight, 1, 8192);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        _ = Math.Clamp(fps, 1, 60);
        var maxFrames = checked(rows * columns);
        var frames = inputFrames
            .Where(frame => frame.Index >= 0 && frame.Index < maxFrames)
            .OrderBy(frame => frame.Index)
            .Take(maxFrames)
            .ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        var outputWidth = checked((columns * cellWidth) + (Math.Max(0, columns - 1) * gutter));
        var outputHeight = checked((rows * cellHeight) + (Math.Max(0, rows - 1) * gutter));
        if (outputWidth <= 0 || outputHeight <= 0 || outputWidth * (long)outputHeight > 120_000_000)
            throw new InvalidOperationException("Sprite sheet output is too large.");

        var outputRgba = new byte[checked(outputWidth * outputHeight * 4)];
        var savedFrames = new List<SpriteSheetFrameView>();
        foreach (var input in frames)
        {
            var sourceRect = ClampRect(input.SourceRect, sourceWidth, sourceHeight);
            var shapePaths = NormalizeShapePaths(input.ShapePaths, sourceWidth, sourceHeight);
            var row = input.Index / columns;
            var column = input.Index % columns;
            var cellRect = new SpriteSheetRect(
                column * (cellWidth + gutter),
                row * (cellHeight + gutter),
                cellWidth,
                cellHeight);
            var (destX, destY) = AlignedDestination(cellRect, sourceRect, padding, horizontalAnchor, verticalAnchor);
            CopySprite(sourceRgba, sourceWidth, sourceHeight, sourceRect, shapePaths, outputRgba, outputWidth, outputHeight, destX, destY);

            var spriteRect = IntersectRect(new SpriteSheetRect(destX, destY, sourceRect.Width, sourceRect.Height), outputWidth, outputHeight);
            var rebasedSourceRect = spriteRect.Width > 0 && spriteRect.Height > 0
                ? spriteRect
                : new SpriteSheetRect(cellRect.X, cellRect.Y, 1, 1);
            var rebasedShapePaths = RebaseShapePaths(shapePaths, sourceRect, destX, destY, outputWidth, outputHeight);
            var previewRgba = CropRect(outputRgba, outputWidth, outputHeight, cellRect);
            var previewDataUrl = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(cellRect.Width, cellRect.Height, previewRgba));
            savedFrames.Add(new SpriteSheetFrameView(
                input.Index,
                string.IsNullOrWhiteSpace(input.Label) ? $"Frame {input.Index + 1}" : input.Label.Trim(),
                rebasedSourceRect,
                rebasedShapePaths,
                cellRect,
                spriteRect,
                previewDataUrl));
        }

        return new SpriteSheetServerRenderResult(
            SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba),
            outputWidth,
            outputHeight,
            savedFrames);
    }

    private static SpriteSheetRect ClampRect(SpriteSheetRect rect, int width, int height)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        return new SpriteSheetRect(
            x,
            y,
            Math.Clamp(rect.Width, 1, Math.Max(1, width - x)),
            Math.Clamp(rect.Height, 1, Math.Max(1, height - y)));
    }

    private static SpriteSheetRect IntersectRect(SpriteSheetRect rect, int width, int height)
    {
        var x1 = Math.Clamp(rect.X, 0, width);
        var y1 = Math.Clamp(rect.Y, 0, height);
        var x2 = Math.Clamp(rect.X + rect.Width, 0, width);
        var y2 = Math.Clamp(rect.Y + rect.Height, 0, height);
        return new SpriteSheetRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private static (int X, int Y) AlignedDestination(
        SpriteSheetRect cellRect,
        SpriteSheetRect sourceRect,
        int padding,
        string? horizontalAnchor,
        string? verticalAnchor)
    {
        var x = NormalizeHorizontalAnchor(horizontalAnchor) switch
        {
            "left" => cellRect.X + padding,
            "right" => cellRect.X + cellRect.Width - padding - sourceRect.Width,
            _ => (int)Math.Round(cellRect.X + ((cellRect.Width - sourceRect.Width) / 2d)),
        };
        var y = NormalizeVerticalAnchor(verticalAnchor) switch
        {
            "top" => cellRect.Y + padding,
            "middle" => (int)Math.Round(cellRect.Y + ((cellRect.Height - sourceRect.Height) / 2d)),
            _ => cellRect.Y + cellRect.Height - padding - sourceRect.Height,
        };

        return (x, y);
    }

    private static void CopySprite(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetRect sourceRect,
        IReadOnlyList<SpriteSheetShapePath> shapePaths,
        byte[] target,
        int targetWidth,
        int targetHeight,
        int destX,
        int destY)
    {
        for (var y = 0; y < sourceRect.Height; y++)
        {
            var targetY = destY + y;
            if (targetY < 0 || targetY >= targetHeight)
                continue;

            var sourceY = sourceRect.Y + y;
            if (sourceY < 0 || sourceY >= sourceHeight)
                continue;

            for (var x = 0; x < sourceRect.Width; x++)
            {
                var targetX = destX + x;
                if (targetX < 0 || targetX >= targetWidth)
                    continue;

                var sourceX = sourceRect.X + x;
                if (sourceX < 0 || sourceX >= sourceWidth)
                    continue;

                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                if (shapePaths.Count > 0
                    && (!IsForeground(source[sourceIndex], source[sourceIndex + 1], source[sourceIndex + 2], source[sourceIndex + 3])
                        || !ContainsShape(shapePaths, sourceX + 0.5d, sourceY + 0.5d)))
                {
                    continue;
                }

                var targetIndex = ((targetY * targetWidth) + targetX) * 4;
                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
                target[targetIndex + 3] = source[sourceIndex + 3];
            }
        }
    }

    private static byte[] CropRect(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetRect rect,
        IReadOnlyList<SpriteSheetShapePath>? shapePaths = null)
    {
        shapePaths ??= [];
        var output = new byte[checked(rect.Width * rect.Height * 4)];
        for (var y = 0; y < rect.Height; y++)
        {
            var sourceY = rect.Y + y;
            if (sourceY < 0 || sourceY >= sourceHeight)
                continue;

            for (var x = 0; x < rect.Width; x++)
            {
                var sourceX = rect.X + x;
                if (sourceX < 0 || sourceX >= sourceWidth)
                    continue;

                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                if (shapePaths.Count > 0
                    && (!IsForeground(source[sourceIndex], source[sourceIndex + 1], source[sourceIndex + 2], source[sourceIndex + 3])
                        || !ContainsShape(shapePaths, sourceX + 0.5d, sourceY + 0.5d)))
                {
                    continue;
                }

                var targetIndex = ((y * rect.Width) + x) * 4;
                output[targetIndex] = source[sourceIndex];
                output[targetIndex + 1] = source[sourceIndex + 1];
                output[targetIndex + 2] = source[sourceIndex + 2];
                output[targetIndex + 3] = source[sourceIndex + 3];
            }
        }

        return output;
    }

    private static IReadOnlyList<SpriteSheetShapePath> NormalizeShapePaths(
        IReadOnlyList<SpriteSheetShapePath>? paths,
        int sourceWidth,
        int sourceHeight)
    {
        if (paths is null || paths.Count == 0)
            return [];

        return paths
            .Select(path => new SpriteSheetShapePath(
                (path.Points ?? [])
                .Select(point => new SpriteSheetPoint(
                    Math.Clamp(point.X, 0, sourceWidth),
                    Math.Clamp(point.Y, 0, sourceHeight)))
                .ToList()))
            .Where(path => path.Points.Count >= 3)
            .ToList();
    }

    private static IReadOnlyList<SpriteSheetShapePath> RebaseShapePaths(
        IReadOnlyList<SpriteSheetShapePath> paths,
        SpriteSheetRect sourceRect,
        int destX,
        int destY,
        int outputWidth,
        int outputHeight)
    {
        if (paths.Count == 0)
            return [];

        return paths
            .Select(path => new SpriteSheetShapePath(
                path.Points
                    .Select(point => new SpriteSheetPoint(
                        Math.Clamp(destX + point.X - sourceRect.X, 0, outputWidth),
                        Math.Clamp(destY + point.Y - sourceRect.Y, 0, outputHeight)))
                    .ToList()))
            .Where(path => path.Points.Count >= 3)
            .ToList();
    }

    private static bool ContainsShape(IReadOnlyList<SpriteSheetShapePath> paths, double x, double y)
    {
        var inside = false;
        foreach (var path in paths)
        {
            if (PointInPath(path.Points, x, y))
                inside = !inside;
        }

        return inside;
    }

    private static bool PointInPath(IReadOnlyList<SpriteSheetPoint> points, double x, double y)
    {
        if (points.Count < 3)
            return false;

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

    private static bool IsForeground(byte r, byte g, byte b, byte a) =>
        a > 16 && !IsMagentaKeyColor(r, g, b);

    private static bool IsMagentaKeyColor(byte r, byte g, byte b) =>
        r >= 210 && b >= 210 && g <= 80;

    private static string NormalizeHorizontalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center",
        };

    private static string NormalizeVerticalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "top" => "top",
            "middle" or "center" => "middle",
            _ => "bottom",
        };
}

internal sealed record SpriteSheetServerRenderResult(
    byte[] PngData,
    int Width,
    int Height,
    IReadOnlyList<SpriteSheetFrameView> Frames);

internal sealed record SpriteSheetServerPreviewResult(
    IReadOnlyList<SpriteSheetFrameView> Frames);
