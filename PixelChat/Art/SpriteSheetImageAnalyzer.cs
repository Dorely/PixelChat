using System.IO.Compression;

namespace PixelChat.Art;

internal static class SpriteSheetImageAnalyzer
{
    public static SpriteSheetDetectionResult Detect(
        Guid sourceAssetId,
        byte[] data,
        string contentType,
        int? knownWidth,
        int? knownHeight,
        int? expectedFrames,
        string? layoutHint,
        string? backgroundMode)
    {
        if (!TryReadPngRgba(data, out var width, out var height, out var rgba))
        {
            var fallbackWidth = knownWidth.GetValueOrDefault(1);
            var fallbackHeight = knownHeight.GetValueOrDefault(1);
            return GridFallback(sourceAssetId, fallbackWidth, fallbackHeight, expectedFrames, layoutHint);
        }

        var frames = DetectFramesFromPixels(rgba, width, height, expectedFrames, backgroundMode);
        if (frames.Count == 0)
            return GridFallback(sourceAssetId, width, height, expectedFrames, layoutHint);

        if (expectedFrames is > 0 && frames.Count > expectedFrames.Value)
        {
            frames = frames
                .OrderByDescending(frame => frame.SourceRect.Width * frame.SourceRect.Height)
                .Take(expectedFrames.Value)
                .OrderBy(frame => frame.SourceRect.Y)
                .ThenBy(frame => frame.SourceRect.X)
                .Select((frame, index) => frame with { Index = index })
                .ToList();
        }

        var rowCount = CountRows(frames);
        var columnCount = rowCount <= 1
            ? frames.Count
            : frames.GroupBy(frame => RowBucket(frame.SourceRect.Y, frames)).Max(group => group.Count());

        return new SpriteSheetDetectionResult(
            sourceAssetId,
            width,
            height,
            Math.Max(1, rowCount),
            Math.Max(1, columnCount),
            frames);
    }

    private static List<SpriteSheetFrameDetectionView> DetectFramesFromPixels(
        byte[] rgba,
        int width,
        int height,
        int? expectedFrames,
        string? backgroundMode)
    {
        var foreground = new bool[width * height];
        var foregroundCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var isForeground = IsForeground(
                    rgba[offset],
                    rgba[offset + 1],
                    rgba[offset + 2],
                    rgba[offset + 3],
                    backgroundMode);
                foreground[(y * width) + x] = isForeground;
                if (isForeground)
                    foregroundCount++;
            }
        }

        if (foregroundCount == 0)
            return [];

        var rowCounts = new int[height];
        for (var y = 0; y < height; y++)
        {
            var count = 0;
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                if (foreground[rowOffset + x])
                    count++;
            }

            rowCounts[y] = count;
        }

        var rowThreshold = Math.Max(2, (int)Math.Ceiling(width * 0.01));
        var rowBands = BuildBands(rowCounts, rowThreshold, minSize: Math.Max(4, height / 160), gapTolerance: Math.Max(3, height / 160));
        if (rowBands.Count == 0)
            return [];

        var frames = new List<SpriteSheetFrameDetectionView>();
        foreach (var rowBand in rowBands)
        {
            var bandHeight = rowBand.End - rowBand.Start + 1;
            var columnCounts = new int[width];
            for (var x = 0; x < width; x++)
            {
                var count = 0;
                for (var y = rowBand.Start; y <= rowBand.End; y++)
                {
                    if (foreground[(y * width) + x])
                        count++;
                }

                columnCounts[x] = count;
            }

            var columnThreshold = Math.Max(2, (int)Math.Ceiling(bandHeight * 0.01));
            var columnBands = BuildBands(columnCounts, columnThreshold, minSize: Math.Max(4, width / 240), gapTolerance: Math.Max(3, width / 256));
            foreach (var columnBand in columnBands)
            {
                if (TryTightRect(foreground, width, rowBand, columnBand, out var rect))
                    frames.Add(new SpriteSheetFrameDetectionView(frames.Count, rect));
            }
        }

        return frames
            .OrderBy(frame => frame.SourceRect.Y)
            .ThenBy(frame => frame.SourceRect.X)
            .Select((frame, index) => frame with { Index = index })
            .ToList();
    }

    private static bool TryTightRect(
        bool[] foreground,
        int width,
        Band rowBand,
        Band columnBand,
        out SpriteSheetRect rect)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        for (var y = rowBand.Start; y <= rowBand.End; y++)
        {
            for (var x = columnBand.Start; x <= columnBand.End; x++)
            {
                if (!foreground[(y * width) + x])
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue)
        {
            rect = new SpriteSheetRect(0, 0, 0, 0);
            return false;
        }

        rect = new SpriteSheetRect(
            minX,
            minY,
            Math.Max(1, maxX - minX + 1),
            Math.Max(1, maxY - minY + 1));
        return true;
    }

    private static List<Band> BuildBands(int[] counts, int threshold, int minSize, int gapTolerance)
    {
        var bands = new List<Band>();
        var start = -1;
        var end = -1;
        var gap = 0;
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] >= threshold)
            {
                if (start < 0)
                    start = index;
                end = index;
                gap = 0;
                continue;
            }

            if (start >= 0)
            {
                gap++;
                if (gap <= gapTolerance)
                    continue;

                var closedEnd = end;
                if (closedEnd - start + 1 >= minSize)
                    bands.Add(new Band(start, closedEnd));
                start = -1;
                end = -1;
                gap = 0;
            }
        }

        if (start >= 0 && end - start + 1 >= minSize)
            bands.Add(new Band(start, end));

        return bands;
    }

    private static bool IsForeground(byte r, byte g, byte b, byte a, string? backgroundMode)
    {
        if (a <= 16)
            return false;

        var mode = string.IsNullOrWhiteSpace(backgroundMode)
            ? "auto"
            : backgroundMode.Trim().ToLowerInvariant();
        if (mode is "alpha" or "transparency")
            return true;

        return !IsMagentaKeyColor(r, g, b);
    }

    private static bool IsMagentaKeyColor(byte r, byte g, byte b) =>
        r >= 210 && b >= 210 && g <= 80;

    private static SpriteSheetDetectionResult GridFallback(
        Guid sourceAssetId,
        int width,
        int height,
        int? expectedFrames,
        string? layoutHint)
    {
        var count = Math.Max(1, expectedFrames.GetValueOrDefault(1));
        var rows = 1;
        var columns = count;
        if (!string.IsNullOrWhiteSpace(layoutHint)
            && layoutHint.Contains("multi", StringComparison.OrdinalIgnoreCase)
            && count > 4)
        {
            rows = (int)Math.Floor(Math.Sqrt(count));
            columns = (int)Math.Ceiling(count / (double)rows);
        }

        var cellWidth = Math.Max(1, width / columns);
        var cellHeight = Math.Max(1, height / rows);
        var frames = new List<SpriteSheetFrameDetectionView>();
        for (var index = 0; index < count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            frames.Add(new SpriteSheetFrameDetectionView(
                index,
                new SpriteSheetRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight)));
        }

        return new SpriteSheetDetectionResult(sourceAssetId, width, height, rows, columns, frames);
    }

    private static int CountRows(IReadOnlyList<SpriteSheetFrameDetectionView> frames)
    {
        if (frames.Count <= 1)
            return frames.Count;

        var ordered = frames.OrderBy(frame => frame.SourceRect.Y).ToList();
        var medianHeight = ordered
            .Select(frame => frame.SourceRect.Height)
            .Order()
            .ElementAt(ordered.Count / 2);
        var rowTolerance = Math.Max(8, medianHeight / 3);
        var rows = new List<int>();
        foreach (var frame in ordered)
        {
            if (rows.Any(rowY => Math.Abs(rowY - frame.SourceRect.Y) <= rowTolerance))
                continue;

            rows.Add(frame.SourceRect.Y);
        }

        return rows.Count;
    }

    private static int RowBucket(int y, IReadOnlyList<SpriteSheetFrameDetectionView> frames)
    {
        var orderedRows = frames
            .Select(frame => frame.SourceRect.Y)
            .Distinct()
            .Order()
            .ToList();
        var medianHeight = frames
            .Select(frame => frame.SourceRect.Height)
            .Order()
            .ElementAt(frames.Count / 2);
        var rowTolerance = Math.Max(8, medianHeight / 3);
        for (var index = 0; index < orderedRows.Count; index++)
        {
            if (Math.Abs(orderedRows[index] - y) <= rowTolerance)
                return index;
        }

        return orderedRows.Count;
    }

    private static bool TryReadPngRgba(byte[] data, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];
        if (data.Length < 33
            || data[0] != 0x89
            || data[1] != 0x50
            || data[2] != 0x4E
            || data[3] != 0x47)
        {
            return false;
        }

        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        using var idat = new MemoryStream();
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = ReadBigEndianInt32(data.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > data.Length)
                return false;

            var typeOffset = offset + 4;
            var chunkOffset = offset + 8;
            if (ChunkTypeEquals(data, typeOffset, (byte)'I', (byte)'H', (byte)'D', (byte)'R'))
            {
                width = ReadBigEndianInt32(data.AsSpan(chunkOffset, 4));
                height = ReadBigEndianInt32(data.AsSpan(chunkOffset + 4, 4));
                bitDepth = data[chunkOffset + 8];
                colorType = data[chunkOffset + 9];
                interlace = data[chunkOffset + 12];
            }
            else if (ChunkTypeEquals(data, typeOffset, (byte)'I', (byte)'D', (byte)'A', (byte)'T'))
            {
                idat.Write(data, chunkOffset, length);
            }
            else if (ChunkTypeEquals(data, typeOffset, (byte)'I', (byte)'E', (byte)'N', (byte)'D'))
            {
                break;
            }

            offset = chunkOffset + length + 4;
        }

        if (width <= 0 || height <= 0 || bitDepth != 8 || interlace != 0 || idat.Length == 0)
            return false;

        var bytesPerPixel = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 0,
        };
        if (bytesPerPixel == 0)
            return false;

        try
        {
            idat.Position = 0;
            using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            zlib.CopyTo(raw);
            var inflated = raw.ToArray();
            var rowBytes = checked(width * bytesPerPixel);
            var requiredBytes = checked((rowBytes + 1) * height);
            if (inflated.Length < requiredBytes)
                return false;

            rgba = new byte[checked(width * height * 4)];
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            var index = 0;
            for (var y = 0; y < height; y++)
            {
                var filter = inflated[index++];
                for (var x = 0; x < rowBytes; x++)
                {
                    var value = inflated[index++];
                    var left = x >= bytesPerPixel ? current[x - bytesPerPixel] : 0;
                    var up = previous[x];
                    var upperLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;
                    current[x] = filter switch
                    {
                        0 => value,
                        1 => unchecked((byte)(value + left)),
                        2 => unchecked((byte)(value + up)),
                        3 => unchecked((byte)(value + ((left + up) / 2))),
                        4 => unchecked((byte)(value + PaethPredictor(left, up, upperLeft))),
                        _ => throw new InvalidOperationException("Unsupported PNG row filter."),
                    };
                }

                CopyRowToRgba(current, colorType, width, y, rgba);
                (previous, current) = (current, previous);
                Array.Clear(current);
            }

            return true;
        }
        catch
        {
            width = 0;
            height = 0;
            rgba = [];
            return false;
        }
    }

    private static void CopyRowToRgba(byte[] row, int colorType, int width, int y, byte[] rgba)
    {
        for (var x = 0; x < width; x++)
        {
            var target = ((y * width) + x) * 4;
            switch (colorType)
            {
                case 0:
                    rgba[target] = row[x];
                    rgba[target + 1] = row[x];
                    rgba[target + 2] = row[x];
                    rgba[target + 3] = byte.MaxValue;
                    break;
                case 2:
                    rgba[target] = row[x * 3];
                    rgba[target + 1] = row[(x * 3) + 1];
                    rgba[target + 2] = row[(x * 3) + 2];
                    rgba[target + 3] = byte.MaxValue;
                    break;
                case 4:
                    rgba[target] = row[x * 2];
                    rgba[target + 1] = row[x * 2];
                    rgba[target + 2] = row[x * 2];
                    rgba[target + 3] = row[(x * 2) + 1];
                    break;
                case 6:
                    rgba[target] = row[x * 4];
                    rgba[target + 1] = row[(x * 4) + 1];
                    rgba[target + 2] = row[(x * 4) + 2];
                    rgba[target + 3] = row[(x * 4) + 3];
                    break;
            }
        }
    }

    private static bool ChunkTypeEquals(byte[] data, int offset, byte a, byte b, byte c, byte d) =>
        data[offset] == a && data[offset + 1] == b && data[offset + 2] == c && data[offset + 3] == d;

    private static int PaethPredictor(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpperLeft = Math.Abs(estimate - upperLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft)
            return left;
        return distanceUp <= distanceUpperLeft ? up : upperLeft;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private sealed record Band(int Start, int End);
}
