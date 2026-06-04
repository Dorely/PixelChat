namespace PixelChat.Art;

public static class DataUrl
{
    public static string ToDataUrl(string contentType, byte[] data) =>
        $"data:{contentType};base64,{Convert.ToBase64String(data)}";

    public static (string ContentType, byte[] Data) Parse(string dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            throw new ArgumentException("Data URL is required.", nameof(dataUrl));

        var comma = dataUrl.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0 || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Value is not a data URL.", nameof(dataUrl));

        var header = dataUrl[5..comma];
        var semicolon = header.IndexOf(';', StringComparison.Ordinal);
        var contentType = semicolon < 0 ? header : header[..semicolon];
        if (string.IsNullOrWhiteSpace(contentType))
            contentType = "application/octet-stream";

        return (contentType, Convert.FromBase64String(dataUrl[(comma + 1)..]));
    }
}
