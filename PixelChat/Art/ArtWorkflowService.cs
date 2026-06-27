using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PixelChat.Llm;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Art;

public sealed class ArtWorkflowService(
    AppDbContext db,
    IImageProvider imageProvider,
    ILlmProviderService providerService,
    IOptions<ImageGenerationOptions> imageOptions,
    IOptions<SpriteAnimationOptions> animationOptions,
    IWebHostEnvironment environment,
    ILogger<ArtWorkflowService> logger) : IArtWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record ImagePayload(string ContentType, byte[] Data, int Width, int Height);
    private sealed record RecipePromptGuidance(
        string PromptTemplate,
        string StyleRulesJson,
        string AvoidRulesJson);
    private sealed record LayoutProfile(
        double ForegroundAspect,
        double ForegroundCoverage,
        int ForegroundWidth,
        int ForegroundHeight,
        bool NeedsLargeCells,
        bool NeedsTallCells,
        bool NeedsLargePadding);

    public async Task<ProjectView> EnsureDefaultProjectAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.Projects.OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return ProjectView(existing);

        var project = new Project { Name = "Untitled Game Art Project" };
        await db.Projects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectView(project);
    }

    public async Task<ProjectView> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Untitled Game Art Project" : name.Trim();
        var project = new Project { Name = trimmed };
        await db.Projects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectView(project);
    }

    public async Task<WorkbenchView> GetWorkbenchAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var selected = projectId is Guid id
            ? await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            : null;
        if (selected is null)
        {
            var defaultProject = await EnsureDefaultProjectAsync(cancellationToken);
            selected = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == defaultProject.Id, cancellationToken);
        }

        var projects = await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        var assets = await db.ArtAssets
            .AsNoTracking()
            .Where(a => a.ProjectId == selected.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ArtAssetListItem(
                a.Id,
                a.ProjectId,
                a.Label,
                a.FileName,
                a.Kind,
                a.ContentType,
                a.Width,
                a.Height,
                a.ParentAssetId,
                a.SourceBatchId,
                a.SourcePromptRecipeId,
                a.SourcePromptRecipeVersion,
                a.IsFavorite,
                a.Notes,
                a.Prompt,
                a.SourceMetadataJson,
                a.CreatedAt,
                a.UpdatedAt))
            .ToListAsync(cancellationToken);
        var batches = await db.GenerationBatches
            .AsNoTracking()
            .Where(b => b.ProjectId == selected.Id)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new GenerationBatchListItem(
                b.Id,
                b.Label,
                b.Provider,
                b.MainlineModel,
                b.ImageModel,
                b.Prompt,
                b.NegativePrompt,
                b.Size,
                b.Background,
                b.Count,
                b.InputAssetIdsJson,
                b.InputMaskIdsJson,
                b.ParentBatchId,
                b.PromptRecipeId,
                b.PromptRecipeVersion,
                b.Status,
                b.Error,
                b.OutputErrorsJson,
                b.OutputStatesJson,
                b.CreatedAt))
            .ToListAsync(cancellationToken);
        var recipes = await db.PromptRecipes
            .AsNoTracking()
            .Where(r => r.ProjectId == selected.Id)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var animationRecipes = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.GuideAsset)
            .Where(r => r.ProjectId == selected.Id)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var spriteSheets = await db.SpriteSheetDefinitions
            .AsNoTracking()
            .Where(s => s.ProjectId == selected.Id)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);
        var spriteSheetIds = spriteSheets.Select(s => s.Id).ToList();
        var spriteSheetFrameRecords = spriteSheetIds.Count == 0
            ? new List<SpriteSheetFrameListItem>()
            : await db.SpriteSheetFrameRecords
                .AsNoTracking()
                .Where(f => f.ProjectId == selected.Id && spriteSheetIds.Contains(f.SpriteSheetDefinitionId))
                .OrderBy(f => f.Index)
                .Select(f => new SpriteSheetFrameListItem(
                    f.Id,
                    f.ProjectId,
                    f.SpriteSheetDefinitionId,
                    f.Index,
                    f.Label,
                    f.SourceX,
                    f.SourceY,
                    f.SourceWidth,
                    f.SourceHeight,
                    f.ShapeJson,
                    f.SourceImageAssetId,
                    f.SourceImageX,
                    f.SourceImageY,
                    f.SourceImageWidth,
                    f.SourceImageHeight,
                    f.CellX,
                    f.CellY,
                    f.CellWidth,
                    f.CellHeight,
                    f.SpriteX,
                    f.SpriteY,
                    f.SpriteWidth,
                    f.SpriteHeight,
                    f.PreviewWidth,
                    f.PreviewHeight,
                    f.WorkingState,
                    f.WorkingWidth,
                    f.WorkingHeight,
                    f.WorkingMargin,
                    f.WorkingUpdatedAt,
                    f.PoseName,
                    f.Phase,
                    f.RootOffsetX,
                    f.RootOffsetY,
                    f.DurationMs,
                    f.FootContactsJson,
                    f.IsKeyframe,
                    f.PivotX,
                    f.PivotY,
                    f.AppliedScale,
                    f.TranslationX,
                    f.TranslationY,
                    f.QaStatus,
                    f.RepairHistoryJson,
                    f.UpdatedAt))
                .ToListAsync(cancellationToken);
        var masks = await db.ImageMasks
            .AsNoTracking()
            .Where(m => m.ProjectId == selected.Id && m.OwnerKind == "asset")
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new ImageMaskListItem(
                m.Id,
                m.ProjectId,
                m.AssetId,
                m.Label,
                m.ContentType,
                m.Width,
                m.Height,
                m.CreatedAt,
                m.UpdatedAt))
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatContextAttachments
            .AsNoTracking()
            .Where(a => a.ProjectId == selected.Id)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        var compareReviewSet = await db.CompareReviewSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.ProjectId == selected.Id, cancellationToken);
        var compareReviewItems = compareReviewSet is null
            ? []
            : await db.CompareReviewSetItems
                .AsNoTracking()
                .Where(item => item.CompareReviewSetId == compareReviewSet.Id)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);
        var currentRecipeVersions = await LoadCurrentRecipeVersionsAsync(selected.Id, recipes.Select(recipe => recipe.Id).ToList(), cancellationToken);
        var assetViews = assets.Select(AssetView).ToList();
        var batchViews = batches.Select(batch => BatchView(batch, assets)).ToList();
        var recipeViews = recipes
            .Select(recipe => RecipeView(recipe, currentRecipeVersions.GetValueOrDefault(recipe.Id)))
            .ToList();
        var animationRecipeViews = animationRecipes
            .Select(AnimationRecipeView)
            .ToList();
        var spriteSheetFramesBySheet = spriteSheetFrameRecords
            .GroupBy(frame => frame.SpriteSheetDefinitionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SpriteSheetFrameListItem>)group.ToList());
        var spriteSheetViews = spriteSheets
            .Select(sheet => SpriteSheetViewFromListItems(sheet, spriteSheetFramesBySheet.GetValueOrDefault(sheet.Id, [])))
            .ToList();
        var maskViews = masks.Select(MaskView).ToList();
        var attachmentViews = attachments.Select(AttachmentView).ToList();

        var providerStatus = await BuildProviderStatusAsync(cancellationToken);
        return new WorkbenchView(
            ProjectView(selected),
            projects.Select(ProjectView).ToList(),
            assetViews,
            batchViews,
            recipeViews,
            animationRecipeViews,
            spriteSheetViews,
            maskViews,
            attachmentViews,
            compareReviewSet is null ? null : CompareReviewSetView(compareReviewSet, compareReviewItems),
            batchViews.FirstOrDefault(b => b.Id == selected.ActiveBatchId),
            spriteSheetViews.FirstOrDefault(s => s.Id == selected.ActiveSpriteSheetId),
            providerStatus);
    }

    public async Task<ImageBinaryView> GetAssetPreviewImageAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");

        if (asset.ThumbnailData is { Length: > 0 } thumbnail)
        {
            return new ImageBinaryView(
                asset.ContentType,
                thumbnail,
                FileNameForAsset(asset, "preview"),
                asset.UpdatedAt);
        }

        if (asset.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            && TryBuildAssetThumbnail(asset.Data, out var thumbnailData))
        {
            asset.ThumbnailData = thumbnailData;
            asset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new ImageBinaryView(
                "image/png",
                thumbnailData,
                FileNameForAsset(asset, "preview"),
                asset.UpdatedAt);
        }

        return new ImageBinaryView(
            asset.ContentType,
            asset.Data,
            FileNameForAsset(asset, "preview"),
            asset.UpdatedAt);
    }

    public async Task<ImageBinaryView> GetAssetFullImageAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        return new ImageBinaryView(
            asset.ContentType,
            asset.Data,
            FileNameForAsset(asset, "full"),
            asset.UpdatedAt);
    }

    public async Task<ImageBinaryView> GetMaskImageAsync(
        Guid projectId,
        Guid maskId,
        CancellationToken cancellationToken = default)
    {
        var mask = await db.ImageMasks
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Id == maskId, cancellationToken)
            ?? throw new InvalidOperationException("Mask was not found.");
        return new ImageBinaryView(
            mask.ContentType,
            mask.Data,
            string.IsNullOrWhiteSpace(mask.Label) ? $"mask-{mask.Id:N}.png" : CleanFileName(mask.Label, "mask") + ".png",
            mask.UpdatedAt);
    }

    public async Task<ImageBinaryView> GetSpriteFramePreviewImageAsync(
        Guid projectId,
        Guid frameId,
        CancellationToken cancellationToken = default)
    {
        var frame = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == frameId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite frame was not found.");
        if (frame.PreviewData.Length == 0)
            throw new InvalidOperationException("Sprite frame preview image was not found.");

        return new ImageBinaryView(
            frame.PreviewContentType,
            frame.PreviewData,
            string.IsNullOrWhiteSpace(frame.Label) ? $"sprite-frame-{frame.Index + 1}.png" : CleanFileName(frame.Label, $"sprite-frame-{frame.Index + 1}") + ".png",
            frame.UpdatedAt);
    }

    public async Task<ImageBinaryView> GetChatVisualImageAsync(
        Guid projectId,
        Guid visualId,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        var visual = await db.AssistantMessageVisuals
            .Include(v => v.AssistantMessage)
            .ThenInclude(m => m.Conversation)
            .FirstOrDefaultAsync(
                v => v.Id == visualId
                    && v.AssistantMessage.Conversation.ProjectId == projectId,
                cancellationToken)
            ?? throw new InvalidOperationException("Chat visual was not found.");

        if (visual.Data is { Length: > 0 } data)
        {
            var servedData = data;
            var contentType = string.IsNullOrWhiteSpace(visual.ContentType)
                ? "image/png"
                : visual.ContentType;
            if (preview)
            {
                if (visual.ThumbnailData is { Length: > 0 } thumbnail)
                {
                    servedData = thumbnail;
                    contentType = "image/png";
                }
                else if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                    && TryBuildAssetThumbnail(data, out var thumbnailData))
                {
                    visual.ThumbnailData = thumbnailData;
                    await db.SaveChangesAsync(cancellationToken);
                    servedData = thumbnailData;
                    contentType = "image/png";
                }
            }

            return new ImageBinaryView(
                contentType,
                servedData,
                ChatVisualFileName(visual, preview),
                visual.CreatedAt);
        }

        if (visual.SourceRefId is not Guid sourceRefId)
            throw new InvalidOperationException("Chat visual image data was not found.");

        return visual.SourceKind.Trim().ToLowerInvariant() switch
        {
            "asset" => preview
                ? await GetAssetPreviewImageAsync(projectId, sourceRefId, cancellationToken)
                : await GetAssetFullImageAsync(projectId, sourceRefId, cancellationToken),
            "mask" => await GetMaskImageAsync(projectId, sourceRefId, cancellationToken),
            "spriteframe" => await GetSpriteFramePreviewImageAsync(projectId, sourceRefId, cancellationToken),
            _ => throw new InvalidOperationException("Chat visual source was not found."),
        };
    }

    public async Task<ArtAssetExportView> GetAssetForExportAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");

        return ExportAssetView(asset);
    }

    public async Task<string> ListAssetsJsonAsync(
        Guid projectId,
        string? kind = null,
        string? query = null,
        bool? favorite = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 12, 50);
        var assets = db.ArtAssets
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId);

        if (TryParseAssetKind(kind, out var parsedKind))
            assets = assets.Where(a => a.Kind == parsedKind);

        if (favorite is bool favoriteValue)
            assets = assets.Where(a => a.IsFavorite == favoriteValue);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            assets = assets.Where(a =>
                EF.Functions.Like(a.Label, pattern)
                || EF.Functions.Like(a.FileName, pattern)
                || EF.Functions.Like(a.Prompt, pattern)
                || EF.Functions.Like(a.Notes, pattern));
        }

        var results = await assets
            .OrderByDescending(a => a.IsFavorite)
            .ThenByDescending(a => a.CreatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            assets = results.Select(CompactAsset),
            returned = results.Count,
            limit = max,
            note = "Use read_asset with an asset id to inspect the full image. This list omits image bytes.",
        }, JsonOptions);
    }

    public async Task<string> ReadAssetJsonAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");

        return JsonSerializer.Serialize(new
        {
            asset = AssetDetail(asset),
            image = new
            {
                availableToModel = true,
                delivery = "model-only image content on this tool call",
                contentType = asset.ContentType,
                fileName = string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
                width = asset.Width,
                height = asset.Height,
                bytes = asset.Data.Length,
                note = "Image bytes are intentionally omitted from JSON and are not attached to visible chat context.",
            },
        }, JsonOptions);
    }

    public async Task<string> ListPromptRecipesJsonAsync(
        Guid projectId,
        string? query = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 20, 50);
        var recipes = db.PromptRecipes
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            recipes = recipes.Where(r =>
                EF.Functions.Like(r.Name, pattern)
                || EF.Functions.Like(r.AssetType, pattern)
                || EF.Functions.Like(r.PromptTemplate, pattern)
                || EF.Functions.Like(r.Notes, pattern));
        }

        var results = await recipes
            .OrderBy(r => r.Name)
            .Take(max)
            .ToListAsync(cancellationToken);
        var currentRecipeVersions = await LoadCurrentRecipeVersionsAsync(projectId, results.Select(recipe => recipe.Id).ToList(), cancellationToken);

        return JsonSerializer.Serialize(new
        {
            recipes = results.Select(recipe => CompactRecipe(recipe, currentRecipeVersions.GetValueOrDefault(recipe.Id))),
            returned = results.Count,
            limit = max,
            note = "Use read_recipe with a recipe id for full reusable guidance.",
        }, JsonOptions);
    }

    public async Task<string> ReadPromptRecipeJsonAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        var currentVersion = await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken) ?? 0;
        return JsonSerializer.Serialize(RecipeView(recipe, currentVersion), JsonOptions);
    }

    public async Task<string> ListAnimationRecipesJsonAsync(
        Guid projectId,
        string? query = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 20, 50);
        var recipes = db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.GuideAsset)
            .Where(r => r.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            recipes = recipes.Where(r =>
                EF.Functions.Like(r.Name, pattern)
                || EF.Functions.Like(r.AnimationKind, pattern)
                || EF.Functions.Like(r.Facing, pattern)
                || EF.Functions.Like(r.PromptScaffold, pattern)
                || EF.Functions.Like(r.Notes, pattern));
        }

        var results = await recipes
            .OrderBy(r => r.AnimationKind)
            .ThenBy(r => r.Facing)
            .ThenBy(r => r.Name)
            .Take(max)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            animationRecipes = results.Select(CompactAnimationRecipe),
            returned = results.Count,
            limit = max,
            note = "Use read_animation_recipe with a recipe id for full reusable motion, layout, guide, and prompt scaffold details.",
        }, JsonOptions);
    }

    public async Task<string> ReadAnimationRecipeJsonAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.GuideAsset)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");

        return JsonSerializer.Serialize(AnimationRecipeView(recipe), JsonOptions);
    }

    public async Task<string> ListGenerationBatchesJsonAsync(
        Guid projectId,
        string? status = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 10, 30);
        var batches = db.GenerationBatches
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId);

        if (TryParseBatchStatus(status, out var parsedStatus))
            batches = batches.Where(b => b.Status == parsedStatus);

        var results = await batches
            .OrderByDescending(b => b.CreatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
        var batchIds = results.Select(b => b.Id).ToList();
        var outputAssets = batchIds.Count == 0
            ? new List<ArtAsset>()
            : await db.ArtAssets
                .AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.SourceBatchId.HasValue && batchIds.Contains(a.SourceBatchId.Value))
                .ToListAsync(cancellationToken);
        var outputIdsByBatch = outputAssets
            .GroupBy(a => a.SourceBatchId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group
                    .OrderBy(a => ReadBatchOutputIndex(a) ?? int.MaxValue)
                    .ThenBy(a => a.CreatedAt)
                    .Select(a => a.Id)
                    .ToList());

        return JsonSerializer.Serialize(new
        {
            batches = results.Select(batch => CompactBatch(
                batch,
                outputIdsByBatch.TryGetValue(batch.Id, out var outputIds) ? outputIds : Array.Empty<Guid>())),
            returned = results.Count,
            limit = max,
            note = "Use read_batch with a batch id for full batch prompt, state, and output details.",
        }, JsonOptions);
    }

    public async Task<string> ReadGenerationBatchJsonAsync(Guid projectId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Generation batch was not found.");
        var outputAssets = await db.ArtAssets
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.SourceBatchId == batchId)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(BatchView(batch, outputAssets), JsonOptions);
    }

    public async Task<string> ListSpriteSheetsJsonAsync(
        Guid projectId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 12, 40);
        var sheets = await db.SpriteSheetDefinitions
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
        var sheetIds = sheets.Select(sheet => sheet.Id).ToList();
        var frameCounts = sheetIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.SpriteSheetFrameRecords
                .AsNoTracking()
                .Where(frame => frame.ProjectId == projectId && sheetIds.Contains(frame.SpriteSheetDefinitionId))
                .GroupBy(frame => frame.SpriteSheetDefinitionId)
                .Select(group => new { Id = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            spriteSheets = sheets.Select(sheet => CompactSpriteSheet(sheet, frameCounts.GetValueOrDefault(sheet.Id))),
            returned = sheets.Count,
            limit = max,
            note = "Use read_sprite_sheet with a sprite sheet id for layout and frame boxes.",
        }, JsonOptions);
    }

    public async Task<string> ReadSpriteSheetJsonAsync(Guid projectId, Guid spriteSheetId, CancellationToken cancellationToken = default)
    {
        var sheet = await LoadSpriteSheetViewAsync(projectId, spriteSheetId, cancellationToken);
        return JsonSerializer.Serialize(CompactSpriteSheet(sheet), JsonOptions);
    }

    public async Task<SpriteAnimationReviewView> BuildSpriteAnimationReviewAsync(
        Guid projectId,
        Guid spriteSheetId,
        int maxFrames = 12,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = sheet.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite animation review requires a PNG working image.");
        var background = EnsureSheetBackground(sheet, rgba, width, height);

        var frameLimit = Math.Clamp(maxFrames <= 0 ? 12 : maxFrames, 1, 24);
        var frames = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .Take(frameLimit)
            .ToListAsync(cancellationToken);
        if (frames.Count == 0)
            throw new InvalidOperationException("No sprite frames were found to review.");

        var updates = frames.Select(FrameUpdateFromRecord).ToList();
        SpriteSheetReviewRenderResult reviewRender;
        var useStabilizedWorkingFrames = frames.Any(frame =>
            NormalizeFrameWorkingState(frame.WorkingState) == "stabilized"
            && frame.WorkingData.Length > 0);
        if (useStabilizedWorkingFrames)
        {
            var inputs = BuildSpriteFrameImageInputsFromRecords(sheet, rgba, width, height, background, frames);
            var render = SpriteSheetServerRenderer.ReassembleIrregularFrames(
                rows: 1,
                columns: frames.Count,
                sheet.Padding,
                sheet.Gutter,
                sheet.HorizontalAnchor,
                sheet.VerticalAnchor,
                background,
                inputs);
            var reviewFrames = render.Frames
                .Select(frame => new SpriteSheetFrameUpdateView(
                    frame.Index,
                    frame.Label,
                    frame.SourceRect,
                    frame.ShapePaths,
                    frame.SourceImageAssetId,
                    frame.SourceImageRect))
                .ToList();
            if (!SpriteSheetPngCodec.TryReadRgba(render.PngData, out var reviewWidth, out var reviewHeight, out var reviewRgba))
                throw new InvalidOperationException("Temporary stabilized review image must be a PNG.");
            reviewRender = SpriteSheetServerRenderer.BuildAnimationReview(
                reviewRgba,
                reviewWidth,
                reviewHeight,
                render.Rows,
                render.Columns,
                render.FrameWidth,
                render.FrameHeight,
                sheet.Padding,
                sheet.Gutter,
                sheet.HorizontalAnchor,
                sheet.VerticalAnchor,
                background,
                reviewFrames,
                sheet.Loop,
                frameLimit);
        }
        else
        {
            reviewRender = SpriteSheetServerRenderer.BuildAnimationReview(
                rgba,
                width,
                height,
                sheet.Rows,
                sheet.Columns,
                sheet.CellWidth,
                sheet.CellHeight,
                sheet.Padding,
                sheet.Gutter,
                sheet.HorizontalAnchor,
                sheet.VerticalAnchor,
                background,
                updates,
                sheet.Loop,
                frameLimit);
        }
        var metrics = SpriteSheetImageAnalyzer.ComputeMotionMetrics(reviewRender.MetricFrames, background, sheet.Loop);
        var images = reviewRender.Images
            .Select(image => new SpriteAnimationReviewImageView(
                image.Label,
                image.FileName,
                "image/png",
                DataUrl.ToDataUrl("image/png", image.PngData),
                image.Kind,
                image.FrameIndex,
                image.FromFrame,
                image.ToFrame))
            .ToList();

        for (var index = 0; index < frames.Count; index++)
        {
            var record = frames[index];
            if (NormalizeFrameWorkingState(record.WorkingState) == "stabilized")
                continue;

            if (record.WorkingData.Length == 0
                || !SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var workingWidth, out var workingHeight, out var workingRgba))
            {
                continue;
            }

            var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
                rgba, width, height, sheet.Rows, sheet.Columns, background, updates, index, record.WorkingMargin);
            if (!SpriteSheetPngCodec.TryReadRgba(extracted.PngData, out var sourceWidth, out var sourceHeight, out var sourceRgba)
                || sourceWidth != workingWidth
                || sourceHeight != workingHeight)
            {
                continue;
            }

            var overlay = SpriteSheetServerRenderer.RenderRemovedPixelsOverlay(
                sourceRgba,
                background,
                workingRgba,
                ResolveWorkingFrameBackground(sheet, workingRgba, workingWidth, workingHeight),
                workingWidth,
                workingHeight);
            images.Add(new SpriteAnimationReviewImageView(
                $"Frame {index + 1} removed pixels vs source (red = erased from source foreground)",
                $"frame-{index + 1}-removed-vs-source.png",
                "image/png",
                DataUrl.ToDataUrl("image/png", overlay.PngData),
                "sourceDiff",
                index,
                null,
                null));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SpriteAnimationReviewView(
            sheet.Id,
            frames.Count,
            sheet.Rows,
            sheet.Columns,
            sheet.Fps,
            sheet.Loop,
            metrics,
            images);
    }

    public async Task<SpriteAnimationReviewImageView> BuildSpriteSheetDetectionAnnotatedSheetAsync(
        Guid projectId,
        SpriteSheetDetectionResult detection,
        CancellationToken cancellationToken = default)
    {
        var source = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == detection.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(source.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet detection annotation requires a PNG source image.");

        var image = SpriteSheetServerRenderer.BuildDetectionAnnotatedSheetView(rgba, width, height, detection);
        return new SpriteAnimationReviewImageView(
            image.Label,
            image.FileName,
            "image/png",
            DataUrl.ToDataUrl("image/png", image.PngData),
            image.Kind,
            image.FrameIndex,
            image.FromFrame,
            image.ToFrame);
    }

    public async Task<SpriteAnimationReviewImageView> BuildSpriteSheetRepairAnnotatedSheetAsync(
        Guid projectId,
        RepairSpriteSheetFramesResult repair,
        CancellationToken cancellationToken = default)
    {
        var imageAssetId = repair.WorkingAssetId ?? repair.SourceAssetId;
        var source = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == imageAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite-sheet repair image was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(source.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet repair annotation requires a PNG image.");

        var image = SpriteSheetServerRenderer.BuildRepairAnnotatedSheetView(
            rgba,
            width,
            height,
            repair.Rows,
            repair.Columns,
            repair.CellWidth,
            repair.CellHeight,
            repair.Gutter,
            repair.Background,
            repair.Frames,
            repair.RejectedSegments);
        return new SpriteAnimationReviewImageView(
            image.Label,
            image.FileName,
            "image/png",
            DataUrl.ToDataUrl("image/png", image.PngData),
            image.Kind,
            image.FrameIndex,
            image.FromFrame,
            image.ToFrame);
    }

    public async Task<SpriteAnimationReviewImageView> BuildSpriteSheetStabilizationAnnotatedSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var stabilization = DeserializeStabilization(sheet.StabilizationJson)
            ?? throw new InvalidOperationException("No stabilization result is saved for this sprite sheet.");
        var working = sheet.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet stabilization annotation requires a PNG working image.");

        var records = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        var background = BackgroundOrDefault(sheet);
        var inputs = BuildStabilizationWorkingFrameInputs(sheet, rgba, width, height, background, records)
            .Select(input => input.Image)
            .ToList();
        return BuildStabilizationDiagnosticImage(background, stabilization, inputs);
    }

    public async Task<SpriteAnimationReviewImageView> BuildSpriteSheetStabilizationAnnotatedSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        SpriteSheetStabilizationView stabilization,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = sheet.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet stabilization annotation requires a PNG working image.");

        var records = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        var background = BackgroundOrDefault(sheet);
        var inputs = BuildStabilizationWorkingFrameInputs(sheet, rgba, width, height, background, records)
            .Select(input => input.Image)
            .ToList();
        return BuildStabilizationDiagnosticImage(background, stabilization, inputs);
    }

    public Task<SpriteAnimationReviewImageView> BuildSpriteSheetAnnotatedSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default) =>
        BuildSpriteSheetAnnotatedSheetAsync(projectId, spriteSheetId, Array.Empty<SpriteSheetRejectedSegmentView>(), cancellationToken);

    public async Task<SpriteAnimationReviewImageView> BuildSpriteSheetAnnotatedSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        IReadOnlyList<SpriteSheetRejectedSegmentView> rejectedSegments,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = sheet.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet annotation requires a PNG working image.");

        var frames = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (frames.Count == 0)
            throw new InvalidOperationException("No sprite frames were found to annotate.");

        var detection = new SpriteSheetDetectionResult(
            sheet.SourceAssetId,
            width,
            height,
            sheet.Rows,
            sheet.Columns,
            BackgroundOrDefault(sheet),
            frames
                .Select(frame => new SpriteSheetFrameDetectionView(
                    frame.Index,
                    RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
                    DeserializeShapePaths(frame.ShapeJson)))
                .ToList())
        {
            RejectedSegments = rejectedSegments,
        };
        var image = SpriteSheetServerRenderer.BuildDetectionAnnotatedSheetView(rgba, width, height, detection);
        return new SpriteAnimationReviewImageView(
            image.Label,
            image.FileName,
            "image/png",
            DataUrl.ToDataUrl("image/png", image.PngData),
            image.Kind,
            image.FrameIndex,
            image.FromFrame,
            image.ToFrame);
    }

    public async Task<BackgroundRemovalExportCacheView?> GetBackgroundRemovalExportCacheAsync(
        Guid projectId,
        Guid assetId,
        BackgroundRemovalExportCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        var normalized = NormalizeBackgroundRemovalCacheRequest(request);
        var sourceHash = Sha256Hex(asset.Data);

        var cache = await db.BackgroundRemovalExportCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.ProjectId == projectId
                && c.AssetId == assetId
                && c.SourceImageSha256 == sourceHash
                && c.RemovalMethod == normalized.RemovalMethod
                && c.ModelName == normalized.ModelName
                && c.RembgPackageVersion == normalized.RembgPackageVersion
                && c.AlphaMatting == normalized.AlphaMatting
                && c.OptionsHash == normalized.OptionsHash,
                cancellationToken);

        return cache is null ? null : BackgroundRemovalCacheView(cache);
    }

    public async Task<BackgroundRemovalExportCacheView> SaveBackgroundRemovalExportCacheAsync(
        Guid projectId,
        Guid assetId,
        SaveBackgroundRemovalExportCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        var normalized = NormalizeBackgroundRemovalCacheRequest(new BackgroundRemovalExportCacheRequest(
            request.RemovalMethod,
            request.ModelName,
            request.RembgPackageVersion,
            request.AlphaMatting,
            request.OptionsHash));
        var sourceHash = Sha256Hex(asset.Data);

        var cache = await db.BackgroundRemovalExportCaches
            .FirstOrDefaultAsync(c =>
                c.ProjectId == projectId
                && c.AssetId == assetId
                && c.SourceImageSha256 == sourceHash
                && c.RemovalMethod == normalized.RemovalMethod
                && c.ModelName == normalized.ModelName
                && c.RembgPackageVersion == normalized.RembgPackageVersion
                && c.AlphaMatting == normalized.AlphaMatting
                && c.OptionsHash == normalized.OptionsHash,
                cancellationToken);
        var now = DateTime.UtcNow;
        if (cache is null)
        {
            cache = new BackgroundRemovalExportCache
            {
                ProjectId = projectId,
                AssetId = assetId,
                SourceImageSha256 = sourceHash,
                RemovalMethod = normalized.RemovalMethod,
                ModelName = normalized.ModelName,
                RembgPackageVersion = normalized.RembgPackageVersion,
                AlphaMatting = normalized.AlphaMatting,
                OptionsHash = normalized.OptionsHash,
                ActualBackend = NormalizeCacheString(request.ActualBackend, "unknown"),
                CreatedAt = now,
                UpdatedAt = now,
            };
            await db.BackgroundRemovalExportCaches.AddAsync(cache, cancellationToken);
        }
        else
        {
            cache.UpdatedAt = now;
            cache.ActualBackend = NormalizeCacheString(request.ActualBackend, "unknown");
        }

        cache.ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "image/png" : request.ContentType.Trim();
        cache.Data = request.Data;
        cache.TransparentPixels = request.TransparentPixels;
        cache.SemiTransparentPixels = request.SemiTransparentPixels;
        cache.OpaquePixels = request.OpaquePixels;

        await db.SaveChangesAsync(cancellationToken);
        return BackgroundRemovalCacheView(cache);
    }

    public async Task<IReadOnlyList<ExportStepCacheView>> GetExportStepCacheAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        var sourceHash = Sha256Hex(asset.Data);

        var steps = await db.ExportStepCaches
            .AsNoTracking()
            .Where(step =>
                step.ProjectId == projectId
                && step.AssetId == assetId
                && step.SourceImageSha256 == sourceHash)
            .OrderBy(step => step.StepIndex)
            .ToListAsync(cancellationToken);

        return steps.Select(ExportStepCacheView).ToList();
    }

    public async Task<ExportStepCacheView> SaveExportStepCacheAsync(
        Guid projectId,
        Guid assetId,
        SaveExportStepCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        if (request.StepIndex < 0)
            throw new InvalidOperationException("Export step index must be zero or greater.");
        if (request.Data.Length == 0)
            throw new InvalidOperationException("Export step PNG data is required.");

        var sourceHash = Sha256Hex(asset.Data);
        var now = DateTime.UtcNow;

        var existingTail = await db.ExportStepCaches
            .Where(step =>
                step.ProjectId == projectId
                && step.AssetId == assetId
                && step.SourceImageSha256 == sourceHash
                && step.StepIndex >= request.StepIndex)
            .ToListAsync(cancellationToken);
        if (existingTail.Count > 0)
            db.ExportStepCaches.RemoveRange(existingTail);

        var step = new ExportStepCache
        {
            ProjectId = projectId,
            AssetId = assetId,
            SourceImageSha256 = sourceHash,
            StepIndex = request.StepIndex,
            ParentImageSha256 = NormalizeCacheString(request.ParentImageSha256, Sha256Hex(asset.Data)),
            OutputImageSha256 = Sha256Hex(request.Data),
            Method = NormalizeCacheString(request.Method, "unknown"),
            OptionsHash = NormalizeCacheString(request.OptionsHash, "default"),
            ModelName = NormalizeCacheString(request.ModelName, string.Empty),
            ActualBackend = NormalizeCacheString(request.ActualBackend, string.Empty),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "image/png" : request.ContentType.Trim(),
            Data = request.Data,
            Width = request.Width,
            Height = request.Height,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.ExportStepCaches.AddAsync(step, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ExportStepCacheView(step);
    }

    public async Task ClearExportStepCacheAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        var sourceHash = Sha256Hex(asset.Data);

        var steps = await db.ExportStepCaches
            .Where(step =>
                step.ProjectId == projectId
                && step.AssetId == assetId
                && step.SourceImageSha256 == sourceHash)
            .ToListAsync(cancellationToken);
        if (steps.Count == 0)
            return;

        db.ExportStepCaches.RemoveRange(steps);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SpriteSheetDetectionResult> DetectSpriteSheetFramesAsync(
        Guid projectId,
        SpriteSheetDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        if (!source.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sprite-sheet frame detection requires a PNG source image.");

        return SpriteSheetImageAnalyzer.Detect(
            source.Id,
            source.Data,
            source.ContentType,
            source.Width,
            source.Height,
            request.ExpectedFrames,
            request.LayoutHint,
            request.BackgroundMode);
    }

    public async Task<RepairSpriteSheetFramesResult> RepairSpriteSheetFramesAsync(
        Guid projectId,
        RepairSpriteSheetFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.SourceAsset)
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? definition.SourceAsset;
        if (!working.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sprite-sheet frame repair requires a PNG working image.");

        var background = BackgroundFromDefinition(definition);
        var analysis = SpriteSheetImageAnalyzer.Repair(
            definition.SourceAssetId,
            working.Data,
            working.ContentType,
            Math.Clamp(request.ExpectedFrames, 1, 256),
            request.LayoutHint,
            background is null
                ? definition.BackgroundColor
                : (background.Mode.Equals("color", StringComparison.OrdinalIgnoreCase) ? BackgroundColor(background) : "alpha"));

        var frames = MergeTargetedRepairFrames(
            await db.SpriteSheetFrameRecords
                .AsNoTracking()
                .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
                .OrderBy(frame => frame.Index)
                .ToListAsync(cancellationToken),
            analysis.Frames,
            request.TargetFrameNumbers);
        var warnings = analysis.Warnings.ToList();
        if (request.TargetFrameNumbers is { Count: > 0 })
            warnings.Add($"{(request.Apply ? "Applied" : "Prepared")} repair candidates only for requested frame number(s): {string.Join(", ", request.TargetFrameNumbers.Order())}.");

        SpriteSheetDefinitionView? saved = null;
        if (request.Apply)
        {
            var (rows, columns) = GridCapacity(analysis.Rows, analysis.Columns, frames.Count);
            saved = await UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
                definition.Id,
                rows,
                columns,
                Math.Max(1, frames.Max(frame => frame.SourceRect.Width)),
                Math.Max(1, frames.Max(frame => frame.SourceRect.Height)),
                Padding: 0,
                Gutter: 0,
                definition.Fps,
                definition.Loop,
                definition.HorizontalAnchor,
                definition.VerticalAnchor,
                frames), cancellationToken);
        }

        return new RepairSpriteSheetFramesResult(
            definition.Id,
            definition.SourceAssetId,
            definition.OutputAssetId,
            request.Apply,
            analysis.ImageWidth,
            analysis.ImageHeight,
            analysis.Rows,
            analysis.Columns,
            Math.Max(1, frames.Max(frame => frame.SourceRect.Width)),
            Math.Max(1, frames.Max(frame => frame.SourceRect.Height)),
            request.Apply ? 0 : definition.Padding,
            request.Apply ? 0 : definition.Gutter,
            definition.Fps,
            definition.Loop,
            NormalizeHorizontalAnchor(definition.HorizontalAnchor),
            NormalizeVerticalAnchor(definition.VerticalAnchor),
            analysis.Background,
            frames,
            warnings.Distinct(StringComparer.Ordinal).ToList(),
            analysis.RejectedSegments,
            analysis.FrameQuality,
            saved);
    }

    public async Task<SpriteSheetDefinitionView> StartSpriteSheetEditAsync(
        Guid projectId,
        Guid sourceAssetId,
        CancellationToken cancellationToken = default)
    {
        var source = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");

        var existing = await db.SpriteSheetDefinitions
            .Where(s => s.ProjectId == projectId && s.SourceAssetId == source.Id && s.OutputAssetId != null)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        SpriteSheetBackground? background = null;
        if (SpriteSheetPngCodec.TryReadRgba(source.Data, out var sourceWidth, out var sourceHeight, out var sourceRgba))
            background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        if (existing is not null)
        {
            var project = await GetProjectAsync(projectId, cancellationToken);
            project.ActiveSpriteSheetId = existing.Id;
            project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
            project.UpdatedAt = now;
            existing.UpdatedAt = now;
            if (background is not null && string.IsNullOrWhiteSpace(existing.BackgroundMode))
                ApplyBackground(existing, background);
            await db.SaveChangesAsync(cancellationToken);
            return await LoadSpriteSheetViewAsync(projectId, existing.Id, cancellationToken);
        }

        var definition = new SpriteSheetDefinition
        {
            ProjectId = projectId,
            SourceAssetId = source.Id,
            Label = CleanSpriteSheetLabel(string.Empty, source.Label),
            Rows = 1,
            Columns = 1,
            CellWidth = Math.Max(1, source.Width ?? 128),
            CellHeight = Math.Max(1, source.Height ?? 128),
            Padding = 8,
            Gutter = 16,
            Fps = 8,
            Loop = true,
            HorizontalAnchor = "center",
            VerticalAnchor = "bottom",
            BackgroundMode = background?.Mode,
            BackgroundColor = background is null ? null : BackgroundColor(background),
            FramesJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var working = CreateAsset(
            projectId,
            definition.Label,
            $"sprite-sheet-working-{now:yyyyMMddHHmmss}.png",
            ArtAssetKind.SpriteSheet,
            source.ContentType,
            source.Data,
            source.Id,
            source.SourceBatchId,
            source.SourcePromptRecipeId,
            source.SourcePromptRecipeVersion,
            source.Prompt,
            new
            {
                Source = "sprite-sheet-working",
                SourceAssetId = source.Id,
                SpriteSheetDefinitionId = definition.Id,
                Mutable = true,
            });
        definition.OutputAssetId = working.Id;

        await db.SpriteSheetDefinitions.AddAsync(definition, cancellationToken);
        await db.ArtAssets.AddAsync(working, cancellationToken);
        var selectedProject = await GetProjectAsync(projectId, cancellationToken);
        selectedProject.ActiveSpriteSheetId = definition.Id;
        selectedProject.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        selectedProject.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return SpriteSheetView(definition, []);
    }

    public async Task<SpriteSheetDefinitionView> ComposeSpriteSheetFromImagesAsync(
        Guid projectId,
        ComposeSpriteSheetFromImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceAssetIds = request.AssetIds
            .Where(id => id != Guid.Empty)
            .ToList();
        if (sourceAssetIds.Count == 0)
            throw new InvalidOperationException("At least one source image asset is required.");

        var assetsById = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId && sourceAssetIds.Distinct().Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        if (assetsById.Count != sourceAssetIds.Distinct().Count())
            throw new InvalidOperationException("One or more source image assets could not be found.");

        var newFrameInputs = new List<SpriteSheetFrameImageInput>();
        foreach (var (assetId, sourceOrder) in sourceAssetIds.Select((assetId, sourceOrder) => (assetId, sourceOrder)))
        {
            var asset = assetsById[assetId];
            if (!asset.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                || !SpriteSheetPngCodec.TryReadRgba(asset.Data, out var width, out var height, out var rgba))
            {
                throw new InvalidOperationException("Sprite-sheet composition requires PNG source images.");
            }

            newFrameInputs.Add(new SpriteSheetFrameImageInput(
                sourceOrder,
                string.IsNullOrWhiteSpace(asset.Label) ? $"Frame {sourceOrder + 1}" : asset.Label,
                rgba,
                width,
                height,
                UsedWorkingImage: true,
                asset.Id,
                new SpriteSheetRect(0, 0, width, height)));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        SpriteSheetDefinition definition;
        ArtAsset working;
        ArtAsset? composedSourceAsset = null;
        SpriteSheetBackground background;
        List<SpriteSheetFrameImageInput> inputs;

        if (request.SpriteSheetId is Guid spriteSheetId && spriteSheetId != Guid.Empty)
        {
            (definition, working, var records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
            if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var sheetWidth, out var sheetHeight, out var sheetRgba))
                throw new InvalidOperationException("Sprite-sheet composition requires a PNG working image.");

            background = EnsureSheetBackground(definition, sheetRgba, sheetWidth, sheetHeight);
            var existingInputs = BuildSpriteFrameImageInputsFromRecords(
                definition,
                sheetRgba,
                sheetWidth,
                sheetHeight,
                background,
                records);
            var insertAt = request.InsertAt is int requestedInsertAt
                ? Math.Clamp(requestedInsertAt, 0, existingInputs.Count)
                : existingInputs.Count;
            inputs = existingInputs
                .Take(insertAt)
                .Concat(newFrameInputs)
                .Concat(existingInputs.Skip(insertAt))
                .Select((input, index) => input with { Index = index })
                .ToList();
        }
        else
        {
            var firstAsset = assetsById[sourceAssetIds[0]];
            background = SpriteSheetImageAnalyzer.ResolveBackground(newFrameInputs[0].Rgba, newFrameInputs[0].Width, newFrameInputs[0].Height);
            definition = new SpriteSheetDefinition
            {
                ProjectId = projectId,
                Label = CleanSpriteSheetLabel(request.Label ?? string.Empty, $"{firstAsset.Label} sheet"),
                Rows = Math.Clamp(request.Rows ?? 1, 1, 32),
                Columns = Math.Clamp(request.Columns ?? sourceAssetIds.Count, 1, 64),
                CellWidth = Math.Max(1, newFrameInputs.Max(frame => frame.Width)),
                CellHeight = Math.Max(1, newFrameInputs.Max(frame => frame.Height)),
                Padding = Math.Clamp(request.Padding ?? 8, 0, 4096),
                Gutter = Math.Clamp(request.Gutter ?? 16, 0, 4096),
                Fps = Math.Clamp(request.Fps ?? 8, 1, 60),
                Loop = request.Loop ?? true,
                HorizontalAnchor = NormalizeHorizontalAnchor(request.HorizontalAnchor),
                VerticalAnchor = NormalizeVerticalAnchor(request.VerticalAnchor),
                BackgroundMode = background.Mode,
                BackgroundColor = background.Mode.Equals("color", StringComparison.OrdinalIgnoreCase) ? BackgroundColor(background) : null,
                FramesJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            };
            inputs = newFrameInputs
                .Select((input, index) => input with { Index = index })
                .ToList();

            var sourcePlaceholder = CreateAsset(
                projectId,
                $"{definition.Label} source",
                $"sprite-sheet-source-{now:yyyyMMddHHmmss}.png",
                ArtAssetKind.SpriteSheet,
                "image/png",
                newFrameInputs[0].Rgba.Length == 0
                    ? []
                    : SpriteSheetPngCodec.EncodeRgba(newFrameInputs[0].Width, newFrameInputs[0].Height, newFrameInputs[0].Rgba),
                parentAssetId: null,
                sourceBatchId: null,
                promptRecipeId: null,
                promptRecipeVersion: null,
                prompt: string.Empty,
                metadata: new
                {
                    Source = "sprite-sheet-composed-source",
                    SpriteSheetDefinitionId = definition.Id,
                    SourceImageAssetIds = sourceAssetIds,
                });
            definition.SourceAssetId = sourcePlaceholder.Id;
            composedSourceAsset = sourcePlaceholder;
            await db.ArtAssets.AddAsync(sourcePlaceholder, cancellationToken);

            working = CreateAsset(
                projectId,
                definition.Label,
                $"sprite-sheet-working-{now:yyyyMMddHHmmss}.png",
                ArtAssetKind.SpriteSheet,
                "image/png",
                sourcePlaceholder.Data,
                sourcePlaceholder.Id,
                sourceBatchId: null,
                promptRecipeId: null,
                promptRecipeVersion: null,
                prompt: string.Empty,
                metadata: new
                {
                    Source = "sprite-sheet-working",
                    SourceAssetId = sourcePlaceholder.Id,
                    SpriteSheetDefinitionId = definition.Id,
                    Mutable = true,
                    SourceImageAssetIds = sourceAssetIds,
                });
            definition.OutputAssetId = working.Id;
            await db.SpriteSheetDefinitions.AddAsync(definition, cancellationToken);
            await db.ArtAssets.AddAsync(working, cancellationToken);
        }

        var (rows, columns) = GridCapacity(
            request.Rows ?? definition.Rows,
            request.Columns ?? definition.Columns,
            inputs.Count);
        var render = SpriteSheetServerRenderer.ReassembleIrregularFrames(
            rows,
            columns,
            request.Padding is int padding ? Math.Clamp(padding, 0, 4096) : definition.Padding,
            request.Gutter is int gutter ? Math.Clamp(gutter, 0, 4096) : definition.Gutter,
            NormalizeHorizontalAnchor(request.HorizontalAnchor ?? definition.HorizontalAnchor),
            NormalizeVerticalAnchor(request.VerticalAnchor ?? definition.VerticalAnchor),
            background,
            inputs);

        UpdateSpriteSheetDefinition(
            definition,
            render.Rows,
            render.Columns,
            render.FrameWidth,
            render.FrameHeight,
            request.Padding is int renderPadding ? Math.Clamp(renderPadding, 0, 4096) : definition.Padding,
            request.Gutter is int renderGutter ? Math.Clamp(renderGutter, 0, 4096) : definition.Gutter,
            request.Fps is int fps ? Math.Clamp(fps, 1, 60) : definition.Fps,
            request.Loop ?? definition.Loop,
            request.HorizontalAnchor ?? definition.HorizontalAnchor,
            request.VerticalAnchor ?? definition.VerticalAnchor);
        UpdateWorkingSpriteAsset(working, new ImagePayload("image/png", render.PngData, render.Width, render.Height), definition, now);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, render.Frames, cancellationToken);

        if (composedSourceAsset is not null)
        {
            composedSourceAsset.ContentType = "image/png";
            composedSourceAsset.Data = render.PngData;
            composedSourceAsset.Width = render.Width;
            composedSourceAsset.Height = render.Height;
            composedSourceAsset.ThumbnailData = null;
            composedSourceAsset.UpdatedAt = now;
            composedSourceAsset.SourceMetadataJson = JsonSerializer.Serialize(new
            {
                Source = "sprite-sheet-composed-source",
                SpriteSheetDefinitionId = definition.Id,
                SourceImageAssetIds = sourceAssetIds,
                definition.Rows,
                definition.Columns,
                definition.CellWidth,
                definition.CellHeight,
                frameCount = inputs.Count,
            }, JsonOptions);
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        definition.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> AutosaveSpriteSheetLayoutAsync(
        Guid projectId,
        AutosaveSpriteSheetLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var (rows, columns) = GridCapacity(request.Rows, request.Columns, request.Frames.Count);
        UpdateSpriteSheetDefinition(definition, rows, columns, request.CellWidth, request.CellHeight, request.Padding, request.Gutter, request.Fps, request.Loop, request.HorizontalAnchor, request.VerticalAnchor);
        var frameViews = BuildPreviewlessFrameViews(definition, request.Frames);
        if (definition.OutputAsset is { } working && SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
        {
            var background = EnsureSheetBackground(definition, rgba, width, height);
            frameViews = SpriteSheetServerRenderer.BuildFramePreviews(
                rgba,
                width,
                height,
                definition.Rows,
                definition.Columns,
                definition.CellWidth,
                definition.CellHeight,
                definition.Padding,
                definition.Gutter,
                definition.Fps,
                definition.HorizontalAnchor,
                definition.VerticalAnchor,
                background,
                request.Frames).Frames;
        }

        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, frameViews, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = definition.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> NormalizeSpriteSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet normalization requires a PNG working image.");
        var background = EnsureSheetBackground(definition, rgba, width, height);

        var records = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        var (rows, columns) = GridCapacity(definition.Rows, definition.Columns, records.Count);
        UpdateSpriteSheetDefinition(definition, rows, columns, definition.CellWidth, definition.CellHeight, definition.Padding, definition.Gutter, definition.Fps, definition.Loop, definition.HorizontalAnchor, definition.VerticalAnchor);
        var updates = records.Select(FrameUpdateFromRecord).ToList();
        var layout = SpriteSheetServerRenderer.Render(
            rgba,
            width,
            height,
            definition.Rows,
            definition.Columns,
            definition.CellWidth,
            definition.CellHeight,
            definition.Padding,
            definition.Gutter,
            definition.Fps,
            definition.HorizontalAnchor,
            definition.VerticalAnchor,
            background,
            updates);
        var parsed = new ImagePayload("image/png", layout.PngData, layout.Width, layout.Height);
        UpdateWorkingSpriteAsset(working, parsed, definition, DateTime.UtcNow);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, layout.Frames, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = DateTime.UtcNow;
        definition.UpdatedAt = project.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> UpdateSpriteSheetFramesAsync(
        Guid projectId,
        UpdateSpriteSheetFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        var (rows, columns) = GridCapacity(request.Rows, request.Columns, request.Frames.Count);
        UpdateSpriteSheetDefinition(definition, rows, columns, request.CellWidth, request.CellHeight, request.Padding, request.Gutter, request.Fps, request.Loop, request.HorizontalAnchor, request.VerticalAnchor);
        IReadOnlyList<SpriteSheetFrameView> frameViews;
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
        {
            frameViews = BuildPreviewlessFrameViews(definition, request.Frames);
        }
        else
        {
            var background = EnsureSheetBackground(definition, rgba, width, height);
            frameViews = SpriteSheetServerRenderer.BuildFramePreviews(
                rgba,
                width,
                height,
                definition.Rows,
                definition.Columns,
                definition.CellWidth,
                definition.CellHeight,
                definition.Padding,
                definition.Gutter,
                definition.Fps,
                definition.HorizontalAnchor,
                definition.VerticalAnchor,
                background,
                request.Frames).Frames;
        }

        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, frameViews, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = definition.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> AdjustSpriteSheetFrameBoxAsync(
        Guid projectId,
        AdjustSpriteSheetFrameBoxRequest request,
        CancellationToken cancellationToken = default)
    {
        var sheet = await LoadSpriteSheetViewAsync(projectId, request.SpriteSheetId, cancellationToken);
        if (sheet.Frames.Count == 0)
            throw new InvalidOperationException("Sprite sheet has no saved frame records to adjust.");

        var targetIndex = Math.Clamp(request.FrameIndex, 0, sheet.Frames.Count - 1);
        var frames = sheet.Frames
            .OrderBy(frame => frame.Index)
            .Select(frame =>
            {
                if (frame.Index != targetIndex)
                {
                    return new SpriteSheetFrameUpdateView(
                        frame.Index,
                        frame.Label,
                        frame.SourceRect,
                        frame.ShapePaths,
                        frame.SourceImageAssetId,
                        frame.SourceImageRect);
                }

                var sourceRect = NormalizeSourceSpriteRect(request.SourceRect);
                return new SpriteSheetFrameUpdateView(
                    frame.Index,
                    string.IsNullOrWhiteSpace(request.Label) ? frame.Label : request.Label.Trim(),
                    sourceRect,
                    frame.ShapePaths,
                    frame.SourceImageAssetId ?? sheet.WorkingAssetId ?? sheet.SourceAssetId,
                    request.SourceImageRect ?? sourceRect);
            })
            .ToList();

        var padding = Math.Max(0, sheet.Padding);
        var cellWidth = request.FitCells
            ? Math.Max(1, frames.Max(frame => Math.Max(1, frame.SourceRect.Width)) + (padding * 2))
            : sheet.CellWidth;
        var cellHeight = request.FitCells
            ? Math.Max(1, frames.Max(frame => Math.Max(1, frame.SourceRect.Height)) + (padding * 2))
            : sheet.CellHeight;

        return await UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
            sheet.Id,
            sheet.Rows,
            sheet.Columns,
            cellWidth,
            cellHeight,
            padding,
            sheet.Gutter,
            sheet.Fps,
            sheet.Loop,
            sheet.HorizontalAnchor,
            sheet.VerticalAnchor,
            frames), cancellationToken);
    }

    public async Task<StabilizeSpriteSheetFramesResult> StabilizeSpriteSheetFramesAsync(
        Guid projectId,
        StabilizeSpriteSheetFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(sheet => sheet.OutputAsset)
            .FirstOrDefaultAsync(sheet => sheet.ProjectId == projectId && sheet.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-sheet stabilization requires a PNG working image.");

        var records = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        var referenceIndex = FrameIndexFromNumber(request.ReferenceFrameNumber);
        if (referenceIndex < 0 || referenceIndex >= records.Count)
            throw new InvalidOperationException("Reference frame number is outside the saved frame range.");

        var background = EnsureSheetBackground(definition, rgba, width, height);
        var inputs = BuildStabilizationWorkingFrameInputs(definition, rgba, width, height, background, records);
        var searchPadding = Math.Clamp(request.SearchPadding ?? 24, 0, 512);
        var minScore = Math.Clamp(request.MinScore ?? 0.68d, -1d, 1d);
        var referenceFrame = records[referenceIndex];
        var sourceAnchorRect = NormalizeStabilizationAnchor(request.AnchorRect, FrameSourceRect(referenceFrame));
        var referenceInput = inputs[referenceIndex];
        var referenceWorkingAnchorRect = NormalizeWorkingStabilizationAnchor(sourceAnchorRect, referenceInput);
        var referenceBackground = ResolveWorkingFrameBackground(definition, referenceInput.Image.Rgba, referenceInput.Image.Width, referenceInput.Image.Height);
        var template = BuildStabilizationTemplate(
            referenceInput.Image.Rgba,
            referenceInput.Image.Width,
            referenceInput.Image.Height,
            referenceWorkingAnchorRect,
            referenceBackground);
        var warnings = template.Warnings.ToList();
        var targetIndexes = TargetFrameIndexes(request.TargetFrameNumbers, records.Count);
        var drafts = new List<StabilizationMatchDraft>();
        var referenceRelativeX = sourceAnchorRect.X - referenceFrame.SourceX;
        var referenceRelativeY = sourceAnchorRect.Y - referenceFrame.SourceY;

        foreach (var index in targetIndexes)
        {
            var input = inputs[index];
            var draft = index == referenceIndex
                ? BuildReferenceStabilizationMatch(input, referenceWorkingAnchorRect)
                : MatchStabilizationFrame(
                    input,
                    template,
                    referenceRelativeX,
                    referenceRelativeY,
                    searchPadding,
                    minScore);
            drafts.Add(draft);
            if (draft.LowConfidence)
                warnings.Add($"Frame {draft.Frame.Record.Index + 1} matched below min score {minScore:0.###}.");
            warnings.AddRange(draft.Warnings.Select(warning => $"Frame {draft.Frame.Record.Index + 1}: {warning}"));
        }

        if (request.TargetFrameNumbers is { Count: > 0 })
            warnings.Add($"{(request.Apply ? "Applied" : "Prepared")} stabilization only for requested frame number(s): {string.Join(", ", request.TargetFrameNumbers.Order())}.");

        var targetCenterX = referenceWorkingAnchorRect.X + (referenceWorkingAnchorRect.Width / 2);
        var targetCenterY = referenceWorkingAnchorRect.Y + (referenceWorkingAnchorRect.Height / 2);
        var placements = BuildStabilizationPlacements(drafts, targetCenterX, targetCenterY);
        var normalizedWidth = placements.NormalizedWidth;
        var normalizedHeight = placements.NormalizedHeight;
        var matches = drafts
            .Select(draft =>
            {
                var placement = placements.ByIndex[draft.Frame.Record.Index];
                return new SpriteSheetStabilizationMatchView(
                    draft.Frame.Record.Index + 1,
                    draft.Frame.Record.Index,
                    FrameSourceRect(draft.Frame.Record),
                    new SpriteSheetRect(0, 0, draft.Frame.Image.Width, draft.Frame.Image.Height),
                    draft.MatchedAnchorRect,
                    placement.PlacementRect,
                    placement.DeltaX,
                    placement.DeltaY,
                    RoundMetric(draft.Score),
                    draft.LowConfidence,
                    Clipped: false,
                    draft.Warnings);
            })
            .OrderBy(match => match.Index)
            .ToList();
        var stabilization = new SpriteSheetStabilizationView(
            referenceIndex + 1,
            referenceIndex,
            sourceAnchorRect,
            referenceWorkingAnchorRect,
            new SpriteSheetPoint(targetCenterX - placements.MinDeltaX, targetCenterY - placements.MinDeltaY),
            normalizedWidth,
            normalizedHeight,
            searchPadding,
            RoundMetric(minScore),
            request.Apply,
            RequiresReassembly: request.Apply,
            DateTime.UtcNow,
            matches,
            warnings.Distinct(StringComparer.Ordinal).ToList());
        var diagnostic = BuildStabilizationDiagnosticImage(background, stabilization, inputs.Select(input => input.Image).ToList());

        var frameViews = inputs
            .Where(input => targetIndexes.Contains(input.Record.Index))
            .OrderBy(input => input.Record.Index)
            .Select(input => new SpriteSheetStabilizationFrameView(
                input.Record.Index + 1,
                input.Record.Index,
                string.IsNullOrWhiteSpace(input.Record.Label) ? $"Frame {input.Record.Index + 1}" : input.Record.Label,
                input.Image.Width,
                input.Image.Height,
                normalizedWidth,
                normalizedHeight,
                input.Image.UsedWorkingImage,
                request.Apply,
                request.Apply ? "stabilized" : NormalizeFrameWorkingState(input.Record.WorkingState)))
            .ToList();

        SpriteSheetDefinitionView? saved = null;
        if (request.Apply)
        {
            var now = DateTime.UtcNow;
            foreach (var draft in drafts)
            {
                var placement = placements.ByIndex[draft.Frame.Record.Index].PlacementRect;
                var rendered = SpriteSheetServerRenderer.RenderPlacedFrameImage(
                    draft.Frame.Image.Rgba,
                    draft.Frame.Image.Width,
                    draft.Frame.Image.Height,
                    background,
                    normalizedWidth,
                    normalizedHeight,
                    placement);
                ApplyFrameWorkingImage(draft.Frame.Record, "stabilized", rendered.PngData, rendered.Width, rendered.Height, margin: 0, now);
                draft.Frame.Record.UpdatedAt = now;
            }

            definition.StabilizationJson = SerializeStabilization(stabilization);
            definition.UpdatedAt = now;
            var project = await GetProjectAsync(projectId, cancellationToken);
            project.ActiveSpriteSheetId = definition.Id;
            project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
            project.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            saved = await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
        }

        return new StabilizeSpriteSheetFramesResult(
            definition.Id,
            definition.SourceAssetId,
            definition.OutputAssetId,
            request.Apply,
            width,
            height,
            stabilization,
            frameViews,
            stabilization.Warnings,
            diagnostic,
            saved);
    }

    public async Task<SpriteSheetDefinitionView> ClearSpriteSheetStabilizationAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .FirstOrDefaultAsync(sheet => sheet.ProjectId == projectId && sheet.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        definition.StabilizationJson = "{}";
        definition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, spriteSheetId, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> ExpandSpriteSheetFramesToCellAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Expanding sprite-sheet frames requires a PNG working image.");
        var background = EnsureSheetBackground(definition, rgba, width, height);

        var records = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        var (rows, columns) = GridCapacity(definition.Rows, definition.Columns, records.Count);
        UpdateSpriteSheetDefinition(definition, rows, columns, definition.CellWidth, definition.CellHeight, definition.Padding, definition.Gutter, definition.Fps, definition.Loop, definition.HorizontalAnchor, definition.VerticalAnchor);
        var updates = records
            .Select(frame => FrameUpdateFromRecord(frame) with
            {
                SourceRect = ExpandRectToCell(
                    RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
                    definition.CellWidth,
                    definition.CellHeight,
                    width,
                    height,
                    definition.HorizontalAnchor,
                    definition.VerticalAnchor)
            })
            .ToList();

        var preview = SpriteSheetServerRenderer.BuildFramePreviews(
            rgba,
            width,
            height,
            definition.Rows,
            definition.Columns,
            definition.CellWidth,
            definition.CellHeight,
            definition.Padding,
            definition.Gutter,
            definition.Fps,
            definition.HorizontalAnchor,
            definition.VerticalAnchor,
            background,
            updates);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, preview.Frames, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = definition.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> DeleteSpriteSheetFrameAsync(
        Guid projectId,
        Guid spriteSheetId,
        Guid frameId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var definition = await db.SpriteSheetDefinitions
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var deletedFrame = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id && frame.Id == frameId)
            .Select(frame => new { frame.Id, frame.Index })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sprite frame was not found.");

        var existingFrames = await LoadSpriteSheetFrameListItemsAsync(projectId, definition.Id, cancellationToken);
        var now = DateTime.UtcNow;
        var remainingFrames = ReindexSpriteSheetFrameListItems(
            definition,
            existingFrames.Where(frame => frame.Id != deletedFrame.Id).ToList(),
            now);
        var metadataUpdates = remainingFrames
            .Where(frame => frame.Index >= deletedFrame.Index)
            .ToList();

        DetachTrackedSpriteSheetFrameRecords(projectId, definition.Id);
        await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.SpriteFrame && a.RefId == frameId)
            .ExecuteDeleteAsync(cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteFrame, [frameId], cancellationToken);
        var deleted = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id && frame.Id == frameId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
            throw new InvalidOperationException("Sprite frame was not found.");

        await UpdateSpriteSheetFrameMetadataAsync(metadataUpdates, cancellationToken);
        definition.FramesJson = SerializeSpriteSheetFramesJson(remainingFrames);
        definition.UpdatedAt = now;

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> ResetSpriteSheetToOriginalAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.SourceAsset)
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        var source = definition.SourceAsset;
        var now = DateTime.UtcNow;

        working.ContentType = source.ContentType;
        working.Data = source.Data;
        working.Width = source.Width;
        working.Height = source.Height;
        working.ThumbnailData = null;
        working.UpdatedAt = now;
        if (SpriteSheetPngCodec.TryReadRgba(source.Data, out var width, out var height, out var rgba))
            ApplyBackground(definition, SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height));
        else
        {
            definition.BackgroundMode = null;
            definition.BackgroundColor = null;
        }
        definition.Rows = 1;
        definition.Columns = 1;
        definition.CellWidth = Math.Max(1, source.Width ?? 128);
        definition.CellHeight = Math.Max(1, source.Height ?? 128);
        definition.Padding = 8;
        definition.Gutter = 16;
        definition.Fps = 8;
        definition.Loop = true;
        definition.HorizontalAnchor = "center";
        definition.VerticalAnchor = "bottom";
        definition.FramesJson = "[]";
        definition.StabilizationJson = "{}";
        definition.UpdatedAt = now;

        DetachTrackedSpriteSheetFrameRecords(projectId, definition.Id);
        var frameIds = db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .Select(frame => frame.Id);
        await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.SpriteFrame && frameIds.Contains(a.RefId))
            .ExecuteDeleteAsync(cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteFrame, frameIds, cancellationToken);
        await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .ExecuteDeleteAsync(cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SpriteSheetView(definition, []);
    }

    public async Task<SpriteFrameWorkingView> IsolateSpriteFrameAsync(
        Guid projectId,
        Guid spriteSheetId,
        int frameIndex,
        int? margin = null,
        CancellationToken cancellationToken = default)
    {
        var (definition, working, records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
        var record = FrameRecordAt(records, frameIndex);
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-frame isolation requires a PNG working image.");

        var background = EnsureSheetBackground(definition, rgba, width, height);
        var marginValue = NormalizeFrameWorkingMargin(margin, definition.Padding);
        var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
            rgba,
            width,
            height,
            definition.Rows,
            definition.Columns,
            background,
            records.Select(FrameUpdateFromRecord).ToList(),
            frameIndex,
            marginValue);

        var now = DateTime.UtcNow;
        ApplyFrameWorkingImage(record, "isolated", extracted.PngData, extracted.Width, extracted.Height, marginValue, now);
        definition.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return FrameWorkingView(definition, record);
    }

    public async Task<IReadOnlyList<SpriteFrameWorkingView>> SplitSpriteSheetFramesAsync(
        Guid projectId,
        Guid spriteSheetId,
        int? margin = null,
        CancellationToken cancellationToken = default)
    {
        var (definition, working, records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-frame splitting requires a PNG working image.");

        var background = EnsureSheetBackground(definition, rgba, width, height);
        var marginValue = NormalizeFrameWorkingMargin(margin, definition.Padding);
        var updates = records.Select(FrameUpdateFromRecord).ToList();
        var now = DateTime.UtcNow;
        foreach (var record in records.OrderBy(record => record.Index))
        {
            var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
                rgba,
                width,
                height,
                definition.Rows,
                definition.Columns,
                background,
                updates,
                record.Index,
                marginValue);
            ApplyFrameWorkingImage(record, "isolated", extracted.PngData, extracted.Width, extracted.Height, marginValue, now);
            record.UpdatedAt = now;
        }

        definition.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return records
            .OrderBy(record => record.Index)
            .Select(record => FrameWorkingView(definition, record))
            .ToList();
    }

    public async Task<SpriteFrameWorkingView> GetSpriteFrameWorkingImageAsync(
        Guid projectId,
        Guid spriteSheetId,
        int frameIndex,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var record = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id && frame.Index == frameIndex)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sprite frame was not found.");
        return FrameWorkingView(definition, record);
    }

    public async Task<SpriteAnimationReviewImageView?> BuildSpriteFrameGridImageAsync(
        Guid projectId,
        Guid spriteSheetId,
        int frameIndex,
        CancellationToken cancellationToken = default)
    {
        var record = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId && frame.Index == frameIndex)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sprite frame was not found.");
        if (record.WorkingData.Length == 0
            || !SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var width, out var height, out var rgba))
        {
            return null;
        }

        var rendered = SpriteSheetServerRenderer.RenderCoordinateGridOverlay(rgba, width, height);
        return new SpriteAnimationReviewImageView(
            $"Frame {frameIndex + 1} coordinate grid",
            $"sprite-frame-{frameIndex + 1}-grid.png",
            "image/png",
            DataUrl.ToDataUrl("image/png", rendered.PngData),
            "grid",
            frameIndex,
            null,
            null);
    }

    public async Task<SpriteFrameWorkingView> EraseSpriteFrameRegionsAsync(
        Guid projectId,
        EraseSpriteFrameRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (definition, record) = await EnsureFrameWorkingImageAsync(
            projectId,
            request.SpriteSheetId,
            request.FrameIndex,
            margin: null,
            cancellationToken);
        if (!SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Isolated sprite frame must be a PNG image.");

        var keepSelection = string.Equals(request.Mode?.Trim(), "keep", StringComparison.OrdinalIgnoreCase);
        var background = ResolveWorkingFrameBackground(definition, rgba, width, height);
        var erased = SpriteSheetServerRenderer.EraseRegions(
            rgba,
            width,
            height,
            background,
            request.Rects,
            request.Polygons,
            keepSelection);

        var now = DateTime.UtcNow;
        ApplyFrameWorkingImage(record, "edited", erased.PngData, erased.Width, erased.Height, record.WorkingMargin, now);
        definition.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return FrameWorkingView(definition, record);
    }

    public async Task<SpriteFrameWorkingView> EditSpriteFrameAsync(
        Guid projectId,
        EditSpriteFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = CleanRequired(request.Prompt, "Frame edit prompt is required.");
        var (definition, record) = await EnsureFrameWorkingImageAsync(
            projectId,
            request.SpriteSheetId,
            request.FrameIndex,
            margin: null,
            cancellationToken);
        if (record.WorkingData.Length == 0)
            throw new InvalidOperationException("Sprite frame isolation failed.");

        var background = NormalizeBackground(request.Background ?? imageOptions.Value.DefaultBackground);
        var providerResult = await imageProvider.EditAsync(new ImageProviderEditRequest(
            BuildPrompt(prompt, string.Empty, recipe: null, background),
            NormalizeSize(imageOptions.Value.DefaultSize),
            1,
            imageOptions.Value.DefaultMainlineModel,
            imageOptions.Value.DefaultImageModel,
            new ImageProviderReference($"sprite-frame-{record.Index + 1}.png", record.WorkingContentType, record.WorkingData),
            Mask: null,
            ReferenceImages: [],
            OutputFormat: "png",
            Quality: imageOptions.Value.DefaultQuality,
            Background: background), cancellationToken);
        var image = providerResult.Images.FirstOrDefault()
            ?? throw new InvalidOperationException("Image provider completed without returning an image.");
        var contentType = NormalizeImageContentType(image.ContentType)
            ?? throw new InvalidOperationException("Image provider returned an unsupported image type.");
        if (!contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            || !SpriteSheetPngCodec.TryReadRgba(image.Data, out var width, out var height, out _))
        {
            throw new InvalidOperationException("Image provider returned a non-PNG frame image.");
        }

        var now = DateTime.UtcNow;
        ApplyFrameWorkingImage(record, "edited", image.Data, width, height, record.WorkingMargin, now);
        definition.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return FrameWorkingView(definition, record);
    }

    public async Task<SpriteFrameWorkingView> ClearSpriteFrameWorkingImageAsync(
        Guid projectId,
        Guid spriteSheetId,
        int frameIndex,
        CancellationToken cancellationToken = default)
    {
        var (definition, _, records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
        var record = FrameRecordAt(records, frameIndex);
        ClearFrameWorkingImage(record, "none");
        var now = DateTime.UtcNow;
        record.UpdatedAt = now;
        definition.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return FrameWorkingView(definition, record);
    }

    public async Task<ReassembleSpriteSheetResult> ReassembleSpriteSheetAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
        var (definition, working, records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var sheetWidth, out var sheetHeight, out var sheetRgba))
            throw new InvalidOperationException("Sprite-sheet reassembly requires a PNG working image.");

        var background = EnsureSheetBackground(definition, sheetRgba, sheetWidth, sheetHeight);
        var updates = records.Select(FrameUpdateFromRecord).ToList();
        var inputs = new List<SpriteSheetFrameImageInput>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.WorkingData.Length > 0)
            {
                if (!SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var width, out var height, out var rgba))
                    throw new InvalidOperationException($"Working image for frame {index + 1} must be a PNG.");
                inputs.Add(new SpriteSheetFrameImageInput(
                    index,
                    record.Label,
                    rgba,
                    width,
                    height,
                    UsedWorkingImage: true,
                    record.SourceImageAssetId,
                    SourceImageRectView(record),
                    NormalizeFrameWorkingState(record.WorkingState)));
                continue;
            }

            var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
                sheetRgba,
                sheetWidth,
                sheetHeight,
                definition.Rows,
                definition.Columns,
                background,
                updates,
                index,
                margin: 0);
            if (!SpriteSheetPngCodec.TryReadRgba(extracted.PngData, out var extractedWidth, out var extractedHeight, out var extractedRgba))
                throw new InvalidOperationException($"Extracted image for frame {index + 1} must be a PNG.");
            inputs.Add(new SpriteSheetFrameImageInput(
                index,
                record.Label,
                extractedRgba,
                extractedWidth,
                    extractedHeight,
                    UsedWorkingImage: false,
                    record.SourceImageAssetId,
                    SourceImageRectView(record),
                    NormalizeFrameWorkingState(record.WorkingState)));
        }

        var render = SpriteSheetServerRenderer.ReassembleIrregularFrames(
            rows: 1,
            columns: records.Count,
            definition.Padding,
            definition.Gutter,
            definition.HorizontalAnchor,
            definition.VerticalAnchor,
            background,
            inputs);

        var now = DateTime.UtcNow;
        UpdateSpriteSheetDefinition(
            definition,
            render.Rows,
            render.Columns,
            render.FrameWidth,
            render.FrameHeight,
            definition.Padding,
            definition.Gutter,
            definition.Fps,
            definition.Loop,
            definition.HorizontalAnchor,
            definition.VerticalAnchor);
        UpdateWorkingSpriteAsset(working, new ImagePayload("image/png", render.PngData, render.Width, render.Height), definition, now);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, render.Frames, cancellationToken);

        var updatedRecords = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        foreach (var frame in updatedRecords)
        {
            ClearFrameWorkingImage(frame, "reassembled");
            frame.UpdatedAt = now;
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        definition.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var saved = await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
        return new ReassembleSpriteSheetResult(
            saved,
            render.FrameInfos.Select(info => new SpriteFrameReassemblyView(
                info.Index,
                info.Label,
                info.UsedWorkingImage,
                info.DetectedRect,
                info.PlacedRect,
                info.Warnings)).ToList(),
            render.Warnings);
    }

    public async Task SelectSpriteSheetAsync(Guid projectId, Guid spriteSheetId, CancellationToken cancellationToken = default)
    {
        if (!await db.SpriteSheetDefinitions.AnyAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken))
            throw new InvalidOperationException("Sprite sheet was not found.");

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = spriteSheetId;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatContextAttachmentView>> AttachSpriteSheetFramesAsync(
        Guid projectId,
        Guid spriteSheetId,
        IReadOnlyList<Guid>? frameIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!await db.SpriteSheetDefinitions.AnyAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken))
            throw new InvalidOperationException("Sprite sheet was not found.");

        var query = db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId);
        if (frameIds is { Count: > 0 })
        {
            var ids = frameIds.Distinct().ToList();
            query = query.Where(frame => ids.Contains(frame.Id));
        }

        var frames = await query
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (frames.Count == 0)
            throw new InvalidOperationException("No sprite frames were found to attach.");

        var attachments = new List<ChatContextAttachmentView>();
        foreach (var frame in frames)
        {
            var label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label;
            attachments.Add(await AttachContextAsync(projectId, ChatContextAttachmentType.SpriteFrame, frame.Id, label, cancellationToken));
        }

        return attachments;
    }

    public async Task<string?> BuildSpriteSheetManifestJsonAsync(
        Guid projectId,
        Guid assetId,
        string pngFileName,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.OutputAssetId == assetId, cancellationToken);
        if (definition is null)
            return null;

        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet asset was not found.");
        var frames = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (frames.Count == 0)
            return null;

        var manifest = new
        {
            version = 1,
            format = "pixelchat.sprite-sheet",
            image = string.IsNullOrWhiteSpace(pngFileName) ? asset.FileName : pngFileName,
            width = asset.Width,
            height = asset.Height,
            definition.Rows,
            definition.Columns,
            definition.CellWidth,
            definition.CellHeight,
            definition.Padding,
            definition.Gutter,
            fps = definition.Fps,
            loop = definition.Loop,
            horizontalAnchor = NormalizeHorizontalAnchor(definition.HorizontalAnchor),
            verticalAnchor = NormalizeVerticalAnchor(definition.VerticalAnchor),
            background = BackgroundOrDefault(definition),
            frames = frames.Select(frame => new
            {
                frame.Index,
                label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
                sourceRect = RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
                frame.SourceImageAssetId,
                sourceImageRect = SourceImageRectView(frame),
                cellRect = RectView(frame.CellX, frame.CellY, frame.CellWidth, frame.CellHeight),
                spriteRect = RectView(frame.SpriteX, frame.SpriteY, frame.SpriteWidth, frame.SpriteHeight),
                shapePaths = DeserializeShapePaths(frame.ShapeJson),
                pivot = new
                {
                    x = frame.PivotX > 0 ? frame.PivotX : PivotForHorizontalAnchor(definition.HorizontalAnchor),
                    y = frame.PivotY > 0 ? frame.PivotY : PivotForVerticalAnchor(definition.VerticalAnchor),
                },
                duration = frame.DurationMs > 0 ? Math.Round(frame.DurationMs / 1000d, 6) : FrameDuration(definition.Fps),
                durationMs = frame.DurationMs > 0 ? frame.DurationMs : (int)Math.Round(FrameDuration(definition.Fps) * 1000d),
                poseName = Clean(frame.PoseName),
                phase = frame.Phase,
                rootOffset = new { x = frame.RootOffsetX, y = frame.RootOffsetY },
                footContacts = DeserializeStrings(frame.FootContactsJson),
                frame.IsKeyframe,
                appliedScale = frame.AppliedScale,
                translation = new { x = frame.TranslationX, y = frame.TranslationY },
                qaStatus = Clean(frame.QaStatus),
                repairHistory = DeserializeStrings(frame.RepairHistoryJson),
            }),
        };
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public async Task SetWorkspaceModeAsync(Guid projectId, WorkspaceMode mode, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = mode;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SelectBatchAsync(Guid projectId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (!await db.GenerationBatches.AnyAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken))
            throw new InvalidOperationException("Generation batch was not found.");

        project.ActiveBatchId = batchId;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GenerationBatchView> StartGenerateImagesAsync(
        Guid projectId,
        GenerateImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var prompt = CleanRequired(request.Prompt, "Prompt is required.");
        var count = ClampCount(request.Count);
        var explicitReferences = await ResolveAssetsAsync(projectId, request.ReferenceAssetIds, cancellationToken);
        if (explicitReferences.Count > imageOptions.Value.MaxReferenceImages)
            throw new InvalidOperationException($"Select no more than {imageOptions.Value.MaxReferenceImages} reference images.");

        PromptRecipe? recipe = null;
        int? promptRecipeVersion = null;
        if (request.PromptRecipeId is Guid recipeId)
        {
            recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
                ?? throw new InvalidOperationException("Prompt recipe was not found.");
            promptRecipeVersion = await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken);
        }

        var references = await MergeRecipeExampleReferenceAsync(projectId, recipe, explicitReferences, excludedAssetId: null, cancellationToken);

        var batch = new GenerationBatch
        {
            ProjectId = projectId,
            Label = $"Batch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel,
            ImageModel = string.IsNullOrWhiteSpace(request.ImageModel)
                ? imageOptions.Value.DefaultImageModel
                : request.ImageModel.Trim(),
            Prompt = prompt,
            NegativePrompt = Clean(request.NegativePrompt),
            Size = NormalizeSize(request.Size),
            Background = NormalizeBackground(request.Background),
            Count = count,
            InputAssetIdsJson = SerializeIds(references.Select(a => a.Id)),
            ParentBatchId = request.ParentBatchId,
            PromptRecipeId = recipe?.Id,
            PromptRecipeVersion = promptRecipeVersion,
            Status = GenerationBatchStatus.Running,
            OutputStatesJson = SerializeOutputStates(CreateInitialOutputStates(count)),
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Compare;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Image generation batch created: projectId={ProjectId}, batchId={BatchId}, count={Count}, size={Size}, mainlineModel={MainlineModel}, imageModel={ImageModel}, referenceImages={ReferenceImageCount}, promptChars={PromptChars}",
            projectId,
            batch.Id,
            batch.Count,
            batch.Size,
            batch.MainlineModel,
            batch.ImageModel,
            references.Count,
            prompt.Length);

        var batchAssets = await db.ArtAssets.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        return BatchView(batch, batchAssets);
    }

    public async Task<ArtAssetView> GenerateBatchOutputAsync(
        Guid projectId,
        Guid batchId,
        int outputIndex,
        CancellationToken cancellationToken = default,
        IProgress<ImageProviderProgress>? progress = null)
    {
        var batch = await db.GenerationBatches.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Generation batch was not found.");
        var references = await ResolveAssetsAsync(projectId, DeserializeIds(batch.InputAssetIdsJson), cancellationToken);
        var recipeGuidance = await LoadRecipePromptGuidanceForBatchAsync(projectId, batch, cancellationToken);

        logger.LogDebug(
            "Image generation output starting: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, size={Size}, mainlineModel={MainlineModel}, imageModel={ImageModel}, referenceImages={ReferenceImageCount}, promptChars={PromptChars}",
            projectId,
            batchId,
            outputIndex,
            batch.Size,
            batch.MainlineModel,
            batch.ImageModel,
            references.Count,
            batch.Prompt.Length);

        ImageProviderResult providerResult;
        try
        {
            providerResult = await imageProvider.GenerateAsync(new ImageProviderGenerateRequest(
                BuildPrompt(batch.Prompt, batch.NegativePrompt, recipeGuidance, batch.Background),
                Clean(batch.NegativePrompt),
                batch.Size,
                1,
                batch.MainlineModel,
                batch.ImageModel,
                references.Select(ToProviderReference).ToList(),
                imageOptions.Value.DefaultOutputFormat,
                imageOptions.Value.DefaultQuality,
                NormalizeBackground(batch.Background)), cancellationToken, progress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Image generation output failed before asset creation: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, mainlineModel={MainlineModel}, imageModel={ImageModel}",
                projectId,
                batchId,
                outputIndex,
                batch.MainlineModel,
                batch.ImageModel);
            throw;
        }

        batch.Provider = providerResult.Provider;
        batch.MainlineModel = providerResult.MainlineModel;
        batch.ImageModel = providerResult.ImageModel;
        batch.RawProviderResponseJson = providerResult.RawMetadataJson;
        batch.UpdatedAt = DateTime.UtcNow;

        var image = providerResult.Images.FirstOrDefault()
            ?? throw new InvalidOperationException("Image provider completed without returning an image.");
        var asset = CreateAsset(
            projectId,
            $"Image {LabelForIndex(outputIndex)}",
            $"generated-{DateTime.UtcNow:yyyyMMddHHmmss}-{outputIndex + 1}.{ExtensionForContentType(image.ContentType)}",
            ArtAssetKind.Generated,
            image.ContentType,
            image.Data,
            parentAssetId: null,
            sourceBatchId: batch.Id,
            promptRecipeId: batch.PromptRecipeId,
            promptRecipeVersion: batch.PromptRecipeVersion,
            prompt: batch.Prompt,
            metadata: new
            {
                OutputIndex = outputIndex,
                batch.PromptRecipeVersion,
                image.RevisedPrompt,
                image.ResponseId,
                image.CallId,
                image.OutputFormat,
                References = references.Select(a => new { a.Id, a.Label, a.ContentType }),
            });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Image generation output succeeded: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, assetId={AssetId}, contentType={ContentType}, width={Width}, height={Height}, imageBytes={ImageBytes}",
            projectId,
            batchId,
            outputIndex,
            asset.Id,
            asset.ContentType,
            asset.Width,
            asset.Height,
            asset.Data.Length);
        return AssetView(asset);
    }

    public async Task MarkGenerationBatchOutputStateAsync(
        Guid projectId,
        Guid batchId,
        GenerationOutputStateView outputState,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken);
        if (batch is null || outputState.OutputIndex < 0 || outputState.OutputIndex >= batch.Count)
            return;

        var states = UpsertOutputState(
            NormalizeOutputStates(batch.OutputStatesJson, batch.Count),
            CleanOutputState(outputState));
        batch.OutputStatesJson = SerializeOutputStates(states);
        batch.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkGenerationBatchOutputFailedAsync(
        Guid projectId,
        Guid batchId,
        int outputIndex,
        string error,
        CancellationToken cancellationToken = default)
    {
        await MarkGenerationBatchOutputFailedAsync(
            projectId,
            batchId,
            new GenerationOutputErrorView(outputIndex, error),
            cancellationToken);
    }

    public async Task MarkGenerationBatchOutputFailedAsync(
        Guid projectId,
        Guid batchId,
        GenerationOutputErrorView outputError,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken);
        if (batch is null)
            return;
        if (outputError.OutputIndex < 0 || outputError.OutputIndex >= batch.Count)
            return;

        var cleanedError = Clean(outputError.Error);
        if (string.IsNullOrWhiteSpace(cleanedError))
            cleanedError = "Image request failed.";

        var cleanedOutputError = outputError with { Error = cleanedError };
        var errors = DeserializeOutputErrors(batch.OutputErrorsJson)
            .Where(item => item.OutputIndex != outputError.OutputIndex)
            .Append(CleanOutputError(cleanedOutputError))
            .OrderBy(item => item.OutputIndex)
            .ToList();
        var existingState = NormalizeOutputStates(batch.OutputStatesJson, batch.Count)
            .FirstOrDefault(item => item.OutputIndex == outputError.OutputIndex);
        var failedState = new GenerationOutputStateView(
            outputError.OutputIndex,
            GenerationOutputStatus.Failed,
            existingState?.Attempt ?? 0,
            "Image request failed.",
            cleanedError,
            outputError.ErrorKind,
            outputError.RequestId,
            outputError.ResponseId,
            outputError.CallId,
            outputError.LastEventType,
            outputError.EventCount,
            existingState?.StartedAt,
            DateTime.UtcNow,
            DateTime.UtcNow);
        batch.OutputErrorsJson = SerializeOutputErrors(errors);
        batch.OutputStatesJson = SerializeOutputStates(UpsertOutputState(NormalizeOutputStates(batch.OutputStatesJson, batch.Count), failedState));
        batch.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Image generation output failure persisted: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, error={Error}",
            projectId,
            batchId,
            outputError.OutputIndex,
            TruncateForLog(cleanedError, 1000));
    }

    public async Task<GenerationBatchView> CompleteGenerationBatchAsync(
        Guid projectId,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Generation batch was not found.");
        var outputAssets = await db.ArtAssets
            .Where(a => a.ProjectId == projectId && a.SourceBatchId == batchId)
            .ToListAsync(cancellationToken);
        var outputIndexes = outputAssets
            .Select(ReadBatchOutputIndex)
            .OfType<int>()
            .Where(index => index >= 0 && index < batch.Count)
            .ToHashSet();
        var unindexedOutputCount = outputAssets.Count(a => ReadBatchOutputIndex(a) is null);
        var outputCount = outputIndexes.Count + unindexedOutputCount;
        var states = NormalizeOutputStates(batch.OutputStatesJson, batch.Count, outputIndexes);
        foreach (var outputIndex in outputIndexes)
        {
            var existingState = states.FirstOrDefault(state => state.OutputIndex == outputIndex);
            states = UpsertOutputState(states, new GenerationOutputStateView(
                outputIndex,
                GenerationOutputStatus.Succeeded,
                existingState?.Attempt ?? 0,
                "Image saved.",
                "",
                RequestId: existingState?.RequestId,
                ResponseId: existingState?.ResponseId,
                CallId: existingState?.CallId,
                LastEventType: existingState?.LastEventType,
                EventCount: existingState?.EventCount ?? 0,
                StartedAt: existingState?.StartedAt,
                UpdatedAt: DateTime.UtcNow,
                CompletedAt: existingState?.CompletedAt ?? DateTime.UtcNow));
        }

        var errors = MergeOutputErrors(
            NormalizeOutputErrors(batch.OutputErrorsJson, batch.Count, outputIndexes),
            states
                .Where(state => IsFailedOutputStatus(state.Status) && !outputIndexes.Contains(state.OutputIndex))
                .Select(OutputErrorFromState));
        var missingErrorSlots = Math.Max(0, batch.Count - outputCount - errors.Count);
        if (missingErrorSlots > 0)
        {
            var errorIndexes = errors.Select(error => error.OutputIndex).ToHashSet();
            var missingErrors = Enumerable.Range(0, batch.Count)
                .Where(index => !outputIndexes.Contains(index) && !errorIndexes.Contains(index))
                .Take(missingErrorSlots)
                .Select(index => new GenerationOutputErrorView(index, "Image request did not return an output before the batch completed."))
                .ToList();
            errors = MergeOutputErrors(errors, missingErrors);
            foreach (var missingError in missingErrors)
            {
                var existingState = states.FirstOrDefault(state => state.OutputIndex == missingError.OutputIndex);
                states = UpsertOutputState(states, new GenerationOutputStateView(
                    missingError.OutputIndex,
                    GenerationOutputStatus.Failed,
                    existingState?.Attempt ?? 0,
                    "Image request failed.",
                    missingError.Error,
                    StartedAt: existingState?.StartedAt,
                    UpdatedAt: DateTime.UtcNow,
                    CompletedAt: DateTime.UtcNow));
            }
        }

        batch.Status = errors.Count switch
        {
            0 when outputCount >= batch.Count => GenerationBatchStatus.Succeeded,
            _ when outputCount == 0 => GenerationBatchStatus.Failed,
            _ => GenerationBatchStatus.CompletedWithErrors,
        };
        batch.Error = errors.Count == 0
            ? string.Empty
            : OutputErrorSummary(errors.Count, batch.Count);
        batch.OutputErrorsJson = SerializeOutputErrors(errors);
        batch.OutputStatesJson = SerializeOutputStates(states);
        batch.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Image generation batch completed: projectId={ProjectId}, batchId={BatchId}, status={Status}, requestedCount={RequestedCount}, outputCount={OutputCount}, errorCount={ErrorCount}",
            projectId,
            batchId,
            batch.Status,
            batch.Count,
            outputCount,
            errors.Count);
        var batchAssets = await db.ArtAssets.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        return BatchView(batch, batchAssets);
    }

    public async Task<GenerationBatchView> GenerateImagesAsync(
        Guid projectId,
        GenerateImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var batch = await StartGenerateImagesAsync(projectId, request, cancellationToken);
        for (var outputIndex = 0; outputIndex < batch.Count; outputIndex++)
        {
            try
            {
                await MarkGenerationBatchOutputStateAsync(
                    projectId,
                    batch.Id,
                    new GenerationOutputStateView(outputIndex, GenerationOutputStatus.Running, 1, "Generating image.", StartedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
                    cancellationToken);
                await GenerateBatchOutputAsync(projectId, batch.Id, outputIndex, cancellationToken);
                await MarkGenerationBatchOutputStateAsync(
                    projectId,
                    batch.Id,
                    new GenerationOutputStateView(outputIndex, GenerationOutputStatus.Succeeded, 1, "Image saved.", UpdatedAt: DateTime.UtcNow, CompletedAt: DateTime.UtcNow),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await MarkGenerationBatchOutputFailedAsync(projectId, batch.Id, outputIndex, ex.Message, cancellationToken);
            }
        }

        return await CompleteGenerationBatchAsync(projectId, batch.Id, cancellationToken);
    }

    public async Task ReconcileInterruptedGenerationBatchesAsync(CancellationToken cancellationToken = default)
    {
        var runningBatches = await db.GenerationBatches
            .Where(batch => batch.Status == GenerationBatchStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var batch in runningBatches)
        {
            var outputAssets = await db.ArtAssets
                .Where(asset => asset.ProjectId == batch.ProjectId && asset.SourceBatchId == batch.Id)
                .ToListAsync(cancellationToken);
            var outputIndexes = outputAssets
                .Select(ReadBatchOutputIndex)
                .OfType<int>()
                .Where(index => index >= 0 && index < batch.Count)
                .ToHashSet();
            var states = NormalizeOutputStates(batch.OutputStatesJson, batch.Count, outputIndexes);
            foreach (var state in states.Where(state => !outputIndexes.Contains(state.OutputIndex) && !IsTerminalOutputStatus(state.Status)).ToList())
            {
                var error = new GenerationOutputErrorView(
                    state.OutputIndex,
                    "Image generation was interrupted before this request completed.",
                    LastEventType: state.LastEventType,
                    EventCount: state.EventCount);
                await MarkGenerationBatchOutputFailedAsync(batch.ProjectId, batch.Id, error, cancellationToken);
            }

            await CompleteGenerationBatchAsync(batch.ProjectId, batch.Id, cancellationToken);
        }
    }

    public async Task<GenerationBatchView> StartEditImageAsync(
        Guid projectId,
        EditImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var prompt = CleanRequired(request.Prompt, "Edit prompt is required.");
        var sourceAsset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        var referenceIds = request.ReferenceAssetIds
            .Where(id => id != sourceAsset.Id)
            .ToList();
        var explicitReferences = await ResolveAssetsAsync(projectId, referenceIds, cancellationToken);
        if (explicitReferences.Count > imageOptions.Value.MaxReferenceImages)
            throw new InvalidOperationException($"Select no more than {imageOptions.Value.MaxReferenceImages} reference images.");
        var count = ClampCount(request.Count);

        PromptRecipe? recipe = null;
        int? promptRecipeVersion = null;
        if (request.PromptRecipeId is Guid recipeId)
        {
            recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
                ?? throw new InvalidOperationException("Prompt recipe was not found.");
            promptRecipeVersion = await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken);
        }

        var references = await MergeRecipeExampleReferenceAsync(projectId, recipe, explicitReferences, sourceAsset.Id, cancellationToken);

        var sourceImage = ResolveEditSourceImage(sourceAsset, request.SourcePngDataUrl);
        var batchId = Guid.NewGuid();
        ImageMask? storedMask = null;
        if (!string.IsNullOrWhiteSpace(request.MaskPngDataUrl))
        {
            storedMask = string.IsNullOrWhiteSpace(request.SourcePngDataUrl)
                ? await UpsertAssetMaskEntityAsync(
                    projectId,
                    sourceAsset,
                    request.MaskPngDataUrl,
                    $"{sourceAsset.Label} mask",
                    requireEditableArea: true,
                    cancellationToken)
                : await CreateEditSourceMaskEntityAsync(
                    projectId,
                    sourceAsset,
                    batchId,
                    request.MaskPngDataUrl,
                    $"{sourceAsset.Label} edit mask",
                    sourceImage,
                    cancellationToken);
            ValidateEditImageAndMask(sourceImage, storedMask);
        }

        var batch = new GenerationBatch
        {
            Id = batchId,
            ProjectId = projectId,
            Label = $"Edit {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel,
            ImageModel = imageOptions.Value.DefaultImageModel,
            Prompt = prompt,
            Size = NormalizeSize(request.Size),
            Background = NormalizeBackground(request.Background),
            Count = count,
            InputAssetIdsJson = SerializeIds(new[] { sourceAsset.Id }.Concat(references.Select(a => a.Id))),
            InputMaskIdsJson = SerializeIds(storedMask is null ? Enumerable.Empty<Guid>() : new[] { storedMask.Id }),
            EditSourceContentType = sourceImage.ContentType,
            EditSourceData = sourceImage.Data,
            EditSourceWidth = sourceImage.Width,
            EditSourceHeight = sourceImage.Height,
            ParentBatchId = sourceAsset.SourceBatchId,
            PromptRecipeId = recipe?.Id,
            PromptRecipeVersion = promptRecipeVersion,
            Status = GenerationBatchStatus.Running,
            OutputStatesJson = SerializeOutputStates(CreateInitialOutputStates(count)),
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
        if (request.SwitchToCompare)
            project.ActiveWorkspaceMode = WorkspaceMode.Compare;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Image edit batch created: projectId={ProjectId}, batchId={BatchId}, sourceAssetId={SourceAssetId}, count={Count}, size={Size}, mainlineModel={MainlineModel}, imageModel={ImageModel}, referenceImages={ReferenceImageCount}, hasMask={HasMask}, promptChars={PromptChars}",
            projectId,
            batch.Id,
            sourceAsset.Id,
            batch.Count,
            batch.Size,
            batch.MainlineModel,
            batch.ImageModel,
            references.Count,
            storedMask is not null,
            prompt.Length);

        var batchAssets = await db.ArtAssets.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        return BatchView(batch, batchAssets);
    }

    public async Task<ArtAssetView> GenerateEditBatchOutputAsync(
        Guid projectId,
        Guid batchId,
        int outputIndex,
        CancellationToken cancellationToken = default,
        IProgress<ImageProviderProgress>? progress = null)
    {
        var batch = await db.GenerationBatches.FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Generation batch was not found.");
        if (outputIndex < 0 || outputIndex >= batch.Count)
            throw new InvalidOperationException("Output index is outside the batch range.");

        var inputAssetIds = DeserializeIds(batch.InputAssetIdsJson);
        var sourceAssetId = inputAssetIds.FirstOrDefault();
        if (sourceAssetId == Guid.Empty)
            throw new InvalidOperationException("Edit batch source asset was not found.");

        var sourceAsset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        var references = await ResolveAssetsAsync(projectId, inputAssetIds.Skip(1).ToList(), cancellationToken);
        var sourceImage = ResolveStoredEditSourceImage(batch, sourceAsset);
        var recipeGuidance = await LoadRecipePromptGuidanceForBatchAsync(projectId, batch, cancellationToken);

        ImageMask? storedMask = null;
        var inputMaskId = DeserializeIds(batch.InputMaskIdsJson).FirstOrDefault();
        if (inputMaskId != Guid.Empty)
        {
            storedMask = await db.ImageMasks.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Id == inputMaskId, cancellationToken)
                ?? throw new InvalidOperationException("Edit mask was not found.");
            ValidateEditImageAndMask(sourceImage, storedMask);
        }

        logger.LogDebug(
            "Image edit output starting: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, sourceAssetId={SourceAssetId}, size={Size}, mainlineModel={MainlineModel}, imageModel={ImageModel}, referenceImages={ReferenceImageCount}, hasMask={HasMask}, promptChars={PromptChars}",
            projectId,
            batchId,
            outputIndex,
            sourceAsset.Id,
            batch.Size,
            batch.MainlineModel,
            batch.ImageModel,
            references.Count,
            storedMask is not null,
            batch.Prompt.Length);

        ImageProviderResult providerResult;
        try
        {
            providerResult = await imageProvider.EditAsync(new ImageProviderEditRequest(
                BuildPrompt(batch.Prompt, string.Empty, recipeGuidance, batch.Background),
                batch.Size,
                1,
                batch.MainlineModel,
                batch.ImageModel,
                new ImageProviderReference(sourceAsset.FileName, sourceImage.ContentType, sourceImage.Data),
                storedMask is null ? null : new ImageProviderReference(storedMask.Label, storedMask.ContentType, storedMask.Data),
                references.Select(ToProviderReference).ToList(),
                imageOptions.Value.DefaultOutputFormat,
                imageOptions.Value.DefaultQuality,
                NormalizeBackground(batch.Background)), cancellationToken, progress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Image edit output failed before asset creation: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, mainlineModel={MainlineModel}, imageModel={ImageModel}",
                projectId,
                batchId,
                outputIndex,
                batch.MainlineModel,
                batch.ImageModel);
            throw;
        }

        batch.Provider = providerResult.Provider;
        batch.MainlineModel = providerResult.MainlineModel;
        batch.ImageModel = providerResult.ImageModel;
        batch.RawProviderResponseJson = providerResult.RawMetadataJson;
        batch.UpdatedAt = DateTime.UtcNow;

        var image = providerResult.Images.FirstOrDefault()
            ?? throw new InvalidOperationException("Image provider completed without returning an image.");
        var asset = CreateAsset(
            projectId,
            $"{sourceAsset.Label} edit {LabelForIndex(outputIndex)}",
            $"edited-{DateTime.UtcNow:yyyyMMddHHmmss}-{outputIndex + 1}.{ExtensionForContentType(image.ContentType)}",
            ArtAssetKind.Edited,
            image.ContentType,
            image.Data,
            sourceAsset.Id,
            batch.Id,
            batch.PromptRecipeId,
            batch.PromptRecipeVersion,
            batch.Prompt,
            new
            {
                OutputIndex = outputIndex,
                batch.PromptRecipeVersion,
                image.RevisedPrompt,
                image.ResponseId,
                image.CallId,
                image.OutputFormat,
                SourceAssetId = sourceAsset.Id,
                MaskId = storedMask?.Id,
                References = references.Select(a => new { a.Id, a.Label, a.ContentType }),
            });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Image edit output succeeded: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}, assetId={AssetId}, contentType={ContentType}, width={Width}, height={Height}, imageBytes={ImageBytes}",
            projectId,
            batchId,
            outputIndex,
            asset.Id,
            asset.ContentType,
            asset.Width,
            asset.Height,
            asset.Data.Length);
        return AssetView(asset);
    }

    public async Task<ArtAssetView> ImportAssetAsync(Guid projectId, ImportAssetRequest request, CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var contentType = NormalizeImageContentType(request.ContentType)
            ?? throw new InvalidOperationException("Only PNG and JPEG images can be imported.");
        if (request.Data.Length == 0)
            throw new InvalidOperationException("Image file is empty.");

        var asset = CreateAsset(
            projectId,
            string.IsNullOrWhiteSpace(request.Label) ? Path.GetFileNameWithoutExtension(request.FileName) : request.Label.Trim(),
            string.IsNullOrWhiteSpace(request.FileName) ? $"imported-{DateTime.UtcNow:yyyyMMddHHmmss}.{ExtensionForContentType(contentType)}" : request.FileName.Trim(),
            ArtAssetKind.Imported,
            contentType,
            request.Data,
            parentAssetId: null,
            sourceBatchId: null,
            promptRecipeId: null,
            promptRecipeVersion: null,
            prompt: string.Empty,
            metadata: new { Source = "import" });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await SetWorkspaceModeAfterAssetMutationAsync(projectId, WorkspaceMode.Edit, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
    }

    public async Task<ArtAssetView> CreateCropAssetAsync(Guid projectId, CropAssetRequest request, CancellationToken cancellationToken = default)
    {
        var parent = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.ParentAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Parent asset was not found.");
        var (contentType, data) = DataUrl.Parse(request.CropDataUrl);
        contentType = NormalizeImageContentType(contentType) ?? "image/png";
        var asset = CreateAsset(
            projectId,
            string.IsNullOrWhiteSpace(request.Label) ? $"{parent.Label} crop" : request.Label.Trim(),
            $"crop-{DateTime.UtcNow:yyyyMMddHHmmss}.{ExtensionForContentType(contentType)}",
            ArtAssetKind.Cropped,
            contentType,
            data,
            parent.Id,
            parent.SourceBatchId,
            parent.SourcePromptRecipeId,
            parent.SourcePromptRecipeVersion,
            parent.Prompt,
            new { ParentAssetId = parent.Id });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await SetWorkspaceModeAfterAssetMutationAsync(projectId, WorkspaceMode.Edit, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
    }

    public async Task<ExtractRegionAsAssetResult> ExtractRegionAsAssetAsync(
        Guid projectId,
        ExtractRegionAsAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var source = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        if (!ImageRgbaDecoder.TryReadRgba(source, out var sourceWidth, out var sourceHeight, out var sourceRgba))
            throw new InvalidOperationException("Region extraction requires a readable PNG or JPEG source image.");

        // Clamp the requested region to the source bounds (source-image coordinate space).
        var rectX = Math.Clamp(request.X, 0, Math.Max(0, sourceWidth - 1));
        var rectY = Math.Clamp(request.Y, 0, Math.Max(0, sourceHeight - 1));
        var rectW = Math.Clamp(request.Width, 1, sourceWidth - rectX);
        var rectH = Math.Clamp(request.Height, 1, sourceHeight - rectY);
        var padding = Math.Clamp(request.Padding, 0, 4096);

        // Determine the opaque output canvas and where the cropped content sits in it.
        int canvasW;
        int canvasH;
        if (request.FixedCanvasWidth is int fixedW and > 0 && request.FixedCanvasHeight is int fixedH and > 0)
        {
            canvasW = Math.Min(fixedW, 8192);
            canvasH = Math.Min(fixedH, 8192);
        }
        else
        {
            canvasW = rectW + (padding * 2);
            canvasH = rectH + (padding * 2);
        }

        var offsetX = request.CenterInCanvas ? Math.Max(0, (canvasW - rectW) / 2) : padding;
        var offsetY = request.CenterInCanvas ? Math.Max(0, (canvasH - rectH) / 2) : padding;

        // Fill with the opaque source background color, then blit the region.
        var canvasRgba = new byte[canvasW * canvasH * 4];
        var bgR = sourceRgba[0];
        var bgG = sourceRgba[1];
        var bgB = sourceRgba[2];
        for (var i = 0; i < canvasW * canvasH; i++)
        {
            canvasRgba[(i * 4) + 0] = bgR;
            canvasRgba[(i * 4) + 1] = bgG;
            canvasRgba[(i * 4) + 2] = bgB;
            canvasRgba[(i * 4) + 3] = 255;
        }

        for (var y = 0; y < rectH; y++)
        {
            var destY = offsetY + y;
            if (destY < 0 || destY >= canvasH)
                continue;
            for (var x = 0; x < rectW; x++)
            {
                var destX = offsetX + x;
                if (destX < 0 || destX >= canvasW)
                    continue;
                var srcIndex = (((rectY + y) * sourceWidth) + (rectX + x)) * 4;
                var destIndex = ((destY * canvasW) + destX) * 4;
                canvasRgba[destIndex + 0] = sourceRgba[srcIndex + 0];
                canvasRgba[destIndex + 1] = sourceRgba[srcIndex + 1];
                canvasRgba[destIndex + 2] = sourceRgba[srcIndex + 2];
                canvasRgba[destIndex + 3] = 255;
            }
        }

        var png = SpriteSheetPngCodec.EncodeRgba(canvasW, canvasH, canvasRgba);
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Label} region" : request.Name.Trim();

        var asset = CreateAsset(
            projectId,
            name,
            $"extracted-{DateTime.UtcNow:yyyyMMddHHmmss}.png",
            ArtAssetKind.Extracted,
            "image/png",
            png,
            source.Id,
            source.SourceBatchId,
            source.SourcePromptRecipeId,
            source.SourcePromptRecipeVersion,
            source.Prompt,
            new { ExtractedFrom = source.Id, Region = new { rectX, rectY, rectW, rectH } });
        await db.ArtAssets.AddAsync(asset, cancellationToken);

        var region = new SpriteRegion
        {
            ProjectId = projectId,
            SourceAssetId = source.Id,
            Name = name,
            X = rectX,
            Y = rectY,
            Width = rectW,
            Height = rectH,
            RegionType = "asset",
        };
        await db.SpriteRegions.AddAsync(region, cancellationToken);

        var standalone = new StandaloneAsset
        {
            ProjectId = projectId,
            SourceRegionId = request.LinkToSource ? region.Id : null,
            OutputAssetId = asset.Id,
            Name = name,
            LogicalWidth = canvasW,
            LogicalHeight = canvasH,
            ContentOffsetX = offsetX,
            ContentOffsetY = offsetY,
            LinkedToSource = request.LinkToSource,
        };
        await db.StandaloneAssets.AddAsync(standalone, cancellationToken);

        await SetWorkspaceModeAfterAssetMutationAsync(projectId, WorkspaceMode.Sprites, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new ExtractRegionAsAssetResult(
            AssetView(asset),
            region.Id,
            standalone.Id,
            canvasW,
            canvasH,
            offsetX,
            offsetY);
    }

    public async Task<ImageMaskView> UpsertAssetMaskAsync(
        Guid projectId,
        Guid assetId,
        string maskDataUrl,
        string label,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        var mask = await UpsertAssetMaskEntityAsync(
            projectId,
            asset,
            maskDataUrl,
            label,
            requireEditableArea: false,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return MaskView(mask);
    }

    public async Task ClearAssetMaskAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken))
            throw new InvalidOperationException("Asset was not found.");

        var masks = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.AssetId == assetId && m.OwnerKind == "asset")
            .ToListAsync(cancellationToken);
        if (masks.Count == 0)
            return;

        var maskIds = masks.Select(m => m.Id).ToList();
        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId
                && a.Type == ChatContextAttachmentType.Mask
                && maskIds.Contains(a.RefId))
            .ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);
        db.ImageMasks.RemoveRange(masks);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PromptRecipeView> SavePromptRecipeAsync(Guid projectId, SavePromptRecipeRequest request, CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        await ValidateRecipeExampleAssetAsync(projectId, request.ExampleAssetId, cancellationToken);
        var recipe = new PromptRecipe
        {
            ProjectId = projectId,
            Name = CleanRequired(request.Name, "Recipe name is required."),
        };
        ApplyRecipeRequest(recipe, request);
        await db.PromptRecipes.AddAsync(recipe, cancellationToken);
        var version = await AppendPromptRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe, version);
    }

    public async Task<PromptRecipeView> UpdatePromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        UpdatePromptRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        await ValidateRecipeExampleAssetAsync(projectId, request.ExampleAssetId, cancellationToken);
        ApplyRecipeRequest(recipe, request);
        recipe.UpdatedAt = DateTime.UtcNow;
        var version = await AppendPromptRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe, version);
    }

    public async Task<PromptRecipeView> DuplicatePromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        string? name = null,
        string source = "user",
        string changeSummary = "",
        CancellationToken cancellationToken = default)
    {
        var sourceRecipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");
        var exampleAssetId = sourceRecipe.ExampleAssetId;
        if (exampleAssetId is Guid sourceExampleAssetId
            && !await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == sourceExampleAssetId, cancellationToken))
        {
            exampleAssetId = null;
        }

        var duplicate = new PromptRecipe
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? $"{sourceRecipe.Name} Copy" : name.Trim(),
            AssetType = sourceRecipe.AssetType,
            PromptTemplate = sourceRecipe.PromptTemplate,
            StyleRulesJson = sourceRecipe.StyleRulesJson,
            AvoidRulesJson = sourceRecipe.AvoidRulesJson,
            ExampleAssetId = exampleAssetId,
            PreferredProvider = sourceRecipe.PreferredProvider,
            PreferredModel = sourceRecipe.PreferredModel,
            PreferredSize = sourceRecipe.PreferredSize,
            ExportDefaultsJson = sourceRecipe.ExportDefaultsJson,
            Notes = sourceRecipe.Notes,
        };
        await db.PromptRecipes.AddAsync(duplicate, cancellationToken);
        var version = await AppendPromptRecipeVersionAsync(
            duplicate,
            source,
            string.IsNullOrWhiteSpace(changeSummary) ? $"Duplicated from recipe '{sourceRecipe.Name}'." : changeSummary,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(duplicate, version);
    }

    public async Task<IReadOnlyList<PromptRecipeVersionView>> ListPromptRecipeVersionsAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.PromptRecipes.AnyAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken))
            throw new InvalidOperationException("Prompt recipe was not found.");

        var versions = await db.PromptRecipeVersions
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.RecipeId == recipeId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);
        return versions.Select(PromptRecipeVersionView).ToList();
    }

    public async Task<string> ListPromptRecipeVersionsJsonAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken = default)
    {
        var versions = await ListPromptRecipeVersionsAsync(projectId, recipeId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            recipeId,
            versions,
            returned = versions.Count,
        }, JsonOptions);
    }

    public async Task<PromptRecipeView> RevertPromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        int version,
        string source,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");
        var snapshot = await db.PromptRecipeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ProjectId == projectId && v.RecipeId == recipeId && v.Version == version, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe version was not found.");

        ApplyRecipeSnapshot(recipe, snapshot);
        if (recipe.ExampleAssetId is Guid exampleAssetId
            && !await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == exampleAssetId, cancellationToken))
        {
            recipe.ExampleAssetId = null;
        }
        recipe.UpdatedAt = DateTime.UtcNow;
        var newVersion = await AppendPromptRecipeVersionAsync(recipe, source, $"Reverted to version {version}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe, newVersion);
    }

    public async Task DeletePromptRecipeAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken);
        if (recipe is null)
            return;

        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.PromptRecipe && a.RefId == recipeId)
            .ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);

        var linkedAssets = await db.ArtAssets
            .Where(a => a.ProjectId == projectId && a.SourcePromptRecipeId == recipeId)
            .ToListAsync(cancellationToken);
        foreach (var asset in linkedAssets)
        {
            asset.SourcePromptRecipeId = null;
            asset.SourcePromptRecipeVersion = null;
        }

        var linkedBatches = await db.GenerationBatches
            .Where(b => b.ProjectId == projectId && b.PromptRecipeId == recipeId)
            .ToListAsync(cancellationToken);
        foreach (var batch in linkedBatches)
        {
            batch.PromptRecipeId = null;
            batch.PromptRecipeVersion = null;
        }

        db.PromptRecipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnimationRecipeView> SaveAnimationRecipeAsync(
        Guid projectId,
        SaveAnimationRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        await ValidateAnimationRecipeReferencesAsync(projectId, request.GuideAssetId, request.PrimaryExampleSpriteSheetId, cancellationToken);

        var recipe = new AnimationRecipe
        {
            ProjectId = projectId,
            Name = CleanRequired(request.Name, "Animation recipe name is required."),
        };
        ApplyAnimationRecipeRequest(recipe, request);
        await db.AnimationRecipes.AddAsync(recipe, cancellationToken);
        await AppendAnimationRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAnimationRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task<AnimationRecipeView> UpdateAnimationRecipeAsync(
        Guid projectId,
        Guid recipeId,
        UpdateAnimationRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");
        await ValidateAnimationRecipeReferencesAsync(projectId, request.GuideAssetId, request.PrimaryExampleSpriteSheetId, cancellationToken);

        ApplyAnimationRecipeRequest(recipe, request);
        recipe.UpdatedAt = DateTime.UtcNow;
        await AppendAnimationRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAnimationRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<AnimationRecipeVersionView>> ListAnimationRecipeVersionsAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.AnimationRecipes.AnyAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken))
            throw new InvalidOperationException("Animation recipe was not found.");

        var versions = await db.AnimationRecipeVersions
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.AnimationRecipeId == recipeId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);
        return versions.Select(AnimationRecipeVersionView).ToList();
    }

    private async Task<AnimationRecipeView> LoadAnimationRecipeViewAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken)
    {
        var recipe = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.GuideAsset)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");
        return AnimationRecipeView(recipe);
    }

    public async Task<string> ListAnimationRecipeVersionsJsonAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken = default)
    {
        var versions = await ListAnimationRecipeVersionsAsync(projectId, recipeId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            animationRecipeId = recipeId,
            versions,
            returned = versions.Count,
        }, JsonOptions);
    }

    public async Task DeleteAnimationRecipeAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken);
        if (recipe is null)
            return;

        db.AnimationRecipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnimationGuideRenderView> GenerateAnimationGuideAsync(
        Guid projectId,
        GenerateAnimationGuideRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        ArtAsset? reference = null;
        if (request.ReferenceAssetId is Guid referenceAssetId)
        {
            reference = await db.ArtAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(asset => asset.ProjectId == projectId && asset.Id == referenceAssetId, cancellationToken)
                ?? throw new InvalidOperationException("Reference asset was not found in this project.");
        }

        var animationKind = CleanRequired(
            string.IsNullOrWhiteSpace(request.AnimationKind) ? "walk" : request.AnimationKind,
            "Animation kind is required.");
        var assetType = NormalizeGuideToken(request.AssetType, InferGuideAssetType(reference));
        var structureType = NormalizeGuideToken(request.StructureType, DefaultGuideStructure(assetType));
        var facing = SpriteFacing.Normalize(request.Facing, assetType == "vfx" ? SpriteFacing.Center : SpriteFacing.SideRight);
        var rootMotion = NormalizeGuideToken(request.RootMotion, "in_place");
        var fps = Math.Clamp(request.Fps ?? animationOptions.Value.DefaultFps, 1, 60);
        var (targetCellWidth, targetCellHeight) = ParseCellSize(
            request.TargetCellSize,
            animationOptions.Value.DefaultFrameCellSize,
            fallbackWidth: 192,
            fallbackHeight: 192);

        var spec = SpriteMotionArchetypes.Build(
            assetType,
            structureType,
            animationKind,
            facing,
            rootMotion,
            request.FrameCount,
            fps,
            targetCellWidth,
            targetCellHeight);
        spec = ResolveMotionClipSpec(spec, request.MotionClipId);

        var layoutProfile = AnalyzeGuideLayoutProfile(reference);
        var layout = BuildGuideLayout(spec, "#ff00ff", layoutProfile);
        MotionGuideRenderResult? motionRender = null;
        if (MotionClipCatalog.IsExternalMotionSpec(spec))
        {
            try
            {
                motionRender = RenderMotionGuideIfAvailable(spec, layout);
                if (motionRender is not null)
                    spec = ApplyMotionSampleMetadata(spec, motionRender.Samples);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falling back to procedural sprite guide for animation kind {AnimationKind}.", spec.AnimationKind);
                spec = spec with
                {
                    GuideRenderer = null,
                    GuideRenderStyle = null,
                    MotionClipId = null,
                    GuideCameraYawDegrees = null,
                    MotionValidationProfile = null,
                    GuideSourcePackage = null,
                    GuideSourceLicense = null,
                };
            }
        }

        var guidePng = motionRender?.GuidePng ?? SpriteGuideRenderer.Render(layout, spec, diagnostic: false);
        var diagnosticPng = motionRender?.DiagnosticPng ?? SpriteGuideRenderer.Render(layout, spec, diagnostic: true);
        var renderer = motionRender?.Metadata.Renderer ?? "procedural_sprite_guide";
        var renderStyle = motionRender?.Metadata.RenderStyle ?? "procedural_shape_guide";
        var label = string.IsNullOrWhiteSpace(request.Label)
            ? $"{TitleCase(animationKind)} {spec.FrameCount}-frame guide"
            : request.Label.Trim();
        var fileStem = CleanFileName(label.ToLowerInvariant().Replace(' ', '-'), "animation-guide");
        var frameOrder = Enumerable.Range(1, spec.FrameCount).ToList();
        var expectedBoxes = layout.Slots
            .OrderBy(slot => slot.FrameIndex)
            .Select(slot => slot.Rect)
            .ToList();
        var promptScaffold = BuildAnimationGuidePromptScaffold(reference, spec, layout);
        var exportDefaultsJson = JsonSerializer.Serialize(new
        {
            rows = layout.Rows,
            columns = layout.Columns,
            cellWidth = spec.TargetCellWidth,
            cellHeight = spec.TargetCellHeight,
            fps = spec.Fps,
            loop = spec.Loop,
            frameOrder,
            anchorStrategy = "root-baseline",
            background = layout.BackgroundColor,
        }, JsonOptions);

        var metadata = new
        {
            source = "generate_animation_guide",
            renderer,
            renderStyle,
            spec.AnimationKind,
            spec.AssetType,
            spec.StructureType,
            spec.Facing,
            spec.RootMotion,
            spec.FrameCount,
            spec.Fps,
            spec.Loop,
            spec.MotionClipId,
            spec.GuideCameraYawDegrees,
            spec.GuideSourcePackage,
            spec.GuideSourceLicense,
            layout.Rows,
            layout.Columns,
            layout.CanvasWidth,
            layout.CanvasHeight,
            layout.GuideCellWidth,
            layout.GuideCellHeight,
            layout.TargetCellWidth,
            layout.TargetCellHeight,
            expectedFrameBoxes = expectedBoxes,
            referenceAssetId = reference?.Id,
        };
        var guide = CreateAsset(
            projectId,
            label,
            $"{fileStem}.png",
            ArtAssetKind.SpriteGuide,
            "image/png",
            guidePng,
            reference?.Id,
            sourceBatchId: null,
            promptRecipeId: null,
            promptRecipeVersion: null,
            promptScaffold,
            metadata);
        var diagnostic = CreateAsset(
            projectId,
            $"{label} diagnostic",
            $"{fileStem}-diagnostic.png",
            ArtAssetKind.SpriteGuide,
            "image/png",
            diagnosticPng,
            guide.Id,
            sourceBatchId: null,
            promptRecipeId: null,
            promptRecipeVersion: null,
            promptScaffold,
            metadata);
        await db.ArtAssets.AddRangeAsync([guide, diagnostic], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new AnimationGuideRenderView(
            guide.Id,
            diagnostic.Id,
            guide.Label,
            spec.AnimationKind,
            spec.AssetType,
            spec.StructureType,
            spec.Facing,
            spec.RootMotion,
            spec.FrameCount,
            frameOrder,
            spec.Fps,
            spec.Loop,
            layout.Rows,
            layout.Columns,
            layout.CanvasWidth,
            layout.CanvasHeight,
            layout.GuideCellWidth,
            layout.GuideCellHeight,
            layout.TargetCellWidth,
            layout.TargetCellHeight,
            expectedBoxes,
            "root-baseline",
            promptScaffold,
            exportDefaultsJson,
            renderer,
            renderStyle,
            spec.MotionClipId,
            motionRender?.Metadata.SourcePackage ?? spec.GuideSourcePackage,
            motionRender?.Metadata.SourceLicense ?? spec.GuideSourceLicense,
            motionRender?.Metadata.SourceUrl,
            "Animation guide assets saved as SpriteGuide. Use guideAssetId first in generate_sprite_sheet_candidates references.");
    }

    public async Task MarkAssetAsync(Guid projectId, Guid assetId, bool? favorite, string? notes, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        if (favorite is not null)
            asset.IsFavorite = favorite.Value;
        if (notes is not null)
            asset.Notes = notes.Trim();
        asset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken);
        if (asset is null)
            return;

        var maskIds = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.AssetId == assetId)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId
                && (a.RefId == assetId || (a.Type == ChatContextAttachmentType.Mask && maskIds.Contains(a.RefId))))
            .ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);

        var linkedSpriteSheetIds = await db.SpriteSheetDefinitions
            .Where(sheet => sheet.ProjectId == projectId && (sheet.SourceAssetId == assetId || sheet.OutputAssetId == assetId))
            .Select(sheet => sheet.Id)
            .ToListAsync(cancellationToken);
        var linkedSpriteFrameIds = linkedSpriteSheetIds.Count == 0
            ? []
            : await db.SpriteSheetFrameRecords
                .Where(frame => frame.ProjectId == projectId && linkedSpriteSheetIds.Contains(frame.SpriteSheetDefinitionId))
                .Select(frame => frame.Id)
                .ToListAsync(cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.Asset, [assetId], cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteSheet, linkedSpriteSheetIds, cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteAnimation, linkedSpriteSheetIds, cancellationToken);
        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteFrame, linkedSpriteFrameIds, cancellationToken);

        var recipes = await db.PromptRecipes
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        foreach (var recipe in recipes)
        {
            if (recipe.ExampleAssetId != assetId)
                continue;
            recipe.ExampleAssetId = null;
            recipe.UpdatedAt = DateTime.UtcNow;
        }

        var now = DateTime.UtcNow;
        await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SourceImageAssetId == assetId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(frame => frame.SourceImageAssetId, (Guid?)null)
                .SetProperty(frame => frame.SourceImageX, 0)
                .SetProperty(frame => frame.SourceImageY, 0)
                .SetProperty(frame => frame.SourceImageWidth, 0)
                .SetProperty(frame => frame.SourceImageHeight, 0)
                .SetProperty(frame => frame.UpdatedAt, now),
                cancellationToken);

        db.ArtAssets.Remove(asset);
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatContextAttachmentView> AttachContextAsync(
        Guid projectId,
        ChatContextAttachmentType type,
        Guid refId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var existing = await db.ChatContextAttachments.FirstOrDefaultAsync(
            a => a.ProjectId == projectId && a.Type == type && a.RefId == refId,
            cancellationToken);
        if (existing is not null)
            return AttachmentView(existing);

        var nextOrder = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId)
            .Select(a => (int?)a.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;
        var attachment = new ChatContextAttachment
        {
            ProjectId = projectId,
            Type = type,
            RefId = refId,
            Label = string.IsNullOrWhiteSpace(label)
                ? await ResolveAttachmentLabelAsync(projectId, type, refId, cancellationToken)
                : label.Trim(),
            SortOrder = nextOrder + 1,
        };
        await db.ChatContextAttachments.AddAsync(attachment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AttachmentView(attachment);
    }

    public async Task RemoveContextAsync(Guid projectId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.ChatContextAttachments.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == attachmentId, cancellationToken);
        if (attachment is null)
            return;
        db.ChatContextAttachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var attachments = await db.ChatContextAttachments.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CompareReviewSetView> SetCompareReviewSetAsync(
        Guid projectId,
        SetCompareReviewSetRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var reviewSet = await GetOrCreateCompareReviewSetAsync(projectId, request.Title, request.Summary, cancellationToken);
        var existingItems = await db.CompareReviewSetItems
            .Where(item => item.CompareReviewSetId == reviewSet.Id)
            .ToListAsync(cancellationToken);
        db.CompareReviewSetItems.RemoveRange(existingItems);
        await db.SaveChangesAsync(cancellationToken);

        reviewSet.Title = NormalizeReviewSetTitle(request.Title, reviewSet.Title);
        reviewSet.Summary = NormalizeReviewSetSummary(request.Summary);
        reviewSet.UpdatedAt = DateTime.UtcNow;
        await AddCompareReviewItemsCoreAsync(projectId, reviewSet, request.Items, cancellationToken);

        if (request.SwitchToCompare)
            project.ActiveWorkspaceMode = WorkspaceMode.Compare;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadCompareReviewSetViewAsync(projectId, reviewSet.Id, cancellationToken);
    }

    public async Task<CompareReviewSetView> AddCompareReviewItemsAsync(
        Guid projectId,
        AddCompareReviewItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var reviewSet = await GetOrCreateCompareReviewSetAsync(projectId, request.Title, request.Summary, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Title))
            reviewSet.Title = request.Title.Trim();
        if (request.Summary is not null)
            reviewSet.Summary = NormalizeReviewSetSummary(request.Summary);
        reviewSet.UpdatedAt = DateTime.UtcNow;

        await AddCompareReviewItemsCoreAsync(projectId, reviewSet, request.Items, cancellationToken);

        if (request.SwitchToCompare)
            project.ActiveWorkspaceMode = WorkspaceMode.Compare;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadCompareReviewSetViewAsync(projectId, reviewSet.Id, cancellationToken);
    }

    public async Task RemoveCompareReviewItemAsync(Guid projectId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await db.CompareReviewSetItems
            .Include(i => i.CompareReviewSet)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.CompareReviewSet.ProjectId == projectId, cancellationToken);
        if (item is null)
            return;

        item.CompareReviewSet.UpdatedAt = DateTime.UtcNow;
        db.CompareReviewSetItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearCompareReviewSetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var reviewSet = await db.CompareReviewSets.FirstOrDefaultAsync(set => set.ProjectId == projectId, cancellationToken);
        if (reviewSet is null)
            return;

        db.CompareReviewSets.Remove(reviewSet);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetWorkspaceStateJsonAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project was not found.");
        var attachments = await db.ChatContextAttachments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        var compareReviewSet = await db.CompareReviewSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.ProjectId == projectId, cancellationToken);
        var compareReviewItems = compareReviewSet is null
            ? []
            : await db.CompareReviewSetItems
                .AsNoTracking()
                .Where(item => item.CompareReviewSetId == compareReviewSet.Id)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);
        var providerStatus = await BuildProviderStatusAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            snapshotMissing = true,
            note = "No live UI snapshot has been published. This fallback only includes persisted project, active ids, visible chat attachments, current review set, and provider status.",
            project = ProjectView(project),
            chatAttachments = attachments.Select(AttachmentView),
            reviewSet = compareReviewSet is null ? null : CompareReviewSetView(compareReviewSet, compareReviewItems),
            provider = providerStatus,
        }, JsonOptions);
    }

    private async Task<ImageMask> CreateEditSourceMaskEntityAsync(
        Guid projectId,
        ArtAsset asset,
        Guid batchId,
        string maskDataUrl,
        string label,
        ImagePayload sourceImage,
        CancellationToken cancellationToken)
    {
        var maskImage = ParsePngDataUrl(maskDataUrl, "Mask must be a PNG data URL.");
        EnsurePngMaskHasAlpha(maskImage.Data, requireEditableArea: true);
        var now = DateTime.UtcNow;
        var mask = new ImageMask
        {
            ProjectId = projectId,
            AssetId = asset.Id,
            Label = string.IsNullOrWhiteSpace(label) ? $"{asset.Label} edit mask" : label.Trim(),
            ContentType = maskImage.ContentType,
            Data = maskImage.Data,
            Width = maskImage.Width,
            Height = maskImage.Height,
            OwnerKind = "editBatch",
            OwnerId = batchId,
            CoordinateSpace = "editSource",
            CreatedAt = now,
            UpdatedAt = now,
        };
        ValidateEditImageAndMask(sourceImage, mask);
        await db.ImageMasks.AddAsync(mask, cancellationToken);
        return mask;
    }

    private async Task<ImageMask> UpsertAssetMaskEntityAsync(
        Guid projectId,
        ArtAsset asset,
        string maskDataUrl,
        string label,
        bool requireEditableArea,
        CancellationToken cancellationToken)
    {
        var maskImage = ParsePngDataUrl(maskDataUrl, "Mask must be a PNG data URL.");
        ValidateMaskFitsAsset(asset, maskImage);
        EnsurePngMaskHasAlpha(maskImage.Data, requireEditableArea);

        var masks = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.AssetId == asset.Id && m.OwnerKind == "asset")
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        ImageMask mask;
        if (masks.Count == 0)
        {
            mask = new ImageMask
            {
                ProjectId = projectId,
                AssetId = asset.Id,
                CreatedAt = now,
            };
            await db.ImageMasks.AddAsync(mask, cancellationToken);
        }
        else
        {
            mask = masks[0];
        }

        mask.Label = string.IsNullOrWhiteSpace(label) ? $"{asset.Label} mask" : label.Trim();
        mask.ContentType = maskImage.ContentType;
        mask.Data = maskImage.Data;
        mask.Width = maskImage.Width;
        mask.Height = maskImage.Height;
        mask.OwnerKind = "asset";
        mask.OwnerId = asset.Id;
        mask.CoordinateSpace = "source";
        mask.UpdatedAt = now;

        var duplicateMasks = masks.Skip(1).ToList();
        if (duplicateMasks.Count > 0)
        {
            var duplicateMaskIds = duplicateMasks.Select(m => m.Id).ToList();
            var duplicateAttachments = await db.ChatContextAttachments
                .Where(a => a.ProjectId == projectId
                    && a.Type == ChatContextAttachmentType.Mask
                    && duplicateMaskIds.Contains(a.RefId))
                .ToListAsync(cancellationToken);
            db.ChatContextAttachments.RemoveRange(duplicateAttachments);
            db.ImageMasks.RemoveRange(duplicateMasks);
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        return mask;
    }

    private static ImagePayload ResolveEditSourceImage(ArtAsset sourceAsset, string? sourcePngDataUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourcePngDataUrl))
            return ParsePngDataUrl(sourcePngDataUrl, "Edit source image must be a PNG data URL.");

        var contentType = NormalizeImageContentType(sourceAsset.ContentType)
            ?? throw new InvalidOperationException("Source asset must be a PNG or JPEG image.");
        var width = sourceAsset.Width;
        var height = sourceAsset.Height;
        if (width is null || height is null || width <= 0 || height <= 0)
        {
            var size = ImageMetadataReader.TryReadSize(sourceAsset.Data, contentType);
            width = size.Width;
            height = size.Height;
        }

        if (width is null || height is null || width <= 0 || height <= 0)
            throw new InvalidOperationException("Source image dimensions could not be read.");

        return new ImagePayload(contentType, sourceAsset.Data, width.Value, height.Value);
    }

    private static ImagePayload ResolveStoredEditSourceImage(GenerationBatch batch, ArtAsset sourceAsset)
    {
        if (batch.EditSourceData is not { Length: > 0 } data)
            return ResolveEditSourceImage(sourceAsset, null);

        var contentType = NormalizeImageContentType(batch.EditSourceContentType ?? string.Empty)
            ?? throw new InvalidOperationException("Edit source snapshot is invalid.");
        var width = batch.EditSourceWidth;
        var height = batch.EditSourceHeight;
        if (width is null || height is null || width <= 0 || height <= 0)
        {
            var size = ImageMetadataReader.TryReadSize(data, contentType);
            width = size.Width;
            height = size.Height;
        }

        if (width is null || height is null || width <= 0 || height <= 0)
            throw new InvalidOperationException("Edit source snapshot dimensions could not be read.");

        return new ImagePayload(contentType, data, width.Value, height.Value);
    }

    private static ImagePayload ParsePngDataUrl(string dataUrl, string error)
    {
        string contentType;
        byte[] data;
        try
        {
            (contentType, data) = DataUrl.Parse(dataUrl);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(error, ex);
        }

        contentType = NormalizeImageContentType(contentType)
            ?? throw new InvalidOperationException(error);
        if (!contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(error);

        var (width, height) = ImageMetadataReader.TryReadSize(data, contentType);
        if (width is null || height is null || width <= 0 || height <= 0)
            throw new InvalidOperationException("PNG dimensions could not be read.");

        return new ImagePayload(contentType, data, width.Value, height.Value);
    }

    private static void ValidateMaskFitsAsset(ArtAsset asset, ImagePayload maskImage)
    {
        if (asset.Width is not > 0 || asset.Height is not > 0)
            return;
        if (asset.Width != maskImage.Width || asset.Height != maskImage.Height)
            throw new InvalidOperationException("Mask dimensions must match the source asset dimensions.");
    }

    private static void ValidateEditImageAndMask(ImagePayload sourceImage, ImageMask mask)
    {
        if (!sourceImage.ContentType.Equals(mask.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source image and mask must use the same image format.");
        if (sourceImage.Width != mask.Width || sourceImage.Height != mask.Height)
            throw new InvalidOperationException("Source image and mask dimensions must match.");
    }

    private static void EnsurePngMaskHasAlpha(byte[] data, bool requireEditableArea)
    {
        var state = ReadPngAlphaState(data);
        if (!state.HasAlphaChannel)
            throw new InvalidOperationException("Mask PNG must contain an alpha channel.");
        if (requireEditableArea && !state.HasEditableArea)
            throw new InvalidOperationException("Paint the editable mask area first.");
    }

    private static (bool HasAlphaChannel, bool HasEditableArea) ReadPngAlphaState(byte[] data)
    {
        if (data.Length < 33
            || data[0] != 0x89
            || data[1] != 0x50
            || data[2] != 0x4E
            || data[3] != 0x47)
        {
            return (false, false);
        }

        var offset = 8;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        using var idat = new MemoryStream();

        while (offset + 8 <= data.Length)
        {
            var length = ReadBigEndianInt32(data.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > data.Length)
                throw new InvalidOperationException("Mask PNG is malformed.");

            var typeOffset = offset + 4;
            var chunkOffset = offset + 8;
            if (ChunkTypeEquals(data, typeOffset, (byte)'I', (byte)'H', (byte)'D', (byte)'R'))
            {
                if (length < 13)
                    throw new InvalidOperationException("Mask PNG is malformed.");
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

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Mask PNG dimensions could not be read.");
        if (colorType is not (4 or 6))
            return (false, false);
        if (bitDepth != 8 || interlace != 0)
            throw new InvalidOperationException("Mask PNG must be an 8-bit non-interlaced PNG with alpha.");
        if (idat.Length == 0)
            throw new InvalidOperationException("Mask PNG is missing image data.");

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var inflated = raw.ToArray();
        var bytesPerPixel = colorType == 6 ? 4 : 2;
        var rowBytes = checked(width * bytesPerPixel);
        var requiredBytes = checked((rowBytes + 1) * height);
        if (inflated.Length < requiredBytes)
            throw new InvalidOperationException("Mask PNG image data is incomplete.");

        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];
        var index = 0;
        var hasEditableArea = false;

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
                    _ => throw new InvalidOperationException("Mask PNG uses an unsupported row filter."),
                };
            }

            var alphaOffset = colorType == 6 ? 3 : 1;
            for (var x = alphaOffset; x < rowBytes; x += bytesPerPixel)
            {
                if (current[x] < byte.MaxValue)
                {
                    hasEditableArea = true;
                    break;
                }
            }

            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return (true, hasEditableArea);
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

    private async Task SetWorkspaceModeAfterAssetMutationAsync(Guid projectId, WorkspaceMode mode, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = mode;
        project.UpdatedAt = DateTime.UtcNow;
    }

    private static void UpdateSpriteSheetDefinition(
        SpriteSheetDefinition definition,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int padding,
        int gutter,
        int fps,
        bool loop,
        string? horizontalAnchor,
        string? verticalAnchor)
    {
        definition.Rows = Math.Clamp(rows, 1, 32);
        definition.Columns = Math.Clamp(columns, 1, 64);
        definition.CellWidth = Math.Clamp(cellWidth, 1, 8192);
        definition.CellHeight = Math.Clamp(cellHeight, 1, 8192);
        definition.Padding = Math.Clamp(padding, 0, 4096);
        definition.Gutter = Math.Clamp(gutter, 0, 4096);
        definition.Fps = Math.Clamp(fps, 1, 60);
        definition.Loop = loop;
        definition.HorizontalAnchor = NormalizeHorizontalAnchor(horizontalAnchor);
        definition.VerticalAnchor = NormalizeVerticalAnchor(verticalAnchor);
        definition.UpdatedAt = DateTime.UtcNow;
    }

    private static (int Rows, int Columns) GridCapacity(int rows, int columns, int frameCount)
    {
        rows = Math.Clamp(rows, 1, 32);
        columns = Math.Clamp(columns, 1, 64);
        if (frameCount <= rows * columns)
            return (rows, columns);
        if (frameCount > 32 * 64)
            throw new InvalidOperationException("Sprite sheets support at most 2048 frames.");

        while (frameCount > rows * columns && columns < 64)
            columns++;
        while (frameCount > rows * columns && rows < 32)
            rows++;
        if (frameCount > rows * columns)
            throw new InvalidOperationException("Sprite frame count exceeds the supported sheet grid.");
        return (rows, columns);
    }

    private static IReadOnlyList<SpriteSheetFrameView> BuildPreviewlessFrameViews(
        SpriteSheetDefinition definition,
        IReadOnlyList<SpriteSheetFrameUpdateView> frames)
    {
        return frames
            .Select((frame, index) =>
            {
                var row = index / definition.Columns;
                var column = index % definition.Columns;
                var sourceRect = NormalizeSourceSpriteRect(frame.SourceRect);
                var cellRect = new SpriteSheetRect(
                    column * (definition.CellWidth + definition.Gutter),
                    row * (definition.CellHeight + definition.Gutter),
                    definition.CellWidth,
                    definition.CellHeight);
                var spriteRect = new SpriteSheetRect(cellRect.X, cellRect.Y, sourceRect.Width, sourceRect.Height);
                return new SpriteSheetFrameView(
                    index,
                    string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label.Trim(),
                    sourceRect,
                    NormalizeShapePaths(frame.ShapePaths),
                    cellRect,
                    spriteRect,
                    string.Empty,
                    frame.SourceImageAssetId,
                    frame.SourceImageRect);
            })
            .ToList();
    }

    private static SpriteSheetFrameUpdateView FrameUpdateFromRecord(SpriteSheetFrameRecord frame) =>
        new(
            frame.Index,
            frame.Label,
            RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
            DeserializeShapePaths(frame.ShapeJson),
            frame.SourceImageAssetId,
            SourceImageRectView(frame));

    private static int FrameIndexFromNumber(int frameNumber)
    {
        if (frameNumber <= 0)
            throw new InvalidOperationException("Frame number must be 1 or greater.");
        return frameNumber - 1;
    }

    private static IReadOnlyList<int> TargetFrameIndexes(IReadOnlyList<int>? targetFrameNumbers, int frameCount)
    {
        if (targetFrameNumbers is not { Count: > 0 })
            return Enumerable.Range(0, frameCount).ToList();

        return targetFrameNumbers
            .Select(FrameIndexFromNumber)
            .Where(index => index >= 0 && index < frameCount)
            .Distinct()
            .Order()
            .ToList();
    }

    private static SpriteSheetRect FrameSourceRect(SpriteSheetFrameRecord frame) =>
        RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight);

    private static List<StabilizationWorkingFrameInput> BuildStabilizationWorkingFrameInputs(
        SpriteSheetDefinition definition,
        byte[] sheetRgba,
        int sheetWidth,
        int sheetHeight,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameRecord> records)
    {
        var marginValue = NormalizeFrameWorkingMargin(null, definition.Padding);
        var updates = records.Select(FrameUpdateFromRecord).ToList();
        var inputs = new List<StabilizationWorkingFrameInput>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var state = NormalizeFrameWorkingState(record.WorkingState);
            if (record.WorkingData.Length > 0)
            {
                if (!SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var width, out var height, out var rgba))
                    throw new InvalidOperationException($"Working image for frame {index + 1} must be a PNG.");
                var margin = Math.Max(0, record.WorkingMargin);
                inputs.Add(new StabilizationWorkingFrameInput(
                    record,
                    new SpriteSheetFrameImageInput(
                        record.Index,
                        record.Label,
                        rgba,
                        width,
                        height,
                        UsedWorkingImage: true,
                        record.SourceImageAssetId,
                        SourceImageRectView(record),
                        state),
                    record.SourceX - margin,
                    record.SourceY - margin));
                continue;
            }

            var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
                sheetRgba,
                sheetWidth,
                sheetHeight,
                definition.Rows,
                definition.Columns,
                background,
                updates,
                record.Index,
                marginValue);
            if (!SpriteSheetPngCodec.TryReadRgba(extracted.PngData, out var extractedWidth, out var extractedHeight, out var extractedRgba))
                throw new InvalidOperationException($"Extracted image for frame {index + 1} must be a PNG.");
            inputs.Add(new StabilizationWorkingFrameInput(
                record,
                new SpriteSheetFrameImageInput(
                    record.Index,
                    record.Label,
                    extractedRgba,
                    extractedWidth,
                    extractedHeight,
                    UsedWorkingImage: false,
                    record.SourceImageAssetId,
                    SourceImageRectView(record),
                    "isolated"),
                record.SourceX - marginValue,
                record.SourceY - marginValue));
        }

        return inputs;
    }

    private static StabilizationPlacementSet BuildStabilizationPlacements(
        IReadOnlyList<StabilizationMatchDraft> drafts,
        int targetCenterX,
        int targetCenterY)
    {
        if (drafts.Count == 0)
            throw new InvalidOperationException("At least one stabilization match is required.");

        var raw = drafts
            .Select(draft =>
            {
                var matchCenterX = draft.MatchedAnchorRect.X + (draft.MatchedAnchorRect.Width / 2);
                var matchCenterY = draft.MatchedAnchorRect.Y + (draft.MatchedAnchorRect.Height / 2);
                var deltaX = targetCenterX - matchCenterX;
                var deltaY = targetCenterY - matchCenterY;
                return new
                {
                    draft.Frame.Record.Index,
                    draft.Frame.Image.Width,
                    draft.Frame.Image.Height,
                    DeltaX = deltaX,
                    DeltaY = deltaY,
                };
            })
            .ToList();
        var minX = raw.Min(item => item.DeltaX);
        var minY = raw.Min(item => item.DeltaY);
        var maxX = raw.Max(item => item.DeltaX + item.Width);
        var maxY = raw.Max(item => item.DeltaY + item.Height);
        var normalizedWidth = Math.Max(1, maxX - minX);
        var normalizedHeight = Math.Max(1, maxY - minY);
        var placements = raw.ToDictionary(
            item => item.Index,
            item => new StabilizationPlacement(
                item.DeltaX,
                item.DeltaY,
                new SpriteSheetRect(item.DeltaX - minX, item.DeltaY - minY, item.Width, item.Height)));
        return new StabilizationPlacementSet(normalizedWidth, normalizedHeight, minX, minY, placements);
    }

    private static SpriteSheetRect NormalizeStabilizationAnchor(SpriteSheetRect anchorRect, SpriteSheetRect referenceFrame)
    {
        var anchor = NormalizeSourceSpriteRect(anchorRect);
        if (anchor.Width < 4 || anchor.Height < 4)
            throw new InvalidOperationException("Anchor box must be at least 4 x 4 pixels.");
        if (!RectContains(referenceFrame, anchor))
            throw new InvalidOperationException("Anchor box must be fully inside the selected reference frame.");
        return anchor;
    }

    private static StabilizationTemplate BuildStabilizationTemplate(
        byte[] rgba,
        int imageWidth,
        int imageHeight,
        SpriteSheetRect anchorRect,
        SpriteSheetBackground background)
    {
        var values = new double[anchorRect.Width * anchorRect.Height];
        var weights = new double[values.Length];
        var weightSum = 0d;
        var warnings = new List<string>();
        for (var y = 0; y < anchorRect.Height; y++)
        {
            for (var x = 0; x < anchorRect.Width; x++)
            {
                var sourceX = anchorRect.X + x;
                var sourceY = anchorRect.Y + y;
                var pixel = (y * anchorRect.Width) + x;
                if (sourceX < 0 || sourceY < 0 || sourceX >= imageWidth || sourceY >= imageHeight)
                    continue;

                var offset = ((sourceY * imageWidth) + sourceX) * 4;
                var r = rgba[offset];
                var g = rgba[offset + 1];
                var b = rgba[offset + 2];
                var a = rgba[offset + 3];
                values[pixel] = Luminance(r, g, b);
                if (!SpriteSheetImageAnalyzer.IsForeground(r, g, b, a, background))
                    continue;

                var alphaWeight = Math.Clamp(a / 255d, 0.05d, 1d);
                weights[pixel] = alphaWeight;
                weightSum += alphaWeight;
            }
        }

        var minimumUsefulPixels = Math.Max(8, values.Length / 10);
        var usefulPixels = weights.Count(weight => weight > 0);
        if (usefulPixels < minimumUsefulPixels)
            warnings.Add("Anchor has few foreground-like pixels; choose a more detailed rigid feature if matches look weak.");
        if (weightSum <= 0)
        {
            Array.Fill(weights, 1d);
            weightSum = weights.Length;
            warnings.Add("Anchor had no foreground-like pixels; matched all pixels in the box.");
        }

        var mean = WeightedMean(values, weights, weightSum);
        var denominator = WeightedVariance(values, weights, mean);
        if (denominator <= 0.000001d)
            warnings.Add("Anchor has very low contrast; template matching may be ambiguous.");

        return new StabilizationTemplate(anchorRect.Width, anchorRect.Height, values, weights, weightSum, mean, denominator, warnings);
    }

    private static SpriteSheetRect NormalizeWorkingStabilizationAnchor(
        SpriteSheetRect sourceAnchorRect,
        StabilizationWorkingFrameInput referenceInput)
    {
        var anchor = new SpriteSheetRect(
            sourceAnchorRect.X - referenceInput.SourceOriginX,
            sourceAnchorRect.Y - referenceInput.SourceOriginY,
            sourceAnchorRect.Width,
            sourceAnchorRect.Height);
        var workingRect = new SpriteSheetRect(0, 0, referenceInput.Image.Width, referenceInput.Image.Height);
        if (!RectContains(workingRect, anchor))
            throw new InvalidOperationException("Anchor box does not map inside the selected reference frame image. Re-isolate the frame or redraw the anchor.");
        return anchor;
    }

    private static StabilizationMatchDraft BuildReferenceStabilizationMatch(
        StabilizationWorkingFrameInput frame,
        SpriteSheetRect anchorRect) =>
        new(frame, anchorRect, 1d, LowConfidence: false, []);

    private static StabilizationMatchDraft MatchStabilizationFrame(
        StabilizationWorkingFrameInput frame,
        StabilizationTemplate template,
        int referenceRelativeX,
        int referenceRelativeY,
        int searchPadding,
        double minScore)
    {
        var imageWidth = frame.Image.Width;
        var imageHeight = frame.Image.Height;
        var expectedX = frame.Record.SourceX + referenceRelativeX - frame.SourceOriginX;
        var expectedY = frame.Record.SourceY + referenceRelativeY - frame.SourceOriginY;
        var minX = Math.Max(0, expectedX - searchPadding);
        var minY = Math.Max(0, expectedY - searchPadding);
        var maxX = Math.Min(imageWidth - template.Width, expectedX + searchPadding);
        var maxY = Math.Min(imageHeight - template.Height, expectedY + searchPadding);
        var warnings = new List<string>();
        if (maxX < minX || maxY < minY)
        {
            minX = 0;
            minY = 0;
            maxX = Math.Max(0, imageWidth - template.Width);
            maxY = Math.Max(0, imageHeight - template.Height);
            warnings.Add("Search area was too small for the anchor and was expanded.");
        }

        var bestX = expectedX;
        var bestY = expectedY;
        var bestScore = double.NegativeInfinity;
        for (var candidateY = minY; candidateY <= maxY; candidateY++)
        {
            for (var candidateX = minX; candidateX <= maxX; candidateX++)
            {
                var score = MatchScore(frame.Image.Rgba, imageWidth, imageHeight, template, candidateX, candidateY);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = candidateX;
                    bestY = candidateY;
                }
            }
        }

        if (double.IsNegativeInfinity(bestScore))
        {
            bestScore = -1d;
            warnings.Add("No valid anchor candidate was found.");
        }

        return new StabilizationMatchDraft(
            frame,
            new SpriteSheetRect(bestX, bestY, template.Width, template.Height),
            bestScore,
            bestScore < minScore,
            warnings);
    }

    private static double MatchScore(byte[] rgba, int imageWidth, int imageHeight, StabilizationTemplate template, int startX, int startY)
    {
        if (startX < 0 || startY < 0 || startX + template.Width > imageWidth || startY + template.Height > imageHeight)
            return double.NegativeInfinity;

        var targetValues = new double[template.Values.Length];
        for (var y = 0; y < template.Height; y++)
        {
            for (var x = 0; x < template.Width; x++)
            {
                var offset = (((startY + y) * imageWidth) + startX + x) * 4;
                targetValues[(y * template.Width) + x] = Luminance(rgba[offset], rgba[offset + 1], rgba[offset + 2]);
            }
        }

        var targetMean = WeightedMean(targetValues, template.Weights, template.WeightSum);
        var targetDenominator = WeightedVariance(targetValues, template.Weights, targetMean);
        if (template.Denominator <= 0.000001d || targetDenominator <= 0.000001d)
            return double.NegativeInfinity;

        var numerator = 0d;
        for (var index = 0; index < template.Values.Length; index++)
            numerator += template.Weights[index] * (template.Values[index] - template.Mean) * (targetValues[index] - targetMean);
        return numerator / Math.Sqrt(template.Denominator * targetDenominator);
    }

    private static double WeightedMean(double[] values, double[] weights, double weightSum)
    {
        if (weightSum <= 0)
            return 0d;
        var sum = 0d;
        for (var index = 0; index < values.Length; index++)
            sum += values[index] * weights[index];
        return sum / weightSum;
    }

    private static double WeightedVariance(double[] values, double[] weights, double mean)
    {
        var sum = 0d;
        for (var index = 0; index < values.Length; index++)
        {
            var delta = values[index] - mean;
            sum += weights[index] * delta * delta;
        }

        return sum;
    }

    private static bool RectContains(SpriteSheetRect container, SpriteSheetRect rect) =>
        rect.X >= container.X
        && rect.Y >= container.Y
        && rect.X + rect.Width <= container.X + container.Width
        && rect.Y + rect.Height <= container.Y + container.Height;

    private static double Luminance(byte r, byte g, byte b) =>
        (0.2126d * r) + (0.7152d * g) + (0.0722d * b);

    private static double RoundMetric(double value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string SerializeStabilization(SpriteSheetStabilizationView stabilization) =>
        JsonSerializer.Serialize(stabilization, JsonOptions);

    private static SpriteSheetStabilizationView? DeserializeStabilization(string? stabilizationJson)
    {
        if (string.IsNullOrWhiteSpace(stabilizationJson) || stabilizationJson.Trim() == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<SpriteSheetStabilizationView>(stabilizationJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SpriteAnimationReviewImageView BuildStabilizationDiagnosticImage(
        SpriteSheetBackground background,
        SpriteSheetStabilizationView stabilization,
        IReadOnlyList<SpriteSheetFrameImageInput> inputFrames)
    {
        var image = SpriteSheetServerRenderer.BuildStabilizationAnnotatedSheetView(background, stabilization, inputFrames);
        return new SpriteAnimationReviewImageView(
            image.Label,
            image.FileName,
            "image/png",
            DataUrl.ToDataUrl("image/png", image.PngData),
            image.Kind,
            image.FrameIndex,
            image.FromFrame,
            image.ToFrame);
    }

    private async Task<(SpriteSheetDefinition Definition, ArtAsset Working, List<SpriteSheetFrameRecord> Records)> LoadSpriteSheetWorkingEntitiesAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken)
    {
        var definition = await db.SpriteSheetDefinitions
            .Include(s => s.OutputAsset)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var working = definition.OutputAsset ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        var records = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        return (definition, working, records);
    }

    private static List<SpriteSheetFrameImageInput> BuildSpriteFrameImageInputsFromRecords(
        SpriteSheetDefinition definition,
        byte[] sheetRgba,
        int sheetWidth,
        int sheetHeight,
        SpriteSheetBackground background,
        IReadOnlyList<SpriteSheetFrameRecord> records)
    {
        var updates = records.Select(FrameUpdateFromRecord).ToList();
        var inputs = new List<SpriteSheetFrameImageInput>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.WorkingData.Length > 0)
            {
                if (!SpriteSheetPngCodec.TryReadRgba(record.WorkingData, out var width, out var height, out var rgba))
                    throw new InvalidOperationException($"Working image for frame {index + 1} must be a PNG.");
                inputs.Add(new SpriteSheetFrameImageInput(
                    index,
                    record.Label,
                    rgba,
                    width,
                    height,
                    UsedWorkingImage: true,
                    record.SourceImageAssetId,
                    SourceImageRectView(record),
                    NormalizeFrameWorkingState(record.WorkingState)));
                continue;
            }

            var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
                sheetRgba,
                sheetWidth,
                sheetHeight,
                definition.Rows,
                definition.Columns,
                background,
                updates,
                index,
                margin: 0);
            if (!SpriteSheetPngCodec.TryReadRgba(extracted.PngData, out var extractedWidth, out var extractedHeight, out var extractedRgba))
                throw new InvalidOperationException($"Extracted image for frame {index + 1} must be a PNG.");
            inputs.Add(new SpriteSheetFrameImageInput(
                index,
                record.Label,
                extractedRgba,
                extractedWidth,
                extractedHeight,
                UsedWorkingImage: false,
                record.SourceImageAssetId,
                SourceImageRectView(record),
                NormalizeFrameWorkingState(record.WorkingState)));
        }

        return inputs;
    }

    private async Task<(SpriteSheetDefinition Definition, SpriteSheetFrameRecord Record)> EnsureFrameWorkingImageAsync(
        Guid projectId,
        Guid spriteSheetId,
        int frameIndex,
        int? margin,
        CancellationToken cancellationToken)
    {
        var (definition, working, records) = await LoadSpriteSheetWorkingEntitiesAsync(projectId, spriteSheetId, cancellationToken);
        var record = FrameRecordAt(records, frameIndex);
        if (record.WorkingData.Length > 0)
            return (definition, record);

        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite-frame isolation requires a PNG working image.");
        var background = EnsureSheetBackground(definition, rgba, width, height);
        var marginValue = NormalizeFrameWorkingMargin(margin, definition.Padding);
        var extracted = SpriteSheetServerRenderer.ExtractFrameRegion(
            rgba,
            width,
            height,
            definition.Rows,
            definition.Columns,
            background,
            records.Select(FrameUpdateFromRecord).ToList(),
            frameIndex,
            marginValue);
        ApplyFrameWorkingImage(record, "isolated", extracted.PngData, extracted.Width, extracted.Height, marginValue, DateTime.UtcNow);
        return (definition, record);
    }

    private static SpriteSheetFrameRecord FrameRecordAt(IReadOnlyList<SpriteSheetFrameRecord> records, int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= records.Count)
            throw new InvalidOperationException("Sprite frame index is outside the saved frame range.");
        return records[frameIndex];
    }

    private static int NormalizeFrameWorkingMargin(int? margin, int sheetPadding)
    {
        var value = margin ?? Math.Max(16, sheetPadding);
        return Math.Clamp(value, 0, 1024);
    }

    private static void ApplyFrameWorkingImage(
        SpriteSheetFrameRecord record,
        string state,
        byte[] pngData,
        int width,
        int height,
        int margin,
        DateTime now)
    {
        if (width <= 0 || height <= 0 || width * (long)height > 120_000_000)
            throw new InvalidOperationException("Working frame image is too large.");
        record.WorkingState = NormalizeFrameWorkingState(state);
        record.WorkingContentType = "image/png";
        record.WorkingData = pngData;
        record.WorkingWidth = width;
        record.WorkingHeight = height;
        record.WorkingMargin = Math.Clamp(margin, 0, 1024);
        record.WorkingUpdatedAt = now;
        record.UpdatedAt = now;
    }

    private static void ClearFrameWorkingImage(SpriteSheetFrameRecord record, string state)
    {
        record.WorkingState = NormalizeFrameWorkingState(state);
        record.WorkingContentType = "image/png";
        record.WorkingData = [];
        record.WorkingWidth = 0;
        record.WorkingHeight = 0;
        record.WorkingMargin = 0;
        record.WorkingUpdatedAt = null;
    }

    private static string NormalizeFrameWorkingState(string? state) =>
        state?.Trim().ToLowerInvariant() switch
        {
            "isolated" => "isolated",
            "edited" => "edited",
            "stabilized" => "stabilized",
            "reassembled" => "reassembled",
            _ => "none",
        };

    private static SpriteSheetBackground ResolveWorkingFrameBackground(
        SpriteSheetDefinition definition,
        byte[] rgba,
        int width,
        int height) =>
        BackgroundFromDefinition(definition) ?? SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height);

    private static SpriteFrameWorkingView FrameWorkingView(SpriteSheetDefinition definition, SpriteSheetFrameRecord frame) =>
        new(
            frame.Id,
            definition.Id,
            frame.Index,
            string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
            NormalizeFrameWorkingState(frame.WorkingState),
            frame.WorkingData.Length == 0 ? string.Empty : DataUrl.ToDataUrl(frame.WorkingContentType, frame.WorkingData),
            frame.WorkingWidth,
            frame.WorkingHeight,
            frame.WorkingMargin,
            frame.WorkingUpdatedAt);

    private static IReadOnlyList<SpriteSheetFrameUpdateView> MergeTargetedRepairFrames(
        IReadOnlyList<SpriteSheetFrameRecord> existingRecords,
        IReadOnlyList<SpriteSheetFrameUpdateView> repairFrames,
        IReadOnlyList<int>? targetFrameNumbers)
    {
        if (targetFrameNumbers is not { Count: > 0 } || existingRecords.Count == 0)
            return ReindexFrameUpdates(repairFrames);

        var repairByIndex = repairFrames.ToDictionary(frame => frame.Index);
        var merged = existingRecords
            .OrderBy(frame => frame.Index)
            .Select(FrameUpdateFromRecord)
            .ToList();
        foreach (var frameNumber in targetFrameNumbers.Distinct().Order())
        {
            var index = frameNumber - 1;
            if (index < 0 || !repairByIndex.TryGetValue(index, out var repairFrame))
                continue;

            if (index < merged.Count)
                merged[index] = repairFrame;
            else
                merged.Add(repairFrame);
        }

        return ReindexFrameUpdates(merged);
    }

    private static IReadOnlyList<SpriteSheetFrameUpdateView> ReindexFrameUpdates(IReadOnlyList<SpriteSheetFrameUpdateView> frames) =>
        frames
            .OrderBy(frame => frame.Index)
            .Select((frame, index) => frame with
            {
                Index = index,
                Label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label,
            })
            .ToList();

    private static SpriteSheetRect ExpandRectToCell(
        SpriteSheetRect rect,
        int cellWidth,
        int cellHeight,
        int imageWidth,
        int imageHeight,
        string? horizontalAnchor,
        string? verticalAnchor)
    {
        cellWidth = Math.Max(1, cellWidth);
        cellHeight = Math.Max(1, cellHeight);
        var pivotX = PivotForHorizontalAnchor(horizontalAnchor);
        var pivotY = PivotForVerticalAnchor(verticalAnchor);
        var anchorX = rect.X + (int)Math.Round(rect.Width * pivotX);
        var anchorY = rect.Y + (int)Math.Round(rect.Height * pivotY);
        var x = anchorX - (int)Math.Round(cellWidth * pivotX);
        var y = anchorY - (int)Math.Round(cellHeight * pivotY);
        x = ShiftInsideImage(x, cellWidth, imageWidth);
        y = ShiftInsideImage(y, cellHeight, imageHeight);
        return new SpriteSheetRect(x, y, cellWidth, cellHeight);
    }

    private static int ShiftInsideImage(int origin, int size, int imageSize)
    {
        if (imageSize <= 0)
            return origin;
        if (size >= imageSize)
            return 0;
        return Math.Clamp(origin, 0, imageSize - size);
    }

    private SpriteSheetBackground EnsureSheetBackground(SpriteSheetDefinition definition, byte[] rgba, int width, int height)
    {
        var existing = BackgroundFromDefinition(definition);
        if (existing is not null)
            return existing;

        var resolved = SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height);
        ApplyBackground(definition, resolved);
        return resolved;
    }

    private static SpriteSheetBackground? BackgroundFromDefinition(SpriteSheetDefinition definition)
    {
        var mode = definition.BackgroundMode?.Trim().ToLowerInvariant();
        if (mode == "alpha")
            return new SpriteSheetBackground("alpha", 0, 0, 0, 0);
        if (mode == "color" && TryParseBackgroundColor(definition.BackgroundColor, out var r, out var g, out var b))
            return new SpriteSheetBackground("color", r, g, b, byte.MaxValue);
        return null;
    }

    private static SpriteSheetBackground BackgroundOrDefault(SpriteSheetDefinition definition) =>
        BackgroundFromDefinition(definition) ?? new SpriteSheetBackground("alpha", 0, 0, 0, 0);

    private static void ApplyBackground(SpriteSheetDefinition definition, SpriteSheetBackground background)
    {
        definition.BackgroundMode = NormalizeSpriteSheetBackgroundMode(background.Mode);
        definition.BackgroundColor = definition.BackgroundMode == "color" ? BackgroundColor(background) : null;
    }

    private static string NormalizeSpriteSheetBackgroundMode(string? mode) =>
        string.Equals(mode, "color", StringComparison.OrdinalIgnoreCase) ? "color" : "alpha";

    private static string BackgroundColor(SpriteSheetBackground background) =>
        $"#{background.R:X2}{background.G:X2}{background.B:X2}";

    private static bool TryParseBackgroundColor(string? value, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6)
            return false;
        return byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out r)
            && byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)
            && byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    private void UpdateWorkingSpriteAsset(
        ArtAsset working,
        ImagePayload image,
        SpriteSheetDefinition definition,
        DateTime now)
    {
        working.ContentType = image.ContentType;
        working.Data = image.Data;
        working.Width = image.Width;
        working.Height = image.Height;
        working.ThumbnailData = null;
        working.UpdatedAt = now;
        working.SourceMetadataJson = JsonSerializer.Serialize(new
        {
            Source = "sprite-sheet-working",
            SourceAssetId = definition.SourceAssetId,
            SpriteSheetDefinitionId = definition.Id,
            Mutable = true,
            definition.Rows,
            definition.Columns,
            definition.CellWidth,
            definition.CellHeight,
            definition.HorizontalAnchor,
            definition.VerticalAnchor,
            definition.BackgroundMode,
            definition.BackgroundColor,
            frameCount = definition.FrameRecords.Count,
        }, JsonOptions);
    }

    private async Task<List<SpriteSheetFrameListItem>> LoadSpriteSheetFrameListItemsAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken)
    {
        return await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .Select(frame => new SpriteSheetFrameListItem(
                frame.Id,
                frame.ProjectId,
                frame.SpriteSheetDefinitionId,
                frame.Index,
                frame.Label,
                frame.SourceX,
                frame.SourceY,
                frame.SourceWidth,
                frame.SourceHeight,
                frame.ShapeJson,
                frame.SourceImageAssetId,
                frame.SourceImageX,
                frame.SourceImageY,
                frame.SourceImageWidth,
                frame.SourceImageHeight,
                frame.CellX,
                frame.CellY,
                frame.CellWidth,
                frame.CellHeight,
                frame.SpriteX,
                frame.SpriteY,
                frame.SpriteWidth,
                frame.SpriteHeight,
                frame.PreviewWidth,
                frame.PreviewHeight,
                frame.WorkingState,
                frame.WorkingWidth,
                frame.WorkingHeight,
                frame.WorkingMargin,
                frame.WorkingUpdatedAt,
                frame.PoseName,
                frame.Phase,
                frame.RootOffsetX,
                frame.RootOffsetY,
                frame.DurationMs,
                frame.FootContactsJson,
                frame.IsKeyframe,
                frame.PivotX,
                frame.PivotY,
                frame.AppliedScale,
                frame.TranslationX,
                frame.TranslationY,
                frame.QaStatus,
                frame.RepairHistoryJson,
                frame.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private static List<SpriteSheetFrameListItem> ReindexSpriteSheetFrameListItems(
        SpriteSheetDefinition definition,
        IReadOnlyList<SpriteSheetFrameListItem> frames,
        DateTime updatedAt)
    {
        return frames
            .OrderBy(frame => frame.Index)
            .Select((frame, index) => WithSpriteSheetFrameLayout(definition, frame, index, updatedAt))
            .ToList();
    }

    private static SpriteSheetFrameListItem WithSpriteSheetFrameLayout(
        SpriteSheetDefinition definition,
        SpriteSheetFrameListItem frame,
        int index,
        DateTime updatedAt)
    {
        var cellRect = CellRectForSpriteSheetIndex(definition, index);
        var sourceRect = RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight);
        var spriteRect = SpriteRectForCell(definition, cellRect, sourceRect);
        return frame with
        {
            Index = index,
            CellX = cellRect.X,
            CellY = cellRect.Y,
            CellWidth = cellRect.Width,
            CellHeight = cellRect.Height,
            SpriteX = spriteRect.X,
            SpriteY = spriteRect.Y,
            SpriteWidth = spriteRect.Width,
            SpriteHeight = spriteRect.Height,
            UpdatedAt = updatedAt,
        };
    }

    private static SpriteSheetRect CellRectForSpriteSheetIndex(SpriteSheetDefinition definition, int index)
    {
        var columns = Math.Max(1, definition.Columns);
        var cellWidth = Math.Max(1, definition.CellWidth);
        var cellHeight = Math.Max(1, definition.CellHeight);
        var gutter = Math.Max(0, definition.Gutter);
        var row = index / columns;
        var column = index % columns;
        return new SpriteSheetRect(
            column * (cellWidth + gutter),
            row * (cellHeight + gutter),
            cellWidth,
            cellHeight);
    }

    private static SpriteSheetRect SpriteRectForCell(
        SpriteSheetDefinition definition,
        SpriteSheetRect cellRect,
        SpriteSheetRect sourceRect)
    {
        var padding = Math.Max(0, definition.Padding);
        var x = NormalizeHorizontalAnchor(definition.HorizontalAnchor) switch
        {
            "left" => cellRect.X + padding,
            "right" => cellRect.X + cellRect.Width - padding - sourceRect.Width,
            _ => (int)Math.Round(cellRect.X + ((cellRect.Width - sourceRect.Width) / 2d)),
        };
        var y = NormalizeVerticalAnchor(definition.VerticalAnchor) switch
        {
            "top" => cellRect.Y + padding,
            "middle" => (int)Math.Round(cellRect.Y + ((cellRect.Height - sourceRect.Height) / 2d)),
            _ => cellRect.Y + cellRect.Height - padding - sourceRect.Height,
        };

        var maxX = cellRect.X + cellRect.Width - sourceRect.Width;
        var maxY = cellRect.Y + cellRect.Height - sourceRect.Height;
        x = maxX < cellRect.X ? cellRect.X : Math.Clamp(x, cellRect.X, maxX);
        y = maxY < cellRect.Y ? cellRect.Y : Math.Clamp(y, cellRect.Y, maxY);
        return new SpriteSheetRect(x, y, sourceRect.Width, sourceRect.Height);
    }

    private void DetachTrackedSpriteSheetFrameRecords(Guid projectId, Guid spriteSheetId)
    {
        foreach (var entry in db.ChangeTracker
            .Entries<SpriteSheetFrameRecord>()
            .Where(entry => entry.Entity.ProjectId == projectId && entry.Entity.SpriteSheetDefinitionId == spriteSheetId)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task UpdateSpriteSheetFrameMetadataAsync(
        IReadOnlyList<SpriteSheetFrameListItem> frames,
        CancellationToken cancellationToken)
    {
        foreach (var frame in frames)
        {
            await db.SpriteSheetFrameRecords
                .Where(record => record.Id == frame.Id
                    && record.ProjectId == frame.ProjectId
                    && record.SpriteSheetDefinitionId == frame.SpriteSheetDefinitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(record => record.Index, frame.Index)
                    .SetProperty(record => record.CellX, frame.CellX)
                    .SetProperty(record => record.CellY, frame.CellY)
                    .SetProperty(record => record.CellWidth, frame.CellWidth)
                    .SetProperty(record => record.CellHeight, frame.CellHeight)
                    .SetProperty(record => record.SpriteX, frame.SpriteX)
                    .SetProperty(record => record.SpriteY, frame.SpriteY)
                    .SetProperty(record => record.SpriteWidth, frame.SpriteWidth)
                    .SetProperty(record => record.SpriteHeight, frame.SpriteHeight)
                    .SetProperty(record => record.UpdatedAt, frame.UpdatedAt),
                    cancellationToken);
        }
    }

    private static string SerializeSpriteSheetFramesJson(IReadOnlyList<SpriteSheetFrameListItem> frames) =>
        JsonSerializer.Serialize(frames
            .OrderBy(frame => frame.Index)
            .Select(frame => new
            {
                frame.Index,
                Label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
                SourceRect = RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
                ShapePaths = DeserializeShapePaths(frame.ShapeJson),
                frame.SourceImageAssetId,
                SourceImageRect = SourceImageRectView(frame),
                CellRect = RectView(frame.CellX, frame.CellY, frame.CellWidth, frame.CellHeight),
                SpriteRect = RectView(frame.SpriteX, frame.SpriteY, frame.SpriteWidth, frame.SpriteHeight),
            }), JsonOptions);

    private async Task UpsertSpriteSheetFrameRecordsAsync(
        Guid projectId,
        SpriteSheetDefinition definition,
        IReadOnlyList<SpriteSheetFrameView> frames,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeSpriteSheetFrameSaves(frames, definition.Rows, definition.Columns, definition.CellWidth, definition.CellHeight);
        definition.FramesJson = JsonSerializer.Serialize(normalized.Select(frame => new
        {
            frame.Index,
            frame.Label,
            frame.SourceRect,
            frame.ShapePaths,
            frame.SourceImageAssetId,
            frame.SourceImageRect,
            frame.CellRect,
            frame.SpriteRect,
        }), JsonOptions);

        var existing = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        var removed = existing.Skip(normalized.Count).ToList();
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(frame => frame.Id).ToList();
            var removedAttachments = await db.ChatContextAttachments
                .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.SpriteFrame && removedIds.Contains(a.RefId))
                .ToListAsync(cancellationToken);
            db.ChatContextAttachments.RemoveRange(removedAttachments);
            await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.SpriteFrame, removedIds, cancellationToken);
            db.SpriteSheetFrameRecords.RemoveRange(removed);
        }

        var now = DateTime.UtcNow;
        for (var position = 0; position < normalized.Count; position++)
        {
            var frame = normalized[position];
            var record = position < existing.Count ? existing[position] : null;
            if (record is null)
            {
                record = new SpriteSheetFrameRecord
                {
                    ProjectId = projectId,
                    SpriteSheetDefinitionId = definition.Id,
                    CreatedAt = now,
                };
                await db.SpriteSheetFrameRecords.AddAsync(record, cancellationToken);
            }

            var shapeJson = JsonSerializer.Serialize(frame.ShapePaths, JsonOptions);
            var geometryChanged = record.SourceX != frame.SourceRect.X
                || record.SourceY != frame.SourceRect.Y
                || record.SourceWidth != frame.SourceRect.Width
                || record.SourceHeight != frame.SourceRect.Height
                || !string.Equals(record.ShapeJson, shapeJson, StringComparison.Ordinal);
            var hasIncomingSourceImage = frame.SourceImageAssetId is not null || frame.SourceImageRect is not null;
            var sourceImageAssetId = hasIncomingSourceImage ? frame.SourceImageAssetId : record.SourceImageAssetId;
            var sourceImageRect = hasIncomingSourceImage
                ? frame.SourceImageRect ?? frame.SourceRect
                : SourceImageRectView(record);
            if (sourceImageAssetId is null)
                sourceImageRect = null;
            var sourceImageChanged = record.SourceImageAssetId != sourceImageAssetId
                || (sourceImageRect is null
                    ? record.SourceImageWidth > 0 || record.SourceImageHeight > 0
                    : record.SourceImageX != sourceImageRect.X
                        || record.SourceImageY != sourceImageRect.Y
                        || record.SourceImageWidth != sourceImageRect.Width
                        || record.SourceImageHeight != sourceImageRect.Height);
            if (NormalizeFrameWorkingState(record.WorkingState) != "none" && (geometryChanged || sourceImageChanged))
                ClearFrameWorkingImage(record, "none");

            record.Index = frame.Index;
            record.Label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label.Trim();
            record.SourceX = frame.SourceRect.X;
            record.SourceY = frame.SourceRect.Y;
            record.SourceWidth = frame.SourceRect.Width;
            record.SourceHeight = frame.SourceRect.Height;
            record.ShapeJson = shapeJson;
            record.SourceImageAssetId = sourceImageAssetId;
            record.SourceImageX = sourceImageRect?.X ?? 0;
            record.SourceImageY = sourceImageRect?.Y ?? 0;
            record.SourceImageWidth = sourceImageRect?.Width ?? 0;
            record.SourceImageHeight = sourceImageRect?.Height ?? 0;
            record.CellX = frame.CellRect.X;
            record.CellY = frame.CellRect.Y;
            record.CellWidth = frame.CellRect.Width;
            record.CellHeight = frame.CellRect.Height;
            record.SpriteX = frame.SpriteRect.X;
            record.SpriteY = frame.SpriteRect.Y;
            record.SpriteWidth = frame.SpriteRect.Width;
            record.SpriteHeight = frame.SpriteRect.Height;
            if (string.IsNullOrWhiteSpace(frame.PreviewPngDataUrl))
            {
                record.PreviewContentType = "image/png";
                record.PreviewData = [];
                record.PreviewWidth = 0;
                record.PreviewHeight = 0;
            }
            else
            {
                var preview = ParsePngDataUrl(frame.PreviewPngDataUrl, "Sprite frame preview must be a PNG data URL.");
                record.PreviewContentType = preview.ContentType;
                record.PreviewData = preview.Data;
                record.PreviewWidth = preview.Width;
                record.PreviewHeight = preview.Height;
            }
            record.UpdatedAt = now;
        }
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
        ?? throw new InvalidOperationException("Project was not found.");

    private async Task ValidateRecipeExampleAssetAsync(Guid projectId, Guid? exampleAssetId, CancellationToken cancellationToken)
    {
        if (exampleAssetId is not Guid assetId)
            return;

        if (!await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken))
            throw new InvalidOperationException("Recipe example image was not found in this project.");
    }

    private async Task ValidateAnimationRecipeReferencesAsync(
        Guid projectId,
        Guid? guideAssetId,
        Guid? primaryExampleSpriteSheetId,
        CancellationToken cancellationToken)
    {
        if (guideAssetId is Guid assetId)
        {
            var guideAsset = await db.ArtAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken);
            if (guideAsset is null)
                throw new InvalidOperationException("Animation recipe guide asset was not found in this project.");
            if (guideAsset.Kind != ArtAssetKind.SpriteGuide)
                throw new InvalidOperationException($"Animation recipe guide asset must be a SpriteGuide asset. Asset {assetId} is {guideAsset.Kind}.");
        }

        if (primaryExampleSpriteSheetId is Guid spriteSheetId
            && !await db.SpriteSheetDefinitions.AnyAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken))
        {
            throw new InvalidOperationException("Animation recipe example sprite sheet was not found in this project.");
        }
    }

    private AnimationSpec ResolveMotionClipSpec(AnimationSpec spec, string? requestedMotionClipId)
    {
        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        MotionClipDefinition? clip = null;
        if (!string.IsNullOrWhiteSpace(requestedMotionClipId))
        {
            clip = catalog.Find(requestedMotionClipId)
                ?? throw new InvalidOperationException($"Motion clip '{requestedMotionClipId}' was not found in the catalog.");
            if (!clip.Supports(spec))
                throw new InvalidOperationException($"Motion clip '{requestedMotionClipId}' does not support {spec.AssetType}/{spec.StructureType}/{spec.AnimationKind}.");
        }
        else if (IsHumanoidWalkSpec(spec))
        {
            clip = catalog.ResolveDefault(spec);
        }

        if (clip is null)
            return spec;

        var sampleCount = clip.ResolveSampleCount(spec.FrameCount);
        if (sampleCount != spec.FrameCount)
        {
            spec = SpriteMotionArchetypes.Build(
                spec.AssetType,
                spec.StructureType,
                spec.AnimationKind,
                spec.Facing,
                spec.RootMotion,
                sampleCount,
                spec.Fps,
                spec.TargetCellWidth,
                spec.TargetCellHeight);
        }

        return spec with
        {
            GuideRenderer = MotionClipCatalog.RendererId,
            GuideRenderStyle = MotionClipCatalog.SkinnedMannequinRenderStyle,
            MotionClipId = clip.ClipId,
            GuideCameraYawDegrees = GltfMotionGuideRenderer.FacingToYawDegrees(spec.Facing),
            MotionValidationProfile = "humanoid_walk",
            GuideSourcePackage = clip.SourcePackage,
            GuideSourceLicense = clip.License,
        };
    }

    private MotionGuideRenderResult? RenderMotionGuideIfAvailable(AnimationSpec spec, LayoutSpec layout)
    {
        if (!MotionClipCatalog.IsExternalMotionSpec(spec))
            return null;

        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        var clip = catalog.Find(spec.MotionClipId)
            ?? throw new InvalidOperationException($"Motion clip '{spec.MotionClipId}' was not found in the catalog.");
        return GltfMotionGuideRenderer.Render(environment.ContentRootPath, clip, layout, spec);
    }

    private static AnimationSpec ApplyMotionSampleMetadata(AnimationSpec spec, IReadOnlyList<MotionGuideFrameSample> samples)
    {
        var byIndex = samples.ToDictionary(sample => sample.FrameIndex);
        return spec with
        {
            Frames = spec.Frames.Select(frame =>
            {
                if (!byIndex.TryGetValue(frame.Index, out var sample))
                    return frame;

                return frame with
                {
                    PoseName = $"sample_{frame.Index + 1:00}_{MotionContactLabel(sample.Contacts)}",
                    Contacts = sample.Contacts,
                    GuideShape = MotionClipCatalog.RendererId,
                };
            }).ToList(),
        };
    }

    private static bool IsHumanoidWalkSpec(AnimationSpec spec)
    {
        var kind = NormalizeGuideToken(spec.AnimationKind, string.Empty);
        if (!kind.Contains("walk", StringComparison.Ordinal))
            return false;

        var assetType = NormalizeGuideToken(spec.AssetType, string.Empty);
        var structure = NormalizeGuideToken(spec.StructureType, string.Empty);
        return assetType.Contains("unit", StringComparison.Ordinal)
            || assetType.Contains("character", StringComparison.Ordinal)
            || assetType.Contains("humanoid", StringComparison.Ordinal)
            || structure.Contains("biped", StringComparison.Ordinal)
            || structure.Contains("humanoid", StringComparison.Ordinal);
    }

    private static string MotionContactLabel(IReadOnlyList<string> contacts)
    {
        if (contacts.Count == 0)
            return "lift";
        var left = contacts.Any(contact => contact.Contains("left", StringComparison.OrdinalIgnoreCase));
        var right = contacts.Any(contact => contact.Contains("right", StringComparison.OrdinalIgnoreCase));
        return (left, right) switch
        {
            (true, true) => "both_contact",
            (true, false) => "left_contact",
            (false, true) => "right_contact",
            _ => "lift",
        };
    }

    private static LayoutProfile AnalyzeGuideLayoutProfile(ArtAsset? reference)
    {
        var foregroundAspect = 1d;
        var foregroundCoverage = 0d;
        var foregroundWidth = 0;
        var foregroundHeight = 0;
        var equippedOrWide = false;
        var bulky = false;
        var text = string.Join(' ', reference?.Label, reference?.Prompt, reference?.Notes).ToLowerInvariant();
        equippedOrWide |= ContainsAny(text, "weapon", "axe", "sword", "shield", "bow", "spear", "staff", "gun", "rifle", "wand", "tail", "wing");
        bulky |= ContainsAny(text, "bulky", "stocky", "large", "giant", "heavy", "wide", "broad", "shield");

        if (reference is not null && SpriteSheetPngCodec.TryReadRgba(reference.Data, out var width, out var height, out var rgba))
        {
            var background = SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height);
            if (SpriteSheetImageAnalyzer.ForegroundBounds(rgba, width, height, background) is { } bounds)
            {
                foregroundWidth = bounds.Width;
                foregroundHeight = bounds.Height;
                foregroundAspect = bounds.Height <= 0 ? 1d : bounds.Width / (double)bounds.Height;
                foregroundCoverage = Math.Max(bounds.Width / (double)Math.Max(1, width), bounds.Height / (double)Math.Max(1, height));
                equippedOrWide |= foregroundAspect >= 0.82d;
                bulky |= foregroundCoverage >= 0.62d;
            }
        }

        var needsTallCells = foregroundAspect <= 0.68d && !equippedOrWide;
        return new LayoutProfile(
            foregroundAspect,
            foregroundCoverage,
            foregroundWidth,
            foregroundHeight,
            NeedsLargeCells: equippedOrWide || bulky,
            NeedsTallCells: needsTallCells,
            NeedsLargePadding: equippedOrWide || bulky);
    }

    private static LayoutSpec BuildGuideLayout(AnimationSpec spec, string backgroundColor, LayoutProfile layoutProfile)
    {
        var (columns, rows) = GuideGridForFrameCount(spec.FrameCount);
        var (guideCellWidth, guideCellHeight) = GuideCellSize(spec.FrameCount, layoutProfile);
        var canvasWidth = checked(columns * guideCellWidth);
        var canvasHeight = checked(rows * guideCellHeight);
        var safeMarginRatio = layoutProfile.NeedsLargePadding ? 0.14d : 0.11d;
        var slots = Enumerable.Range(0, spec.FrameCount)
            .Select(index =>
            {
                var rect = CellRectForGuideGrid(index, columns, rows, canvasWidth, canvasHeight);
                var margin = Math.Max(24, (int)Math.Round(Math.Min(rect.Width, rect.Height) * safeMarginRatio));
                var safe = new SpriteSheetRect(
                    rect.X + margin,
                    rect.Y + margin,
                    Math.Max(1, rect.Width - (margin * 2)),
                    Math.Max(1, rect.Height - (margin * 2)));
                var baseline = rect.Y + (int)Math.Round(rect.Height * 0.83d);
                var root = new SpriteSheetPoint(rect.X + rect.Width / 2, baseline);
                return new SlotSpec(index, rect, root, baseline, safe);
            })
            .ToList();
        return new LayoutSpec(
            canvasWidth,
            canvasHeight,
            rows,
            columns,
            guideCellWidth,
            guideCellHeight,
            spec.TargetCellWidth,
            spec.TargetCellHeight,
            backgroundColor,
            slots);
    }

    private static (int Columns, int Rows) GuideGridForFrameCount(int frameCount) =>
        frameCount switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            <= 4 => (2, 2),
            <= 6 => (3, 2),
            <= 8 => (4, 2),
            <= 12 => (4, 3),
            _ => (4, 4),
        };

    private static (int Width, int Height) GuideCellSize(int frameCount, LayoutProfile layoutProfile)
    {
        if (frameCount <= 1)
            return (1024, 1024);
        if (frameCount == 2)
            return layoutProfile.NeedsLargeCells ? (1024, 1024) : (768, 1024);
        if (frameCount <= 6)
            return layoutProfile.NeedsLargeCells ? (768, 768) : (512, 512);
        if (frameCount <= 8)
            return layoutProfile.NeedsLargeCells || layoutProfile.NeedsTallCells ? (512, 768) : (512, 512);

        return (512, 512);
    }

    private static SpriteSheetRect CellRectForGuideGrid(int index, int columns, int rows, int canvasWidth, int canvasHeight)
    {
        var row = index / columns;
        var col = index % columns;
        var x0 = col * canvasWidth / columns;
        var x1 = (col + 1) * canvasWidth / columns;
        var y0 = row * canvasHeight / rows;
        var y1 = (row + 1) * canvasHeight / rows;
        return new SpriteSheetRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private static string BuildAnimationGuidePromptScaffold(ArtAsset? reference, AnimationSpec spec, LayoutSpec layout)
    {
        var guideRole = MotionClipCatalog.IsExternalMotionSpec(spec)
            ? $"Image 1 is a sampled mannequin motion guide from clip {spec.MotionClipId}; it controls exact slot positions, frame order, root/pivot anchors, safe margins, body pose, foot contacts, and camera yaw. Do not reproduce guide marks."
            : "Image 1 is a structure guide; it controls exact slot positions, frame order, root/pivot anchors, safe margins, and motion layout. Do not reproduce guide marks.";
        var referenceRole = reference is null
            ? "Image 2, when supplied, controls the subject identity, silhouette, palette, materials, outfit, equipment, and rendering style."
            : $"Image 2 is the canonical subject reference '{reference.Label}'. It controls identity, silhouette, palette, materials, outfit, equipment, and rendering style.";

        return $"""
        GOAL
        Render a {spec.FrameCount}-frame {spec.AssetType} {spec.AnimationKind} animation as a {layout.Columns} column by {layout.Rows} row sprite-sheet grid.

        INPUT ROLES
        {guideRole}
        {referenceRole}
        Optional later references may control art style, but the guide remains the only motion/layout guide.

        MOTION CONTRACT
        Facing/direction: {SpriteFacing.Normalize(spec.Facing)} ({SpriteFacing.ToPromptPhrase(spec.Facing)}). Root motion: {spec.RootMotion}. Read frame order left-to-right across each row, then lower rows. Do not mirror the guide, swap leading/trailing limbs, or turn the subject to a different camera angle.

        OUTPUT CONTRACT
        Exactly {spec.FrameCount} complete frames, one subject per cell, no text, no labels, no borders, no extra poses, no overlap between cells, no cropping. Keep identity, apparent scale, camera, line style, and root/pivot position stable. Keep the full silhouette inside each guide cell's safe region, including weapons, shields, hands, feet, ears, hair, tails, wings, clothing, accessories, and effects.

        CLEANUP CONTRACT
        Do not reproduce guide lines, frame boxes, safe boxes, root marks, skeleton/mannequin marks, contact markers, numbers, labels, or construction marks.

        BACKGROUND
        Use one flat opaque chroma color: {layout.BackgroundColor}. No shadows, floors, scenery, gradients, transparent checkerboards, or detached effects outside the guided subject.
        """;
    }

    private static (int Width, int Height) ParseCellSize(string? requested, string configured, int fallbackWidth, int fallbackHeight)
    {
        if (TryParseCellSize(requested, out var requestedWidth, out var requestedHeight))
            return (requestedWidth, requestedHeight);
        if (TryParseCellSize(configured, out var configuredWidth, out var configuredHeight))
            return (configuredWidth, configuredHeight);
        return (fallbackWidth, fallbackHeight);
    }

    private static bool TryParseCellSize(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().ToLowerInvariant().Split('x', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out width)
            || !int.TryParse(parts[1], out height))
        {
            return false;
        }

        width = Math.Clamp(width, 1, 4096);
        height = Math.Clamp(height, 1, 4096);
        return true;
    }

    private static string InferGuideAssetType(ArtAsset? reference)
    {
        if (reference is null)
            return "unit";

        var text = string.Join(' ', reference.Label, reference.Prompt, reference.Notes).ToLowerInvariant();
        if (ContainsAny(text, "tower", "turret", "cannon"))
            return "tower";
        if (ContainsAny(text, "projectile", "bullet", "missile", "arrow"))
            return "projectile";
        if (ContainsAny(text, "vfx", "effect", "explosion", "slash", "spark"))
            return "vfx";
        return "unit";
    }

    private static string DefaultGuideStructure(string assetType) =>
        assetType switch
        {
            "tower" => "tower_pivot",
            "projectile" => "directional_projectile",
            "vfx" => "radial_vfx",
            _ => "biped",
        };

    private static string NormalizeGuideToken(string? value, string fallback)
    {
        var cleaned = value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string TitleCase(string value)
    {
        var words = value.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0
            ? "Animation"
            : string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + (word.Length == 1 ? string.Empty : word[1..])));
    }

    private async Task<Dictionary<Guid, int>> LoadCurrentRecipeVersionsAsync(
        Guid projectId,
        IReadOnlyList<Guid> recipeIds,
        CancellationToken cancellationToken)
    {
        if (recipeIds.Count == 0)
            return [];

        var ids = recipeIds.Distinct().ToList();
        return await db.PromptRecipeVersions
            .AsNoTracking()
            .Where(version => version.ProjectId == projectId && ids.Contains(version.RecipeId))
            .GroupBy(version => version.RecipeId)
            .Select(group => new { RecipeId = group.Key, Version = group.Max(version => version.Version) })
            .ToDictionaryAsync(item => item.RecipeId, item => item.Version, cancellationToken);
    }

    private async Task<int?> GetCurrentRecipeVersionAsync(Guid recipeId, CancellationToken cancellationToken) =>
        await db.PromptRecipeVersions
            .AsNoTracking()
            .Where(version => version.RecipeId == recipeId)
            .Select(version => (int?)version.Version)
            .MaxAsync(cancellationToken);

    private async Task<List<ArtAsset>> MergeRecipeExampleReferenceAsync(
        Guid projectId,
        PromptRecipe? recipe,
        IReadOnlyList<ArtAsset> explicitReferences,
        Guid? excludedAssetId,
        CancellationToken cancellationToken)
    {
        var maxReferences = Math.Max(0, imageOptions.Value.MaxReferenceImages);
        var references = new List<ArtAsset>();
        if (maxReferences == 0)
            return references;

        if (recipe?.ExampleAssetId is Guid exampleAssetId && exampleAssetId != excludedAssetId)
        {
            var example = await db.ArtAssets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == exampleAssetId, cancellationToken);
            if (example is not null)
                references.Add(example);
        }

        foreach (var reference in explicitReferences)
        {
            if (reference.Id == excludedAssetId || references.Any(existing => existing.Id == reference.Id))
                continue;
            references.Add(reference);
            if (references.Count >= maxReferences)
                break;
        }

        return references;
    }

    private async Task<RecipePromptGuidance?> LoadRecipePromptGuidanceForBatchAsync(
        Guid projectId,
        GenerationBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.PromptRecipeId is not Guid recipeId)
            return null;

        if (batch.PromptRecipeVersion is int version)
        {
            var snapshot = await db.PromptRecipeVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.ProjectId == projectId && v.RecipeId == recipeId && v.Version == version, cancellationToken);
            if (snapshot is not null)
                return RecipeGuidance(snapshot);
        }

        var recipe = await db.PromptRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken);
        return recipe is null ? null : RecipeGuidance(recipe);
    }

    private static RecipePromptGuidance RecipeGuidance(PromptRecipe recipe) =>
        new(recipe.PromptTemplate, recipe.StyleRulesJson, recipe.AvoidRulesJson);

    private static RecipePromptGuidance RecipeGuidance(PromptRecipeVersion version) =>
        new(version.PromptTemplate, version.StyleRulesJson, version.AvoidRulesJson);

    private async Task<List<ArtAsset>> ResolveAssetsAsync(Guid projectId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken)
    {
        var ids = assetIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var assets = await db.ArtAssets.Where(a => a.ProjectId == projectId && ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (assets.Count != ids.Count)
            throw new InvalidOperationException("One or more reference assets could not be found.");
        var byId = assets.ToDictionary(a => a.Id);
        return ids.Select(id => byId[id]).ToList();
    }

    private async Task<string> ResolveAttachmentLabelAsync(Guid projectId, ChatContextAttachmentType type, Guid refId, CancellationToken cancellationToken)
    {
        return type switch
        {
            ChatContextAttachmentType.Asset or ChatContextAttachmentType.Crop =>
                await db.ArtAssets.Where(a => a.ProjectId == projectId && a.Id == refId).Select(a => a.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Asset",
            ChatContextAttachmentType.Mask =>
                await db.ImageMasks.Where(m => m.ProjectId == projectId && m.Id == refId).Select(m => m.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Mask",
            ChatContextAttachmentType.PromptRecipe =>
                await db.PromptRecipes.Where(r => r.ProjectId == projectId && r.Id == refId).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken)
                ?? "Recipe",
            ChatContextAttachmentType.GenerationBatch =>
                await db.GenerationBatches.Where(b => b.ProjectId == projectId && b.Id == refId).Select(b => b.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Batch",
            ChatContextAttachmentType.SpriteFrame =>
                await db.SpriteSheetFrameRecords.Where(f => f.ProjectId == projectId && f.Id == refId).Select(f => f.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Sprite frame",
            _ => "Context"
        };
    }

    private async Task<CompareReviewSet> GetOrCreateCompareReviewSetAsync(
        Guid projectId,
        string? title,
        string? summary,
        CancellationToken cancellationToken)
    {
        var reviewSet = await db.CompareReviewSets.FirstOrDefaultAsync(set => set.ProjectId == projectId, cancellationToken);
        if (reviewSet is not null)
            return reviewSet;

        reviewSet = new CompareReviewSet
        {
            ProjectId = projectId,
            Title = NormalizeReviewSetTitle(title, "Current Review"),
            Summary = NormalizeReviewSetSummary(summary),
        };
        await db.CompareReviewSets.AddAsync(reviewSet, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return reviewSet;
    }

    private async Task AddCompareReviewItemsCoreAsync(
        Guid projectId,
        CompareReviewSet reviewSet,
        IReadOnlyList<CompareReviewSetItemRequest> requests,
        CancellationToken cancellationToken)
    {
        var dedupedRequests = requests
            .Where(request => request.RefId != Guid.Empty)
            .GroupBy(request => CompareReviewKey(request.Kind, request.RefId))
            .Select(group => group.Last())
            .ToList();
        if (dedupedRequests.Count == 0)
            return;

        var existingItems = await db.CompareReviewSetItems
            .Where(item => item.CompareReviewSetId == reviewSet.Id)
            .ToListAsync(cancellationToken);
        var existingByKey = existingItems.ToDictionary(item => CompareReviewKey(item.Kind, item.RefId));
        var nextOrder = existingItems.Count == 0 ? 0 : existingItems.Max(item => item.SortOrder) + 1;

        foreach (var request in dedupedRequests)
        {
            await EnsureCompareReviewTargetExistsAsync(projectId, request.Kind, request.RefId, cancellationToken);
            var key = CompareReviewKey(request.Kind, request.RefId);
            var label = string.IsNullOrWhiteSpace(request.Label)
                ? await ResolveCompareReviewItemLabelAsync(projectId, request.Kind, request.RefId, cancellationToken)
                : request.Label.Trim();
            var notes = request.Notes?.Trim() ?? string.Empty;
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.Label = label;
                existing.Notes = notes;
                continue;
            }

            var item = new CompareReviewSetItem
            {
                CompareReviewSetId = reviewSet.Id,
                Kind = request.Kind,
                RefId = request.RefId,
                Label = label,
                Notes = notes,
                SortOrder = nextOrder++,
            };
            await db.CompareReviewSetItems.AddAsync(item, cancellationToken);
        }
    }

    private async Task EnsureCompareReviewTargetExistsAsync(
        Guid projectId,
        CompareReviewItemKind kind,
        Guid refId,
        CancellationToken cancellationToken)
    {
        var exists = kind switch
        {
            CompareReviewItemKind.Asset =>
                await db.ArtAssets.AnyAsync(asset => asset.ProjectId == projectId && asset.Id == refId, cancellationToken),
            CompareReviewItemKind.GenerationBatch =>
                await db.GenerationBatches.AnyAsync(batch => batch.ProjectId == projectId && batch.Id == refId, cancellationToken),
            CompareReviewItemKind.SpriteSheet or CompareReviewItemKind.SpriteAnimation =>
                await db.SpriteSheetDefinitions.AnyAsync(sheet => sheet.ProjectId == projectId && sheet.Id == refId, cancellationToken),
            CompareReviewItemKind.SpriteFrame =>
                await db.SpriteSheetFrameRecords.AnyAsync(frame => frame.ProjectId == projectId && frame.Id == refId, cancellationToken),
            _ => false,
        };

        if (!exists)
            throw new InvalidOperationException($"Compare review target was not found for {kind} {refId}.");
    }

    private async Task<string> ResolveCompareReviewItemLabelAsync(
        Guid projectId,
        CompareReviewItemKind kind,
        Guid refId,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            CompareReviewItemKind.Asset =>
                await db.ArtAssets.Where(asset => asset.ProjectId == projectId && asset.Id == refId).Select(asset => asset.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Asset",
            CompareReviewItemKind.GenerationBatch =>
                await db.GenerationBatches.Where(batch => batch.ProjectId == projectId && batch.Id == refId).Select(batch => batch.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Batch",
            CompareReviewItemKind.SpriteSheet =>
                await db.SpriteSheetDefinitions.Where(sheet => sheet.ProjectId == projectId && sheet.Id == refId).Select(sheet => sheet.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Sprite sheet",
            CompareReviewItemKind.SpriteAnimation =>
                await db.SpriteSheetDefinitions.Where(sheet => sheet.ProjectId == projectId && sheet.Id == refId).Select(sheet => sheet.Label).FirstOrDefaultAsync(cancellationToken)
                is { } animationLabel ? $"{animationLabel} animation" : "Sprite animation",
            CompareReviewItemKind.SpriteFrame =>
                await db.SpriteSheetFrameRecords.Where(frame => frame.ProjectId == projectId && frame.Id == refId).Select(frame => frame.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Sprite frame",
            _ => "Review item",
        };
    }

    private async Task<CompareReviewSetView> LoadCompareReviewSetViewAsync(Guid projectId, Guid reviewSetId, CancellationToken cancellationToken)
    {
        var reviewSet = await db.CompareReviewSets
            .AsNoTracking()
            .FirstOrDefaultAsync(set => set.ProjectId == projectId && set.Id == reviewSetId, cancellationToken)
            ?? throw new InvalidOperationException("Compare review set was not found.");
        var items = await db.CompareReviewSetItems
            .AsNoTracking()
            .Where(item => item.CompareReviewSetId == reviewSet.Id)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return CompareReviewSetView(reviewSet, items);
    }

    private async Task RemoveCompareReviewItemsAsync(
        Guid projectId,
        CompareReviewItemKind kind,
        IEnumerable<Guid> refIds,
        CancellationToken cancellationToken)
    {
        var ids = refIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        var items = await db.CompareReviewSetItems
            .Include(item => item.CompareReviewSet)
            .Where(item => item.CompareReviewSet.ProjectId == projectId && item.Kind == kind && ids.Contains(item.RefId))
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
            return;

        foreach (var reviewSet in items.Select(item => item.CompareReviewSet).DistinctBy(set => set.Id))
            reviewSet.UpdatedAt = DateTime.UtcNow;
        db.CompareReviewSetItems.RemoveRange(items);
    }

    private static string NormalizeReviewSetTitle(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();

    private static string NormalizeReviewSetSummary(string? summary) =>
        summary?.Trim() ?? string.Empty;

    private static string CompareReviewKey(CompareReviewItemKind kind, Guid refId) =>
        $"{kind}:{refId:D}";

    private async Task<ProviderStatusView> BuildProviderStatusAsync(CancellationToken cancellationToken)
    {
        var connected = await providerService.IsOpenAIAccountConnectedAsync(cancellationToken);
        return connected
            ? new ProviderStatusView(true, "OpenAI account connected")
            : new ProviderStatusView(false, "Connect OpenAI account in Providers before generating or editing images.");
    }

    private ArtAsset CreateAsset(
        Guid projectId,
        string label,
        string fileName,
        ArtAssetKind kind,
        string contentType,
        byte[] data,
        Guid? parentAssetId,
        Guid? sourceBatchId,
        Guid? promptRecipeId,
        int? promptRecipeVersion,
        string prompt,
        object metadata)
    {
        var (width, height) = ImageMetadataReader.TryReadSize(data, contentType);
        return new ArtAsset
        {
            ProjectId = projectId,
            Label = label.Trim(),
            FileName = fileName,
            Kind = kind,
            ContentType = contentType,
            Data = data,
            Width = width,
            Height = height,
            ParentAssetId = parentAssetId,
            SourceBatchId = sourceBatchId,
            SourcePromptRecipeId = promptRecipeId,
            SourcePromptRecipeVersion = promptRecipeVersion,
            Prompt = prompt,
            SourceMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
        };
    }

    private static string BuildPrompt(string prompt, string negativePrompt, RecipePromptGuidance? recipe, string? background)
    {
        var parts = new List<string>();
        if (recipe is not null)
        {
            parts.Add("Recipe guidance: Use the following reusable style and production guide for this class of asset. The specific subject and one-off requirements come from the request that follows.");
            if (!string.IsNullOrWhiteSpace(recipe.PromptTemplate))
                parts.Add("Recipe prompt:\n" + recipe.PromptTemplate.Trim());
        }

        parts.Add(recipe is null
            ? prompt.Trim()
            : "Specific request:\n" + prompt.Trim());

        if (recipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(recipe.StyleRulesJson))
            {
                var styleRules = DeserializeStrings(recipe.StyleRulesJson);
                if (styleRules.Count > 0)
                    parts.Add("Reusable rules: " + string.Join("; ", styleRules));
            }
        }

        var avoidRules = new List<string>();
        if (!string.IsNullOrWhiteSpace(negativePrompt))
            avoidRules.Add(negativePrompt.Trim());
        if (recipe is not null)
            avoidRules.AddRange(DeserializeStrings(recipe.AvoidRulesJson));
        if (avoidRules.Count > 0)
            parts.Add("Avoid: " + string.Join("; ", avoidRules));
        if (NormalizeBackground(background) == "removable")
        {
            parts.Add(
                """
                Export background requirement:
                Place the asset on a flat, solid chroma-key magenta background using exactly #ff00ff. The same solid magenta should be visible through open holes, railings, gaps, cutouts, and transparent-looking interior spaces. Do not use checkerboards, transparency grids, white or gray faux transparency, texture, gradients, shadows, reflections, floor planes, scenery, or extra props in the background.
                """);
        }

        return string.Join("\n\n", parts);
    }

    private static void ApplyRecipeRequest(PromptRecipe recipe, SavePromptRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Recipe name is required.");
        recipe.AssetType = Clean(request.AssetType);
        recipe.PromptTemplate = CleanRequired(request.PromptTemplate, "Prompt template is required.");
        recipe.StyleRulesJson = SerializeStrings(request.StyleRules);
        recipe.AvoidRulesJson = SerializeStrings(request.AvoidRules);
        recipe.ExampleAssetId = request.ExampleAssetId;
        recipe.PreferredProvider = Clean(request.PreferredProvider);
        recipe.PreferredModel = Clean(request.PreferredModel);
        recipe.PreferredSize = Clean(request.PreferredSize);
        recipe.Notes = Clean(request.Notes);
    }

    private static void ApplyRecipeRequest(PromptRecipe recipe, UpdatePromptRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Recipe name is required.");
        recipe.AssetType = Clean(request.AssetType);
        recipe.PromptTemplate = CleanRequired(request.PromptTemplate, "Prompt template is required.");
        recipe.StyleRulesJson = SerializeStrings(request.StyleRules);
        recipe.AvoidRulesJson = SerializeStrings(request.AvoidRules);
        recipe.ExampleAssetId = request.ExampleAssetId;
        recipe.PreferredProvider = Clean(request.PreferredProvider);
        recipe.PreferredModel = Clean(request.PreferredModel);
        recipe.PreferredSize = Clean(request.PreferredSize);
        recipe.Notes = Clean(request.Notes);
    }

    private static void ApplyAnimationRecipeRequest(AnimationRecipe recipe, SaveAnimationRecipeRequest request)
    {
        var frameCount = NormalizeFrameCount(request.FrameCount, request.ExpectedFrameBoxes.Count);
        recipe.Name = CleanRequired(request.Name, "Animation recipe name is required.");
        recipe.AnimationKind = CleanRequired(request.AnimationKind, "Animation kind is required.");
        recipe.Facing = Clean(request.Facing);
        recipe.FrameCount = frameCount;
        recipe.FrameOrderJson = SerializeFrameOrder(request.FrameOrder, frameCount);
        recipe.Fps = Math.Clamp(request.Fps <= 0 ? 8 : request.Fps, 1, 60);
        recipe.Loop = request.Loop;
        recipe.GuideAssetId = request.GuideAssetId;
        recipe.ExpectedFrameBoxesJson = SerializeFrameBoxes(request.ExpectedFrameBoxes);
        recipe.AnchorStrategy = string.IsNullOrWhiteSpace(request.AnchorStrategy) ? "recipe-defined" : request.AnchorStrategy.Trim();
        recipe.PromptScaffold = CleanRequired(request.PromptScaffold, "Prompt scaffold is required.");
        recipe.ExportDefaultsJson = NormalizeJsonObject(request.ExportDefaultsJson);
        recipe.Notes = Clean(request.Notes);
        recipe.PrimaryExampleSpriteSheetId = request.PrimaryExampleSpriteSheetId;
    }

    private static void ApplyAnimationRecipeRequest(AnimationRecipe recipe, UpdateAnimationRecipeRequest request)
    {
        var frameCount = NormalizeFrameCount(request.FrameCount, request.ExpectedFrameBoxes.Count);
        recipe.Name = CleanRequired(request.Name, "Animation recipe name is required.");
        recipe.AnimationKind = CleanRequired(request.AnimationKind, "Animation kind is required.");
        recipe.Facing = Clean(request.Facing);
        recipe.FrameCount = frameCount;
        recipe.FrameOrderJson = SerializeFrameOrder(request.FrameOrder, frameCount);
        recipe.Fps = Math.Clamp(request.Fps <= 0 ? 8 : request.Fps, 1, 60);
        recipe.Loop = request.Loop;
        recipe.GuideAssetId = request.GuideAssetId;
        recipe.ExpectedFrameBoxesJson = SerializeFrameBoxes(request.ExpectedFrameBoxes);
        recipe.AnchorStrategy = string.IsNullOrWhiteSpace(request.AnchorStrategy) ? "recipe-defined" : request.AnchorStrategy.Trim();
        recipe.PromptScaffold = CleanRequired(request.PromptScaffold, "Prompt scaffold is required.");
        recipe.ExportDefaultsJson = NormalizeJsonObject(request.ExportDefaultsJson);
        recipe.Notes = Clean(request.Notes);
        recipe.PrimaryExampleSpriteSheetId = request.PrimaryExampleSpriteSheetId;
    }

    private async Task<int> AppendPromptRecipeVersionAsync(
        PromptRecipe recipe,
        string? source,
        string? changeSummary,
        CancellationToken cancellationToken)
    {
        var latestVersion = await db.PromptRecipeVersions
            .Where(version => version.RecipeId == recipe.Id)
            .Select(version => (int?)version.Version)
            .MaxAsync(cancellationToken)
            ?? 0;
        var normalizedSource = NormalizeRecipeVersionSource(source);
        var normalizedSummary = string.IsNullOrWhiteSpace(changeSummary)
            ? $"Saved by {normalizedSource}."
            : changeSummary.Trim();

        var nextVersion = latestVersion + 1;
        await db.PromptRecipeVersions.AddAsync(new PromptRecipeVersion
        {
            ProjectId = recipe.ProjectId,
            RecipeId = recipe.Id,
            Version = nextVersion,
            Name = recipe.Name,
            AssetType = recipe.AssetType,
            PromptTemplate = recipe.PromptTemplate,
            StyleRulesJson = recipe.StyleRulesJson,
            AvoidRulesJson = recipe.AvoidRulesJson,
            ExampleAssetId = recipe.ExampleAssetId,
            PreferredProvider = recipe.PreferredProvider,
            PreferredModel = recipe.PreferredModel,
            PreferredSize = recipe.PreferredSize,
            ExportDefaultsJson = recipe.ExportDefaultsJson,
            Notes = recipe.Notes,
            Source = normalizedSource,
            ChangeSummary = normalizedSummary,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        return nextVersion;
    }

    private async Task<int> AppendAnimationRecipeVersionAsync(
        AnimationRecipe recipe,
        string? source,
        string? changeSummary,
        CancellationToken cancellationToken)
    {
        var latestVersion = await db.AnimationRecipeVersions
            .Where(version => version.AnimationRecipeId == recipe.Id)
            .Select(version => (int?)version.Version)
            .MaxAsync(cancellationToken)
            ?? 0;
        var normalizedSource = NormalizeRecipeVersionSource(source);
        var normalizedSummary = string.IsNullOrWhiteSpace(changeSummary)
            ? $"Saved by {normalizedSource}."
            : changeSummary.Trim();

        var nextVersion = latestVersion + 1;
        recipe.CurrentVersion = nextVersion;
        await db.AnimationRecipeVersions.AddAsync(new AnimationRecipeVersion
        {
            ProjectId = recipe.ProjectId,
            AnimationRecipeId = recipe.Id,
            Version = nextVersion,
            Name = recipe.Name,
            AnimationKind = recipe.AnimationKind,
            Facing = recipe.Facing,
            FrameCount = recipe.FrameCount,
            FrameOrderJson = recipe.FrameOrderJson,
            Fps = recipe.Fps,
            Loop = recipe.Loop,
            GuideAssetId = recipe.GuideAssetId,
            ExpectedFrameBoxesJson = recipe.ExpectedFrameBoxesJson,
            AnchorStrategy = recipe.AnchorStrategy,
            PromptScaffold = recipe.PromptScaffold,
            ExportDefaultsJson = recipe.ExportDefaultsJson,
            Notes = recipe.Notes,
            PrimaryExampleSpriteSheetId = recipe.PrimaryExampleSpriteSheetId,
            Source = normalizedSource,
            ChangeSummary = normalizedSummary,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        return nextVersion;
    }

    private static void ApplyRecipeSnapshot(PromptRecipe recipe, PromptRecipeVersion snapshot)
    {
        recipe.Name = snapshot.Name;
        recipe.AssetType = snapshot.AssetType;
        recipe.PromptTemplate = snapshot.PromptTemplate;
        recipe.StyleRulesJson = snapshot.StyleRulesJson;
        recipe.AvoidRulesJson = snapshot.AvoidRulesJson;
        recipe.ExampleAssetId = snapshot.ExampleAssetId;
        recipe.PreferredProvider = snapshot.PreferredProvider;
        recipe.PreferredModel = snapshot.PreferredModel;
        recipe.PreferredSize = snapshot.PreferredSize;
        recipe.ExportDefaultsJson = snapshot.ExportDefaultsJson;
        recipe.Notes = snapshot.Notes;
    }

    private static string NormalizeRecipeVersionSource(string? source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            _ => "user",
        };

    private static ImageProviderReference ToProviderReference(ArtAsset asset) =>
        new(asset.FileName, asset.ContentType, asset.Data);

    private static int NormalizeToolLimit(int? limit, int defaultValue, int maxValue) =>
        Math.Clamp(limit is int value && value > 0 ? value : defaultValue, 1, maxValue);

    private static bool TryParseAssetKind(string? kind, out ArtAssetKind assetKind)
    {
        assetKind = default;
        if (string.IsNullOrWhiteSpace(kind))
            return false;

        return Enum.TryParse(kind.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out assetKind);
    }

    private static bool TryParseBatchStatus(string? status, out GenerationBatchStatus batchStatus)
    {
        batchStatus = default;
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return Enum.TryParse(status.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out batchStatus);
    }

    private static object CompactAsset(ArtAsset asset) => new
    {
        asset.Id,
        asset.Label,
        fileName = string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
        asset.Kind,
        asset.ContentType,
        asset.Width,
        asset.Height,
        asset.ParentAssetId,
        asset.SourceBatchId,
        batchOutputIndex = ReadBatchOutputIndex(asset),
        asset.SourcePromptRecipeId,
        asset.SourcePromptRecipeVersion,
        asset.IsFavorite,
        asset.Notes,
        promptPreview = Preview(asset.Prompt, 240),
        asset.CreatedAt,
    };

    private static object AssetDetail(ArtAsset asset) => new
    {
        asset.Id,
        asset.Label,
        fileName = string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
        asset.Kind,
        asset.ContentType,
        asset.Width,
        asset.Height,
        asset.ParentAssetId,
        asset.SourceBatchId,
        batchOutputIndex = ReadBatchOutputIndex(asset),
        asset.SourcePromptRecipeId,
        asset.SourcePromptRecipeVersion,
        asset.IsFavorite,
        asset.Notes,
        asset.Prompt,
        asset.SourceMetadataJson,
        asset.CreatedAt,
        asset.UpdatedAt,
    };

    private static object CompactRecipe(PromptRecipe recipe, int currentVersion) => new
    {
        recipe.Id,
        recipe.Name,
        currentVersion,
        useCase = recipe.AssetType,
        promptPreview = Preview(recipe.PromptTemplate, 320),
        styleRuleCount = DeserializeStrings(recipe.StyleRulesJson).Count,
        avoidRuleCount = DeserializeStrings(recipe.AvoidRulesJson).Count,
        recipe.ExampleAssetId,
        recipe.PreferredProvider,
        recipe.PreferredModel,
        recipe.PreferredSize,
        notesPreview = Preview(recipe.Notes, 220),
        recipe.CreatedAt,
    };

    private static object CompactAnimationRecipe(AnimationRecipe recipe) => new
    {
        recipe.Id,
        recipe.Name,
        recipe.CurrentVersion,
        recipe.AnimationKind,
        recipe.Facing,
        recipe.FrameCount,
        frameOrder = DeserializeFrameOrder(recipe.FrameOrderJson, recipe.FrameCount),
        recipe.Fps,
        recipe.Loop,
        recipe.GuideAssetId,
        guideAssetKind = recipe.GuideAsset?.Kind.ToString() ?? string.Empty,
        guideAssetValid = recipe.GuideAssetId is null || recipe.GuideAsset?.Kind == ArtAssetKind.SpriteGuide,
        expectedFrameBoxCount = DeserializeFrameBoxes(recipe.ExpectedFrameBoxesJson).Count,
        recipe.AnchorStrategy,
        promptPreview = Preview(recipe.PromptScaffold, 320),
        recipe.PrimaryExampleSpriteSheetId,
        notesPreview = Preview(recipe.Notes, 220),
        recipe.CreatedAt,
    };

    private static object CompactBatch(GenerationBatch batch, IReadOnlyList<Guid> outputAssetIds) => new
    {
        batch.Id,
        batch.Label,
        batch.Status,
        batch.ImageModel,
        batch.Size,
        background = NormalizeBackground(batch.Background),
        batch.Count,
        inputAssetIds = DeserializeIds(batch.InputAssetIdsJson),
        inputMaskIds = DeserializeIds(batch.InputMaskIdsJson),
        outputAssetIds,
        batch.ParentBatchId,
        batch.PromptRecipeId,
        batch.PromptRecipeVersion,
        promptPreview = Preview(batch.Prompt, 360),
        negativePromptPreview = Preview(batch.NegativePrompt, 220),
        batch.Error,
        batch.CreatedAt,
    };

    private static object CompactSpriteSheet(SpriteSheetDefinition spriteSheet, int frameCount) => new
    {
        spriteSheet.Id,
        spriteSheet.SourceAssetId,
        workingAssetId = spriteSheet.OutputAssetId,
        spriteSheet.Label,
        spriteSheet.Rows,
        spriteSheet.Columns,
        spriteSheet.CellWidth,
        spriteSheet.CellHeight,
        spriteSheet.Padding,
        spriteSheet.Gutter,
        fps = spriteSheet.Fps,
        loop = spriteSheet.Loop,
        horizontalAnchor = NormalizeHorizontalAnchor(spriteSheet.HorizontalAnchor),
        verticalAnchor = NormalizeVerticalAnchor(spriteSheet.VerticalAnchor),
        background = BackgroundOrDefault(spriteSheet),
        frameCount,
        stabilization = CompactStabilization(DeserializeStabilization(spriteSheet.StabilizationJson)),
        spriteSheet.UpdatedAt,
    };

    private static object CompactAsset(ArtAssetView asset) => new
    {
        asset.Id,
        asset.Label,
        kind = asset.Kind.ToString(),
        asset.Width,
        asset.Height,
        asset.ParentAssetId,
        asset.SourceBatchId,
        asset.IsFavorite,
        asset.SourcePromptRecipeId,
        asset.SourcePromptRecipeVersion,
        asset.Notes,
    };

    private static object CompactSpriteSheet(SpriteSheetDefinitionView spriteSheet) => new
    {
        spriteSheet.Id,
        spriteSheet.SourceAssetId,
        spriteSheet.WorkingAssetId,
        spriteSheet.Label,
        spriteSheet.Rows,
        spriteSheet.Columns,
        spriteSheet.CellWidth,
        spriteSheet.CellHeight,
        spriteSheet.Padding,
        spriteSheet.Gutter,
        fps = spriteSheet.Fps,
        loop = spriteSheet.Loop,
        background = spriteSheet.Background,
        stabilization = CompactStabilization(spriteSheet.Stabilization),
        frames = spriteSheet.Frames.Select(frame => new
        {
            frame.Id,
            frame.Index,
            frame.Label,
            frame.SourceRect,
            frame.SourceImageAssetId,
            frame.SourceImageRect,
            frame.CellRect,
            frame.SpriteRect,
            hasShapePaths = HasShapePaths(frame.ShapePaths),
            shapePathCount = ShapePathCount(frame.ShapePaths),
            shapePointCount = ShapePointCount(frame.ShapePaths),
            frame.PreviewWidth,
            frame.PreviewHeight,
            frame.WorkingState,
            frame.WorkingWidth,
            frame.WorkingHeight,
            frame.WorkingMargin,
            frame.WorkingUpdatedAt,
            translation = new { x = frame.TranslationX, y = frame.TranslationY },
        }),
    };

    private static object? CompactStabilization(SpriteSheetStabilizationView? stabilization)
    {
        if (stabilization is null)
            return null;
        var lowConfidence = stabilization.Matches.Count(match => match.LowConfidence);
        return new
        {
            stabilization.ReferenceFrameNumber,
            stabilization.ReferenceFrameIndex,
            stabilization.AnchorRect,
            stabilization.ReferenceWorkingAnchorRect,
            stabilization.ReferenceAnchorCenter,
            stabilization.NormalizedWidth,
            stabilization.NormalizedHeight,
            stabilization.SearchPadding,
            stabilization.MinScore,
            stabilization.Applied,
            stabilization.RequiresReassembly,
            stabilization.UpdatedAt,
            matchCount = stabilization.Matches.Count,
            lowConfidenceCount = lowConfidence,
            clippedCount = stabilization.Matches.Count(match => match.Clipped),
            warnings = stabilization.Warnings,
            matches = stabilization.Matches.Select(match => new
            {
                match.FrameNumber,
                match.Index,
                match.SourceRect,
                match.InputFrameRect,
                match.MatchedAnchorRect,
                match.PlacementRect,
                match.DeltaX,
                match.DeltaY,
                match.Score,
                match.LowConfidence,
                match.Clipped,
                match.Warnings,
            }),
        };
    }

    private static bool HasShapePaths(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        ShapePathCount(shapePaths) > 0;

    private static int ShapePathCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths.Count(path => path.Points.Count >= 3);

    private static int ShapePointCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths
            .Where(path => path.Points.Count >= 3)
            .Sum(path => path.Points.Count);

    private static string Preview(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private static ProjectView ProjectView(Project project) =>
        new(
            project.Id,
            project.Name,
            project.ActiveWorkspaceMode,
            project.ActiveBatchId,
            project.ActiveSpriteSheetId,
            project.ActiveFrameSetId,
            string.IsNullOrWhiteSpace(project.ActiveSpriteMode) ? "source" : project.ActiveSpriteMode,
            project.ActiveSpriteSourceAssetId,
            project.ActiveSpriteFrameId,
            string.IsNullOrWhiteSpace(project.ActiveSpriteRegionIdsJson) ? "[]" : project.ActiveSpriteRegionIdsJson,
            project.UpdatedAt);

    private static ArtAssetView AssetView(ArtAsset asset) =>
        AssetView(AssetListItem(asset));

    private static ArtAssetView AssetView(ArtAssetListItem asset) =>
        new(
            asset.Id,
            asset.Label,
            string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
            asset.Kind,
            asset.ContentType,
            AssetPreviewImageUrl(asset.ProjectId, asset.Id, asset.UpdatedAt),
            AssetFullImageUrl(asset.ProjectId, asset.Id, asset.UpdatedAt),
            asset.Width,
            asset.Height,
            asset.ParentAssetId,
            asset.SourceBatchId,
            ReadBatchOutputIndex(asset),
            asset.SourcePromptRecipeId,
            asset.SourcePromptRecipeVersion,
            asset.IsFavorite,
            asset.Notes,
            asset.Prompt,
            asset.CreatedAt);

    private static ArtAssetExportView ExportAssetView(ArtAsset asset) =>
        new(
            asset.Id,
            asset.Label,
            string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
            asset.Kind,
            asset.ContentType,
            DataUrl.ToDataUrl(asset.ContentType, asset.Data),
            asset.Width,
            asset.Height);

    private static SpriteSheetDefinitionView SpriteSheetView(
        SpriteSheetDefinition spriteSheet,
        IReadOnlyList<SpriteSheetFrameRecord> frames) =>
        SpriteSheetViewFromListItems(spriteSheet, frames.Select(ToSpriteSheetFrameListItem).ToList());

    private static SpriteSheetDefinitionView SpriteSheetViewFromListItems(
        SpriteSheetDefinition spriteSheet,
        IReadOnlyList<SpriteSheetFrameListItem> frames) =>
        new(
            spriteSheet.Id,
            spriteSheet.SourceAssetId,
            spriteSheet.OutputAssetId,
            spriteSheet.Label,
            spriteSheet.Rows,
            spriteSheet.Columns,
            spriteSheet.CellWidth,
            spriteSheet.CellHeight,
            spriteSheet.Padding,
            spriteSheet.Gutter,
            spriteSheet.Fps,
            spriteSheet.Loop,
            NormalizeHorizontalAnchor(spriteSheet.HorizontalAnchor),
            NormalizeVerticalAnchor(spriteSheet.VerticalAnchor),
            BackgroundOrDefault(spriteSheet),
            frames
                .OrderBy(frame => frame.Index)
                .Select(frame => FrameRecordView(spriteSheet, frame))
                .ToList(),
            DeserializeStabilization(spriteSheet.StabilizationJson),
            spriteSheet.CreatedAt,
            spriteSheet.UpdatedAt);

    private static BackgroundRemovalExportCacheView BackgroundRemovalCacheView(BackgroundRemovalExportCache cache) =>
        new(
            cache.Id,
            cache.AssetId,
            cache.SourceImageSha256,
            cache.RemovalMethod,
            cache.ModelName,
            cache.RembgPackageVersion,
            cache.AlphaMatting,
            cache.OptionsHash,
            cache.ContentType,
            DataUrl.ToDataUrl(cache.ContentType, cache.Data),
            cache.TransparentPixels,
            cache.SemiTransparentPixels,
            cache.OpaquePixels,
            cache.ActualBackend,
            cache.CreatedAt,
            cache.UpdatedAt);

    private static ExportStepCacheView ExportStepCacheView(ExportStepCache step) =>
        new(
            step.Id,
            step.AssetId,
            step.SourceImageSha256,
            step.StepIndex,
            step.ParentImageSha256,
            step.OutputImageSha256,
            step.Method,
            step.OptionsHash,
            step.ModelName,
            step.ActualBackend,
            step.ContentType,
            DataUrl.ToDataUrl(step.ContentType, step.Data),
            step.Width,
            step.Height,
            step.CreatedAt,
            step.UpdatedAt);

    private static BackgroundRemovalExportCacheRequest NormalizeBackgroundRemovalCacheRequest(BackgroundRemovalExportCacheRequest request) =>
        new(
            NormalizeCacheString(request.RemovalMethod, "local-ai"),
            NormalizeCacheString(request.ModelName, "birefnet-massive"),
            NormalizeCacheString(request.RembgPackageVersion, "unknown"),
            request.AlphaMatting,
            NormalizeCacheString(request.OptionsHash, "default"));

    private static List<SpriteSheetFrameView> NormalizeSpriteSheetFrameSaves(
        IReadOnlyList<SpriteSheetFrameView> frames,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight)
    {
        var maxFrames = checked(rows * columns);
        if (frames.Count > maxFrames)
            throw new InvalidOperationException("Sprite frame count exceeds the configured sheet grid.");

        return frames
            .Select((frame, index) =>
            {
                var row = index / columns;
                var column = index % columns;
                var fallbackCell = new SpriteSheetRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                return new SpriteSheetFrameView(
                    index,
                    string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label.Trim(),
                    NormalizeSourceSpriteRect(frame.SourceRect),
                    NormalizeShapePaths(frame.ShapePaths),
                    NormalizeSpriteRect(frame.CellRect, fallbackCell),
                    NormalizeSpriteRect(frame.SpriteRect, fallbackCell),
                    frame.PreviewPngDataUrl,
                    frame.SourceImageAssetId,
                    frame.SourceImageRect is null ? null : NormalizeSourceSpriteRect(frame.SourceImageRect));
            })
            .ToList();
    }

    private static SpriteSheetRect NormalizeSourceSpriteRect(SpriteSheetRect rect) =>
        new(
            Math.Clamp(rect.X, -32768, 32767),
            Math.Clamp(rect.Y, -32768, 32767),
            Math.Clamp(rect.Width, 1, 32767),
            Math.Clamp(rect.Height, 1, 32767));

    private static SpriteSheetRect NormalizeSpriteRect(SpriteSheetRect rect) =>
        new(
            Math.Max(0, rect.X),
            Math.Max(0, rect.Y),
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height));

    private static SpriteSheetRect NormalizeSpriteRect(SpriteSheetRect rect, SpriteSheetRect fallback) =>
        rect.Width <= 0 || rect.Height <= 0 ? fallback : NormalizeSpriteRect(rect);

    private static IReadOnlyList<SpriteSheetShapePath> NormalizeShapePaths(IReadOnlyList<SpriteSheetShapePath>? paths)
    {
        if (paths is null || paths.Count == 0)
            return [];

        return paths
            .Select(path => new SpriteSheetShapePath(
                (path.Points ?? [])
                .Select(point => new SpriteSheetPoint(
                    Math.Clamp(point.X, 0, 32767),
                    Math.Clamp(point.Y, 0, 32767)))
                .ToList()))
            .Where(path => path.Points.Count >= 3)
            .ToList();
    }

    private static IReadOnlyList<SpriteSheetShapePath> DeserializeShapePaths(string? shapeJson)
    {
        if (string.IsNullOrWhiteSpace(shapeJson))
            return [];

        try
        {
            return NormalizeShapePaths(JsonSerializer.Deserialize<List<SpriteSheetShapePath>>(shapeJson, JsonOptions));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int NormalizeFrameCount(int frameCount, int expectedBoxCount) =>
        Math.Clamp(frameCount > 0 ? frameCount : expectedBoxCount, 1, 120);

    private static string SerializeFrameOrder(IReadOnlyList<int>? frameOrder, int frameCount) =>
        JsonSerializer.Serialize(NormalizeFrameOrder(frameOrder, frameCount), JsonOptions);

    private static IReadOnlyList<int> DeserializeFrameOrder(string? value, int frameCount)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultFrameOrder(frameCount);

        try
        {
            return NormalizeFrameOrder(JsonSerializer.Deserialize<List<int>>(value, JsonOptions), frameCount);
        }
        catch (JsonException)
        {
            return DefaultFrameOrder(frameCount);
        }
    }

    private static IReadOnlyList<int> NormalizeFrameOrder(IReadOnlyList<int>? frameOrder, int frameCount)
    {
        var count = NormalizeFrameCount(frameCount, 0);
        var order = (frameOrder ?? [])
            .Where(index => index >= 0 && index < count)
            .Distinct()
            .ToList();
        if (order.Count == 0)
            return DefaultFrameOrder(count);

        foreach (var index in DefaultFrameOrder(count))
        {
            if (!order.Contains(index))
                order.Add(index);
        }

        return order;
    }

    private static IReadOnlyList<int> DefaultFrameOrder(int frameCount) =>
        Enumerable.Range(0, NormalizeFrameCount(frameCount, 0)).ToList();

    private static string SerializeFrameBoxes(IReadOnlyList<SpriteSheetRect>? boxes) =>
        JsonSerializer.Serialize(NormalizeFrameBoxes(boxes), JsonOptions);

    private static IReadOnlyList<SpriteSheetRect> DeserializeFrameBoxes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return NormalizeFrameBoxes(JsonSerializer.Deserialize<List<SpriteSheetRect>>(value, JsonOptions));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SpriteSheetRect> NormalizeFrameBoxes(IReadOnlyList<SpriteSheetRect>? boxes)
    {
        if (boxes is null || boxes.Count == 0)
            return [];

        return boxes
            .Where(box => box.Width > 0 && box.Height > 0)
            .Select(NormalizeSpriteRect)
            .ToList();
    }

    private static string NormalizeHorizontalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center",
        };

    private static string NormalizeVerticalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "top" => "top",
            "middle" or "center" => "middle",
            _ => "bottom",
        };

    private static double PivotForHorizontalAnchor(string? value) =>
        NormalizeHorizontalAnchor(value) switch
        {
            "left" => 0d,
            "right" => 1d,
            _ => 0.5d,
        };

    private static double PivotForVerticalAnchor(string? value) =>
        NormalizeVerticalAnchor(value) switch
        {
            "top" => 0d,
            "middle" => 0.5d,
            _ => 1d,
        };

    private static string NormalizeCacheString(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static GenerationBatchView BatchView(GenerationBatch batch, IReadOnlyList<ArtAsset> assets) =>
        BatchView(ToGenerationBatchListItem(batch), assets.Select(AssetListItem).ToList());

    private static GenerationBatchView BatchView(GenerationBatchListItem batch, IReadOnlyList<ArtAssetListItem> assets)
    {
        var outputAssets = assets
            .Where(a => a.SourceBatchId == batch.Id)
            .OrderBy(a => ReadBatchOutputIndex(a) ?? int.MaxValue)
            .ThenBy(a => a.CreatedAt)
            .ToList();
        var outputIndexes = outputAssets
            .Select(ReadBatchOutputIndex)
            .OfType<int>()
            .Where(index => index >= 0 && index < batch.Count)
            .ToHashSet();
        var outputStates = NormalizeOutputStates(batch.OutputStatesJson, batch.Count, outputIndexes);
        var outputErrors = MergeOutputErrors(
            NormalizeOutputErrors(batch.OutputErrorsJson, batch.Count, outputIndexes),
            outputStates
                .Where(state => IsFailedOutputStatus(state.Status) && !outputIndexes.Contains(state.OutputIndex))
                .Select(OutputErrorFromState));
        var displayError = outputErrors.Count > 0
            ? OutputErrorSummary(outputErrors.Count, batch.Count)
            : batch.Error;

        return new(
            batch.Id,
            batch.Label,
            batch.Provider,
            batch.MainlineModel,
            batch.ImageModel,
            batch.Prompt,
            batch.NegativePrompt,
            batch.Size,
            NormalizeBackground(batch.Background),
            batch.Count,
            DeserializeIds(batch.InputAssetIdsJson),
            DeserializeIds(batch.InputMaskIdsJson),
            outputAssets.Select(a => a.Id).ToList(),
            batch.ParentBatchId,
            batch.PromptRecipeId,
            batch.PromptRecipeVersion,
            batch.Status,
            displayError,
            outputStates,
            outputErrors,
            batch.CreatedAt);
    }

    private static PromptRecipeView RecipeView(PromptRecipe recipe, int currentVersion) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.AssetType,
            recipe.PromptTemplate,
            DeserializeStrings(recipe.StyleRulesJson),
            DeserializeStrings(recipe.AvoidRulesJson),
            recipe.ExampleAssetId,
            recipe.PreferredProvider,
            recipe.PreferredModel,
            recipe.PreferredSize,
            recipe.Notes,
            currentVersion,
            recipe.CreatedAt);

    private static PromptRecipeVersionView PromptRecipeVersionView(PromptRecipeVersion version) =>
        new(
            version.Id,
            version.RecipeId,
            version.Version,
            version.Name,
            version.Source,
            version.ChangeSummary,
            version.ExampleAssetId,
            version.CreatedAt);

    private static AnimationRecipeView AnimationRecipeView(AnimationRecipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.AnimationKind,
            recipe.Facing,
            recipe.FrameCount,
            DeserializeFrameOrder(recipe.FrameOrderJson, recipe.FrameCount),
            recipe.Fps,
            recipe.Loop,
            recipe.GuideAssetId,
            DeserializeFrameBoxes(recipe.ExpectedFrameBoxesJson),
            recipe.AnchorStrategy,
            recipe.PromptScaffold,
            recipe.ExportDefaultsJson,
            recipe.Notes,
            recipe.PrimaryExampleSpriteSheetId,
            recipe.CurrentVersion,
            recipe.CreatedAt,
            recipe.UpdatedAt,
            recipe.GuideAsset?.Kind.ToString() ?? string.Empty,
            recipe.GuideAssetId is null || recipe.GuideAsset?.Kind == ArtAssetKind.SpriteGuide);

    private static AnimationRecipeVersionView AnimationRecipeVersionView(AnimationRecipeVersion version) =>
        new(
            version.Id,
            version.AnimationRecipeId,
            version.Version,
            version.Name,
            version.AnimationKind,
            version.Facing,
            version.FrameCount,
            DeserializeFrameOrder(version.FrameOrderJson, version.FrameCount),
            version.Fps,
            version.Loop,
            version.GuideAssetId,
            DeserializeFrameBoxes(version.ExpectedFrameBoxesJson),
            version.AnchorStrategy,
            version.PromptScaffold,
            version.ExportDefaultsJson,
            version.Notes,
            version.PrimaryExampleSpriteSheetId,
            version.Source,
            version.ChangeSummary,
            version.CreatedAt);

    private static ImageMaskView MaskView(ImageMask mask) =>
        MaskView(ToImageMaskListItem(mask));

    private static ImageMaskView MaskView(ImageMaskListItem mask) =>
        new(
            mask.Id,
            mask.AssetId,
            mask.Label,
            mask.ContentType,
            MaskImageUrl(mask.ProjectId, mask.Id, mask.UpdatedAt),
            mask.Width,
            mask.Height,
            mask.CreatedAt);

    private static ChatContextAttachmentView AttachmentView(ChatContextAttachment attachment) =>
        new(attachment.Id, attachment.Type, attachment.RefId, attachment.Label, attachment.SortOrder);

    private static CompareReviewSetView CompareReviewSetView(CompareReviewSet reviewSet, IReadOnlyList<CompareReviewSetItem> items) =>
        new(
            reviewSet.Id,
            reviewSet.Title,
            reviewSet.Summary,
            items
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedAt)
                .Select(item => new CompareReviewSetItemView(
                    item.Id,
                    item.Kind,
                    item.RefId,
                    item.Label,
                    item.Notes,
                    item.SortOrder,
                    item.CreatedAt))
                .ToList(),
            reviewSet.UpdatedAt);

    private async Task<SpriteSheetDefinitionView> LoadSpriteSheetViewAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken)
    {
        var sheet = await db.SpriteSheetDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == spriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        var frames = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(frame => frame.Index)
            .Select(frame => new SpriteSheetFrameListItem(
                frame.Id,
                frame.ProjectId,
                frame.SpriteSheetDefinitionId,
                frame.Index,
                frame.Label,
                frame.SourceX,
                frame.SourceY,
                frame.SourceWidth,
                frame.SourceHeight,
                frame.ShapeJson,
                frame.SourceImageAssetId,
                frame.SourceImageX,
                frame.SourceImageY,
                frame.SourceImageWidth,
                frame.SourceImageHeight,
                frame.CellX,
                frame.CellY,
                frame.CellWidth,
                frame.CellHeight,
                frame.SpriteX,
                frame.SpriteY,
                frame.SpriteWidth,
                frame.SpriteHeight,
                frame.PreviewWidth,
                frame.PreviewHeight,
                frame.WorkingState,
                 frame.WorkingWidth,
                 frame.WorkingHeight,
                 frame.WorkingMargin,
                 frame.WorkingUpdatedAt,
                 frame.PoseName,
                 frame.Phase,
                 frame.RootOffsetX,
                 frame.RootOffsetY,
                 frame.DurationMs,
                 frame.FootContactsJson,
                 frame.IsKeyframe,
                 frame.PivotX,
                 frame.PivotY,
                 frame.AppliedScale,
                 frame.TranslationX,
                 frame.TranslationY,
                 frame.QaStatus,
                 frame.RepairHistoryJson,
                 frame.UpdatedAt))
            .ToListAsync(cancellationToken);
        return SpriteSheetViewFromListItems(sheet, frames);
    }

    private static SpriteSheetFrameRecordView FrameRecordView(SpriteSheetDefinition spriteSheet, SpriteSheetFrameRecord frame) =>
        FrameRecordView(spriteSheet, ToSpriteSheetFrameListItem(frame));

    private static SpriteSheetFrameRecordView FrameRecordView(SpriteSheetDefinition spriteSheet, SpriteSheetFrameListItem frame) =>
        new(
            frame.Id,
            spriteSheet.Id,
            frame.Index,
            string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
            RectViewPreserveOrigin(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
            DeserializeShapePaths(frame.ShapeJson),
            RectView(frame.CellX, frame.CellY, frame.CellWidth, frame.CellHeight),
            RectView(frame.SpriteX, frame.SpriteY, frame.SpriteWidth, frame.SpriteHeight),
            SpriteFramePreviewImageUrl(frame.ProjectId, frame.Id, frame.UpdatedAt),
            frame.PreviewWidth,
            frame.PreviewHeight,
            NormalizeFrameWorkingState(frame.WorkingState),
            frame.WorkingWidth,
            frame.WorkingHeight,
            frame.WorkingMargin,
            frame.WorkingUpdatedAt,
            frame.DurationMs > 0 ? Math.Round(frame.DurationMs / 1000d, 6) : FrameDuration(spriteSheet.Fps),
            frame.SourceImageAssetId,
            SourceImageRectView(frame),
            frame.PoseName,
            frame.Phase,
            frame.RootOffsetX,
            frame.RootOffsetY,
            frame.DurationMs,
            DeserializeStrings(frame.FootContactsJson),
            frame.IsKeyframe,
            frame.PivotX,
            frame.PivotY,
            frame.AppliedScale,
            frame.TranslationX,
            frame.TranslationY,
            frame.QaStatus,
            DeserializeStrings(frame.RepairHistoryJson));

    private static SpriteSheetRect RectView(int x, int y, int width, int height) =>
        new(Math.Max(0, x), Math.Max(0, y), Math.Max(1, width), Math.Max(1, height));

    private static SpriteSheetRect RectViewPreserveOrigin(int x, int y, int width, int height) =>
        new(x, y, Math.Max(1, width), Math.Max(1, height));

    private static SpriteSheetRect? SourceImageRectView(SpriteSheetFrameRecord frame) =>
        frame.SourceImageAssetId is null || frame.SourceImageWidth <= 0 || frame.SourceImageHeight <= 0
            ? null
            : RectViewPreserveOrigin(frame.SourceImageX, frame.SourceImageY, frame.SourceImageWidth, frame.SourceImageHeight);

    private static SpriteSheetRect? SourceImageRectView(SpriteSheetFrameListItem frame) =>
        frame.SourceImageAssetId is null || frame.SourceImageWidth <= 0 || frame.SourceImageHeight <= 0
            ? null
            : RectViewPreserveOrigin(frame.SourceImageX, frame.SourceImageY, frame.SourceImageWidth, frame.SourceImageHeight);

    private int ClampCount(int count) =>
        Math.Clamp(count <= 0 ? 1 : count, 1, Math.Max(1, imageOptions.Value.MaxOutputs));

    private static double FrameDuration(int fps) =>
        Math.Round(1d / Math.Clamp(fps, 1, 60), 6);

    private static string NormalizeSize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim();

    private static string NormalizeBackground(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "removable" or "removablecolor" or "removable-color" or "transparent" or "chroma" or "chromakey" or "chroma-key" => "removable",
            "opaque" => "opaque",
            "auto" => "auto",
            _ => "auto",
        };

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string CleanRequired(string value, string error)
    {
        var trimmed = Clean(value);
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException(error);
        return trimmed;
    }

    private static string NormalizeJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "{}";

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Serialize(document.RootElement, JsonOptions)
                : "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static string CleanSpriteSheetLabel(string value, string sourceLabel)
    {
        var trimmed = Clean(value);
        if (!string.IsNullOrWhiteSpace(trimmed))
            return trimmed;

        return string.IsNullOrWhiteSpace(sourceLabel)
            ? "Sprite sheet"
            : $"{sourceLabel} sprite sheet";
    }

    private static string? NormalizeImageContentType(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "image/jpg" => "image/jpeg",
            _ => null,
        };

    private static string ExtensionForContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";

    private static string AssetPreviewImageUrl(Guid projectId, Guid assetId, DateTime updatedAt) =>
        $"/media/projects/{projectId:D}/assets/{assetId:D}/preview{VersionQuery(updatedAt)}";

    private static string AssetFullImageUrl(Guid projectId, Guid assetId, DateTime updatedAt) =>
        $"/media/projects/{projectId:D}/assets/{assetId:D}/full{VersionQuery(updatedAt)}";

    private static string MaskImageUrl(Guid projectId, Guid maskId, DateTime updatedAt) =>
        $"/media/projects/{projectId:D}/masks/{maskId:D}{VersionQuery(updatedAt)}";

    private static string SpriteFramePreviewImageUrl(Guid projectId, Guid frameId, DateTime updatedAt) =>
        $"/media/projects/{projectId:D}/sprite-frames/{frameId:D}/preview{VersionQuery(updatedAt)}";

    private static string VersionQuery(DateTime updatedAt)
    {
        var utc = updatedAt.Kind == DateTimeKind.Utc ? updatedAt : DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc);
        return $"?v={utc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string FileNameForAsset(ArtAsset asset, string suffix)
    {
        var fileName = string.IsNullOrWhiteSpace(asset.FileName)
            ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}"
            : asset.FileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = "." + ExtensionForContentType(asset.ContentType);
        return CleanFileName($"{stem}-{suffix}", $"asset-{suffix}") + extension;
    }

    private static string ChatVisualFileName(AssistantMessageVisual visual, bool preview)
    {
        var fileName = string.IsNullOrWhiteSpace(visual.FileName)
            ? $"chat-visual-{visual.Id:N}.{ExtensionForContentType(visual.ContentType)}"
            : visual.FileName;
        if (!preview)
            return CleanFileName(Path.GetFileNameWithoutExtension(fileName), $"chat-visual-{visual.Id:N}")
                + (Path.GetExtension(fileName) is { Length: > 0 } extension
                    ? extension
                    : "." + ExtensionForContentType(visual.ContentType));

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var previewExtension = visual.ThumbnailData is { Length: > 0 }
            ? ".png"
            : Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(previewExtension))
            previewExtension = "." + ExtensionForContentType(visual.ContentType);

        return CleanFileName($"{stem}-preview", $"chat-visual-{visual.Id:N}-preview") + previewExtension;
    }

    private static string CleanFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((string.IsNullOrWhiteSpace(value) ? fallback : value.Trim())
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static bool TryBuildAssetThumbnail(byte[] sourceData, out byte[] thumbnailData)
    {
        const int maxDimension = 384;
        thumbnailData = [];
        if (!SpriteSheetPngCodec.TryReadRgba(sourceData, out var width, out var height, out var rgba))
            return false;
        if (width <= 0 || height <= 0 || (width <= maxDimension && height <= maxDimension))
            return false;

        var scale = Math.Min(maxDimension / (double)width, maxDimension / (double)height);
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        var target = new byte[targetWidth * targetHeight * 4];
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)(y * (height / (double)targetHeight)));
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(width - 1, (int)(x * (width / (double)targetWidth)));
                var sourceIndex = ((sourceY * width) + sourceX) * 4;
                var targetIndex = ((y * targetWidth) + x) * 4;
                target[targetIndex] = rgba[sourceIndex];
                target[targetIndex + 1] = rgba[sourceIndex + 1];
                target[targetIndex + 2] = rgba[sourceIndex + 2];
                target[targetIndex + 3] = rgba[sourceIndex + 3];
            }
        }

        thumbnailData = SpriteSheetPngCodec.EncodeRgba(targetWidth, targetHeight, target);
        return thumbnailData.Length > 0;
    }

    private static string LabelForIndex(int index)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return index >= 0 && index < alphabet.Length
            ? alphabet[index].ToString()
            : (index + 1).ToString();
    }

    private static string SerializeIds(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.ToList(), JsonOptions);

    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList(), JsonOptions);

    private static string SerializeOutputErrors(IEnumerable<GenerationOutputErrorView> errors) =>
        JsonSerializer.Serialize(errors
            .Where(error => error.OutputIndex >= 0)
            .Select(CleanOutputError)
            .Where(error => !string.IsNullOrWhiteSpace(error.Error))
            .GroupBy(error => error.OutputIndex)
            .Select(group => group.Last())
            .OrderBy(error => error.OutputIndex)
            .ToList(), JsonOptions);

    private static string SerializeOutputStates(IEnumerable<GenerationOutputStateView> states) =>
        JsonSerializer.Serialize(states
            .Where(state => state.OutputIndex >= 0)
            .Select(CleanOutputState)
            .GroupBy(state => state.OutputIndex)
            .Select(group => group.Last())
            .OrderBy(state => state.OutputIndex)
            .ToList(), JsonOptions);

    private static string OutputErrorSummary(int errorCount, int requestedCount) =>
        $"{errorCount} of {requestedCount} image request(s) failed.";

    private static string TruncateForLog(string value, int maxChars) =>
        string.IsNullOrEmpty(value) || value.Length <= maxChars
            ? value
            : value[..maxChars] + "...";

    private static ArtAssetListItem AssetListItem(ArtAsset asset) =>
        new(
            asset.Id,
            asset.ProjectId,
            asset.Label,
            asset.FileName,
            asset.Kind,
            asset.ContentType,
            asset.Width,
            asset.Height,
            asset.ParentAssetId,
            asset.SourceBatchId,
            asset.SourcePromptRecipeId,
            asset.SourcePromptRecipeVersion,
            asset.IsFavorite,
            asset.Notes,
            asset.Prompt,
            asset.SourceMetadataJson,
            asset.CreatedAt,
            asset.UpdatedAt);

    private static GenerationBatchListItem ToGenerationBatchListItem(GenerationBatch batch) =>
        new(
            batch.Id,
            batch.Label,
            batch.Provider,
            batch.MainlineModel,
            batch.ImageModel,
            batch.Prompt,
            batch.NegativePrompt,
            batch.Size,
            batch.Background,
            batch.Count,
            batch.InputAssetIdsJson,
            batch.InputMaskIdsJson,
            batch.ParentBatchId,
            batch.PromptRecipeId,
            batch.PromptRecipeVersion,
            batch.Status,
            batch.Error,
            batch.OutputErrorsJson,
            batch.OutputStatesJson,
            batch.CreatedAt);

    private static ImageMaskListItem ToImageMaskListItem(ImageMask mask) =>
        new(
            mask.Id,
            mask.ProjectId,
            mask.AssetId,
            mask.Label,
            mask.ContentType,
            mask.Width,
            mask.Height,
            mask.CreatedAt,
            mask.UpdatedAt);

    private static SpriteSheetFrameListItem ToSpriteSheetFrameListItem(SpriteSheetFrameRecord frame) =>
        new(
            frame.Id,
            frame.ProjectId,
            frame.SpriteSheetDefinitionId,
            frame.Index,
            frame.Label,
            frame.SourceX,
            frame.SourceY,
            frame.SourceWidth,
            frame.SourceHeight,
            frame.ShapeJson,
            frame.SourceImageAssetId,
            frame.SourceImageX,
            frame.SourceImageY,
            frame.SourceImageWidth,
            frame.SourceImageHeight,
            frame.CellX,
            frame.CellY,
            frame.CellWidth,
            frame.CellHeight,
            frame.SpriteX,
            frame.SpriteY,
            frame.SpriteWidth,
            frame.SpriteHeight,
            frame.PreviewWidth,
            frame.PreviewHeight,
            frame.WorkingState,
            frame.WorkingWidth,
            frame.WorkingHeight,
            frame.WorkingMargin,
            frame.WorkingUpdatedAt,
            frame.PoseName,
            frame.Phase,
            frame.RootOffsetX,
            frame.RootOffsetY,
            frame.DurationMs,
            frame.FootContactsJson,
            frame.IsKeyframe,
            frame.PivotX,
            frame.PivotY,
            frame.AppliedScale,
            frame.TranslationX,
            frame.TranslationY,
            frame.QaStatus,
            frame.RepairHistoryJson,
            frame.UpdatedAt);

    private static int? ReadBatchOutputIndex(ArtAssetListItem asset) =>
        ReadBatchOutputIndex(asset.SourceMetadataJson) ?? ReadBatchOutputIndexFromLabel(asset.Label);

    private static int? ReadBatchOutputIndex(ArtAsset asset) =>
        ReadBatchOutputIndex(asset.SourceMetadataJson) ?? ReadBatchOutputIndexFromLabel(asset.Label);

    private static int? ReadBatchOutputIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (!root.TryGetProperty("outputIndex", out var outputIndex)
                && !root.TryGetProperty("OutputIndex", out outputIndex))
            {
                return null;
            }

            if (outputIndex.ValueKind == JsonValueKind.Number && outputIndex.TryGetInt32(out var numberValue))
                return numberValue;
            if (outputIndex.ValueKind == JsonValueKind.String && int.TryParse(outputIndex.GetString(), out var stringValue))
                return stringValue;
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static int? ReadBatchOutputIndexFromLabel(string label)
    {
        const string prefix = "Image ";
        var trimmed = Clean(label);
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = trimmed[prefix.Length..].Trim();
        if (token.Length == 1)
        {
            var letter = char.ToUpperInvariant(token[0]);
            if (letter is >= 'A' and <= 'Z')
                return letter - 'A';
        }

        return int.TryParse(token, out var number) && number > 0
            ? number - 1
            : null;
    }

    private static List<GenerationOutputErrorView> NormalizeOutputErrors(
        string value,
        int requestedCount,
        ISet<int>? completedOutputIndexes = null) =>
        DeserializeOutputErrors(value)
            .Where(error => error.OutputIndex >= 0 && error.OutputIndex < requestedCount)
            .Where(error => !string.IsNullOrWhiteSpace(error.Error))
            .Where(error => completedOutputIndexes is null || !completedOutputIndexes.Contains(error.OutputIndex))
            .Select(CleanOutputError)
            .GroupBy(error => error.OutputIndex)
            .Select(group => group.Last())
            .OrderBy(error => error.OutputIndex)
            .ToList();

    private static List<GenerationOutputStateView> NormalizeOutputStates(
        string value,
        int requestedCount,
        ISet<int>? completedOutputIndexes = null)
    {
        var now = DateTime.UtcNow;
        var states = DeserializeOutputStates(value)
            .Where(state => state.OutputIndex >= 0 && state.OutputIndex < requestedCount)
            .Select(CleanOutputState)
            .GroupBy(state => state.OutputIndex)
            .Select(group => group.Last())
            .ToDictionary(state => state.OutputIndex);

        for (var outputIndex = 0; outputIndex < requestedCount; outputIndex++)
        {
            if (completedOutputIndexes is not null && completedOutputIndexes.Contains(outputIndex))
            {
                states[outputIndex] = states.TryGetValue(outputIndex, out var existing)
                    ? existing with
                    {
                        Status = GenerationOutputStatus.Succeeded,
                        Message = "Image saved.",
                        Error = string.Empty,
                        UpdatedAt = existing.UpdatedAt ?? now,
                        CompletedAt = existing.CompletedAt ?? now,
                    }
                    : new GenerationOutputStateView(outputIndex, GenerationOutputStatus.Succeeded, Message: "Image saved.", UpdatedAt: now, CompletedAt: now);
                continue;
            }

            if (!states.ContainsKey(outputIndex))
                states[outputIndex] = new GenerationOutputStateView(outputIndex, GenerationOutputStatus.Queued, Message: "Waiting for earlier image requests.");
        }

        return states.Values
            .OrderBy(state => state.OutputIndex)
            .ToList();
    }

    private static List<Guid> DeserializeIds(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> DeserializeStrings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<GenerationOutputStateView> CreateInitialOutputStates(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new GenerationOutputStateView(index, GenerationOutputStatus.Queued, Message: "Waiting for earlier image requests."))
            .ToList();

    private static List<GenerationOutputStateView> UpsertOutputState(
        IEnumerable<GenerationOutputStateView> states,
        GenerationOutputStateView state) =>
        states
            .Where(item => item.OutputIndex != state.OutputIndex)
            .Append(CleanOutputState(state))
            .OrderBy(item => item.OutputIndex)
            .ToList();

    private static List<GenerationOutputErrorView> MergeOutputErrors(
        IEnumerable<GenerationOutputErrorView> first,
        IEnumerable<GenerationOutputErrorView> second) =>
        first
            .Concat(second)
            .Select(CleanOutputError)
            .Where(error => error.OutputIndex >= 0)
            .Where(error => !string.IsNullOrWhiteSpace(error.Error))
            .GroupBy(error => error.OutputIndex)
            .Select(group => group.Last())
            .OrderBy(error => error.OutputIndex)
            .ToList();

    private static GenerationOutputErrorView OutputErrorFromState(GenerationOutputStateView state) =>
        new(
            state.OutputIndex,
            string.IsNullOrWhiteSpace(state.Error) ? "Image request failed." : state.Error,
            state.ErrorKind,
            state.RequestId,
            state.ResponseId,
            state.CallId,
            LastEventType: state.LastEventType,
            EventCount: state.EventCount);

    private static GenerationOutputErrorView CleanOutputError(GenerationOutputErrorView error) =>
        error with
        {
            Error = Clean(error.Error),
            ErrorKind = CleanNullable(error.ErrorKind),
            RequestId = CleanNullable(error.RequestId),
            ResponseId = CleanNullable(error.ResponseId),
            CallId = CleanNullable(error.CallId),
            LastEventType = CleanNullable(error.LastEventType),
        };

    private static GenerationOutputStateView CleanOutputState(GenerationOutputStateView state) =>
        state with
        {
            Message = Clean(state.Message),
            Error = Clean(state.Error),
            ErrorKind = CleanNullable(state.ErrorKind),
            RequestId = CleanNullable(state.RequestId),
            ResponseId = CleanNullable(state.ResponseId),
            CallId = CleanNullable(state.CallId),
            LastEventType = CleanNullable(state.LastEventType),
        };

    private static bool IsTerminalOutputStatus(GenerationOutputStatus status) =>
        status is GenerationOutputStatus.Succeeded or GenerationOutputStatus.Failed or GenerationOutputStatus.Cancelled;

    private static bool IsFailedOutputStatus(GenerationOutputStatus status) =>
        status is GenerationOutputStatus.Failed or GenerationOutputStatus.Cancelled;

    private static string? CleanNullable(string? value)
    {
        var cleaned = Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static List<GenerationOutputErrorView> DeserializeOutputErrors(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<GenerationOutputErrorView>>(value, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<GenerationOutputStateView> DeserializeOutputStates(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<GenerationOutputStateView>>(value, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ArtAssetListItem(
        Guid Id,
        Guid ProjectId,
        string Label,
        string FileName,
        ArtAssetKind Kind,
        string ContentType,
        int? Width,
        int? Height,
        Guid? ParentAssetId,
        Guid? SourceBatchId,
        Guid? SourcePromptRecipeId,
        int? SourcePromptRecipeVersion,
        bool IsFavorite,
        string Notes,
        string Prompt,
        string SourceMetadataJson,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record GenerationBatchListItem(
        Guid Id,
        string Label,
        string Provider,
        string MainlineModel,
        string ImageModel,
        string Prompt,
        string NegativePrompt,
        string Size,
        string Background,
        int Count,
        string InputAssetIdsJson,
        string InputMaskIdsJson,
        Guid? ParentBatchId,
        Guid? PromptRecipeId,
        int? PromptRecipeVersion,
        GenerationBatchStatus Status,
        string Error,
        string OutputErrorsJson,
        string OutputStatesJson,
        DateTime CreatedAt);

    private sealed record ImageMaskListItem(
        Guid Id,
        Guid ProjectId,
        Guid AssetId,
        string Label,
        string ContentType,
        int Width,
        int Height,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record SpriteSheetFrameListItem(
        Guid Id,
        Guid ProjectId,
        Guid SpriteSheetDefinitionId,
        int Index,
        string Label,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight,
        string ShapeJson,
        Guid? SourceImageAssetId,
        int SourceImageX,
        int SourceImageY,
        int SourceImageWidth,
        int SourceImageHeight,
        int CellX,
        int CellY,
        int CellWidth,
        int CellHeight,
        int SpriteX,
        int SpriteY,
        int SpriteWidth,
        int SpriteHeight,
        int PreviewWidth,
        int PreviewHeight,
        string WorkingState,
        int WorkingWidth,
        int WorkingHeight,
        int WorkingMargin,
        DateTime? WorkingUpdatedAt,
        string PoseName = "",
        double Phase = 0,
        int RootOffsetX = 0,
        int RootOffsetY = 0,
        int DurationMs = 0,
        string FootContactsJson = "[]",
        bool IsKeyframe = false,
        int PivotX = 0,
        int PivotY = 0,
        double AppliedScale = 1,
        int TranslationX = 0,
        int TranslationY = 0,
        string QaStatus = "",
        string RepairHistoryJson = "[]",
        DateTime UpdatedAt = default);

    private sealed record StabilizationTemplate(
        int Width,
        int Height,
        double[] Values,
        double[] Weights,
        double WeightSum,
        double Mean,
        double Denominator,
        IReadOnlyList<string> Warnings);

    private sealed record StabilizationWorkingFrameInput(
        SpriteSheetFrameRecord Record,
        SpriteSheetFrameImageInput Image,
        int SourceOriginX,
        int SourceOriginY);

    private sealed record StabilizationMatchDraft(
        StabilizationWorkingFrameInput Frame,
        SpriteSheetRect MatchedAnchorRect,
        double Score,
        bool LowConfidence,
        IReadOnlyList<string> Warnings);

    private sealed record StabilizationPlacement(
        int DeltaX,
        int DeltaY,
        SpriteSheetRect PlacementRect);

    private sealed record StabilizationPlacementSet(
        int NormalizedWidth,
        int NormalizedHeight,
        int MinDeltaX,
        int MinDeltaY,
        IReadOnlyDictionary<int, StabilizationPlacement> ByIndex);
}
