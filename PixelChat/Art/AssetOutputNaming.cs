using System.Text;

namespace PixelChat.Art;

public static class AssetOutputNaming
{
    public static string PromptLabel(string prompt, int outputIndex, string fallback = "Image")
    {
        var words = prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var excerpt = string.Join(' ', words.Take(5));
        excerpt = SafeStem(excerpt, fallback);
        if (excerpt.Length > 60)
            excerpt = excerpt[..60].TrimEnd(' ', '.', '-');
        if (excerpt.Length == 0)
            excerpt = fallback;
        return $"{excerpt} {BatchLetter(outputIndex)}";
    }

    public static string BatchLetter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var letters = new StringBuilder();
        do
        {
            letters.Insert(0, (char)('A' + index % 26));
            index = index / 26 - 1;
        } while (index >= 0);
        return letters.ToString();
    }

    public static string FileName(string label, string contentType)
    {
        var extension = contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
        var stem = label.Trim();
        if (stem.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            stem = Path.GetFileNameWithoutExtension(stem);
        return SafeStem(stem, "asset") + extension;
    }

    public static string SafeStem(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(value.Select(character => char.IsControl(character) || invalid.Contains(character) ? '-' : character).ToArray());
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (cleaned.Length == 0)
            return fallback;
        if (cleaned.Length > 180)
            cleaned = cleaned[..180].TrimEnd(' ', '.');
        var baseName = cleaned.Split('.')[0].ToUpperInvariant();
        if (baseName is "CON" or "PRN" or "AUX" or "NUL"
            || baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && baseName[3] is >= '1' and <= '9')
            cleaned = "_" + cleaned;
        return cleaned;
    }
}
