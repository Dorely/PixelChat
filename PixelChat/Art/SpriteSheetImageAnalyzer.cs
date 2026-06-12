namespace PixelChat.Art;

internal static class SpriteSheetImageAnalyzer
{
    internal static SpriteAnimationMetricsView ComputeMotionMetrics(
        IReadOnlyList<SpriteAnimationFramePixels> frames,
        SpriteSheetBackground background,
        bool loop)
    {
        var ordered = frames.OrderBy(frame => frame.Index).ToList();
        if (ordered.Count < 2)
            return new SpriteAnimationMetricsView([], 0, 0, 0);

        var stats = ordered.Select(frame => BuildFrameStats(frame, background)).ToList();
        var pairs = new List<SpriteAnimationFramePairMetricsView>();
        for (var index = 0; index < stats.Count - 1; index++)
            pairs.Add(BuildPairMetrics(stats[index], stats[index + 1], loopSeam: false));

        if (loop && stats.Count > 1)
            pairs.Add(BuildPairMetrics(stats[^1], stats[0], loopSeam: true));

        var meanCentroidDrift = pairs.Count == 0 ? 0 : pairs.Average(pair => pair.CentroidDistance);
        var maxCentroidDrift = pairs.Count == 0 ? 0 : pairs.Max(pair => pair.CentroidDistance);
        var meanArea = stats.Average(stat => stat.Area);
        var areaVariancePercent = meanArea <= 0
            ? 0
            : Math.Sqrt(stats.Average(stat => Math.Pow(stat.Area - meanArea, 2))) / meanArea * 100d;

        return new SpriteAnimationMetricsView(
            pairs,
            RoundMetric(meanCentroidDrift),
            RoundMetric(maxCentroidDrift),
            RoundMetric(areaVariancePercent));
    }

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
        if (!SpriteSheetPngCodec.TryReadRgba(data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet frame detection requires a PNG source image.");

        var background = ResolveBackground(rgba, width, height, backgroundMode);
        var frames = DetectFramesFromPixels(rgba, width, height, expectedFrames, background);
        if (frames.Count == 0)
            return GridFallback(sourceAssetId, width, height, expectedFrames, layoutHint, background);

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
            background,
            frames);
    }

    public static SpriteSheetBackground ResolveBackground(byte[] rgba, int width, int height, string? hint = null)
    {
        var normalizedHint = string.IsNullOrWhiteSpace(hint) ? "auto" : hint.Trim().ToLowerInvariant();
        if (normalizedHint is "alpha" or "transparent" or "transparency")
            return new SpriteSheetBackground("alpha", 0, 0, 0, 0);
        if (TryParseHexColor(normalizedHint, out var hintR, out var hintG, out var hintB))
            return new SpriteSheetBackground("color", hintR, hintG, hintB, byte.MaxValue);

        var samples = EnumerateBorderPixels(rgba, width, height).ToList();
        if (samples.Count == 0)
            return new SpriteSheetBackground("alpha", 0, 0, 0, 0);

        var transparent = samples.Count(pixel => pixel.A <= 16);
        if (transparent > samples.Count / 2)
            return new SpriteSheetBackground("alpha", 0, 0, 0, 0);

        var dominant = samples
            .Where(pixel => pixel.A > 16)
            .GroupBy(pixel => ((pixel.R >> 3) << 16) | ((pixel.G >> 3) << 8) | (pixel.B >> 3))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (dominant is null)
            return new SpriteSheetBackground("alpha", 0, 0, 0, 0);

        var count = dominant.Count();
        return new SpriteSheetBackground(
            "color",
            (byte)Math.Round(dominant.Sum(pixel => pixel.R) / (double)count),
            (byte)Math.Round(dominant.Sum(pixel => pixel.G) / (double)count),
            (byte)Math.Round(dominant.Sum(pixel => pixel.B) / (double)count),
            byte.MaxValue);
    }

    internal static bool IsForeground(byte r, byte g, byte b, byte a, SpriteSheetBackground background)
    {
        if (a <= 16)
            return false;

        var mode = string.IsNullOrWhiteSpace(background.Mode)
            ? "alpha"
            : background.Mode.Trim().ToLowerInvariant();
        if (mode == "alpha")
            return true;

        const int colorTolerance = 12;
        return Math.Abs(r - background.R) > colorTolerance
            || Math.Abs(g - background.G) > colorTolerance
            || Math.Abs(b - background.B) > colorTolerance;
    }

    private static IEnumerable<(byte R, byte G, byte B, byte A)> EnumerateBorderPixels(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
            yield break;

        for (var x = 0; x < width; x++)
        {
            yield return PixelAt(rgba, width, x, 0);
            if (height > 1)
                yield return PixelAt(rgba, width, x, height - 1);
        }

        for (var y = 1; y < height - 1; y++)
        {
            yield return PixelAt(rgba, width, 0, y);
            if (width > 1)
                yield return PixelAt(rgba, width, width - 1, y);
        }
    }

    private static (byte R, byte G, byte B, byte A) PixelAt(byte[] rgba, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return (rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
    }

    private static bool TryParseHexColor(string value, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;
        var normalized = value.StartsWith('#') ? value[1..] : value;
        if (normalized.Length != 6)
            return false;

        return byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out r)
            && byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)
            && byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    private static FrameForegroundStats BuildFrameStats(SpriteAnimationFramePixels frame, SpriteSheetBackground background)
    {
        var foreground = new bool[frame.Width * frame.Height];
        var area = 0;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        double sumX = 0;
        double sumY = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = (y * frame.Width) + x;
                var offset = pixel * 4;
                var isForeground = IsForeground(
                    frame.Rgba[offset],
                    frame.Rgba[offset + 1],
                    frame.Rgba[offset + 2],
                    frame.Rgba[offset + 3],
                    background);
                foreground[pixel] = isForeground;
                if (!isForeground)
                    continue;

                area++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                sumX += x + 0.5d;
                sumY += y + 0.5d;
            }
        }

        var bounds = area == 0
            ? new SpriteSheetRect(0, 0, 0, 0)
            : new SpriteSheetRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return new FrameForegroundStats(
            frame.Index,
            frame.Width,
            frame.Height,
            foreground,
            area,
            area == 0 ? 0 : sumX / area,
            area == 0 ? 0 : sumY / area,
            bounds);
    }

    private static SpriteAnimationFramePairMetricsView BuildPairMetrics(
        FrameForegroundStats from,
        FrameForegroundStats to,
        bool loopSeam)
    {
        var centroidDeltaX = to.CentroidX - from.CentroidX;
        var centroidDeltaY = to.CentroidY - from.CentroidY;
        var centroidDistance = Math.Sqrt((centroidDeltaX * centroidDeltaX) + (centroidDeltaY * centroidDeltaY));
        var areaChangePercent = Math.Abs(to.Area - from.Area) / (double)Math.Max(1, Math.Max(from.Area, to.Area)) * 100d;
        var pixelDiffPercent = ForegroundPixelDiffPercent(from, to);

        return new SpriteAnimationFramePairMetricsView(
            from.Index,
            to.Index,
            loopSeam,
            RoundMetric(centroidDeltaX),
            RoundMetric(centroidDeltaY),
            RoundMetric(centroidDistance),
            to.Bounds.Width - from.Bounds.Width,
            to.Bounds.Height - from.Bounds.Height,
            RoundMetric(areaChangePercent),
            RoundMetric(pixelDiffPercent));
    }

    private static double ForegroundPixelDiffPercent(FrameForegroundStats from, FrameForegroundStats to)
    {
        var width = Math.Min(from.Width, to.Width);
        var height = Math.Min(from.Height, to.Height);
        var changed = 0;
        var union = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var fromForeground = from.Foreground[(y * from.Width) + x];
                var toForeground = to.Foreground[(y * to.Width) + x];
                if (fromForeground || toForeground)
                    union++;
                if (fromForeground != toForeground)
                    changed++;
            }
        }

        return union == 0 ? 0 : changed / (double)union * 100d;
    }

    private static double RoundMetric(double value) =>
        Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static List<SpriteSheetFrameDetectionView> DetectFramesFromPixels(
        byte[] rgba,
        int width,
        int height,
        int? expectedFrames,
        SpriteSheetBackground background)
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
                    background);
                foreground[(y * width) + x] = isForeground;
                if (isForeground)
                    foregroundCount++;
            }
        }

        if (foregroundCount == 0)
            return [];

        var components = FindComponents(foreground, width, height, foregroundCount);
        if (components.Count == 0)
            return [];

        var groups = BuildComponentGroups(components, foregroundCount, width, height, expectedFrames);
        if (groups.Count == 0)
            return [];

        return groups
            .Select((group, index) => new SpriteSheetFrameDetectionView(index, group.Rect, BuildShapePaths(group.Pixels, width, height, group.Rect)))
            .OrderBy(frame => frame.SourceRect.Y)
            .ThenBy(frame => frame.SourceRect.X)
            .Select((frame, index) => frame with { Index = index })
            .ToList();
    }

    private static List<Component> FindComponents(bool[] foreground, int width, int height, int foregroundCount)
    {
        var visited = new bool[foreground.Length];
        var components = new List<Component>();
        var queue = new Queue<int>(Math.Min(foregroundCount, 4096));
        var minArea = Math.Max(4, (int)Math.Ceiling(width * height * 0.00001d));

        for (var index = 0; index < foreground.Length; index++)
        {
            if (!foreground[index] || visited[index])
                continue;

            var component = new Component();
            visited[index] = true;
            queue.Enqueue(index);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var x = current % width;
                var y = current / width;
                component.Add(current, x, y);

                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                        continue;

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        var nx = x + dx;
                        if (nx < 0 || nx >= width)
                            continue;

                        var neighbor = (ny * width) + nx;
                        if (!foreground[neighbor] || visited[neighbor])
                            continue;

                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Area >= minArea)
                components.Add(component);
        }

        return components;
    }

    private static List<ComponentGroup> BuildComponentGroups(
        List<Component> components,
        int foregroundCount,
        int width,
        int height,
        int? expectedFrames)
    {
        var ordered = components.OrderByDescending(component => component.Area).ToList();
        var primaryAreaThreshold = Math.Max(64, (int)Math.Ceiling(foregroundCount * 0.01d));
        var primaryComponents = expectedFrames is > 0
            ? ordered.Take(expectedFrames.Value).ToList()
            : ordered.Where(component => component.Area >= primaryAreaThreshold).ToList();
        if (primaryComponents.Count == 0 && ordered.Count > 0)
            primaryComponents.Add(ordered[0]);

        var primarySet = primaryComponents.ToHashSet();
        var groups = primaryComponents.Select(component => new ComponentGroup(component)).ToList();
        var mergeDistance = Math.Max(12, Math.Min(width, height) / 32);
        foreach (var component in ordered.Where(component => !primarySet.Contains(component)))
        {
            var nearest = groups
                .Select(group => new { Group = group, Distance = RectDistance(component.Rect, group.Rect) })
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (nearest is null)
                continue;

            if (expectedFrames is > 0 || nearest.Distance <= mergeDistance || component.Area >= primaryAreaThreshold)
            {
                nearest.Group.Add(component);
            }
        }

        return groups
            .Where(group => group.Area > 0)
            .OrderBy(group => group.Rect.Y)
            .ThenBy(group => group.Rect.X)
            .ToList();
    }

    private static int RectDistance(SpriteSheetRect left, SpriteSheetRect right)
    {
        var leftRight = left.X + left.Width;
        var rightRight = right.X + right.Width;
        var leftBottom = left.Y + left.Height;
        var rightBottom = right.Y + right.Height;
        var dx = Math.Max(0, Math.Max(right.X - leftRight, left.X - rightRight));
        var dy = Math.Max(0, Math.Max(right.Y - leftBottom, left.Y - rightBottom));
        return Math.Max(dx, dy);
    }

    private static IReadOnlyList<SpriteSheetShapePath> BuildShapePaths(
        IReadOnlyList<int> pixels,
        int width,
        int height,
        SpriteSheetRect fallbackRect)
    {
        if (pixels.Count == 0)
            return [RectPath(fallbackRect)];

        var pixelSet = pixels.ToHashSet();
        var edges = new HashSet<Edge>();
        foreach (var index in pixels)
        {
            var x = index % width;
            var y = index / width;
            if (y == 0 || !pixelSet.Contains(((y - 1) * width) + x))
                edges.Add(new Edge(new EdgePoint(x, y), new EdgePoint(x + 1, y)));
            if (x == width - 1 || !pixelSet.Contains((y * width) + x + 1))
                edges.Add(new Edge(new EdgePoint(x + 1, y), new EdgePoint(x + 1, y + 1)));
            if (y == height - 1 || !pixelSet.Contains(((y + 1) * width) + x))
                edges.Add(new Edge(new EdgePoint(x + 1, y + 1), new EdgePoint(x, y + 1)));
            if (x == 0 || !pixelSet.Contains((y * width) + x - 1))
                edges.Add(new Edge(new EdgePoint(x, y + 1), new EdgePoint(x, y)));
        }

        if (edges.Count == 0)
            return [RectPath(fallbackRect)];

        var outgoing = edges
            .GroupBy(edge => edge.Start)
            .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.End.Y).ThenBy(edge => edge.End.X).ToList());
        var paths = new List<SpriteSheetShapePath>();
        var guardLimit = Math.Max(16, edges.Count + 1);
        while (edges.Count > 0)
        {
            var edge = edges
                .OrderBy(item => item.Start.Y)
                .ThenBy(item => item.Start.X)
                .ThenBy(item => item.End.Y)
                .ThenBy(item => item.End.X)
                .First();
            var start = edge.Start;
            var current = edge.End;
            var points = new List<EdgePoint> { start };
            edges.Remove(edge);

            var guard = 0;
            while (current != start && guard++ < guardLimit)
            {
                points.Add(current);
                if (!outgoing.TryGetValue(current, out var candidates))
                    break;

                var next = candidates.FirstOrDefault(edges.Contains);
                if (next == default)
                    break;

                edges.Remove(next);
                current = next.End;
            }

            var simplified = SimplifyPath(points);
            if (current == start && simplified.Count >= 3)
            {
                paths.Add(new SpriteSheetShapePath(
                    simplified
                        .Select(point => new SpriteSheetPoint(point.X, point.Y))
                        .ToList()));
            }
        }

        return paths.Count == 0 ? [RectPath(fallbackRect)] : paths;
    }

    private static List<EdgePoint> SimplifyPath(List<EdgePoint> points)
    {
        if (points.Count <= 3)
            return points;

        var withoutDuplicates = new List<EdgePoint>();
        foreach (var point in points)
        {
            if (withoutDuplicates.Count == 0 || withoutDuplicates[^1] != point)
                withoutDuplicates.Add(point);
        }

        if (withoutDuplicates.Count <= 3)
            return withoutDuplicates;

        var simplified = new List<EdgePoint>();
        for (var index = 0; index < withoutDuplicates.Count; index++)
        {
            var previous = withoutDuplicates[(index - 1 + withoutDuplicates.Count) % withoutDuplicates.Count];
            var current = withoutDuplicates[index];
            var next = withoutDuplicates[(index + 1) % withoutDuplicates.Count];
            if ((previous.X == current.X && current.X == next.X)
                || (previous.Y == current.Y && current.Y == next.Y))
            {
                continue;
            }

            simplified.Add(current);
        }

        return simplified.Count >= 3 ? simplified : withoutDuplicates;
    }

    private static SpriteSheetShapePath RectPath(SpriteSheetRect rect) =>
        new(
        [
            new SpriteSheetPoint(rect.X, rect.Y),
            new SpriteSheetPoint(rect.X + rect.Width, rect.Y),
            new SpriteSheetPoint(rect.X + rect.Width, rect.Y + rect.Height),
            new SpriteSheetPoint(rect.X, rect.Y + rect.Height),
        ]);

    private static SpriteSheetDetectionResult GridFallback(
        Guid sourceAssetId,
        int width,
        int height,
        int? expectedFrames,
        string? layoutHint,
        SpriteSheetBackground background)
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
                new SpriteSheetRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight),
                []));
        }

        return new SpriteSheetDetectionResult(sourceAssetId, width, height, rows, columns, background, frames);
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

    private sealed class Component
    {
        public List<int> Pixels { get; } = [];
        public int MinX { get; private set; } = int.MaxValue;
        public int MinY { get; private set; } = int.MaxValue;
        public int MaxX { get; private set; } = int.MinValue;
        public int MaxY { get; private set; } = int.MinValue;
        public int Area => Pixels.Count;
        public SpriteSheetRect Rect => new(MinX, MinY, Math.Max(1, MaxX - MinX + 1), Math.Max(1, MaxY - MinY + 1));

        public void Add(int index, int x, int y)
        {
            Pixels.Add(index);
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }
    }

    private sealed class ComponentGroup
    {
        public ComponentGroup(Component component) => Add(component);

        public List<int> Pixels { get; } = [];
        public int MinX { get; private set; } = int.MaxValue;
        public int MinY { get; private set; } = int.MaxValue;
        public int MaxX { get; private set; } = int.MinValue;
        public int MaxY { get; private set; } = int.MinValue;
        public int Area => Pixels.Count;
        public SpriteSheetRect Rect => new(MinX, MinY, Math.Max(1, MaxX - MinX + 1), Math.Max(1, MaxY - MinY + 1));

        public void Add(Component component)
        {
            Pixels.AddRange(component.Pixels);
            MinX = Math.Min(MinX, component.MinX);
            MinY = Math.Min(MinY, component.MinY);
            MaxX = Math.Max(MaxX, component.MaxX);
            MaxY = Math.Max(MaxY, component.MaxY);
        }
    }

    private readonly record struct EdgePoint(int X, int Y);

    private readonly record struct Edge(EdgePoint Start, EdgePoint End);

    private sealed record FrameForegroundStats(
        int Index,
        int Width,
        int Height,
        bool[] Foreground,
        int Area,
        double CentroidX,
        double CentroidY,
        SpriteSheetRect Bounds);
}

internal sealed record SpriteAnimationFramePixels(
    int Index,
    string Label,
    int Width,
    int Height,
    byte[] Rgba);
