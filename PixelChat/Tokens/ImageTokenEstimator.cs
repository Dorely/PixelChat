namespace PixelChat.Tokens;

public enum ImageTokenDetail
{
    Auto,
    Low,
    High,
    Original
}

public interface IImageTokenEstimator
{
    ImageTokenEstimate Count(int width, int height, string? modelName, ImageTokenDetail detail = ImageTokenDetail.Auto);
}

public sealed record ImageTokenEstimate(
    int TokenCount,
    string Method,
    bool IsExact,
    int Width,
    int Height,
    int CountedWidth,
    int CountedHeight,
    string? ModelName = null,
    string? Warning = null);

public sealed class OpenAIImageTokenEstimator : IImageTokenEstimator
{
    private const int PatchSize = 32;
    private const int TileSize = 512;
    private const int TileHighShortSide = 768;
    private const int TileHighMaxDimension = 2048;

    public ImageTokenEstimate Count(int width, int height, string? modelName, ImageTokenDetail detail = ImageTokenDetail.Auto)
    {
        if (width <= 0 || height <= 0)
        {
            return new ImageTokenEstimate(
                0,
                Method: "image-estimate",
                IsExact: false,
                Width: width,
                Height: height,
                CountedWidth: 0,
                CountedHeight: 0,
                ModelName: modelName,
                Warning: "Image dimensions were not available.");
        }

        var profile = ResolveProfile(modelName, detail);
        return profile.Kind == ImageTokenizationKind.Tile
            ? CountTile(width, height, modelName, profile)
            : CountPatch(width, height, modelName, profile);
    }

    private static ImageTokenEstimate CountPatch(int width, int height, string? modelName, ImageTokenProfile profile)
    {
        var counted = ResizeToMaxDimension(width, height, profile.MaxDimension);
        if (CountPatches(counted.Width, counted.Height) > profile.PatchBudget)
            counted = ResizeForPatchBudget(counted.Width, counted.Height, profile.PatchBudget);

        var patches = CountPatches(counted.Width, counted.Height);
        var tokens = (int)Math.Ceiling(patches * profile.Multiplier);
        return new ImageTokenEstimate(
            tokens,
            Method: profile.Method,
            IsExact: false,
            Width: width,
            Height: height,
            CountedWidth: counted.Width,
            CountedHeight: counted.Height,
            ModelName: modelName,
            Warning: profile.Warning);
    }

    private static ImageTokenEstimate CountTile(int width, int height, string? modelName, ImageTokenProfile profile)
    {
        if (profile.Detail == ImageTokenDetail.Low)
        {
            return new ImageTokenEstimate(
                profile.BaseTokens,
                Method: profile.Method,
                IsExact: false,
                Width: width,
                Height: height,
                CountedWidth: TileSize,
                CountedHeight: TileSize,
                ModelName: modelName,
                Warning: profile.Warning);
        }

        var counted = ResizeForHighDetailTiles(width, height);
        var tiles = CeilDiv(counted.Width, TileSize) * CeilDiv(counted.Height, TileSize);
        var tokens = profile.BaseTokens + tiles * profile.TileTokens;
        return new ImageTokenEstimate(
            tokens,
            Method: profile.Method,
            IsExact: false,
            Width: width,
            Height: height,
            CountedWidth: counted.Width,
            CountedHeight: counted.Height,
            ModelName: modelName,
            Warning: profile.Warning);
    }

    private static ImageTokenProfile ResolveProfile(string? modelName, ImageTokenDetail detail)
    {
        var normalized = (modelName ?? string.Empty).Trim().ToLowerInvariant();
        var resolvedDetail = detail == ImageTokenDetail.Auto ? ResolveDefaultDetail(normalized) : detail;

        if (normalized.StartsWith("gpt-5.4-mini", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 1.62);

        if (normalized.StartsWith("gpt-5.4-nano", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 2.46);

        if (normalized.StartsWith("gpt-5-mini", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 1.62);

        if (normalized.StartsWith("gpt-5-nano", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 2.46);

        if (normalized.StartsWith("gpt-4.1-mini", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 1.62);

        if (normalized.StartsWith("gpt-4.1-nano", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 2.46);

        if (normalized.StartsWith("o4-mini", StringComparison.Ordinal))
            return PatchProfile(resolvedDetail, patchBudget: 1536, maxDimension: 2048, multiplier: 1.72);

        if (normalized.StartsWith("gpt-5.5", StringComparison.Ordinal))
            return PatchProfile(
                resolvedDetail,
                patchBudget: resolvedDetail == ImageTokenDetail.Original ? 10000 : 2500,
                maxDimension: resolvedDetail == ImageTokenDetail.Original ? 6000 : 2048,
                multiplier: 1.0);

        if (normalized.StartsWith("gpt-5.4", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-5.3", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-5.2", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-5.1", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-5-codex", StringComparison.Ordinal))
        {
            return PatchProfile(
                resolvedDetail,
                patchBudget: resolvedDetail == ImageTokenDetail.Original ? 10000 : 2500,
                maxDimension: resolvedDetail == ImageTokenDetail.Original ? 6000 : 2048,
                multiplier: 1.0);
        }

        if (normalized is "gpt-5" or "gpt-5-chat-latest")
            return TileProfile(resolvedDetail, baseTokens: 70, tileTokens: 140);

        if (normalized.StartsWith("gpt-4o-mini", StringComparison.Ordinal))
            return TileProfile(resolvedDetail, baseTokens: 2833, tileTokens: 5667);

        if (normalized.StartsWith("gpt-4o", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-4.1", StringComparison.Ordinal)
            || normalized.StartsWith("gpt-4.5", StringComparison.Ordinal))
        {
            return TileProfile(resolvedDetail, baseTokens: 85, tileTokens: 170);
        }

        if (normalized.StartsWith("computer-use-preview", StringComparison.Ordinal))
            return TileProfile(resolvedDetail, baseTokens: 65, tileTokens: 129);

        if (normalized.StartsWith('o'))
            return TileProfile(resolvedDetail, baseTokens: 75, tileTokens: 150);

        return PatchProfile(
            resolvedDetail,
            patchBudget: 1536,
            maxDimension: 2048,
            multiplier: 1.0,
            warning: string.IsNullOrWhiteSpace(modelName)
                ? "No image-token model was supplied; used the default patch estimate."
                : $"No image-token profile is configured for model '{modelName}'; used the default patch estimate.");
    }

    private static ImageTokenDetail ResolveDefaultDetail(string normalizedModelName)
    {
        if (normalizedModelName.StartsWith("gpt-5.5", StringComparison.Ordinal))
            return ImageTokenDetail.Original;

        return ImageTokenDetail.High;
    }

    private static ImageTokenProfile PatchProfile(
        ImageTokenDetail detail,
        int patchBudget,
        int maxDimension,
        double multiplier,
        string? warning = null)
    {
        if (detail == ImageTokenDetail.Low)
        {
            patchBudget = Math.Min(patchBudget, 256);
            maxDimension = Math.Min(maxDimension, 512);
        }

        return new ImageTokenProfile(
            ImageTokenizationKind.Patch,
            detail,
            Method: "image-patch-estimate",
            PatchBudget: patchBudget,
            MaxDimension: maxDimension,
            Multiplier: multiplier,
            BaseTokens: 0,
            TileTokens: 0,
            Warning: warning);
    }

    private static ImageTokenProfile TileProfile(
        ImageTokenDetail detail,
        int baseTokens,
        int tileTokens,
        string? warning = null) =>
        new(
            ImageTokenizationKind.Tile,
            detail,
            Method: "image-tile-estimate",
            PatchBudget: 0,
            MaxDimension: 0,
            Multiplier: 1.0,
            BaseTokens: baseTokens,
            TileTokens: tileTokens,
            Warning: warning);

    private static (int Width, int Height) ResizeToMaxDimension(int width, int height, int maxDimension)
    {
        var currentMax = Math.Max(width, height);
        if (maxDimension <= 0 || currentMax <= maxDimension)
            return (width, height);

        var scale = (double)maxDimension / currentMax;
        return (
            Math.Max(1, (int)Math.Floor(width * scale)),
            Math.Max(1, (int)Math.Floor(height * scale)));
    }

    private static (int Width, int Height) ResizeForPatchBudget(int width, int height, int patchBudget)
    {
        var shrinkFactor = Math.Sqrt((PatchSize * PatchSize * (double)patchBudget) / (width * (double)height));
        var widthPatches = width * shrinkFactor / PatchSize;
        var heightPatches = height * shrinkFactor / PatchSize;
        var adjustedFactor = shrinkFactor * Math.Min(
            Math.Floor(widthPatches) / widthPatches,
            Math.Floor(heightPatches) / heightPatches);

        if (double.IsNaN(adjustedFactor) || adjustedFactor <= 0)
            adjustedFactor = shrinkFactor;

        return (
            Math.Max(PatchSize, (int)Math.Floor(width * adjustedFactor + 0.000001)),
            Math.Max(PatchSize, (int)Math.Floor(height * adjustedFactor + 0.000001)));
    }

    private static (int Width, int Height) ResizeForHighDetailTiles(int width, int height)
    {
        double countedWidth = width;
        double countedHeight = height;
        var longest = Math.Max(countedWidth, countedHeight);
        if (longest > TileHighMaxDimension)
        {
            var scale = TileHighMaxDimension / longest;
            countedWidth *= scale;
            countedHeight *= scale;
        }

        var shortest = Math.Min(countedWidth, countedHeight);
        if (shortest > TileHighShortSide)
        {
            var scale = TileHighShortSide / shortest;
            countedWidth *= scale;
            countedHeight *= scale;
        }

        return (
            Math.Max(1, (int)Math.Ceiling(countedWidth)),
            Math.Max(1, (int)Math.Ceiling(countedHeight)));
    }

    private static int CountPatches(int width, int height) =>
        CeilDiv(width, PatchSize) * CeilDiv(height, PatchSize);

    private static int CeilDiv(int value, int divisor) =>
        (value + divisor - 1) / divisor;

    private enum ImageTokenizationKind
    {
        Patch,
        Tile
    }

    private sealed record ImageTokenProfile(
        ImageTokenizationKind Kind,
        ImageTokenDetail Detail,
        string Method,
        int PatchBudget,
        int MaxDimension,
        double Multiplier,
        int BaseTokens,
        int TileTokens,
        string? Warning);
}
