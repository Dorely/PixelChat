namespace PixelChat.Art;

internal static class SpriteSheetServerRenderer
{
    private const long MaxPixels = 120_000_000;

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
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames)
    {
        var frames = NormalizeFrames(inputFrames, rows, columns, sourceWidth, sourceHeight);
        if (frames.Count == 0)
            return new SpriteSheetServerPreviewResult([]);

        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        cellWidth = Math.Clamp(cellWidth, 1, 8192);
        cellHeight = Math.Clamp(cellHeight, 1, 8192);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        _ = Math.Clamp(fps, 1, 60);
        if (frames.Count > rows * columns)
            throw new InvalidOperationException("Sprite frame count exceeds the configured sheet grid.");

        var savedFrames = new List<SpriteSheetFrameView>();
        foreach (var frame in frames)
        {
            var cellRect = CellRectForIndex(frame.Index, columns, cellWidth, cellHeight, gutter);
            var (destX, destY) = AlignedDestination(cellRect with { X = 0, Y = 0 }, frame.SourceRect, padding, horizontalAnchor, verticalAnchor);
            var spriteRect = new SpriteSheetRect(cellRect.X + destX, cellRect.Y + destY, frame.SourceRect.Width, frame.SourceRect.Height);
            var previewRgba = CropFrame(sourceRgba, sourceWidth, sourceHeight, frame, frames, background);
            var previewDataUrl = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(frame.SourceRect.Width, frame.SourceRect.Height, previewRgba));
            savedFrames.Add(new SpriteSheetFrameView(
                frame.Index,
                frame.Label,
                frame.SourceRect,
                frame.ShapePaths,
                cellRect,
                spriteRect,
                previewDataUrl,
                frame.SourceImageAssetId,
                frame.SourceImageRect));
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
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames)
    {
        var frames = NormalizeFrames(inputFrames, rows, columns, sourceWidth, sourceHeight);
        if (frames.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        cellWidth = Math.Clamp(cellWidth, 1, 8192);
        cellHeight = Math.Clamp(cellHeight, 1, 8192);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        _ = Math.Clamp(fps, 1, 60);
        if (frames.Count > rows * columns)
            throw new InvalidOperationException("Sprite frame count exceeds the configured sheet grid.");

        var outputWidth = checked((columns * cellWidth) + (Math.Max(0, columns - 1) * gutter));
        var outputHeight = checked((rows * cellHeight) + (Math.Max(0, rows - 1) * gutter));
        ValidateCanvasSize(outputWidth, outputHeight, "Sprite sheet output is too large.");

        var outputRgba = NewFilledCanvas(outputWidth, outputHeight, background);
        var savedFrames = new List<SpriteSheetFrameView>();
        foreach (var frame in frames)
        {
            var cellRect = CellRectForIndex(frame.Index, columns, cellWidth, cellHeight, gutter);
            var (relativeDestX, relativeDestY) = AlignedDestination(cellRect with { X = 0, Y = 0 }, frame.SourceRect, padding, horizontalAnchor, verticalAnchor);
            var destX = cellRect.X + relativeDestX;
            var destY = cellRect.Y + relativeDestY;
            CopyFrame(sourceRgba, sourceWidth, sourceHeight, frame, frames, background, outputRgba, outputWidth, outputHeight, destX, destY);

            var sourceOnOutput = new SpriteSheetRect(destX, destY, frame.SourceRect.Width, frame.SourceRect.Height);
            var spriteRect = IntersectRect(sourceOnOutput, outputWidth, outputHeight);
            var rebasedShapePaths = RebaseShapePaths(frame.ShapePaths, frame.SourceRect, destX, destY, outputWidth, outputHeight);
            var previewRgba = CropRect(outputRgba, outputWidth, outputHeight, cellRect, background);
            var previewDataUrl = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(cellRect.Width, cellRect.Height, previewRgba));
            savedFrames.Add(new SpriteSheetFrameView(
                frame.Index,
                frame.Label,
                spriteRect.Width > 0 && spriteRect.Height > 0 ? spriteRect : new SpriteSheetRect(cellRect.X, cellRect.Y, 1, 1),
                rebasedShapePaths,
                cellRect,
                spriteRect,
                previewDataUrl,
                frame.SourceImageAssetId,
                frame.SourceImageRect));
        }

        return new SpriteSheetServerRenderResult(
            SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba),
            outputWidth,
            outputHeight,
            savedFrames);
    }

    public static SpriteSheetFrameWorkingRenderResult ExtractFrameRegion(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames,
        int frameIndex,
        int margin)
    {
        var frames = NormalizeFrames(inputFrames, rows, columns, sourceWidth, sourceHeight);
        if (frameIndex < 0 || frameIndex >= frames.Count)
            throw new InvalidOperationException("Sprite frame index is outside the saved frame range.");

        margin = Math.Clamp(margin, 0, 1024);
        var frame = frames[frameIndex];
        var expanded = new SpriteSheetRect(
            frame.SourceRect.X - margin,
            frame.SourceRect.Y - margin,
            checked(frame.SourceRect.Width + (margin * 2)),
            checked(frame.SourceRect.Height + (margin * 2)));
        ValidateCanvasSize(expanded.Width, expanded.Height, "Isolated sprite frame is too large.");

        var output = NewFilledCanvas(expanded.Width, expanded.Height, background);
        var expandedFrame = frame with { SourceRect = expanded };
        CopyFrame(sourceRgba, sourceWidth, sourceHeight, expandedFrame, frames, background, output, expanded.Width, expanded.Height, 0, 0);
        return new SpriteSheetFrameWorkingRenderResult(
            SpriteSheetPngCodec.EncodeRgba(expanded.Width, expanded.Height, output),
            expanded.Width,
            expanded.Height);
    }

    public static SpriteSheetFrameWorkingRenderResult EraseRegions(
        byte[] sourceRgba,
        int width,
        int height,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetRect> rects,
        IReadOnlyList<SpriteSheetShapePath>? polygons,
        bool keepSelection = false)
    {
        ValidateCanvasSize(width, height, "Isolated sprite frame is too large.");
        if (sourceRgba.Length < width * height * 4)
            throw new InvalidOperationException("Isolated sprite frame pixels are incomplete.");

        var output = (byte[])sourceRgba.Clone();
        var normalizedRects = (rects ?? [])
            .Select(rect => IntersectRect(rect, width, height))
            .Where(rect => rect.Width > 0 && rect.Height > 0)
            .ToList();
        var normalizedPolygons = NormalizeShapePaths(polygons, width, height);
        if (normalizedRects.Count == 0 && normalizedPolygons.Count == 0)
        {
            if (keepSelection)
                throw new InvalidOperationException("Keep mode requires at least one rect or polygon that overlaps the working frame.");

            return new SpriteSheetFrameWorkingRenderResult(
                SpriteSheetPngCodec.EncodeRgba(width, height, output),
                width,
                height);
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var insideRect = normalizedRects.Any(rect =>
                    x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height);
                var insidePolygon = normalizedPolygons.Count > 0 && ContainsShape(normalizedPolygons, x + 0.5d, y + 0.5d);
                var selected = insideRect || insidePolygon;
                if (selected == keepSelection)
                    continue;

                WriteBackground(output, ((y * width) + x) * 4, background);
            }
        }

        return new SpriteSheetFrameWorkingRenderResult(
            SpriteSheetPngCodec.EncodeRgba(width, height, output),
            width,
            height);
    }

    public static SpriteSheetFrameWorkingRenderResult RenderRemovedPixelsOverlay(
        byte[] sourceRgba,
        SpriteSheetBackground sourceBackground,
        byte[] workingRgba,
        SpriteSheetBackground workingBackground,
        int width,
        int height)
    {
        ValidateCanvasSize(width, height, "Isolated sprite frame is too large.");
        if (sourceRgba.Length < width * height * 4 || workingRgba.Length < width * height * 4)
            throw new InvalidOperationException("Isolated sprite frame pixels are incomplete.");

        var output = (byte[])workingRgba.Clone();
        for (var index = 0; index < width * height * 4; index += 4)
        {
            var sourceForeground = SpriteSheetImageAnalyzer.IsForeground(
                sourceRgba[index], sourceRgba[index + 1], sourceRgba[index + 2], sourceRgba[index + 3], sourceBackground);
            var workingForeground = SpriteSheetImageAnalyzer.IsForeground(
                workingRgba[index], workingRgba[index + 1], workingRgba[index + 2], workingRgba[index + 3], workingBackground);
            if (!sourceForeground || workingForeground)
                continue;

            output[index] = 255;
            output[index + 1] = 0;
            output[index + 2] = 0;
            output[index + 3] = 255;
        }

        return new SpriteSheetFrameWorkingRenderResult(
            SpriteSheetPngCodec.EncodeRgba(width, height, output),
            width,
            height);
    }

    public static SpriteSheetFrameWorkingRenderResult RenderCoordinateGridOverlay(byte[] sourceRgba, int width, int height)
    {
        ValidateCanvasSize(width, height, "Isolated sprite frame is too large.");
        if (sourceRgba.Length < width * height * 4)
            throw new InvalidOperationException("Isolated sprite frame pixels are incomplete.");

        var output = (byte[])sourceRgba.Clone();
        var step = Math.Min(width, height) < 128 ? 16 : 32;
        for (var x = step; x < width; x += step)
        {
            for (var y = 0; y < height; y++)
                BlendPixel(output, ((y * width) + x) * 4, 0, 0, 0, 90);
        }

        for (var y = step; y < height; y += step)
        {
            for (var x = 0; x < width; x++)
                BlendPixel(output, ((y * width) + x) * 4, 0, 0, 0, 90);
        }

        var labelStep = step * Math.Max(1, (int)Math.Ceiling(34d / step));
        for (var x = labelStep; x < width; x += labelStep)
            DrawCoordinateLabel(output, width, height, x, x + 1, 1);
        for (var y = labelStep; y < height; y += labelStep)
            DrawCoordinateLabel(output, width, height, y, 1, y + 1);

        return new SpriteSheetFrameWorkingRenderResult(
            SpriteSheetPngCodec.EncodeRgba(width, height, output),
            width,
            height);
    }

    private static void DrawCoordinateLabel(byte[] rgba, int width, int height, int value, int x, int y)
    {
        var digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        const int scale = 1;
        var digitWidth = 3 * scale;
        var digitHeight = 5 * scale;
        var gap = scale;
        var textWidth = (digits.Length * digitWidth) + (Math.Max(0, digits.Length - 1) * gap);
        var barWidth = textWidth + 2;
        var barHeight = digitHeight + 2;
        x = Math.Clamp(x, 0, Math.Max(0, width - barWidth));
        y = Math.Clamp(y, 0, Math.Max(0, height - barHeight));
        FillRect(rgba, width, height, x, y, barWidth, barHeight, 0, 0, 0, 200);

        var cursor = x + 1;
        foreach (var digit in digits)
        {
            DrawDigit(rgba, width, height, cursor, y + 1, scale, digit);
            cursor += digitWidth + gap;
        }
    }

    public static SpriteSheetReassembleRenderResult ReassembleIrregularFrames(
        int rows,
        int columns,
        int padding,
        int gutter,
        string? horizontalAnchor,
        string? verticalAnchor,
        SpriteSheetBackground sheetBackground,
        IReadOnlyList<SpriteSheetFrameImageInput> inputFrames)
    {
        if (inputFrames.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        if (rows * columns < inputFrames.Count)
            columns = Math.Clamp((int)Math.Ceiling(inputFrames.Count / (double)rows), 1, 64);
        if (rows * columns < inputFrames.Count)
            rows = Math.Clamp((int)Math.Ceiling(inputFrames.Count / (double)columns), 1, 32);
        if (rows * columns < inputFrames.Count)
            throw new InvalidOperationException("Sprite frame count exceeds the configured sheet grid.");

        var prepared = inputFrames
            .Select(input =>
            {
                ValidateCanvasSize(input.Width, input.Height, "Isolated sprite frame is too large.");
                if (input.Rgba.Length < input.Width * input.Height * 4)
                    throw new InvalidOperationException("Isolated sprite frame pixels are incomplete.");

                var preserveFullCanvas = string.Equals(input.WorkingState, "stabilized", StringComparison.OrdinalIgnoreCase);
                var detectionBackground = preserveFullCanvas
                    ? sheetBackground
                    : input.UsedWorkingImage
                    ? SpriteSheetImageAnalyzer.ResolveBackground(input.Rgba, input.Width, input.Height)
                    : sheetBackground;
                var warnings = new List<string>();
                SpriteSheetRect bounds;
                if (preserveFullCanvas)
                {
                    bounds = new SpriteSheetRect(0, 0, input.Width, input.Height);
                }
                else
                {
                    var foregroundBounds = SpriteSheetImageAnalyzer.ForegroundBounds(input.Rgba, input.Width, input.Height, detectionBackground);
                    if (foregroundBounds is null)
                    {
                        warnings.Add("no foreground was detected; used the full frame canvas");
                        bounds = new SpriteSheetRect(0, 0, input.Width, input.Height);
                    }
                    else
                    {
                        bounds = foregroundBounds;
                    }
                }

                return new PreparedReassembleFrame(input, detectionBackground, bounds, preserveFullCanvas, warnings);
            })
            .ToList();

        var medianBoundsWidth = MedianValue(prepared.Select(frame => frame.Bounds.Width));
        var medianBoundsHeight = MedianValue(prepared.Select(frame => frame.Bounds.Height));
        foreach (var (frame, index) in prepared.Select((frame, index) => (frame, index)))
        {
            if (frame.PreserveFullCanvas)
                continue;

            if ((medianBoundsWidth > 0 && frame.Bounds.Width > medianBoundsWidth * 3 / 2)
                || (medianBoundsHeight > 0 && frame.Bounds.Height > medianBoundsHeight * 3 / 2))
            {
                frame.Warnings.Add(
                    $"detected foreground bounds {frame.Bounds.Width}x{frame.Bounds.Height} are much larger than the median {medianBoundsWidth}x{medianBoundsHeight}; stray edge artifacts may be inflating this frame's cell size and placement");
            }
        }

        var frameWidth = Math.Max(1, prepared.Max(frame => frame.Bounds.Width) + (padding * 2));
        var frameHeight = Math.Max(1, prepared.Max(frame => frame.Bounds.Height) + (padding * 2));
        var outputWidth = checked((columns * frameWidth) + (Math.Max(0, columns - 1) * gutter));
        var outputHeight = checked((rows * frameHeight) + (Math.Max(0, rows - 1) * gutter));
        ValidateCanvasSize(outputWidth, outputHeight, "Sprite sheet output is too large.");

        var outputRgba = NewFilledCanvas(outputWidth, outputHeight, sheetBackground);
        var savedFrames = new List<SpriteSheetFrameView>();
        var frameInfos = new List<SpriteSheetReassembleFrameRenderInfo>();
        var warnings = new List<string>();
        for (var index = 0; index < prepared.Count; index++)
        {
            var frame = prepared[index];
            var cellRect = CellRectForIndex(index, columns, frameWidth, frameHeight, gutter);
            var (destX, destY) = AlignedDestination(cellRect, frame.Bounds, padding, horizontalAnchor, verticalAnchor);
            var placedRect = new SpriteSheetRect(destX, destY, frame.Bounds.Width, frame.Bounds.Height);
            var clippedPlacedRect = IntersectRect(placedRect, outputWidth, outputHeight);
            if (clippedPlacedRect.Width != placedRect.Width || clippedPlacedRect.Height != placedRect.Height)
                frame.Warnings.Add("foreground was clipped during reassembly");

            if (frame.PreserveFullCanvas)
            {
                CopyImage(
                    frame.Input.Rgba,
                    frame.Input.Width,
                    frame.Input.Height,
                    outputRgba,
                    outputWidth,
                    outputHeight,
                    destX,
                    destY);
            }
            else
            {
                CopyForegroundBounds(
                    frame.Input.Rgba,
                    frame.Input.Width,
                    frame.Input.Height,
                    frame.Bounds,
                    frame.Background,
                    outputRgba,
                    outputWidth,
                    outputHeight,
                    destX,
                    destY);
            }

            var label = string.IsNullOrWhiteSpace(frame.Input.Label) ? $"Frame {index + 1}" : frame.Input.Label.Trim();
            var previewRgba = CropRect(outputRgba, outputWidth, outputHeight, cellRect, sheetBackground);
            var previewDataUrl = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(cellRect.Width, cellRect.Height, previewRgba));
            savedFrames.Add(new SpriteSheetFrameView(
                index,
                label,
                cellRect,
                [],
                cellRect,
                clippedPlacedRect.Width > 0 && clippedPlacedRect.Height > 0 ? clippedPlacedRect : new SpriteSheetRect(cellRect.X, cellRect.Y, 1, 1),
                previewDataUrl,
                frame.Input.SourceImageAssetId,
                frame.Input.SourceImageRect));

            if (frame.Warnings.Count > 0)
                warnings.AddRange(frame.Warnings.Select(warning => $"Frame {index + 1}: {warning}"));
            frameInfos.Add(new SpriteSheetReassembleFrameRenderInfo(
                index,
                label,
                frame.Input.UsedWorkingImage,
                frame.Bounds,
                clippedPlacedRect.Width > 0 && clippedPlacedRect.Height > 0 ? clippedPlacedRect : new SpriteSheetRect(cellRect.X, cellRect.Y, 1, 1),
                frame.Warnings));
        }

        return new SpriteSheetReassembleRenderResult(
            SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba),
            outputWidth,
            outputHeight,
            frameWidth,
            frameHeight,
            rows,
            columns,
            savedFrames,
            frameInfos,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    internal static SpriteSheetReviewRenderResult BuildAnimationReview(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int padding,
        int gutter,
        string? horizontalAnchor,
        string? verticalAnchor,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames,
        bool loop,
        int maxFrames)
    {
        var frames = NormalizeFrames(inputFrames, rows, columns, sourceWidth, sourceHeight)
            .Take(Math.Clamp(maxFrames <= 0 ? 12 : maxFrames, 1, 24))
            .ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");
        if (sourceRgba.Length < sourceWidth * (long)sourceHeight * 4)
            throw new InvalidOperationException("Sprite animation review source pixels are incomplete.");

        cellWidth = Math.Clamp(cellWidth, 1, 8192);
        cellHeight = Math.Clamp(cellHeight, 1, 8192);
        padding = Math.Clamp(padding, 0, 4096);
        gutter = Math.Clamp(gutter, 0, 4096);
        ValidateCanvasSize(cellWidth, cellHeight, "Sprite animation review images are too large.");

        var metricFrames = new List<SpriteAnimationFramePixels>();
        var images = new List<SpriteSheetReviewImage>();
        foreach (var frame in frames)
        {
            var cellRgba = NewFilledCanvas(cellWidth, cellHeight, background);
            var cell = new SpriteSheetRect(0, 0, cellWidth, cellHeight);
            var (destX, destY) = AlignedDestination(cell, frame.SourceRect, padding, horizontalAnchor, verticalAnchor);
            CopyFrame(sourceRgba, sourceWidth, sourceHeight, frame, frames, background, cellRgba, cellWidth, cellHeight, destX, destY);
            var label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label;
            metricFrames.Add(new SpriteAnimationFramePixels(frame.Index, label, cellWidth, cellHeight, cellRgba));

            var labeled = (byte[])cellRgba.Clone();
            DrawIndexLabel(labeled, cellWidth, cellHeight, frame.Index, 0, 0);
            images.Add(new SpriteSheetReviewImage(
                label,
                $"sprite-frame-{frame.Index + 1}.png",
                "frame",
                frame.Index,
                null,
                null,
                SpriteSheetPngCodec.EncodeRgba(cellWidth, cellHeight, labeled)));
        }

        images.Insert(0, BuildAnnotatedSheetView(
            sourceRgba,
            sourceWidth,
            sourceHeight,
            rows,
            columns,
            cellWidth,
            cellHeight,
            gutter,
            background,
            frames,
            "Sprite sheet view",
            "sprite-sheet-view.png"));

        for (var index = 0; index < metricFrames.Count - 1; index++)
            images.Add(BuildPairDiff(metricFrames[index], metricFrames[index + 1], background));
        if (loop && metricFrames.Count > 1)
            images.Add(BuildPairDiff(metricFrames[^1], metricFrames[0], background));

        var onion = BuildOnionSkin(metricFrames, cellWidth, cellHeight, background);
        DrawIndexSequence(onion, cellWidth, cellHeight, metricFrames.Select(frame => frame.Index).ToList());
        images.Add(new SpriteSheetReviewImage(
            "Onion-skin overlay",
            "sprite-animation-onion-skin.png",
            "onion-skin",
            null,
            null,
            null,
            SpriteSheetPngCodec.EncodeRgba(cellWidth, cellHeight, onion)));

        var filmstrip = BuildFilmstrip(metricFrames, cellWidth, cellHeight, background);
        images.Add(new SpriteSheetReviewImage(
            "Filmstrip, left-to-right frames 1..N",
            "sprite-animation-filmstrip.png",
            "filmstrip",
            null,
            null,
            null,
            SpriteSheetPngCodec.EncodeRgba(checked((cellWidth * metricFrames.Count) + Math.Max(0, metricFrames.Count - 1)), cellHeight, filmstrip)));

        return new SpriteSheetReviewRenderResult(metricFrames, images);
    }

    internal static SpriteSheetReviewImage BuildDetectionAnnotatedSheetView(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetDetectionResult detection)
    {
        var frames = detection.Frames
            .Select((frame, index) => new RenderFrame(
                index,
                $"Frame {index + 1}",
                NormalizeSourceRect(frame.SourceRect),
                NormalizeShapePaths(frame.ShapePaths, sourceWidth, sourceHeight)))
            .ToList();
        var cellWidth = Math.Max(1, sourceWidth / Math.Max(1, detection.Columns));
        var cellHeight = Math.Max(1, sourceHeight / Math.Max(1, detection.Rows));
        return BuildAnnotatedSheetView(
            sourceRgba,
            sourceWidth,
            sourceHeight,
            detection.Rows,
            detection.Columns,
            cellWidth,
            cellHeight,
            0,
            detection.Background,
            frames,
            "Detected sprite-sheet frames with frame numbers",
            "sprite-sheet-detection-numbered-frames.png",
            detection.RejectedSegments);
    }

    internal static SpriteSheetReviewImage BuildRepairAnnotatedSheetView(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int gutter,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames,
        IReadOnlyList<SpriteSheetRejectedSegmentView> rejectedSegments)
    {
        var frames = inputFrames
            .Select((frame, index) => new RenderFrame(
                index,
                string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label,
                NormalizeSourceRect(frame.SourceRect),
                NormalizeShapePaths(frame.ShapePaths, sourceWidth, sourceHeight)))
            .ToList();
        return BuildAnnotatedSheetView(
            sourceRgba,
            sourceWidth,
            sourceHeight,
            rows,
            columns,
            cellWidth,
            cellHeight,
            gutter,
            background,
            frames,
            "Sprite-sheet repair view with rejected segments",
            "sprite-sheet-repair-numbered-frames.png",
            rejectedSegments);
    }

    internal static SpriteSheetFrameWorkingRenderResult RenderPlacedFrameImage(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetBackground background,
        int outputWidth,
        int outputHeight,
        SpriteSheetRect placement)
    {
        outputWidth = Math.Max(1, outputWidth);
        outputHeight = Math.Max(1, outputHeight);
        ValidateCanvasSize(outputWidth, outputHeight, "Stabilized sprite frame is too large.");
        var output = NewFilledCanvas(outputWidth, outputHeight, background);
        CopyImage(sourceRgba, sourceWidth, sourceHeight, output, outputWidth, outputHeight, placement.X, placement.Y);
        return new SpriteSheetFrameWorkingRenderResult(
            SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, output),
            outputWidth,
            outputHeight);
    }

    private static SpriteSheetReviewImage BuildAnnotatedSheetView(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int gutter,
        SpriteSheetBackground background,
        IReadOnlyList<RenderFrame> frames,
        string label,
        string fileName,
        IReadOnlyList<SpriteSheetRejectedSegmentView>? rejectedSegments = null)
    {
        ValidateCanvasSize(sourceWidth, sourceHeight, "Annotated sprite sheet image is too large.");
        var output = NewFilledCanvas(sourceWidth, sourceHeight, background);
        Array.Copy(sourceRgba, output, Math.Min(sourceRgba.Length, output.Length));

        DrawGrid(output, sourceWidth, sourceHeight, rows, columns, cellWidth, cellHeight, gutter);
        foreach (var segment in rejectedSegments ?? Array.Empty<SpriteSheetRejectedSegmentView>())
        {
            DrawRectangle(output, sourceWidth, sourceHeight, segment.Rect, 239, 68, 68, 240, 3);
            DrawIndexLabel(output, sourceWidth, sourceHeight, segment.Index, segment.Rect.X, segment.Rect.Y);
        }

        foreach (var frame in frames)
        {
            DrawRectangle(output, sourceWidth, sourceHeight, frame.SourceRect, 31, 111, 235, 230, 2);
            DrawShapePaths(output, sourceWidth, sourceHeight, frame.ShapePaths, 32, 210, 115, 240, 2);
            DrawIndexLabel(output, sourceWidth, sourceHeight, frame.Index, frame.SourceRect.X, frame.SourceRect.Y);
        }

        return new SpriteSheetReviewImage(
            label,
            fileName,
            "sheet-view",
            null,
            null,
            null,
            SpriteSheetPngCodec.EncodeRgba(sourceWidth, sourceHeight, output));
    }

    private static SpriteSheetReviewImage BuildPairDiff(
        SpriteAnimationFramePixels from,
        SpriteAnimationFramePixels to,
        SpriteSheetBackground background)
    {
        var width = Math.Max(from.Width, to.Width);
        var height = Math.Max(from.Height, to.Height);
        ValidateCanvasSize(width, height, "Sprite animation diff image is too large.");
        var output = NewFilledCanvas(width, height, background);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var fromPixel = TryGetPixel(from, x, y, out var fr, out var fg, out var fb, out var fa)
                    && SpriteSheetImageAnalyzer.IsForeground(fr, fg, fb, fa, background);
                var toPixel = TryGetPixel(to, x, y, out var tr, out var tg, out var tb, out var ta)
                    && SpriteSheetImageAnalyzer.IsForeground(tr, tg, tb, ta, background);
                if (!fromPixel && !toPixel)
                    continue;

                var targetIndex = ((y * width) + x) * 4;
                if (fromPixel && toPixel)
                    SetPixel(output, targetIndex, 190, 190, 190, 210);
                else if (fromPixel)
                    SetPixel(output, targetIndex, 230, 65, 80, 220);
                else
                    SetPixel(output, targetIndex, 35, 190, 220, 220);
            }
        }

        DrawIndexLabel(output, width, height, from.Index, 0, 0);
        DrawIndexLabel(output, width, height, to.Index, Math.Max(0, width / 2), 0);
        return new SpriteSheetReviewImage(
            $"Frame {from.Index + 1} vs {to.Index + 1}",
            $"sprite-diff-{from.Index + 1}-vs-{to.Index + 1}.png",
            "pair-diff",
            null,
            from.Index,
            to.Index,
            SpriteSheetPngCodec.EncodeRgba(width, height, output));
    }

    private static byte[] BuildOnionSkin(IReadOnlyList<SpriteAnimationFramePixels> frames, int width, int height, SpriteSheetBackground background)
    {
        var output = NewFilledCanvas(width, height, background);
        var overlayAlpha = Math.Clamp(180 / Math.Max(1, frames.Count), 28, 96);
        foreach (var frame in frames)
        {
            for (var y = 0; y < Math.Min(height, frame.Height); y++)
            {
                for (var x = 0; x < Math.Min(width, frame.Width); x++)
                {
                    var sourceIndex = ((y * frame.Width) + x) * 4;
                    if (!SpriteSheetImageAnalyzer.IsForeground(frame.Rgba[sourceIndex], frame.Rgba[sourceIndex + 1], frame.Rgba[sourceIndex + 2], frame.Rgba[sourceIndex + 3], background))
                        continue;

                    var alpha = Math.Min(frame.Rgba[sourceIndex + 3], overlayAlpha);
                    BlendPixel(output, ((y * width) + x) * 4, frame.Rgba[sourceIndex], frame.Rgba[sourceIndex + 1], frame.Rgba[sourceIndex + 2], (byte)alpha);
                }
            }
        }

        return output;
    }

    private static byte[] BuildFilmstrip(IReadOnlyList<SpriteAnimationFramePixels> frames, int frameWidth, int frameHeight, SpriteSheetBackground background)
    {
        var outputWidth = checked((frameWidth * frames.Count) + Math.Max(0, frames.Count - 1));
        var output = NewFilledCanvas(outputWidth, frameHeight, background);
        for (var separator = 1; separator < frames.Count; separator++)
        {
            var x = (separator * frameWidth) + separator - 1;
            for (var y = 0; y < frameHeight; y++)
                SetPixel(output, ((y * outputWidth) + x) * 4, 160, 160, 160, byte.MaxValue);
        }

        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            var destX = frameIndex * (frameWidth + 1);
            for (var y = 0; y < Math.Min(frameHeight, frame.Height); y++)
            {
                for (var x = 0; x < Math.Min(frameWidth, frame.Width); x++)
                {
                    var sourceIndex = ((y * frame.Width) + x) * 4;
                    var targetIndex = ((y * outputWidth) + destX + x) * 4;
                    output[targetIndex] = frame.Rgba[sourceIndex];
                    output[targetIndex + 1] = frame.Rgba[sourceIndex + 1];
                    output[targetIndex + 2] = frame.Rgba[sourceIndex + 2];
                    output[targetIndex + 3] = frame.Rgba[sourceIndex + 3];
                }
            }

            DrawIndexLabel(output, outputWidth, frameHeight, frame.Index, destX, 0);
        }

        return output;
    }

    private static void CopyFrame(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        RenderFrame frame,
        IReadOnlyList<RenderFrame> allFrames,
        SpriteSheetBackground background,
        byte[] target,
        int targetWidth,
        int targetHeight,
        int destX,
        int destY)
    {
        var neighbors = IntersectingNeighborShapes(frame, allFrames);
        var currentHasShape = frame.ShapePaths.Count > 0;
        for (var y = 0; y < frame.SourceRect.Height; y++)
        {
            var targetY = destY + y;
            if (targetY < 0 || targetY >= targetHeight)
                continue;

            for (var x = 0; x < frame.SourceRect.Width; x++)
            {
                var targetX = destX + x;
                if (targetX < 0 || targetX >= targetWidth)
                    continue;

                var sourceX = frame.SourceRect.X + x;
                var sourceY = frame.SourceRect.Y + y;
                var targetIndex = ((targetY * targetWidth) + targetX) * 4;
                if (sourceX < 0 || sourceX >= sourceWidth || sourceY < 0 || sourceY >= sourceHeight)
                {
                    WriteBackground(target, targetIndex, background);
                    continue;
                }

                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                if (ShouldReplaceWithBackground(
                    source[sourceIndex],
                    source[sourceIndex + 1],
                    source[sourceIndex + 2],
                    source[sourceIndex + 3],
                    sourceX + 0.5d,
                    sourceY + 0.5d,
                    frame.ShapePaths,
                    currentHasShape,
                    neighbors,
                    background))
                {
                    WriteBackground(target, targetIndex, background);
                    continue;
                }

                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
                target[targetIndex + 3] = source[sourceIndex + 3];
            }
        }
    }

    private static byte[] CropFrame(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        RenderFrame frame,
        IReadOnlyList<RenderFrame> allFrames,
        SpriteSheetBackground background)
    {
        ValidateCanvasSize(frame.SourceRect.Width, frame.SourceRect.Height, "Sprite frame preview is too large.");
        var output = NewFilledCanvas(frame.SourceRect.Width, frame.SourceRect.Height, background);
        CopyFrame(source, sourceWidth, sourceHeight, frame, allFrames, background, output, frame.SourceRect.Width, frame.SourceRect.Height, 0, 0);
        return output;
    }

    private static byte[] CropRect(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetRect rect,
        SpriteSheetBackground background)
    {
        ValidateCanvasSize(rect.Width, rect.Height, "Sprite frame preview is too large.");
        var output = NewFilledCanvas(rect.Width, rect.Height, background);
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
                var targetIndex = ((y * rect.Width) + x) * 4;
                output[targetIndex] = source[sourceIndex];
                output[targetIndex + 1] = source[sourceIndex + 1];
                output[targetIndex + 2] = source[sourceIndex + 2];
                output[targetIndex + 3] = source[sourceIndex + 3];
            }
        }

        return output;
    }

    private static void CopyImage(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        byte[] target,
        int targetWidth,
        int targetHeight,
        int destX,
        int destY)
    {
        for (var y = 0; y < sourceHeight; y++)
        {
            var targetY = destY + y;
            if (targetY < 0 || targetY >= targetHeight)
                continue;

            for (var x = 0; x < sourceWidth; x++)
            {
                var targetX = destX + x;
                if (targetX < 0 || targetX >= targetWidth)
                    continue;

                var sourceIndex = ((y * sourceWidth) + x) * 4;
                var targetIndex = ((targetY * targetWidth) + targetX) * 4;
                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
                target[targetIndex + 3] = source[sourceIndex + 3];
            }
        }
    }

    private static void CopyForegroundBounds(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetRect bounds,
        SpriteSheetBackground sourceBackground,
        byte[] target,
        int targetWidth,
        int targetHeight,
        int destX,
        int destY)
    {
        for (var y = 0; y < bounds.Height; y++)
        {
            var sourceY = bounds.Y + y;
            var targetY = destY + y;
            if (sourceY < 0 || sourceY >= sourceHeight || targetY < 0 || targetY >= targetHeight)
                continue;

            for (var x = 0; x < bounds.Width; x++)
            {
                var sourceX = bounds.X + x;
                var targetX = destX + x;
                if (sourceX < 0 || sourceX >= sourceWidth || targetX < 0 || targetX >= targetWidth)
                    continue;

                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                if (!SpriteSheetImageAnalyzer.IsForeground(
                    source[sourceIndex],
                    source[sourceIndex + 1],
                    source[sourceIndex + 2],
                    source[sourceIndex + 3],
                    sourceBackground))
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

    private static bool ShouldReplaceWithBackground(
        byte r,
        byte g,
        byte b,
        byte a,
        double sourceX,
        double sourceY,
        IReadOnlyList<SpriteSheetShapePath> ownShapePaths,
        bool currentHasShape,
        IReadOnlyList<IReadOnlyList<SpriteSheetShapePath>> neighborShapePaths,
        SpriteSheetBackground background)
    {
        if (!SpriteSheetImageAnalyzer.IsForeground(r, g, b, a, background))
            return false;
        if (currentHasShape && ContainsShape(ownShapePaths, sourceX, sourceY))
            return false;

        return neighborShapePaths.Any(paths => ContainsShape(paths, sourceX, sourceY));
    }

    private static IReadOnlyList<IReadOnlyList<SpriteSheetShapePath>> IntersectingNeighborShapes(
        RenderFrame frame,
        IReadOnlyList<RenderFrame> allFrames)
    {
        return allFrames
            .Where(neighbor => neighbor.Index != frame.Index && neighbor.ShapePaths.Count > 0 && RectsIntersect(frame.SourceRect, ShapeBounds(neighbor.ShapePaths)))
            .Select(neighbor => neighbor.ShapePaths)
            .ToList();
    }

    private static List<RenderFrame> NormalizeFrames(
        IReadOnlyList<SpriteSheetFrameUpdateView> inputFrames,
        int rows,
        int columns,
        int sourceWidth,
        int sourceHeight)
    {
        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        if (inputFrames.Count > rows * columns)
            throw new InvalidOperationException("Sprite frame count exceeds the configured sheet grid.");

        return inputFrames
            .Select((frame, index) => new RenderFrame(
                index,
                string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label.Trim(),
                NormalizeSourceRect(frame.SourceRect),
                NormalizeShapePaths(frame.ShapePaths, sourceWidth, sourceHeight),
                frame.SourceImageAssetId,
                frame.SourceImageRect is null ? null : NormalizeSourceRect(frame.SourceImageRect)))
            .ToList();
    }

    private static SpriteSheetRect NormalizeSourceRect(SpriteSheetRect rect) =>
        new(
            Math.Clamp(rect.X, -32768, 32767),
            Math.Clamp(rect.Y, -32768, 32767),
            Math.Clamp(rect.Width, 1, 8192),
            Math.Clamp(rect.Height, 1, 8192));

    private static SpriteSheetRect CellRectForIndex(int index, int columns, int cellWidth, int cellHeight, int gutter)
    {
        var row = index / columns;
        var column = index % columns;
        return new SpriteSheetRect(
            column * (cellWidth + gutter),
            row * (cellHeight + gutter),
            cellWidth,
            cellHeight);
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

        var maxX = cellRect.X + cellRect.Width - sourceRect.Width;
        var maxY = cellRect.Y + cellRect.Height - sourceRect.Height;
        x = maxX < cellRect.X ? cellRect.X : Math.Clamp(x, cellRect.X, maxX);
        y = maxY < cellRect.Y ? cellRect.Y : Math.Clamp(y, cellRect.Y, maxY);
        return (x, y);
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

    private static SpriteSheetRect ShapeBounds(IReadOnlyList<SpriteSheetShapePath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToList();
        if (points.Count == 0)
            return new SpriteSheetRect(0, 0, 1, 1);

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return new SpriteSheetRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private static bool RectsIntersect(SpriteSheetRect left, SpriteSheetRect right) =>
        left.X < right.X + right.Width
        && left.X + left.Width > right.X
        && left.Y < right.Y + right.Height
        && left.Y + left.Height > right.Y;

    private static SpriteSheetRect IntersectRect(SpriteSheetRect rect, int width, int height)
    {
        var x1 = Math.Clamp(rect.X, 0, width);
        var y1 = Math.Clamp(rect.Y, 0, height);
        var x2 = Math.Clamp(rect.X + rect.Width, 0, width);
        var y2 = Math.Clamp(rect.Y + rect.Height, 0, height);
        return new SpriteSheetRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private static byte[] NewFilledCanvas(int width, int height, SpriteSheetBackground background)
    {
        ValidateCanvasSize(width, height, "Image is too large.");
        var rgba = new byte[checked(width * height * 4)];
        FillRgba(rgba, background);
        return rgba;
    }

    private static void FillRgba(byte[] rgba, SpriteSheetBackground background)
    {
        for (var index = 0; index < rgba.Length; index += 4)
            WriteBackground(rgba, index, background);
    }

    private static void WriteBackground(byte[] target, int index, SpriteSheetBackground background)
    {
        target[index] = background.R;
        target[index + 1] = background.G;
        target[index + 2] = background.B;
        target[index + 3] = background.Mode.Equals("alpha", StringComparison.OrdinalIgnoreCase) ? (byte)0 : background.A;
    }

    private static void SetPixel(byte[] target, int index, byte r, byte g, byte b, byte a)
    {
        target[index] = r;
        target[index + 1] = g;
        target[index + 2] = b;
        target[index + 3] = a;
    }

    private static void BlendPixel(byte[] target, int targetIndex, byte r, byte g, byte b, byte a)
    {
        var sourceAlpha = a / 255d;
        var targetAlpha = target[targetIndex + 3] / 255d;
        var outputAlpha = sourceAlpha + (targetAlpha * (1 - sourceAlpha));
        if (outputAlpha <= 0)
            return;

        target[targetIndex] = (byte)Math.Round(((r * sourceAlpha) + (target[targetIndex] * targetAlpha * (1 - sourceAlpha))) / outputAlpha);
        target[targetIndex + 1] = (byte)Math.Round(((g * sourceAlpha) + (target[targetIndex + 1] * targetAlpha * (1 - sourceAlpha))) / outputAlpha);
        target[targetIndex + 2] = (byte)Math.Round(((b * sourceAlpha) + (target[targetIndex + 2] * targetAlpha * (1 - sourceAlpha))) / outputAlpha);
        target[targetIndex + 3] = (byte)Math.Round(outputAlpha * 255);
    }

    private static bool TryGetPixel(SpriteAnimationFramePixels frame, int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        r = 0;
        g = 0;
        b = 0;
        a = 0;
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
            return false;

        var index = ((y * frame.Width) + x) * 4;
        r = frame.Rgba[index];
        g = frame.Rgba[index + 1];
        b = frame.Rgba[index + 2];
        a = frame.Rgba[index + 3];
        return true;
    }

    private static void DrawGrid(byte[] rgba, int width, int height, int rows, int columns, int cellWidth, int cellHeight, int gutter)
    {
        if (rows <= 0 || columns <= 0 || cellWidth <= 0 || cellHeight <= 0)
            return;

        for (var column = 1; column < columns; column++)
        {
            var x = column * cellWidth + (column - 1) * gutter;
            DrawLine(rgba, width, height, x, 0, x, height - 1, 245, 159, 0, 210, 1);
            if (gutter > 0)
                DrawLine(rgba, width, height, x + gutter, 0, x + gutter, height - 1, 245, 159, 0, 150, 1);
        }

        for (var row = 1; row < rows; row++)
        {
            var y = row * cellHeight + (row - 1) * gutter;
            DrawLine(rgba, width, height, 0, y, width - 1, y, 245, 159, 0, 210, 1);
            if (gutter > 0)
                DrawLine(rgba, width, height, 0, y + gutter, width - 1, y + gutter, 245, 159, 0, 150, 1);
        }
    }

    private static void DrawRectangle(byte[] rgba, int width, int height, SpriteSheetRect rect, byte r, byte g, byte b, byte a, int thickness)
    {
        for (var offset = 0; offset < thickness; offset++)
        {
            DrawLine(rgba, width, height, rect.X, rect.Y + offset, rect.X + rect.Width - 1, rect.Y + offset, r, g, b, a, 1);
            DrawLine(rgba, width, height, rect.X, rect.Y + rect.Height - 1 - offset, rect.X + rect.Width - 1, rect.Y + rect.Height - 1 - offset, r, g, b, a, 1);
            DrawLine(rgba, width, height, rect.X + offset, rect.Y, rect.X + offset, rect.Y + rect.Height - 1, r, g, b, a, 1);
            DrawLine(rgba, width, height, rect.X + rect.Width - 1 - offset, rect.Y, rect.X + rect.Width - 1 - offset, rect.Y + rect.Height - 1, r, g, b, a, 1);
        }
    }

    private static void DrawShapePaths(
        byte[] rgba,
        int width,
        int height,
        IReadOnlyList<SpriteSheetShapePath> shapePaths,
        byte r,
        byte g,
        byte b,
        byte a,
        int thickness)
    {
        foreach (var path in shapePaths)
        {
            var points = path.Points;
            if (points.Count < 2)
                continue;

            for (var index = 0; index < points.Count; index++)
            {
                var start = points[index];
                var end = points[(index + 1) % points.Count];
                DrawLine(rgba, width, height, start.X, start.Y, end.X, end.Y, r, g, b, a, thickness);
            }
        }
    }

    private static void DrawLine(byte[] rgba, int width, int height, int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a, int thickness)
    {
        var dx = Math.Abs(x2 - x1);
        var sx = x1 < x2 ? 1 : -1;
        var dy = -Math.Abs(y2 - y1);
        var sy = y1 < y2 ? 1 : -1;
        var error = dx + dy;
        var x = x1;
        var y = y1;
        while (true)
        {
            FillRect(rgba, width, height, x, y, thickness, thickness, r, g, b, a);
            if (x == x2 && y == y2)
                break;

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    private static void FillRect(byte[] rgba, int width, int height, int x, int y, int rectWidth, int rectHeight, byte r, byte g, byte b, byte a)
    {
        var startX = Math.Clamp(x, 0, width);
        var startY = Math.Clamp(y, 0, height);
        var endX = Math.Clamp(x + rectWidth, 0, width);
        var endY = Math.Clamp(y + rectHeight, 0, height);
        for (var py = startY; py < endY; py++)
        {
            for (var px = startX; px < endX; px++)
                BlendPixel(rgba, ((py * width) + px) * 4, r, g, b, a);
        }
    }

    private static void DrawIndexSequence(byte[] rgba, int width, int height, IReadOnlyList<int> indexes)
    {
        var x = 0;
        foreach (var index in indexes)
        {
            DrawIndexLabel(rgba, width, height, index, x, 0);
            x += 24;
            if (x >= width)
                break;
        }
    }

    private static void DrawIndexLabel(byte[] rgba, int width, int height, int index, int x, int y)
    {
        var digits = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var scale = Math.Clamp(Math.Min(width, height) / 80, 2, 6);
        var digitWidth = 3 * scale;
        var digitHeight = 5 * scale;
        var gap = scale;
        var textWidth = (digits.Length * digitWidth) + Math.Max(0, digits.Length - 1) * gap;
        var barWidth = textWidth + (4 * scale);
        var barHeight = digitHeight + (4 * scale);
        x = Math.Clamp(x, 0, Math.Max(0, width - barWidth));
        y = Math.Clamp(y, 0, Math.Max(0, height - barHeight));
        FillRect(rgba, width, height, x, y, barWidth, barHeight, 0, 0, 0, 220);

        var cursor = x + (2 * scale);
        var top = y + (2 * scale);
        foreach (var digit in digits)
        {
            DrawDigit(rgba, width, height, cursor, top, scale, digit);
            cursor += digitWidth + gap;
        }
    }

    private static void DrawDigit(byte[] rgba, int width, int height, int x, int y, int scale, char digit)
    {
        var rows = digit switch
        {
            '0' => new[] { "111", "101", "101", "101", "111" },
            '1' => new[] { "010", "110", "010", "010", "111" },
            '2' => new[] { "111", "001", "111", "100", "111" },
            '3' => new[] { "111", "001", "111", "001", "111" },
            '4' => new[] { "101", "101", "111", "001", "001" },
            '5' => new[] { "111", "100", "111", "001", "111" },
            '6' => new[] { "111", "100", "111", "101", "111" },
            '7' => new[] { "111", "001", "001", "010", "010" },
            '8' => new[] { "111", "101", "111", "101", "111" },
            '9' => new[] { "111", "101", "111", "001", "111" },
            _ => new[] { "000", "000", "000", "000", "000" },
        };
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] == '1')
                    FillRect(rgba, width, height, x + column * scale, y + row * scale, scale, scale, 255, 255, 255, 255);
            }
        }
    }

    private static int MedianValue(IEnumerable<int> values)
    {
        var sorted = values.Where(value => value > 0).OrderBy(value => value).ToList();
        if (sorted.Count == 0)
            return 0;
        return sorted[sorted.Count / 2];
    }

    private static void ValidateCanvasSize(int width, int height, string error)
    {
        if (width <= 0 || height <= 0 || width * (long)height > MaxPixels)
            throw new InvalidOperationException(error);
    }

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

    private sealed record RenderFrame(
        int Index,
        string Label,
        SpriteSheetRect SourceRect,
        IReadOnlyList<SpriteSheetShapePath> ShapePaths,
        Guid? SourceImageAssetId = null,
        SpriteSheetRect? SourceImageRect = null);

    private sealed record PreparedReassembleFrame(
        SpriteSheetFrameImageInput Input,
        SpriteSheetBackground Background,
        SpriteSheetRect Bounds,
        bool PreserveFullCanvas,
        List<string> Warnings);
}

internal sealed record SpriteSheetFrameWorkingRenderResult(
    byte[] PngData,
    int Width,
    int Height);

internal sealed record SpriteSheetFrameImageInput(
    int Index,
    string Label,
    byte[] Rgba,
    int Width,
    int Height,
    bool UsedWorkingImage,
    Guid? SourceImageAssetId = null,
    SpriteSheetRect? SourceImageRect = null,
    string WorkingState = "");

internal sealed record SpriteSheetReassembleFrameRenderInfo(
    int Index,
    string Label,
    bool UsedWorkingImage,
    SpriteSheetRect DetectedRect,
    SpriteSheetRect PlacedRect,
    IReadOnlyList<string> Warnings);

internal sealed record SpriteSheetReassembleRenderResult(
    byte[] PngData,
    int Width,
    int Height,
    int FrameWidth,
    int FrameHeight,
    int Rows,
    int Columns,
    IReadOnlyList<SpriteSheetFrameView> Frames,
    IReadOnlyList<SpriteSheetReassembleFrameRenderInfo> FrameInfos,
    IReadOnlyList<string> Warnings);

internal sealed record SpriteSheetServerRenderResult(
    byte[] PngData,
    int Width,
    int Height,
    IReadOnlyList<SpriteSheetFrameView> Frames);

internal sealed record SpriteSheetServerPreviewResult(
    IReadOnlyList<SpriteSheetFrameView> Frames);

internal sealed record SpriteSheetReviewRenderResult(
    IReadOnlyList<SpriteAnimationFramePixels> MetricFrames,
    IReadOnlyList<SpriteSheetReviewImage> Images);

internal sealed record SpriteSheetReviewImage(
    string Label,
    string FileName,
    string Kind,
    int? FrameIndex,
    int? FromFrame,
    int? ToFrame,
    byte[] PngData);
