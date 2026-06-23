namespace PixelChat.Art;

internal static class SpriteFrameExtractor
{
    public static SpriteFixedSlotExtractionResult Extract(
        byte[] sourceData,
        LayoutSpec layout,
        AnimationSpec animation,
        IReadOnlyDictionary<int, SpriteFrameOverride> frameOverrides)
    {
        if (!SpriteSheetPngCodec.TryReadRgba(sourceData, out var sourceWidth, out var sourceHeight, out var sourceRgba))
            throw new InvalidOperationException("Animation candidate must be a PNG image.");

        var scaledLayout = SpriteLayoutScaler.ScaleToSource(layout, sourceWidth, sourceHeight);
        var sourceLayout = scaledLayout.Layout;

        var (cr, cg, cb) = ParseHex(layout.BackgroundColor);
        var frames = new List<SpriteFixedSlotFrame>();
        var outputWidth = checked(layout.Columns * layout.TargetCellWidth);
        var outputHeight = checked(layout.Rows * layout.TargetCellHeight);
        var outputRgba = NewTransparent(outputWidth, outputHeight);
        foreach (var slot in sourceLayout.Slots.OrderBy(slot => slot.FrameIndex))
        {
            if (slot.Rect.Width < 4 || slot.Rect.Height < 4)
                throw new InvalidOperationException($"Frame {slot.FrameIndex + 1} slot is too small after scaling.");

            var frameSpec = animation.Frames.First(frame => frame.Index == slot.FrameIndex);
            byte[] cell;
            SpriteSheetRect sourceRect;
            Guid? sourceAssetId = null;
            if (frameOverrides.TryGetValue(slot.FrameIndex, out var replacement))
            {
                if (!SpriteSheetPngCodec.TryReadRgba(replacement.Data, out var replacementWidth, out var replacementHeight, out var replacementRgba))
                    throw new InvalidOperationException($"Replacement frame {slot.FrameIndex + 1} must be a PNG image.");
                cell = FitToCell(RemoveChroma(replacementRgba, replacementWidth, replacementHeight, cr, cg, cb), replacementWidth, replacementHeight, layout.TargetCellWidth, layout.TargetCellHeight);
                sourceRect = replacement.SourceRect;
                sourceAssetId = replacement.SourceAssetId;
            }
            else
            {
                var cropped = Crop(sourceRgba, sourceWidth, sourceHeight, slot.Rect);
                cell = FitToCell(RemoveChroma(cropped, slot.Rect.Width, slot.Rect.Height, cr, cg, cb), slot.Rect.Width, slot.Rect.Height, layout.TargetCellWidth, layout.TargetCellHeight);
                sourceRect = slot.Rect;
            }

            var row = slot.FrameIndex / layout.Columns;
            var column = slot.FrameIndex % layout.Columns;
            var destX = column * layout.TargetCellWidth;
            var destY = row * layout.TargetCellHeight;
            Copy(cell, layout.TargetCellWidth, layout.TargetCellHeight, outputRgba, outputWidth, outputHeight, destX, destY);
            var cellRect = new SpriteSheetRect(destX, destY, layout.TargetCellWidth, layout.TargetCellHeight);
            var bounds = SpriteSheetImageAnalyzer.ForegroundBounds(cell, layout.TargetCellWidth, layout.TargetCellHeight, new SpriteSheetBackground("alpha", 0, 0, 0, 0))
                ?? new SpriteSheetRect(0, 0, 1, 1);
            var preview = DataUrl.ToDataUrl("image/png", SpriteSheetPngCodec.EncodeRgba(layout.TargetCellWidth, layout.TargetCellHeight, cell));
            frames.Add(new SpriteFixedSlotFrame(
                slot.FrameIndex,
                string.IsNullOrWhiteSpace(frameSpec.PoseName) ? $"Frame {slot.FrameIndex + 1}" : frameSpec.PoseName,
                cellRect,
                cellRect,
                new SpriteSheetRect(destX + bounds.X, destY + bounds.Y, bounds.Width, bounds.Height),
                preview,
                sourceAssetId,
                sourceRect,
                frameSpec));
        }

        return new SpriteFixedSlotExtractionResult(
            outputWidth,
            outputHeight,
            SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba),
            frames,
            scaledLayout.Warning);
    }

    private static byte[] RemoveChroma(byte[] rgba, int width, int height, byte cr, byte cg, byte cb)
    {
        var output = (byte[])rgba.Clone();
        for (var offset = 0; offset < output.Length; offset += 4)
        {
            var distance = Math.Abs(output[offset] - cr) + Math.Abs(output[offset + 1] - cg) + Math.Abs(output[offset + 2] - cb);
            if (distance <= 48 || output[offset + 3] <= 16)
            {
                output[offset] = 0;
                output[offset + 1] = 0;
                output[offset + 2] = 0;
                output[offset + 3] = 0;
            }
        }

        return output;
    }

    private static byte[] FitToCell(byte[] rgba, int width, int height, int targetWidth, int targetHeight)
    {
        var output = NewTransparent(targetWidth, targetHeight);
        if (width <= 0 || height <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return output;

        var scale = Math.Min(targetWidth / (double)width, targetHeight / (double)height);
        var drawWidth = Math.Max(1, (int)Math.Round(width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(height * scale));
        var destX = (targetWidth - drawWidth) / 2;
        var destY = (targetHeight - drawHeight) / 2;
        for (var y = 0; y < drawHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)Math.Floor(y / scale));
            for (var x = 0; x < drawWidth; x++)
            {
                var sourceX = Math.Min(width - 1, (int)Math.Floor(x / scale));
                var sourceOffset = ((sourceY * width) + sourceX) * 4;
                var targetOffset = (((destY + y) * targetWidth) + destX + x) * 4;
                output[targetOffset] = rgba[sourceOffset];
                output[targetOffset + 1] = rgba[sourceOffset + 1];
                output[targetOffset + 2] = rgba[sourceOffset + 2];
                output[targetOffset + 3] = rgba[sourceOffset + 3];
            }
        }

        return output;
    }

    private static byte[] Crop(byte[] rgba, int width, int height, SpriteSheetRect rect)
    {
        var output = NewTransparent(rect.Width, rect.Height);
        for (var y = 0; y < rect.Height; y++)
        {
            var sourceY = rect.Y + y;
            if (sourceY < 0 || sourceY >= height)
                continue;
            for (var x = 0; x < rect.Width; x++)
            {
                var sourceX = rect.X + x;
                if (sourceX < 0 || sourceX >= width)
                    continue;
                var sourceOffset = ((sourceY * width) + sourceX) * 4;
                var targetOffset = ((y * rect.Width) + x) * 4;
                output[targetOffset] = rgba[sourceOffset];
                output[targetOffset + 1] = rgba[sourceOffset + 1];
                output[targetOffset + 2] = rgba[sourceOffset + 2];
                output[targetOffset + 3] = rgba[sourceOffset + 3];
            }
        }

        return output;
    }

    private static void Copy(byte[] source, int sourceWidth, int sourceHeight, byte[] target, int targetWidth, int targetHeight, int destX, int destY)
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
                var sourceOffset = ((y * sourceWidth) + x) * 4;
                var targetOffset = ((targetY * targetWidth) + targetX) * 4;
                target[targetOffset] = source[sourceOffset];
                target[targetOffset + 1] = source[sourceOffset + 1];
                target[targetOffset + 2] = source[sourceOffset + 2];
                target[targetOffset + 3] = source[sourceOffset + 3];
            }
        }
    }

    private static byte[] NewTransparent(int width, int height) => new byte[checked(width * height * 4)];

    private static (byte R, byte G, byte B) ParseHex(string value)
    {
        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6
            || !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return (255, 0, 255);
        }

        return (r, g, b);
    }
}

internal sealed record SpriteFrameOverride(
    Guid SourceAssetId,
    byte[] Data,
    SpriteSheetRect SourceRect);

internal sealed record SpriteFixedSlotExtractionResult(
    int Width,
    int Height,
    byte[] PngData,
    IReadOnlyList<SpriteFixedSlotFrame> Frames,
    string LayoutWarning = "");

internal sealed record SpriteFixedSlotFrame(
    int Index,
    string Label,
    SpriteSheetRect SourceRect,
    SpriteSheetRect CellRect,
    SpriteSheetRect SpriteRect,
    string PreviewPngDataUrl,
    Guid? SourceImageAssetId,
    SpriteSheetRect SourceImageRect,
    FrameSpec FrameSpec);
