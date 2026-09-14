using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Art;

public sealed record ImageModelSelection(string Model, string Quality);

public static class ImageModelCatalog
{
    public static readonly string[] Models = ["gpt-image-2", "gpt-image-2.5-flare", "gpt-image-2.5-sunburst"];
    public static IReadOnlyList<string> Qualities(string model) => SupportsTransparency(model)
        ? ["auto", "low", "medium", "high", "xhigh", "max"]
        : ["auto", "low", "medium", "high"];
    public static bool SupportsTransparency(string model) => model is "gpt-image-2.5-flare" or "gpt-image-2.5-sunburst";

    public static void Validate(string model, string quality, string background, string format)
    {
        if (!Models.Contains(model))
            throw new InvalidOperationException($"Unsupported image model '{model}'. Select Image 2, Flare, or Sunburst.");
        if (!Qualities(model).Contains(quality))
            throw new InvalidOperationException($"Quality '{quality}' is not supported by {model}.");
        if (background != ImageBackgroundModes.Transparent)
            return;
        if (!SupportsTransparency(model))
            throw new InvalidOperationException("Native transparency requires Image 2.5 Flare or Sunburst. Select a 2.5 model or another background.");
        if (format is not ("png" or "webp"))
            throw new InvalidOperationException("Native transparency requires PNG or WebP output; JPEG cannot store alpha.");
    }
}

/// <summary>App-wide preferences use short-lived contexts, independent of renderer circuits.</summary>
public sealed class ImageModelSelectionService(IServiceScopeFactory scopes, IOptions<ImageGenerationOptions> options)
{
    private readonly SemaphoreSlim _writes = new(1, 1);

    public async Task<ImageModelSelection> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var preferences = await db.WorkbenchPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.Id == 1, cancellationToken);
        return preferences is null
            ? new(options.Value.DefaultImageModel, options.Value.DefaultQuality)
            : new(preferences.ImageModel, preferences.ImageQuality);
    }

    public async Task<ImageModelSelection> SetAsync(string model, string quality, CancellationToken cancellationToken = default)
    {
        if (!ImageModelCatalog.Qualities(model).Contains(quality))
            quality = "auto";
        ImageModelCatalog.Validate(model, quality, ImageBackgroundModes.Auto, "png");
        await _writes.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var preferences = await db.WorkbenchPreferences.SingleOrDefaultAsync(p => p.Id == 1, cancellationToken);
            if (preferences is null)
            {
                preferences = new WorkbenchPreferences();
                db.WorkbenchPreferences.Add(preferences);
            }
            preferences.ImageModel = model;
            preferences.ImageQuality = quality;
            await db.SaveChangesAsync(cancellationToken);
            return new(model, quality);
        }
        finally { _writes.Release(); }
    }
}
