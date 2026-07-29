namespace PixelChat.Art;

public static class ImageBackgroundModes
{
    public const string Auto = "auto";
    public const string Opaque = "opaque";
    public const string Removable = "removable";
    public const string Current = "current";

    public static string NormalizeGeneration(string? value, string fallback = Auto) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "removable" or "removablecolor" or "removable-color" or "transparent" or "chroma" or "chromakey" or "chroma-key" => Removable,
            "opaque" => Opaque,
            "auto" => Auto,
            _ => NormalizeFallback(fallback),
        };

    public static string NormalizeRecipePreference(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "removable" or "removablecolor" or "removable-color" or "transparent" or "chroma" or "chromakey" or "chroma-key" => Removable,
            "opaque" => Opaque,
            "auto" or "natural" => Auto,
            _ => Current,
        };

    public static string ResolveGeneration(string? requested, string? recipePreference, string fallback = Auto)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return NormalizeGeneration(requested, fallback);

        var normalizedPreference = NormalizeRecipePreference(recipePreference);
        return normalizedPreference == Current
            ? NormalizeGeneration(fallback)
            : normalizedPreference;
    }

    public static bool IsRemovable(string? value) =>
        NormalizeGeneration(value) == Removable;

    private static string NormalizeFallback(string? fallback) =>
        fallback?.Trim().ToLowerInvariant() switch
        {
            Removable => Removable,
            Opaque => Opaque,
            _ => Auto,
        };
}
