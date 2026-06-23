namespace PixelChat.Art;

internal static class SpriteChromaSelector
{
    private static readonly (byte R, byte G, byte B)[] Candidates =
    [
        (255, 0, 255),
        (0, 229, 255),
        (0, 255, 64),
        (255, 128, 0),
        (128, 0, 255),
    ];

    public static (string Hex, IReadOnlyList<string> Palette) Select(byte[] imageData)
    {
        if (!SpriteSheetPngCodec.TryReadRgba(imageData, out var width, out var height, out var rgba))
            return ("#ff00ff", []);

        var palette = SamplePalette(rgba, width, height);
        if (palette.Count == 0)
            return ("#ff00ff", []);

        var best = Candidates
            .Select(candidate => new
            {
                candidate,
                distance = palette.Min(color => Distance(candidate, color)),
            })
            .OrderByDescending(item => item.distance)
            .First()
            .candidate;

        return (ToHex(best), palette.Take(24).Select(ToHex).ToList());
    }

    private static List<(byte R, byte G, byte B)> SamplePalette(byte[] rgba, int width, int height)
    {
        var strideX = Math.Max(1, width / 96);
        var strideY = Math.Max(1, height / 96);
        var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
        for (var y = 0; y < height; y += strideY)
        {
            for (var x = 0; x < width; x += strideX)
            {
                var offset = ((y * width) + x) * 4;
                if (rgba[offset + 3] <= 32)
                    continue;
                var key = ((rgba[offset] >> 4) << 8) | ((rgba[offset + 1] >> 4) << 4) | (rgba[offset + 2] >> 4);
                buckets.TryGetValue(key, out var existing);
                buckets[key] = (existing.R + rgba[offset], existing.G + rgba[offset + 1], existing.B + rgba[offset + 2], existing.Count + 1);
            }
        }

        return buckets.Values
            .OrderByDescending(value => value.Count)
            .Take(48)
            .Select(value => ((byte)(value.R / value.Count), (byte)(value.G / value.Count), (byte)(value.B / value.Count)))
            .ToList();
    }

    private static double Distance((byte R, byte G, byte B) left, (byte R, byte G, byte B) right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static string ToHex((byte R, byte G, byte B) color) =>
        $"#{color.R:x2}{color.G:x2}{color.B:x2}";
}
