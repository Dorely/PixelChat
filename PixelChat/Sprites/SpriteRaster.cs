using System.Security.Cryptography;
using PixelChat.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PixelChat.Sprites;

/// <summary>Deterministic RGBA operations shared by commands, previews and export.</summary>
public sealed class SpriteRaster(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public static SpriteRaster Blank(int width, int height)
    {
        CheckSize(width, height);
        return new(width, height, new byte[checked(width * height * 4)]);
    }

    public static void CheckSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16_777_216)
            throw new InvalidOperationException("Canvas must be 1–8192 pixels per side and at most 16 megapixels.");
    }

    public static SpriteRaster Decode(byte[] png)
    {
        var info = Image.Identify(png);
        CheckSize(info.Width, info.Height);
        using var image = Image.Load<Rgba32>(png);
        var result = Blank(image.Width, image.Height);
        image.CopyPixelDataTo(result.Pixels);
        return result;
    }

    public SpriteBitmap Encode()
    {
        using var image = Image.LoadPixelData<Rgba32>(Pixels, Width, Height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var data = stream.ToArray();
        return new SpriteBitmap { Hash = Convert.ToHexStringLower(SHA256.HashData(data)), Data = data, Width = Width, Height = Height };
    }

    public SpriteRaster Resize(int width, int height, string resampling)
    {
        CheckSize(width, height);
        if (resampling is not ("nearest" or "smooth")) throw new InvalidOperationException("Specify nearest or smooth resampling.");
        using var image = Image.LoadPixelData<Rgba32>(Pixels, Width, Height);
        image.Mutate(c => c.Resize(width, height, resampling == "nearest" ? KnownResamplers.NearestNeighbor : KnownResamplers.Lanczos3));
        var result = Blank(width, height);
        image.CopyPixelDataTo(result.Pixels);
        return result;
    }

    public Rgba32 Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return default;
        var i = ((y * Width) + x) * 4;
        return new(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void Put(int x, int y, Rgba32 color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        var i = ((y * Width) + x) * 4;
        Pixels[i] = color.R; Pixels[i + 1] = color.G; Pixels[i + 2] = color.B; Pixels[i + 3] = color.A;
    }

    public void Blend(int x, int y, Rgba32 color, double opacity = 1)
    {
        if (color.A == 0 || opacity == 0) return;
        var before = Get(x, y);
        var a = color.A / 255d * opacity;
        var b = before.A / 255d * (1 - a);
        var total = a + b;
        static byte Round(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        Put(x, y, new(Round((color.R * a + before.R * b) / total), Round((color.G * a + before.G * b) / total),
            Round((color.B * a + before.B * b) / total), Round(total * 255)));
    }

    public static SpriteRaster Composite(SpriteDocument document, SpriteFrame frame, Func<string, SpriteBitmap> bitmap)
    {
        var output = Blank(frame.Width, frame.Height);
        var first = true;
        foreach (var layer in document.Layers.Where(l => l.Visible && l.Opacity > 0))
        {
            if (!frame.Cels.TryGetValue(layer.Id, out var hash)) continue;
            var source = Decode(bitmap(hash).Data);
            for (var y = 0; y < Math.Min(source.Height, output.Height); y++)
                for (var x = 0; x < Math.Min(source.Width, output.Width); x++)
                    if (first && layer.Opacity == 1) output.Put(x, y, source.Get(x, y));
                    else output.Blend(x, y, source.Get(x, y), layer.Opacity);
            first = false;
        }
        return output;
    }

    public static Rgba32 Color(string text)
    {
        if (!Rgba32.TryParseHex(text.TrimStart('#'), out var color)) throw new InvalidOperationException("Colors must be #RRGGBB or #RRGGBBAA.");
        return color;
    }

    public static bool InsidePolygon(int x, int y, IReadOnlyList<SpritePoint> points)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[i]; var b = points[j];
            if ((a.Y > y + .5) != (b.Y > y + .5) && x + .5 < (b.X - a.X) * (y + .5 - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
