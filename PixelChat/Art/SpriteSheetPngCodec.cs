using System.IO.Compression;

namespace PixelChat.Art;

internal static class SpriteSheetPngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryReadRgba(byte[] data, out int width, out int height, out byte[] rgba)
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

    public static byte[] EncodeRgba(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("PNG dimensions must be positive.");
        if (rgba.Length < checked(width * height * 4))
            throw new InvalidOperationException("PNG RGBA data is incomplete.");

        using var png = new MemoryStream();
        png.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        WriteBigEndianInt32(ihdr[..4], width);
        WriteBigEndianInt32(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(png, "IHDR", ihdr);

        using var raw = new MemoryStream();
        var rowBytes = checked(width * 4);
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * rowBytes, rowBytes);
        }

        using var compressed = new MemoryStream();
        raw.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
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

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndianInt32(length, payload.Length);
        stream.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        typeBytes[0] = (byte)type[0];
        typeBytes[1] = (byte)type[1];
        typeBytes[2] = (byte)type[2];
        typeBytes[3] = (byte)type[3];
        stream.Write(typeBytes);
        stream.Write(payload);

        var crc = Crc32(typeBytes, payload);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndianUInt32(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> payload)
    {
        var crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, payload);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc;
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

    private static void WriteBigEndianInt32(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static void WriteBigEndianUInt32(Span<byte> target, uint value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }
}
