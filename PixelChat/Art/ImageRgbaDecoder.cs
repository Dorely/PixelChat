using PixelChat.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Art;

public static class ImageRgbaDecoder
{
    public static bool TryReadRgba(ArtAsset asset, out int width, out int height, out byte[] rgba)
    {
        if (TryReadRgba(asset.Data, out width, out height, out rgba))
        {
            asset.Width ??= width;
            asset.Height ??= height;
            return true;
        }

        return false;
    }

    public static bool TryReadRgba(byte[] data, out int width, out int height, out byte[] rgba)
    {
        if (SpriteSheetPngCodec.TryReadRgba(data, out width, out height, out rgba))
            return true;

        try
        {
            using var image = Image.Load<Rgba32>(data);
            var imageWidth = image.Width;
            var imageHeight = image.Height;
            var pixels = new byte[imageWidth * imageHeight * 4];
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < imageHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var offset = y * imageWidth * 4;
                    for (var x = 0; x < imageWidth; x++)
                    {
                        pixels[offset++] = row[x].R;
                        pixels[offset++] = row[x].G;
                        pixels[offset++] = row[x].B;
                        pixels[offset++] = row[x].A;
                    }
                }
            });
            width = imageWidth;
            height = imageHeight;
            rgba = pixels;
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
}
