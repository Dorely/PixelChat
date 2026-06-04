namespace PixelChat.Art;

public static class ImageMetadataReader
{
    public static (int? Width, int? Height) TryReadSize(byte[] data, string contentType)
    {
        if (data.Length >= 24
            && data[0] == 0x89
            && data[1] == 0x50
            && data[2] == 0x4E
            && data[3] == 0x47)
        {
            return (
                ReadBigEndianInt32(data.AsSpan(16, 4)),
                ReadBigEndianInt32(data.AsSpan(20, 4)));
        }

        if (data.Length >= 4
            && data[0] == 0xFF
            && data[1] == 0xD8
            && contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadJpegSize(data);
        }

        return (null, null);
    }

    private static (int? Width, int? Height) TryReadJpegSize(byte[] data)
    {
        var index = 2;
        while (index + 9 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                index++;
                continue;
            }

            while (index < data.Length && data[index] == 0xFF)
                index++;
            if (index >= data.Length)
                break;

            var marker = data[index++];
            if (marker is 0xD8 or 0xD9)
                continue;
            if (index + 2 > data.Length)
                break;

            var length = ReadBigEndianUInt16(data.AsSpan(index, 2));
            if (length < 2 || index + length > data.Length)
                break;

            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (index + 7 > data.Length)
                    break;
                var height = ReadBigEndianUInt16(data.AsSpan(index + 3, 2));
                var width = ReadBigEndianUInt16(data.AsSpan(index + 5, 2));
                return (width, height);
            }

            index += length;
        }

        return (null, null);
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static int ReadBigEndianUInt16(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 8) | bytes[1];
}
