using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Chat;

/// <summary>Measured source alpha and an opaque inspection view, never a replacement for source artwork.</summary>
public static class ModelImageInspection
{
    public const string DefaultBackground = "#808080";
    private const string MetadataMarker = "Measured SOURCE alpha, before compositing: ";

    public static bool TryDescribe(string caption, out string description)
    {
        description = caption;
        var start = caption.IndexOf(MetadataMarker, StringComparison.Ordinal);
        if (start < 0) return false;
        var json = caption[(start + MetadataMarker.Length)..].Split('\n', 2)[0];
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var metadata = parsed.RootElement;
            if (!metadata.GetProperty("inspectionIsOpaqueComposite").GetBoolean()) return false;
            description = FormattableString.Invariant($"Inspection background {metadata.GetProperty("inspectionBackground").GetString()} · Source alpha: {metadata.GetProperty("fullyTransparentPixels").GetInt64():N0} fully transparent, {metadata.GetProperty("partiallyTransparentPixels").GetInt64():N0} partially transparent, and {metadata.GetProperty("opaquePixels").GetInt64():N0} opaque pixels.");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public static string Background(string? color)
    {
        if (color is null) return DefaultBackground;
        if (color.Length != 7 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("backgroundColor must be an opaque #RRGGBB color, or null for neutral gray.", nameof(color));
        return color.ToUpperInvariant();
    }

    public static IReadOnlyList<AIContent> FromDataUrl(string dataUrl, string name, string? backgroundColor = null) =>
        Create(DataUrl.Parse(dataUrl).Data, name, backgroundColor);

    public static IReadOnlyList<AIContent> Create(byte[] source, string name, string? backgroundColor = null)
    {
        var background = Background(backgroundColor);
        using var image = Image.Load<Rgba32>(source);
        var color = Rgba32.ParseHex(background[1..]);
        long transparent = 0, partial = 0, opaque = 0;
        byte min = 255, max = 0;
        // Only the first frame is displayed; explicitly identify this scope for animated inputs.
        using var preview = image.Frames.CloneFrame(0);
        preview.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    min = Math.Min(min, pixel.A); max = Math.Max(max, pixel.A);
                    if (pixel.A == 0) transparent++;
                    else if (pixel.A == 255) opaque++;
                    else partial++;
                    row[x] = new Rgba32(Blend(pixel.R, color.R, pixel.A), Blend(pixel.G, color.G, pixel.A), Blend(pixel.B, color.B, pixel.A), 255);
                }
            }
        });
        using var output = new MemoryStream();
        preview.SaveAsPng(output);
        var metadata = JsonSerializer.Serialize(new
        {
            image = name, sourceSha256 = Convert.ToHexString(SHA256.HashData(source)),
            width = image.Width, height = image.Height, sourceFrameCount = image.Frames.Count, measuredFrameIndex = 0,
            hasTransparency = transparent + partial > 0,
            fullyTransparentPixels = transparent, partiallyTransparentPixels = partial, opaquePixels = opaque,
            minAlpha = min, maxAlpha = max, alphaRange = "0=invisible, 255=opaque",
            inspectionBackground = background, inspectionIsOpaqueComposite = true,
        });
        return
        [
            new TextContent($"Inspection background {background} (display only). {MetadataMarker}{metadata}\nRGB at alpha 0 is invisible, not background haze. Partial alpha can be intentional antialiasing, glow, or translucency. The displayed solid background is not source artwork. Before cleanup, re-view with backgroundColor distinct from the visible palette and verify the suspected defect. Measurements describe this image/crop, not unviewed frames. Original artwork is unchanged."),
            new DataContent(output.ToArray(), "image/png") { Name = Path.GetFileNameWithoutExtension(name) + "-inspection.png" },
        ];
    }

    private static byte Blend(byte foreground, byte background, byte alpha) =>
        (byte)((foreground * alpha + background * (255 - alpha) + 127) / 255);
}
