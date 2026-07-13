using System.Globalization;
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
    private sealed record RecipePromptGuidance(string Prompt);
    private sealed record AnimationRecipePromptGuidance(
        string Name,
        string Prompt);
    private sealed record LayoutProfile(
        double ForegroundAspect,
        double ForegroundCoverage,
        int ForegroundWidth,
        int ForegroundHeight,
        bool NeedsLargeCells,
        bool NeedsTallCells,
        bool NeedsLargePadding);
    private sealed record AnimationGuideRenderDraft(
        ArtAsset? Reference,
        AnimationSpec Spec,
        LayoutSpec Layout,
        byte[] GuidePng,
        byte[] DiagnosticPng,
        MotionGuideRenderResult? MotionRender,
        string Renderer,
        string RenderStyle,
        string Label,
        string PromptScaffold);

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
                a.SourceAnimationRecipeId,
                a.SourceAnimationRecipeVersion,
                a.IsFavorite,
                a.ReviewStatus,
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
                b.AnimationRecipeId,
                b.AnimationRecipeVersion,
                b.Status,
                b.Error,
                b.OutputErrorsJson,
                b.OutputStatesJson,
                b.CreatedAt,
                b.ReviewCompletedBy,
                b.ReviewCompletedAt))
            .ToListAsync(cancellationToken);
        var reviewDecisions = await db.AssetReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.ProjectId == selected.Id)
            .OrderBy(decision => decision.CreatedAt)
            .ThenBy(decision => decision.Id)
            .ToListAsync(cancellationToken);
        var recipes = await db.PromptRecipes
            .AsNoTracking()
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
            .Where(r => r.ProjectId == selected.Id)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var animationRecipes = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
            .Where(r => r.ProjectId == selected.Id)
            .OrderBy(r => r.Name)
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
        var currentDecisions = reviewDecisions
            .GroupBy(decision => decision.AssetId)
            .ToDictionary(group => group.Key, group => group.Last());
        var latestAgentDecisions = reviewDecisions
            .Where(decision => decision.Actor == AssetReviewActor.Assistant && decision.Decision != AssetReviewDecisionKind.Clear)
            .GroupBy(decision => decision.AssetId)
            .ToDictionary(group => group.Key, group => group.Last());
        var assetViews = assets
            .Select(asset => AssetView(
                asset,
                currentDecisions.GetValueOrDefault(asset.Id),
                latestAgentDecisions.GetValueOrDefault(asset.Id)))
            .ToList();
        var batchViews = batches.Select(batch => BatchView(batch, assets)).ToList();
        var recipeViews = recipes
            .Select(recipe => RecipeView(recipe, currentRecipeVersions.GetValueOrDefault(recipe.Id)))
            .ToList();
        var animationRecipeViews = animationRecipes
            .Select(AnimationRecipeView)
            .ToList();
        var maskViews = masks.Select(MaskView).ToList();
        var attachmentViews = attachments.Select(AttachmentView).ToList();

        var providerStatus = await BuildProviderStatusAsync(cancellationToken);
        return new WorkbenchView(
            ProjectView(selected),
            projects.Select(ProjectView).ToList(),
            assetViews.Where(asset => asset.ReviewStatus == AssetReviewStatus.Kept).ToList(),
            assetViews.Where(asset => asset.ReviewStatus == AssetReviewStatus.Pending).ToList(),
            assetViews.Where(asset => asset.ReviewStatus == AssetReviewStatus.Rejected).ToList(),
            batchViews,
            recipeViews,
            animationRecipeViews,
            maskViews,
            attachmentViews,
            compareReviewSet is null ? null : CompareReviewSetView(compareReviewSet, compareReviewItems),
            batchViews.FirstOrDefault(b => b.Id == selected.ActiveBatchId),
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
        string? reviewStatus = null,
        CancellationToken cancellationToken = default)
    {
        var max = NormalizeToolLimit(limit, 12, 50);
        var assets = db.ArtAssets
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId);

        if (!string.Equals(reviewStatus?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var parsedReviewStatus = TryParseAssetReviewStatus(reviewStatus, out var requestedReviewStatus)
                ? requestedReviewStatus
                : AssetReviewStatus.Kept;
            assets = assets.Where(a => a.ReviewStatus == parsedReviewStatus);
        }

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
            note = "Assets default to kept status. Pass reviewStatus pending, rejected, or all to inspect other lifecycle states. Use read_asset with an asset id to inspect the full image.",
        }, JsonOptions);
    }

    public async Task<string> ReadAssetJsonAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return JsonSerializer.Serialize(new
            {
                found = false,
                assetId,
                error = "Asset was not found.",
                note = "The asset id is not available in this project. Call list_assets to refresh available asset ids before retrying.",
                image = new
                {
                    availableToModel = false,
                },
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            found = true,
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
            .Include(r => r.Attachments)
            .Where(r => r.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            recipes = recipes.Where(r =>
                EF.Functions.Like(r.Name, pattern)
                || EF.Functions.Like(r.Prompt, pattern)
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
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
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
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
            .Where(r => r.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            recipes = recipes.Where(r =>
                EF.Functions.Like(r.Name, pattern)
                || EF.Functions.Like(r.Prompt, pattern)
                || EF.Functions.Like(r.Notes, pattern));
        }

        var results = await recipes
            .OrderBy(r => r.Name)
            .Take(max)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            animationRecipes = results.Select(CompactAnimationRecipe),
            returned = results.Count,
            limit = max,
            note = "Use read_animation_recipe with a recipe id for full reusable animation guidance and attachments.",
        }, JsonOptions);
    }

    public async Task<string> ReadAnimationRecipeJsonAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");

        return JsonSerializer.Serialize(AnimationRecipeView(recipe), JsonOptions);
    }

    public Task<IReadOnlyList<MotionClipView>> ListMotionClipsAsync(
        string? query = null,
        string? animationKind = null,
        bool? loop = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = SelectMotionClips(query, animationKind, loop, limit);
        return Task.FromResult<IReadOnlyList<MotionClipView>>(results.Select(MotionClipView).ToList());
    }

    public Task<string> ListMotionClipsJsonAsync(
        string? query = null,
        string? animationKind = null,
        bool? loop = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        var max = NormalizeToolLimit(limit, 50, 100);
        var results = SelectMotionClips(query, animationKind, loop, limit);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            motionClips = results.Select(CompactMotionClip),
            returned = results.Count,
            totalAvailable = catalog.Clips.Count,
            limit = max,
            note = "Pass a returned motionClipId to generate_animation_guide when a GLTF mannequin motion guide should control the sprite-sheet poses.",
        }, JsonOptions));
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
        var decisions = await db.AssetReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.ProjectId == projectId && decision.SourceBatchId == batchId)
            .OrderBy(decision => decision.CreatedAt)
            .ThenBy(decision => decision.Id)
            .ToListAsync(cancellationToken);
        var currentDecisions = decisions.GroupBy(decision => decision.AssetId).ToDictionary(group => group.Key, group => group.Last());
        var agentDecisions = decisions
            .Where(decision => decision.Actor == AssetReviewActor.Assistant && decision.Decision != AssetReviewDecisionKind.Clear)
            .GroupBy(decision => decision.AssetId)
            .ToDictionary(group => group.Key, group => group.Last());

        return JsonSerializer.Serialize(new
        {
            batch = BatchView(batch, outputAssets),
            outputs = outputAssets
                .OrderBy(asset => ReadBatchOutputIndex(asset) ?? int.MaxValue)
                .ThenBy(asset => asset.CreatedAt)
                .Select(asset => AssetView(
                    AssetListItem(asset),
                    currentDecisions.GetValueOrDefault(asset.Id),
                    agentDecisions.GetValueOrDefault(asset.Id))),
        }, JsonOptions);
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
            recipe = await db.PromptRecipes
                .Include(r => r.Attachments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
                ?? throw new InvalidOperationException("Prompt recipe was not found.");
            promptRecipeVersion = await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken);
        }

        AnimationRecipe? animationRecipe = null;
        int? animationRecipeVersion = null;
        if (request.AnimationRecipeId is Guid animationRecipeId)
        {
            animationRecipe = await db.AnimationRecipes
                .Include(r => r.Attachments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == animationRecipeId, cancellationToken)
                ?? throw new InvalidOperationException("Animation recipe was not found.");
            animationRecipeVersion = animationRecipe.CurrentVersion > 0
                ? animationRecipe.CurrentVersion
                : await GetCurrentAnimationRecipeVersionAsync(animationRecipe.Id, cancellationToken);
        }

        var references = await MergeGenerationReferencesAsync(projectId, recipe, animationRecipe, explicitReferences, excludedAssetId: null, cancellationToken);

        var outputLabel = Clean(request.OutputLabel);
        var batch = new GenerationBatch
        {
            ProjectId = projectId,
            Label = string.IsNullOrWhiteSpace(outputLabel)
                ? $"Batch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
                : outputLabel,
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
            AnimationRecipeId = animationRecipe?.Id,
            AnimationRecipeVersion = animationRecipeVersion,
            Status = GenerationBatchStatus.Running,
            OutputStatesJson = SerializeOutputStates(CreateInitialOutputStates(count)),
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Batches;
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
        var animationRecipeGuidance = await LoadAnimationRecipePromptGuidanceForBatchAsync(projectId, batch, cancellationToken);

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
                BuildPrompt(batch.Prompt, batch.NegativePrompt, recipeGuidance, animationRecipeGuidance, batch.Background),
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
        var fallbackLabel = $"Image {LabelForIndex(outputIndex)}";
        var asset = CreateAsset(
            projectId,
            OutputAssetLabel(batch.Label, outputIndex, batch.Count, fallbackLabel),
            $"generated-{DateTime.UtcNow:yyyyMMddHHmmss}-{outputIndex + 1}.{ExtensionForContentType(image.ContentType)}",
            ArtAssetKind.Generated,
            image.ContentType,
            image.Data,
            parentAssetId: null,
            sourceBatchId: batch.Id,
            promptRecipeId: batch.PromptRecipeId,
            promptRecipeVersion: batch.PromptRecipeVersion,
            animationRecipeId: batch.AnimationRecipeId,
            animationRecipeVersion: batch.AnimationRecipeVersion,
            prompt: batch.Prompt,
            metadata: new
            {
                OutputIndex = outputIndex,
                batch.PromptRecipeVersion,
                batch.AnimationRecipeVersion,
                image.RevisedPrompt,
                image.ResponseId,
                image.CallId,
                image.OutputFormat,
                References = references.Select(a => new { a.Id, a.Label, a.ContentType }),
            });
        asset.ReviewStatus = AssetReviewStatus.Pending;
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
            recipe = await db.PromptRecipes
                .Include(r => r.Attachments)
                    .ThenInclude(a => a.Asset)
                .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
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

        var outputLabel = Clean(request.OutputLabel);
        var batch = new GenerationBatch
        {
            Id = batchId,
            ProjectId = projectId,
            Label = string.IsNullOrWhiteSpace(outputLabel)
                ? $"Edit {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
                : outputLabel,
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
        if (request.SwitchToBatches)
            project.ActiveWorkspaceMode = WorkspaceMode.Batches;
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
                BuildPrompt(batch.Prompt, string.Empty, recipeGuidance, animationRecipe: null, background: batch.Background),
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
        var fallbackLabel = $"{sourceAsset.Label} edit {LabelForIndex(outputIndex)}";
        var asset = CreateAsset(
            projectId,
            OutputAssetLabel(batch.Label, outputIndex, batch.Count, fallbackLabel),
            $"edited-{DateTime.UtcNow:yyyyMMddHHmmss}-{outputIndex + 1}.{ExtensionForContentType(image.ContentType)}",
            ArtAssetKind.Edited,
            image.ContentType,
            image.Data,
            sourceAsset.Id,
            batch.Id,
            batch.PromptRecipeId,
            batch.PromptRecipeVersion,
            batch.AnimationRecipeId,
            batch.AnimationRecipeVersion,
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
        asset.ReviewStatus = AssetReviewStatus.Pending;
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
            animationRecipeId: null,
            animationRecipeVersion: null,
            prompt: string.Empty,
            metadata: new { Source = string.IsNullOrWhiteSpace(request.Source) ? "import" : request.Source.Trim() });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        if (request.SwitchToEdit)
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
            parent.SourceAnimationRecipeId,
            parent.SourceAnimationRecipeVersion,
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
            source.SourceAnimationRecipeId,
            source.SourceAnimationRecipeVersion,
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
        var recipe = new PromptRecipe
        {
            ProjectId = projectId,
            Name = CleanRequired(request.Name, "Recipe name is required."),
        };
        ApplyRecipeRequest(recipe, request);
        await db.PromptRecipes.AddAsync(recipe, cancellationToken);
        _ = await AppendPromptRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadPromptRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task<PromptRecipeView> UpdatePromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        UpdatePromptRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        ApplyRecipeRequest(recipe, request);
        recipe.UpdatedAt = DateTime.UtcNow;
        _ = await AppendPromptRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadPromptRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task<PromptRecipeView> DuplicatePromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        string? name = null,
        string source = "user",
        string changeSummary = "",
        CancellationToken cancellationToken = default)
    {
        var sourceRecipe = await db.PromptRecipes
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        var duplicate = new PromptRecipe
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? $"{sourceRecipe.Name} Copy" : name.Trim(),
            Prompt = sourceRecipe.Prompt,
            Notes = sourceRecipe.Notes,
        };
        await db.PromptRecipes.AddAsync(duplicate, cancellationToken);
        foreach (var attachment in sourceRecipe.Attachments.OrderBy(a => a.SortOrder))
        {
            await db.RecipeAssetAttachments.AddAsync(new RecipeAssetAttachment
            {
                ProjectId = projectId,
                PromptRecipeId = duplicate.Id,
                AssetId = attachment.AssetId,
                Role = NormalizeRecipeAttachmentRole(attachment.Role),
                SortOrder = attachment.SortOrder,
                Notes = attachment.Notes,
            }, cancellationToken);
        }
        _ = await AppendPromptRecipeVersionAsync(
            duplicate,
            source,
            string.IsNullOrWhiteSpace(changeSummary) ? $"Duplicated from recipe '{sourceRecipe.Name}'." : changeSummary,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadPromptRecipeViewAsync(projectId, duplicate.Id, cancellationToken);
    }

    public async Task<PromptRecipeView> ReplacePromptRecipeAttachmentsAsync(
        Guid projectId,
        Guid recipeId,
        IReadOnlyList<RecipeAssetAttachmentRequest> attachments,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        await ReplaceRecipeAttachmentsAsync(projectId, promptRecipeId: recipeId, animationRecipeId: null, attachments, cancellationToken);
        recipe.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadPromptRecipeViewAsync(projectId, recipeId, cancellationToken);
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
        recipe.UpdatedAt = DateTime.UtcNow;
        _ = await AppendPromptRecipeVersionAsync(recipe, source, $"Reverted to version {version}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadPromptRecipeViewAsync(projectId, recipe.Id, cancellationToken);
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

        ApplyAnimationRecipeRequest(recipe, request);
        recipe.UpdatedAt = DateTime.UtcNow;
        await AppendAnimationRecipeVersionAsync(recipe, request.Source, request.ChangeSummary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAnimationRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task<AnimationRecipeView> ReplaceAnimationRecipeAttachmentsAsync(
        Guid projectId,
        Guid recipeId,
        IReadOnlyList<RecipeAssetAttachmentRequest> attachments,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");

        await ReplaceRecipeAttachmentsAsync(projectId, promptRecipeId: null, animationRecipeId: recipeId, attachments, cancellationToken);
        recipe.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAnimationRecipeViewAsync(projectId, recipeId, cancellationToken);
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

    private async Task<PromptRecipeView> LoadPromptRecipeViewAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken)
    {
        var recipe = await db.PromptRecipes
            .AsNoTracking()
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");
        var currentVersion = await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken) ?? 0;
        return RecipeView(recipe, currentVersion);
    }

    private async Task<AnimationRecipeView> LoadAnimationRecipeViewAsync(
        Guid projectId,
        Guid recipeId,
        CancellationToken cancellationToken)
    {
        var recipe = await db.AnimationRecipes
            .AsNoTracking()
            .Include(r => r.Attachments)
                .ThenInclude(a => a.Asset)
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

    public async Task<AnimationRecipeView> RevertAnimationRecipeAsync(
        Guid projectId,
        Guid recipeId,
        int version,
        string source,
        CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe was not found.");
        var snapshot = await db.AnimationRecipeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ProjectId == projectId && v.AnimationRecipeId == recipeId && v.Version == version, cancellationToken)
            ?? throw new InvalidOperationException("Animation recipe version was not found.");

        ApplyAnimationRecipeSnapshot(recipe, snapshot);
        recipe.UpdatedAt = DateTime.UtcNow;
        await AppendAnimationRecipeVersionAsync(recipe, source, $"Reverted to version {version}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAnimationRecipeViewAsync(projectId, recipe.Id, cancellationToken);
    }

    public async Task DeleteAnimationRecipeAsync(Guid projectId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.AnimationRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken);
        if (recipe is null)
            return;

        var assets = await db.ArtAssets
            .Where(a => a.ProjectId == projectId && a.SourceAnimationRecipeId == recipeId)
            .ToListAsync(cancellationToken);
        foreach (var asset in assets)
        {
            asset.SourceAnimationRecipeId = null;
            asset.SourceAnimationRecipeVersion = null;
        }

        var batches = await db.GenerationBatches
            .Where(b => b.ProjectId == projectId && b.AnimationRecipeId == recipeId)
            .ToListAsync(cancellationToken);
        foreach (var batch in batches)
        {
            batch.AnimationRecipeId = null;
            batch.AnimationRecipeVersion = null;
        }

        db.AnimationRecipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AnimationGuideRenderDraft> BuildAnimationGuideRenderDraftAsync(
        Guid projectId,
        GenerateAnimationGuideRequest request,
        CancellationToken cancellationToken)
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
        var requestedGuideCell = string.IsNullOrWhiteSpace(request.GuideCellSize)
            ? ((int Width, int Height)?)null
            : ParseCellSize(
                request.GuideCellSize,
                configured: string.Empty,
                fallbackWidth: targetCellWidth,
                fallbackHeight: targetCellHeight);
        var requestedGuideCanvas = string.IsNullOrWhiteSpace(request.GuideCanvasSize)
            ? ((int Width, int Height)?)null
            : ParseCellSize(
                request.GuideCanvasSize,
                configured: string.Empty,
                fallbackWidth: targetCellWidth,
                fallbackHeight: targetCellHeight);
        var layoutOnly = request.LayoutOnly == true;

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
        if (layoutOnly)
        {
            spec = spec with
            {
                GuideRenderer = "layout_box_guide",
                GuideRenderStyle = "labeled_bounded_boxes",
                MotionClipId = null,
                GuideCameraYawDegrees = null,
                GuideCameraPitchDegrees = null,
                MotionValidationProfile = null,
                GuideSourcePackage = null,
                GuideSourceLicense = null,
            };
        }
        else
        {
            spec = ResolveMotionClipSpec(
                spec,
                request.MotionClipId,
                fpsWasRequested: request.Fps is not null,
                rootMotionWasRequested: !string.IsNullOrWhiteSpace(request.RootMotion));
        }

        if (!layoutOnly && request.GuideCameraYawDegrees is double yaw)
            spec = spec with { GuideCameraYawDegrees = NormalizeYawDegrees(yaw) };
        if (!layoutOnly && request.GuideCameraPitchDegrees is double pitch)
            spec = spec with { GuideCameraPitchDegrees = NormalizePitchDegrees(pitch) };
        if (request.Loop is bool loop)
            spec = spec with { Loop = loop };

        var layoutProfile = AnalyzeGuideLayoutProfile(reference);
        var layout = BuildGuideLayout(
            spec,
            "#ff00ff",
            layoutProfile,
            requestedRows: request.Rows,
            requestedColumns: request.Columns,
            requestedGuideCell: requestedGuideCell,
            requestedCanvasSize: requestedGuideCanvas,
            requestedSafeMarginPercent: request.SafeMarginPercent);
        MotionGuideRenderResult? motionRender = null;
        if (!layoutOnly && MotionClipCatalog.IsExternalMotionSpec(spec))
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
                    GuideCameraPitchDegrees = null,
                    MotionValidationProfile = null,
                    GuideSourcePackage = null,
                    GuideSourceLicense = null,
                };
            }
        }

        var guidePng = layoutOnly
            ? SpriteGuideRenderer.RenderLayoutOnly(layout, diagnostic: false)
            : motionRender?.GuidePng ?? SpriteGuideRenderer.Render(layout, spec, diagnostic: false);
        var diagnosticPng = layoutOnly
            ? SpriteGuideRenderer.RenderLayoutOnly(layout, diagnostic: true)
            : motionRender?.DiagnosticPng ?? SpriteGuideRenderer.Render(layout, spec, diagnostic: true);
        var renderer = layoutOnly ? "layout_box_guide" : motionRender?.Metadata.Renderer ?? "procedural_sprite_guide";
        var renderStyle = layoutOnly ? "labeled_bounded_boxes" : motionRender?.Metadata.RenderStyle ?? "procedural_shape_guide";
        var label = string.IsNullOrWhiteSpace(request.Label)
            ? $"{TitleCase(animationKind)} {spec.FrameCount}-frame guide"
            : request.Label.Trim();
        var promptScaffold = layoutOnly
            ? BuildLayoutOnlyGuidePromptScaffold(reference, spec, layout)
            : BuildAnimationGuidePromptScaffold(reference, spec, layout);

        return new AnimationGuideRenderDraft(
            reference,
            spec,
            layout,
            guidePng,
            diagnosticPng,
            motionRender,
            renderer,
            renderStyle,
            label,
            promptScaffold);
    }

    public async Task<AnimationGuidePreviewView> PreviewAnimationGuideAsync(
        Guid projectId,
        GenerateAnimationGuideRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await BuildAnimationGuideRenderDraftAsync(projectId, request, cancellationToken);
        return new AnimationGuidePreviewView(
            draft.Label,
            DataUrl.ToDataUrl("image/png", draft.GuidePng),
            draft.Spec.AnimationKind,
            draft.Spec.AssetType,
            draft.Spec.StructureType,
            draft.Spec.Facing,
            draft.Spec.RootMotion,
            draft.Spec.FrameCount,
            draft.Spec.Fps,
            draft.Spec.Loop,
            draft.Layout.Rows,
            draft.Layout.Columns,
            draft.Layout.CanvasWidth,
            draft.Layout.CanvasHeight,
            draft.Layout.GuideCellWidth,
            draft.Layout.GuideCellHeight,
            draft.Layout.TargetCellWidth,
            draft.Layout.TargetCellHeight,
            draft.Spec.GuideCameraYawDegrees,
            draft.Spec.GuideCameraPitchDegrees,
            draft.Renderer,
            draft.RenderStyle,
            draft.Spec.MotionClipId);
    }

    public async Task<AnimationGuideRenderView> GenerateAnimationGuideAsync(
        Guid projectId,
        GenerateAnimationGuideRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await BuildAnimationGuideRenderDraftAsync(projectId, request, cancellationToken);
        var fileStem = CleanFileName(draft.Label.ToLowerInvariant().Replace(' ', '-'), "animation-guide");
        var frameOrder = Enumerable.Range(1, draft.Spec.FrameCount).ToList();
        var expectedBoxes = draft.Layout.Slots
            .OrderBy(slot => slot.FrameIndex)
            .Select(slot => slot.Rect)
            .ToList();
        var anchorStrategy = draft.Renderer == "layout_box_guide" ? "centered-boxes" : "root-baseline";
        var exportDefaultsJson = JsonSerializer.Serialize(new
        {
            rows = draft.Layout.Rows,
            columns = draft.Layout.Columns,
            cellWidth = draft.Spec.TargetCellWidth,
            cellHeight = draft.Spec.TargetCellHeight,
            fps = draft.Spec.Fps,
            loop = draft.Spec.Loop,
            frameOrder,
            anchorStrategy,
            background = draft.Layout.BackgroundColor,
        }, JsonOptions);

        var metadata = new
        {
            source = "generate_animation_guide",
            renderer = draft.Renderer,
            renderStyle = draft.RenderStyle,
            layoutOnly = draft.Renderer == "layout_box_guide",
            draft.Spec.AnimationKind,
            draft.Spec.AssetType,
            draft.Spec.StructureType,
            draft.Spec.Facing,
            draft.Spec.RootMotion,
            draft.Spec.FrameCount,
            draft.Spec.Fps,
            draft.Spec.Loop,
            draft.Spec.MotionClipId,
            draft.Spec.GuideCameraYawDegrees,
            draft.Spec.GuideCameraPitchDegrees,
            draft.Spec.GuideSourcePackage,
            draft.Spec.GuideSourceLicense,
            draft.Layout.Rows,
            draft.Layout.Columns,
            draft.Layout.CanvasWidth,
            draft.Layout.CanvasHeight,
            draft.Layout.GuideCellWidth,
            draft.Layout.GuideCellHeight,
            draft.Layout.TargetCellWidth,
            draft.Layout.TargetCellHeight,
            expectedFrameBoxes = expectedBoxes,
            referenceAssetId = draft.Reference?.Id,
        };
        var guide = CreateAsset(
            projectId,
            draft.Label,
            $"{fileStem}.png",
            ArtAssetKind.SpriteGuide,
            "image/png",
            draft.GuidePng,
            draft.Reference?.Id,
            sourceBatchId: null,
            promptRecipeId: null,
            promptRecipeVersion: null,
            animationRecipeId: null,
            animationRecipeVersion: null,
            draft.PromptScaffold,
            metadata);
        ArtAsset? diagnostic = null;
        if (draft.Renderer != "layout_box_guide")
        {
            diagnostic = CreateAsset(
                projectId,
                $"{draft.Label} diagnostic",
                $"{fileStem}-diagnostic.png",
                ArtAssetKind.SpriteGuide,
                "image/png",
                draft.DiagnosticPng,
                guide.Id,
                sourceBatchId: null,
                promptRecipeId: null,
                promptRecipeVersion: null,
                animationRecipeId: null,
                animationRecipeVersion: null,
                draft.PromptScaffold,
                metadata);
        }

        if (diagnostic is null)
            await db.ArtAssets.AddAsync(guide, cancellationToken);
        else
            await db.ArtAssets.AddRangeAsync([guide, diagnostic], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new AnimationGuideRenderView(
            guide.Id,
            diagnostic?.Id ?? guide.Id,
            guide.Label,
            draft.Spec.AnimationKind,
            draft.Spec.AssetType,
            draft.Spec.StructureType,
            draft.Spec.Facing,
            draft.Spec.RootMotion,
            draft.Spec.FrameCount,
            frameOrder,
            draft.Spec.Fps,
            draft.Spec.Loop,
            draft.Layout.Rows,
            draft.Layout.Columns,
            draft.Layout.CanvasWidth,
            draft.Layout.CanvasHeight,
            draft.Layout.GuideCellWidth,
            draft.Layout.GuideCellHeight,
            draft.Layout.TargetCellWidth,
            draft.Layout.TargetCellHeight,
            expectedBoxes,
            anchorStrategy,
            draft.PromptScaffold,
            exportDefaultsJson,
            draft.Renderer,
            draft.RenderStyle,
            draft.Spec.MotionClipId,
            draft.Spec.GuideCameraYawDegrees,
            draft.Spec.GuideCameraPitchDegrees,
            draft.MotionRender?.Metadata.SourcePackage ?? draft.Spec.GuideSourcePackage,
            draft.MotionRender?.Metadata.SourceLicense ?? draft.Spec.GuideSourceLicense,
            draft.MotionRender?.Metadata.SourceUrl,
            draft.Renderer == "layout_box_guide"
                ? "Layout guide assets saved as SpriteGuide. Use guideAssetId first in generate_sprite_sheet_candidates references."
                : "Animation guide assets saved as SpriteGuide. Use guideAssetId first in generate_sprite_sheet_candidates references.");
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

    public async Task<BatchReviewOperationResult> MarkBatchReviewOutputsAsync(
        Guid projectId,
        Guid batchId,
        IReadOnlyList<AssetReviewDecisionRequest> decisions,
        AssetReviewActor actor,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == batchId, cancellationToken);
        if (batch is null)
            return FailedBatchReviewOperation(batchId, "Generation batch was not found.");
        if (!IsTerminalBatchStatus(batch.Status))
            return FailedBatchReviewOperation(batchId, "A batch can only be reviewed after generation finishes.");

        var requested = decisions
            .Where(decision => decision.AssetId != Guid.Empty)
            .GroupBy(decision => decision.AssetId)
            .Select(group => group.Last())
            .ToList();
        if (requested.Count == 0)
            return FailedBatchReviewOperation(batchId, "At least one review decision is required.");

        var invalidDecision = requested.FirstOrDefault(request =>
            request.Decision is not (AssetReviewDecisionKind.Keep or AssetReviewDecisionKind.Reject or AssetReviewDecisionKind.Clear));
        if (invalidDecision is not null)
            return FailedBatchReviewOperation(batchId, "Review decisions must be keep, reject, or clear.");
        if (actor == AssetReviewActor.Assistant && requested.Any(request => request.Decision == AssetReviewDecisionKind.Clear))
            return FailedBatchReviewOperation(batchId, "The assistant cannot clear a review decision.");
        if (actor == AssetReviewActor.Assistant && requested.Any(request => string.IsNullOrWhiteSpace(request.Reason)))
            return FailedBatchReviewOperation(batchId, "Assistant review decisions require a concise reason.");

        var requestedIds = requested.Select(decision => decision.AssetId).ToList();
        var assets = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId
                && asset.SourceBatchId == batchId
                && requestedIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count != requested.Count)
            return FailedBatchReviewOperation(batchId, "Every review decision must reference an output from this batch.");
        if (assets.Any(asset => asset.ReviewStatus != AssetReviewStatus.Pending))
        {
            if (batch.ReviewCompletedAt is not null && assets.All(asset => asset.ReviewStatus != AssetReviewStatus.Pending))
            {
                return new BatchReviewOperationResult(
                    batchId,
                    Succeeded: true,
                    AlreadyCompleted: true,
                    AffectedCount: 0,
                    KeepCount: 0,
                    RejectCount: 0,
                    $"Batch review was already finished on {batch.ReviewCompletedAt.Value:O}; no marks were changed.");
            }

            return FailedBatchReviewOperation(batchId, "Only pending batch outputs can be marked for review.");
        }

        var now = DateTime.UtcNow;
        foreach (var request in requested)
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            await db.AssetReviewDecisions.AddAsync(new AssetReviewDecision
            {
                ProjectId = projectId,
                AssetId = request.AssetId,
                SourceBatchId = batchId,
                Decision = request.Decision,
                Actor = actor,
                Reason = reason,
                CreatedAt = now,
            }, cancellationToken);
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = WorkspaceMode.Review;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new BatchReviewOperationResult(
            batchId,
            Succeeded: true,
            AlreadyCompleted: false,
            AffectedCount: requested.Count,
            KeepCount: requested.Count(request => request.Decision == AssetReviewDecisionKind.Keep),
            RejectCount: requested.Count(request => request.Decision == AssetReviewDecisionKind.Reject),
            "Review marks were saved and are visible in Review.");
    }

    public async Task<BatchReviewOperationResult> FinishBatchReviewAsync(
        Guid projectId,
        Guid batchId,
        AssetReviewActor actor,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.GenerationBatches
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == batchId, cancellationToken);
        if (batch is null)
            return FailedBatchReviewOperation(batchId, "Generation batch was not found.");
        if (!IsTerminalBatchStatus(batch.Status))
            return FailedBatchReviewOperation(batchId, "A batch can only be reviewed after generation finishes.");

        var pendingAssets = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId
                && asset.SourceBatchId == batchId
                && asset.ReviewStatus == AssetReviewStatus.Pending)
            .ToListAsync(cancellationToken);
        if (pendingAssets.Count == 0)
        {
            if (batch.ReviewCompletedAt is not null)
            {
                return new BatchReviewOperationResult(
                    batchId,
                    Succeeded: true,
                    AlreadyCompleted: true,
                    AffectedCount: 0,
                    KeepCount: 0,
                    RejectCount: 0,
                    $"Batch review was already finished on {batch.ReviewCompletedAt.Value:O}; no assets were changed.");
            }

            return FailedBatchReviewOperation(batchId, "This batch has no pending outputs to review.");
        }

        var pendingIds = pendingAssets.Select(asset => asset.Id).ToList();
        var decisions = await db.AssetReviewDecisions
            .Where(decision => decision.ProjectId == projectId && pendingIds.Contains(decision.AssetId))
            .OrderBy(decision => decision.CreatedAt)
            .ThenBy(decision => decision.Id)
            .ToListAsync(cancellationToken);
        var latestByAsset = decisions
            .GroupBy(decision => decision.AssetId)
            .ToDictionary(group => group.Key, group => group.Last());

        if (actor == AssetReviewActor.Assistant)
        {
            var missingDecision = pendingAssets.FirstOrDefault(asset =>
                !latestByAsset.TryGetValue(asset.Id, out var decision)
                || decision.Actor != AssetReviewActor.Assistant
                || decision.Decision is not (AssetReviewDecisionKind.Keep or AssetReviewDecisionKind.Reject)
                || string.IsNullOrWhiteSpace(decision.Reason));
            if (missingDecision is not null)
                return FailedBatchReviewOperation(
                    batchId,
                    $"The assistant must explicitly mark every pending output Keep or Reject with a reason before finishing review. Asset {missingDecision.Id} is not ready.");
        }

        var now = DateTime.UtcNow;
        var keepCount = 0;
        foreach (var asset in pendingAssets)
        {
            var keep = latestByAsset.TryGetValue(asset.Id, out var decision)
                && decision.Decision == AssetReviewDecisionKind.Keep;
            asset.ReviewStatus = keep ? AssetReviewStatus.Kept : AssetReviewStatus.Rejected;
            if (keep)
                keepCount++;
            asset.UpdatedAt = now;
        }

        batch.ReviewCompletedBy = actor;
        batch.ReviewCompletedAt = now;
        batch.UpdatedAt = now;
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = WorkspaceMode.Review;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new BatchReviewOperationResult(
            batchId,
            Succeeded: true,
            AlreadyCompleted: false,
            AffectedCount: pendingAssets.Count,
            KeepCount: keepCount,
            RejectCount: pendingAssets.Count - keepCount,
            "Batch review finished. Kept outputs are in Library and rejected outputs are in Rejected.");
    }

    private static BatchReviewOperationResult FailedBatchReviewOperation(Guid batchId, string message) =>
        new(
            batchId,
            Succeeded: false,
            AlreadyCompleted: false,
            AffectedCount: 0,
            KeepCount: 0,
            RejectCount: 0,
            message);

    public async Task MoveAssetsReviewStatusAsync(
        Guid projectId,
        IReadOnlyList<Guid> assetIds,
        AssetReviewStatus status,
        AssetReviewActor actor,
        CancellationToken cancellationToken = default)
    {
        if (status is not (AssetReviewStatus.Kept or AssetReviewStatus.Rejected))
            throw new InvalidOperationException("Assets can only be moved to Kept or Rejected.");

        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return;
        var assets = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId && ids.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count != ids.Count)
            throw new InvalidOperationException("One or more assets were not found.");

        var now = DateTime.UtcNow;
        foreach (var asset in assets)
        {
            if (asset.ReviewStatus == AssetReviewStatus.Pending)
                throw new InvalidOperationException("Pending batch outputs must be finished through Review.");
            asset.ReviewStatus = status;
            asset.UpdatedAt = now;
            await db.AssetReviewDecisions.AddAsync(new AssetReviewDecision
            {
                ProjectId = projectId,
                AssetId = asset.Id,
                SourceBatchId = asset.SourceBatchId,
                Decision = status == AssetReviewStatus.Kept ? AssetReviewDecisionKind.Keep : AssetReviewDecisionKind.Reject,
                Actor = actor,
                Reason = actor == AssetReviewActor.User ? "Moved by user." : "Moved by assistant.",
                CreatedAt = now,
            }, cancellationToken);
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRejectedAssetsAsync(Guid projectId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return;
        var count = await db.ArtAssets.CountAsync(
            asset => asset.ProjectId == projectId && ids.Contains(asset.Id) && asset.ReviewStatus == AssetReviewStatus.Rejected,
            cancellationToken);
        if (count != ids.Count)
            throw new InvalidOperationException("Only rejected assets can be bulk deleted.");

        await DeleteAssetsAsync(projectId, ids, cancellationToken);
    }

    public async Task<ArtAssetView> RenameAssetAsync(Guid projectId, Guid assetId, string label, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        asset.Label = CleanRequired(label, "Asset name is required.");
        asset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
    }

    public async Task DeleteAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        await DeleteAssetsAsync(projectId, [assetId], cancellationToken);
    }

    private async Task DeleteAssetsAsync(Guid projectId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken)
    {
        var ids = assetIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var assets = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId && ids.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count == 0)
            return;

        var maskIds = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && ids.Contains(m.AssetId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId
                && (ids.Contains(a.RefId) || (a.Type == ChatContextAttachmentType.Mask && maskIds.Contains(a.RefId))))
            .ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);

        await RemoveCompareReviewItemsAsync(projectId, CompareReviewItemKind.Asset, ids, cancellationToken);

        var sourceFrameSetIds = await db.FrameSets
            .Where(frameSet => frameSet.ProjectId == projectId && frameSet.SourceAssetId != null && ids.Contains(frameSet.SourceAssetId.Value))
            .Select(frameSet => frameSet.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        await MarkDeletedBatchOutputsAsync(assets, now, cancellationToken);
        db.ArtAssets.RemoveRange(assets);
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project.ActiveSpriteSourceAssetId is Guid activeAssetId && ids.Contains(activeAssetId))
        {
            project.ActiveSpriteSourceAssetId = null;
            project.ActiveSpriteRegionIdsJson = "[]";
            if (SpriteWorkspaceModes.Normalize(project.ActiveSpriteMode) == SpriteWorkspaceModes.Source
                && project.ActiveFrameSetId is Guid activeFrameSetId
                && sourceFrameSetIds.Contains(activeFrameSetId))
            {
                project.ActiveSpriteMode = SpriteWorkspaceModes.Frames;
            }
        }

        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkDeletedBatchOutputsAsync(
        IReadOnlyList<ArtAsset> assets,
        DateTime deletedAt,
        CancellationToken cancellationToken)
    {
        var batchIds = assets.Select(asset => asset.SourceBatchId).OfType<Guid>().Distinct().ToList();
        if (batchIds.Count == 0)
            return;

        var batches = await db.GenerationBatches
            .Where(batch => batchIds.Contains(batch.Id))
            .ToListAsync(cancellationToken);
        foreach (var batch in batches)
        {
            var states = NormalizeOutputStates(batch.OutputStatesJson, batch.Count);
            foreach (var asset in assets.Where(asset => asset.SourceBatchId == batch.Id))
            {
                if (ReadBatchOutputIndex(asset) is not int outputIndex || outputIndex < 0 || outputIndex >= batch.Count)
                    continue;
                var previous = states.LastOrDefault(state => state.OutputIndex == outputIndex);
                states = UpsertOutputState(states, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Deleted,
                    previous?.Attempt ?? 0,
                    "Deleted from asset storage.",
                    StartedAt: previous?.StartedAt,
                    UpdatedAt: deletedAt,
                    CompletedAt: deletedAt));
            }

            batch.OutputStatesJson = SerializeOutputStates(states);
            batch.UpdatedAt = deletedAt;
        }
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

        if (request.SwitchToReview)
            project.ActiveWorkspaceMode = WorkspaceMode.Review;
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

        if (request.SwitchToReview)
            project.ActiveWorkspaceMode = WorkspaceMode.Review;
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

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = WorkspaceMode.Review;
        project.UpdatedAt = DateTime.UtcNow;
        item.CompareReviewSet.UpdatedAt = DateTime.UtcNow;
        db.CompareReviewSetItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearCompareReviewSetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var reviewSet = await db.CompareReviewSets.FirstOrDefaultAsync(set => set.ProjectId == projectId, cancellationToken);
        if (reviewSet is null)
            return;

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = WorkspaceMode.Review;
        project.UpdatedAt = DateTime.UtcNow;
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


    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
        ?? throw new InvalidOperationException("Project was not found.");

    private async Task ReplaceRecipeAttachmentsAsync(
        Guid projectId,
        Guid? promptRecipeId,
        Guid? animationRecipeId,
        IReadOnlyList<RecipeAssetAttachmentRequest> attachments,
        CancellationToken cancellationToken)
    {
        var existing = promptRecipeId is Guid promptId
            ? await db.RecipeAssetAttachments
                .Where(a => a.ProjectId == projectId && a.PromptRecipeId == promptId)
                .ToListAsync(cancellationToken)
            : await db.RecipeAssetAttachments
                .Where(a => a.ProjectId == projectId && a.AnimationRecipeId == animationRecipeId)
                .ToListAsync(cancellationToken);
        db.RecipeAssetAttachments.RemoveRange(existing);

        var assetIds = attachments
            .Select(a => a.AssetId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (assetIds.Count > 0)
        {
            var validAssetIds = await db.ArtAssets
                .AsNoTracking()
                .Where(a => a.ProjectId == projectId && assetIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            var missing = assetIds.Except(validAssetIds).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException("One or more recipe attachment assets were not found in this project.");
        }

        var sortOrder = 0;
        foreach (var attachment in attachments.Where(a => a.AssetId != Guid.Empty))
        {
            await db.RecipeAssetAttachments.AddAsync(new RecipeAssetAttachment
            {
                ProjectId = projectId,
                PromptRecipeId = promptRecipeId,
                AnimationRecipeId = animationRecipeId,
                AssetId = attachment.AssetId,
                Role = NormalizeRecipeAttachmentRole(attachment.Role),
                SortOrder = sortOrder++,
                Notes = Clean(attachment.Notes),
            }, cancellationToken);
        }
    }

    private AnimationSpec ResolveMotionClipSpec(
        AnimationSpec spec,
        string? requestedMotionClipId,
        bool fpsWasRequested,
        bool rootMotionWasRequested)
    {
        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        MotionClipDefinition? clip = null;
        if (!string.IsNullOrWhiteSpace(requestedMotionClipId))
        {
            clip = catalog.Find(requestedMotionClipId)
                ?? throw new InvalidOperationException($"Motion clip '{requestedMotionClipId}' was not found in the catalog.");
            if (!clip.SupportsTarget(spec))
                throw new InvalidOperationException($"Motion clip '{requestedMotionClipId}' does not support {spec.AssetType}/{spec.StructureType} targets.");
        }
        else if (IsHumanoidWalkSpec(spec))
        {
            clip = catalog.ResolveDefault(spec);
        }

        if (clip is null)
            return spec;

        var resolvedFps = !fpsWasRequested && clip.DefaultFps > 0
            ? Math.Clamp(clip.DefaultFps, 1, 60)
            : spec.Fps;
        var resolvedRootMotion = !rootMotionWasRequested && !string.IsNullOrWhiteSpace(clip.RootMotion)
            ? NormalizeGuideToken(clip.RootMotion, spec.RootMotion)
            : spec.RootMotion;
        if (resolvedFps != spec.Fps || !string.Equals(resolvedRootMotion, spec.RootMotion, StringComparison.Ordinal))
        {
            spec = SpriteMotionArchetypes.Build(
                spec.AssetType,
                spec.StructureType,
                spec.AnimationKind,
                spec.Facing,
                resolvedRootMotion,
                spec.FrameCount,
                resolvedFps,
                spec.TargetCellWidth,
                spec.TargetCellHeight);
        }

        return spec with
        {
            RootMotion = resolvedRootMotion,
            Fps = resolvedFps,
            GuideRenderer = MotionClipCatalog.RendererId,
            GuideRenderStyle = MotionClipCatalog.SkinnedMannequinRenderStyle,
            MotionClipId = clip.ClipId,
            GuideCameraYawDegrees = GltfMotionGuideRenderer.FacingToYawDegrees(spec.Facing),
            GuideCameraPitchDegrees = 0d,
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

    private static LayoutSpec BuildGuideLayout(
        AnimationSpec spec,
        string backgroundColor,
        LayoutProfile layoutProfile,
        int? requestedRows = null,
        int? requestedColumns = null,
        (int Width, int Height)? requestedGuideCell = null,
        (int Width, int Height)? requestedCanvasSize = null,
        double? requestedSafeMarginPercent = null)
    {
        var (defaultColumns, defaultRows) = GuideGridForFrameCount(spec.FrameCount);
        var columns = Math.Clamp(requestedColumns ?? defaultColumns, 1, 8);
        var rows = Math.Clamp(requestedRows ?? defaultRows, 1, 8);
        if (rows * columns < spec.FrameCount)
            rows = Math.Clamp((int)Math.Ceiling(spec.FrameCount / (double)columns), 1, 8);
        if (rows * columns < spec.FrameCount)
            columns = Math.Clamp((int)Math.Ceiling(spec.FrameCount / (double)rows), 1, 8);

        var (defaultCellWidth, defaultCellHeight) = GuideCellSize(spec.FrameCount, layoutProfile);
        int guideCellWidth;
        int guideCellHeight;
        int canvasWidth;
        int canvasHeight;
        if (requestedCanvasSize is { } canvasSize)
        {
            canvasWidth = Math.Clamp(canvasSize.Width, 256, 4096);
            canvasHeight = Math.Clamp(canvasSize.Height, 256, 4096);
            var slotWidth = Math.Max(1, canvasWidth / columns);
            var slotHeight = Math.Max(1, canvasHeight / rows);
            guideCellWidth = Math.Clamp(requestedGuideCell?.Width ?? slotWidth, 1, slotWidth);
            guideCellHeight = Math.Clamp(requestedGuideCell?.Height ?? slotHeight, 1, slotHeight);
        }
        else
        {
            guideCellWidth = Math.Clamp(requestedGuideCell?.Width ?? defaultCellWidth, 64, 2048);
            guideCellHeight = Math.Clamp(requestedGuideCell?.Height ?? defaultCellHeight, 64, 2048);
            canvasWidth = checked(columns * guideCellWidth);
            canvasHeight = checked(rows * guideCellHeight);
        }
        var safeMarginRatio = requestedSafeMarginPercent is double safeMarginPercent
            ? Math.Clamp(safeMarginPercent, 0d, 40d) / 100d
            : layoutProfile.NeedsLargePadding ? 0.14d : 0.11d;
        var slots = Enumerable.Range(0, spec.FrameCount)
            .Select(index =>
            {
                var gridSlot = CellRectForGuideGrid(index, columns, rows, canvasWidth, canvasHeight);
                var rect = CenterRectInSlot(gridSlot, guideCellWidth, guideCellHeight);
                var margin = Math.Max(0, (int)Math.Round(Math.Min(rect.Width, rect.Height) * safeMarginRatio));
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

    private static SpriteSheetRect CenterRectInSlot(SpriteSheetRect slot, int width, int height)
    {
        var rectWidth = Math.Clamp(width, 1, slot.Width);
        var rectHeight = Math.Clamp(height, 1, slot.Height);
        return new SpriteSheetRect(
            slot.X + ((slot.Width - rectWidth) / 2),
            slot.Y + ((slot.Height - rectHeight) / 2),
            rectWidth,
            rectHeight);
    }

    private static string BuildLayoutOnlyGuidePromptScaffold(ArtAsset? reference, AnimationSpec spec, LayoutSpec layout)
    {
        var referenceRole = reference is null
            ? "Image 2, when supplied, controls the subject identity, silhouette, palette, materials, outfit, equipment, and rendering style."
            : $"Image 2 is the canonical subject reference '{reference.Label}'. It controls identity, silhouette, palette, materials, outfit, equipment, and rendering style.";

        return $"""
        GOAL
        Render a {spec.FrameCount}-frame sprite-sheet grid as a {layout.Columns} column by {layout.Rows} row layout.

        INPUT ROLES
        Image 1 is a labeled wireframe layout guide. Its dark outer rectangles and numbered tabs identify the exact render box for each frame; its dashed inner rectangles identify the safe region. It controls exact frame order, slot positions, frame boxes, safe margins, and per-frame boundaries. It does not define a specific pose, action, or motion cycle.
        {referenceRole}
        Optional later references may control art style, but the layout guide remains the only frame placement guide.

        LAYOUT CONTRACT
        Read frame order left-to-right across each row, then lower rows. Put exactly one complete sprite frame inside each numbered dark-bordered render box, not merely anywhere in the larger grid slot. Keep each subject fully inside that frame's dashed safe region and keep apparent scale, camera, and style stable across frames.

        OUTPUT CONTRACT
        Exactly {spec.FrameCount} complete frames, one subject per cell, no text, no labels, no borders, no extra poses, no overlap between cells, no cropping.

        CLEANUP CONTRACT
        Do not reproduce guide lines, frame boxes, safe boxes, root marks, numbers, labels, or construction marks.

        BACKGROUND
        Use one flat opaque chroma color: {layout.BackgroundColor}. No shadows, floors, scenery, gradients, transparent checkerboards, or detached effects outside the guided subject.
        """;
    }

    private static string BuildAnimationGuidePromptScaffold(ArtAsset? reference, AnimationSpec spec, LayoutSpec layout)
    {
        var guideRole = MotionClipCatalog.IsExternalMotionSpec(spec)
            ? $"Image 1 is a sampled mannequin motion guide from clip {spec.MotionClipId}; it controls exact slot positions, frame order, root/pivot anchors, safe margins, body pose, foot contacts, camera yaw, and camera pitch/elevation. Do not reproduce guide marks."
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

    private static double NormalizeYawDegrees(double yaw)
    {
        if (double.IsNaN(yaw) || double.IsInfinity(yaw))
            return 0d;

        var normalized = yaw % 360d;
        if (normalized > 180d)
            normalized -= 360d;
        if (normalized < -180d)
            normalized += 360d;
        return Math.Round(normalized, 2);
    }

    private static double NormalizePitchDegrees(double pitch)
    {
        if (double.IsNaN(pitch) || double.IsInfinity(pitch))
            return 0d;

        return Math.Round(Math.Clamp(pitch, -45d, 45d), 2);
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

    private async Task<int?> GetCurrentAnimationRecipeVersionAsync(Guid recipeId, CancellationToken cancellationToken) =>
        await db.AnimationRecipeVersions
            .AsNoTracking()
            .Where(version => version.AnimationRecipeId == recipeId)
            .Select(version => (int?)version.Version)
            .MaxAsync(cancellationToken);

    private Task<List<ArtAsset>> MergeRecipeExampleReferenceAsync(
        Guid projectId,
        PromptRecipe? recipe,
        IReadOnlyList<ArtAsset> explicitReferences,
        Guid? excludedAssetId,
        CancellationToken cancellationToken) =>
        MergeGenerationReferencesAsync(projectId, recipe, animationRecipe: null, explicitReferences, excludedAssetId, cancellationToken);

    private Task<List<ArtAsset>> MergeGenerationReferencesAsync(
        Guid projectId,
        PromptRecipe? promptRecipe,
        AnimationRecipe? animationRecipe,
        IReadOnlyList<ArtAsset> explicitReferences,
        Guid? excludedAssetId,
        CancellationToken cancellationToken)
    {
        var maxReferences = Math.Max(0, imageOptions.Value.MaxReferenceImages);
        var references = new List<ArtAsset>();
        if (maxReferences == 0)
            return Task.FromResult(references);

        AddRecipeAttachmentReferences(references, animationRecipe?.Attachments, maxReferences, excludedAssetId, role: RecipeAssetAttachmentRoles.Guide);
        AddRecipeAttachmentReferences(references, promptRecipe?.Attachments, maxReferences, excludedAssetId, role: RecipeAssetAttachmentRoles.Guide);
        AddRecipeAttachmentReferences(references, animationRecipe?.Attachments, maxReferences, excludedAssetId, role: null);
        AddRecipeAttachmentReferences(references, promptRecipe?.Attachments, maxReferences, excludedAssetId, role: null);
        foreach (var reference in explicitReferences)
            AddReferenceIfRoom(references, reference, maxReferences, excludedAssetId);
        return Task.FromResult(references);
    }

    private static void AddRecipeAttachmentReferences(
        List<ArtAsset> references,
        IEnumerable<RecipeAssetAttachment>? attachments,
        int maxReferences,
        Guid? excludedAssetId,
        string? role)
    {
        if (attachments is null)
            return;

        foreach (var attachment in attachments.OrderBy(a => a.SortOrder))
        {
            if (role is null)
            {
                if (NormalizeRecipeAttachmentRole(attachment.Role) == RecipeAssetAttachmentRoles.Guide)
                    continue;
            }
            else if (NormalizeRecipeAttachmentRole(attachment.Role) != role)
            {
                continue;
            }

            if (attachment.Asset is not null)
                AddReferenceIfRoom(references, attachment.Asset, maxReferences, excludedAssetId);
        }
    }

    private static void AddReferenceIfRoom(List<ArtAsset> references, ArtAsset reference, int maxReferences, Guid? excludedAssetId)
    {
        if (references.Count >= maxReferences || reference.Id == excludedAssetId || references.Any(existing => existing.Id == reference.Id))
            return;

        references.Add(reference);
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
        new(recipe.Prompt);

    private static RecipePromptGuidance RecipeGuidance(PromptRecipeVersion version) =>
        new(version.Prompt);

    private async Task<AnimationRecipePromptGuidance?> LoadAnimationRecipePromptGuidanceForBatchAsync(
        Guid projectId,
        GenerationBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.AnimationRecipeId is not Guid animationRecipeId)
            return null;

        if (batch.AnimationRecipeVersion is int version)
        {
            var snapshot = await db.AnimationRecipeVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.ProjectId == projectId && v.AnimationRecipeId == animationRecipeId && v.Version == version, cancellationToken);
            if (snapshot is not null)
                return AnimationRecipeGuidance(snapshot);
        }

        var recipe = await db.AnimationRecipes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == animationRecipeId, cancellationToken);
        return recipe is null ? null : AnimationRecipeGuidance(recipe);
    }

    private static AnimationRecipePromptGuidance AnimationRecipeGuidance(AnimationRecipe recipe) =>
        new(recipe.Name, recipe.Prompt);

    private static AnimationRecipePromptGuidance AnimationRecipeGuidance(AnimationRecipeVersion version) =>
        new(version.Name, version.Prompt);

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
            CompareReviewItemKind.Frame =>
                await db.Frames.AnyAsync(frame => frame.ProjectId == projectId && frame.Id == refId, cancellationToken),
            CompareReviewItemKind.Animation =>
                await db.FrameSets.AnyAsync(frameSet => frameSet.ProjectId == projectId && frameSet.Id == refId, cancellationToken),
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
            CompareReviewItemKind.Frame =>
                await db.Frames.Where(frame => frame.ProjectId == projectId && frame.Id == refId).Select(frame => frame.Name).FirstOrDefaultAsync(cancellationToken)
                ?? "Frame",
            CompareReviewItemKind.Animation =>
                await db.FrameSets.Where(frameSet => frameSet.ProjectId == projectId && frameSet.Id == refId).Select(frameSet => frameSet.Name).FirstOrDefaultAsync(cancellationToken)
                is { } animationName ? $"{animationName} animation" : "Animation",
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
        Guid? animationRecipeId,
        int? animationRecipeVersion,
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
            SourceAnimationRecipeId = animationRecipeId,
            SourceAnimationRecipeVersion = animationRecipeVersion,
            Prompt = prompt,
            SourceMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
        };
    }

    private static string BuildPrompt(
        string prompt,
        string negativePrompt,
        RecipePromptGuidance? recipe,
        AnimationRecipePromptGuidance? animationRecipe,
        string? background)
    {
        var parts = new List<string>();
        if (recipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(recipe.Prompt))
                parts.Add("Style direction (reusable):\n" + recipe.Prompt.Trim());
        }

        if (animationRecipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(animationRecipe.Prompt))
                parts.Add("Motion direction (positions and timing only, not visual style):\n" + animationRecipe.Prompt.Trim());
        }

        parts.Add(recipe is null && animationRecipe is null
            ? prompt.Trim()
            : "Task:\n" + prompt.Trim());

        var constraints = BuildConstraintLines(negativePrompt);
        if (NormalizeBackground(background) == "removable")
        {
            constraints.Add("background must be flat, solid chroma-key magenta using exactly #ff00ff");
            constraints.Add("the same solid magenta must be visible through open holes, railings, gaps, cutouts, and transparent-looking interior spaces");
            constraints.Add("no checkerboards, transparency grids, white or gray faux transparency, texture, gradients, shadows, reflections, floor planes, scenery, or extra props in the background");
        }

        if (constraints.Count > 0)
            parts.Add("Constraints:\n" + string.Join("\n", constraints));

        return string.Join("\n\n", parts);
    }

    private static List<string> BuildConstraintLines(string? negativePrompt)
    {
        if (string.IsNullOrWhiteSpace(negativePrompt))
            return [];

        return negativePrompt
            .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizeConstraintLine)
            .ToList();
    }

    private static string NormalizeConstraintLine(string item)
    {
        var trimmed = item.Trim().TrimEnd('.');
        return trimmed.StartsWith("no ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("do not ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("never ", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "no " + trimmed;
    }

    private static void ApplyRecipeRequest(PromptRecipe recipe, SavePromptRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Recipe name is required.");
        recipe.Prompt = CleanRequired(request.Prompt, "Recipe prompt is required.");
        recipe.Notes = Clean(request.Notes);
    }

    private static void ApplyRecipeRequest(PromptRecipe recipe, UpdatePromptRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Recipe name is required.");
        recipe.Prompt = CleanRequired(request.Prompt, "Recipe prompt is required.");
        recipe.Notes = Clean(request.Notes);
    }

    private static void ApplyAnimationRecipeRequest(AnimationRecipe recipe, SaveAnimationRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Animation recipe name is required.");
        recipe.Prompt = CleanRequired(request.Prompt, "Animation recipe prompt is required.");
        recipe.Notes = Clean(request.Notes);
    }

    private static void ApplyAnimationRecipeRequest(AnimationRecipe recipe, UpdateAnimationRecipeRequest request)
    {
        recipe.Name = CleanRequired(request.Name, "Animation recipe name is required.");
        recipe.Prompt = CleanRequired(request.Prompt, "Animation recipe prompt is required.");
        recipe.Notes = Clean(request.Notes);
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
            Prompt = recipe.Prompt,
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
            Prompt = recipe.Prompt,
            Notes = recipe.Notes,
            Source = normalizedSource,
            ChangeSummary = normalizedSummary,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        return nextVersion;
    }

    private static void ApplyRecipeSnapshot(PromptRecipe recipe, PromptRecipeVersion snapshot)
    {
        recipe.Name = snapshot.Name;
        recipe.Prompt = snapshot.Prompt;
        recipe.Notes = snapshot.Notes;
    }

    private static void ApplyAnimationRecipeSnapshot(AnimationRecipe recipe, AnimationRecipeVersion snapshot)
    {
        recipe.Name = snapshot.Name;
        recipe.Prompt = snapshot.Prompt;
        recipe.Notes = snapshot.Notes;
    }

    private static string NormalizeRecipeVersionSource(string? source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            _ => "user",
        };

    private static string NormalizeRecipeAttachmentRole(string? role) =>
        role?.Trim().ToLowerInvariant() switch
        {
            RecipeAssetAttachmentRoles.Guide => RecipeAssetAttachmentRoles.Guide,
            _ => RecipeAssetAttachmentRoles.Example,
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

    private static bool TryParseAssetReviewStatus(string? status, out AssetReviewStatus reviewStatus)
    {
        reviewStatus = AssetReviewStatus.Kept;
        return !string.IsNullOrWhiteSpace(status)
            && Enum.TryParse(status.Trim(), ignoreCase: true, out reviewStatus);
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
        asset.ReviewStatus,
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
        asset.ReviewStatus,
        asset.Notes,
        asset.Prompt,
        asset.SourceMetadataJson,
        asset.CreatedAt,
        asset.UpdatedAt,
    };

    private IReadOnlyList<MotionClipDefinition> SelectMotionClips(string? query, string? animationKind, bool? loop, int? limit)
    {
        var max = NormalizeToolLimit(limit, 50, 100);
        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        var clips = catalog.Clips.AsEnumerable();

        if (loop is bool loopValue)
            clips = clips.Where(clip => clip.Loop == loopValue);

        if (!string.IsNullOrWhiteSpace(animationKind))
        {
            var normalizedKind = MotionClipCatalog.Normalize(animationKind);
            clips = clips.Where(clip => MotionClipMatchesTerm(clip, normalizedKind));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = MotionClipCatalog.Normalize(query);
            clips = clips.Where(clip => MotionClipMatchesTerm(clip, normalizedQuery));
        }

        return clips
            .OrderBy(clip => clip.DisplayName)
            .Take(max)
            .ToList();
    }

    private static MotionClipView MotionClipView(MotionClipDefinition clip) => new(
        clip.ClipId,
        clip.DisplayName,
        clip.AnimationName,
        clip.Aliases,
        clip.SupportedAnimationKinds,
        clip.SearchTags,
        clip.Loop,
        clip.RootMotion,
        clip.DefaultFps,
        clip.AllowedSampleCounts,
        clip.SupportedAssetTypes,
        clip.SourcePackage,
        clip.SourceUrl,
        clip.License);

    private static object CompactMotionClip(MotionClipDefinition clip) => new
    {
        motionClipId = clip.ClipId,
        clip.DisplayName,
        clip.AnimationName,
        clip.Aliases,
        supportedAnimationKinds = clip.SupportedAnimationKinds,
        searchTags = clip.SearchTags,
        loopRecommended = clip.Loop,
        recommendedRootMotion = clip.RootMotion,
        clip.DefaultFps,
        clip.AllowedSampleCounts,
        supportedAssetTypes = clip.SupportedAssetTypes,
        clip.SourcePackage,
        clip.SourceUrl,
        clip.License,
    };

    private static bool MotionClipMatchesTerm(MotionClipDefinition clip, string normalizedTerm)
    {
        if (string.IsNullOrWhiteSpace(normalizedTerm))
            return true;

        return MotionClipSearchValues(clip)
            .Select(MotionClipCatalog.Normalize)
            .Any(value => value.Contains(normalizedTerm, StringComparison.Ordinal));
    }

    private static IEnumerable<string> MotionClipSearchValues(MotionClipDefinition clip)
    {
        yield return clip.ClipId;
        yield return clip.DisplayName;
        yield return clip.AnimationName;
        yield return clip.RootMotion;
        foreach (var alias in clip.Aliases)
            yield return alias;
        foreach (var kind in clip.SupportedAnimationKinds)
            yield return kind;
        foreach (var tag in clip.SearchTags)
            yield return tag;
    }

    private static object CompactRecipe(PromptRecipe recipe, int currentVersion) => new
    {
        recipe.Id,
        recipe.Name,
        currentVersion,
        promptPreview = Preview(recipe.Prompt, 320),
        attachmentCount = recipe.Attachments.Count,
        guideCount = recipe.Attachments.Count(a => NormalizeRecipeAttachmentRole(a.Role) == RecipeAssetAttachmentRoles.Guide),
        exampleCount = recipe.Attachments.Count(a => NormalizeRecipeAttachmentRole(a.Role) == RecipeAssetAttachmentRoles.Example),
        notesPreview = Preview(recipe.Notes, 220),
        recipe.CreatedAt,
    };

    private static object CompactAnimationRecipe(AnimationRecipe recipe) => new
    {
        recipe.Id,
        recipe.Name,
        recipe.CurrentVersion,
        promptPreview = Preview(recipe.Prompt, 320),
        attachmentCount = recipe.Attachments.Count,
        guideCount = recipe.Attachments.Count(a => NormalizeRecipeAttachmentRole(a.Role) == RecipeAssetAttachmentRoles.Guide),
        exampleCount = recipe.Attachments.Count(a => NormalizeRecipeAttachmentRole(a.Role) == RecipeAssetAttachmentRoles.Example),
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
        batch.AnimationRecipeId,
        batch.AnimationRecipeVersion,
        promptPreview = Preview(batch.Prompt, 360),
        negativePromptPreview = Preview(batch.NegativePrompt, 220),
        batch.Error,
        batch.CreatedAt,
        batch.ReviewCompletedBy,
        batch.ReviewCompletedAt,
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
        asset.ReviewStatus,
        asset.CurrentReviewDecision,
        asset.LatestAgentReviewDecision,
        asset.SourcePromptRecipeId,
        asset.SourcePromptRecipeVersion,
        asset.SourceAnimationRecipeId,
        asset.SourceAnimationRecipeVersion,
        asset.Notes,
    };


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
            project.ActiveFrameSetId,
            string.IsNullOrWhiteSpace(project.ActiveSpriteMode) ? "source" : project.ActiveSpriteMode,
            project.ActiveSpriteSourceAssetId,
            project.ActiveSpriteFrameId,
            string.IsNullOrWhiteSpace(project.ActiveSpriteRegionIdsJson) ? "[]" : project.ActiveSpriteRegionIdsJson,
            project.UpdatedAt);

    private static ArtAssetView AssetView(ArtAsset asset) =>
        AssetView(AssetListItem(asset));

    private static ArtAssetView AssetView(
        ArtAssetListItem asset,
        AssetReviewDecision? currentDecision = null,
        AssetReviewDecision? latestAgentDecision = null) =>
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
            asset.SourceAnimationRecipeId,
            asset.SourceAnimationRecipeVersion,
            asset.IsFavorite,
            asset.Notes,
            asset.Prompt,
            asset.CreatedAt,
            asset.ReviewStatus,
            ReviewDecisionView(currentDecision),
            ReviewDecisionView(latestAgentDecision));

    private static AssetReviewDecisionView? ReviewDecisionView(AssetReviewDecision? decision) =>
        decision is null
            ? null
            : new(
                decision.Id,
                decision.AssetId,
                decision.SourceBatchId,
                decision.Decision,
                decision.Actor,
                decision.Reason,
                decision.CreatedAt);

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
            batch.AnimationRecipeId,
            batch.AnimationRecipeVersion,
            batch.Status,
            displayError,
            outputStates,
            outputErrors,
            batch.CreatedAt,
            batch.ReviewCompletedBy,
            batch.ReviewCompletedAt);
    }

    private static PromptRecipeView RecipeView(PromptRecipe recipe, int currentVersion) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.Prompt,
            recipe.Notes,
            recipe.Attachments
                .OrderBy(attachment => attachment.SortOrder)
                .ThenBy(attachment => attachment.CreatedAt)
                .Select(RecipeAttachmentView)
                .ToList(),
            currentVersion,
            recipe.CreatedAt);

    private static RecipeAssetAttachmentView RecipeAttachmentView(RecipeAssetAttachment attachment) =>
        new(
            attachment.Id,
            attachment.AssetId,
            NormalizeRecipeAttachmentRole(attachment.Role),
            attachment.SortOrder,
            attachment.Notes,
            attachment.Asset.Label,
            attachment.Asset.Kind,
            AssetPreviewImageUrl(attachment.ProjectId, attachment.AssetId, attachment.Asset.UpdatedAt),
            attachment.Asset.Width,
            attachment.Asset.Height,
            attachment.CreatedAt);

    private static PromptRecipeVersionView PromptRecipeVersionView(PromptRecipeVersion version) =>
        new(
            version.Id,
            version.RecipeId,
            version.Version,
            version.Name,
            version.Notes,
            version.Source,
            version.ChangeSummary,
            version.CreatedAt);

    private static AnimationRecipeView AnimationRecipeView(AnimationRecipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.Prompt,
            recipe.Notes,
            recipe.Attachments
                .OrderBy(attachment => attachment.SortOrder)
                .ThenBy(attachment => attachment.CreatedAt)
                .Select(RecipeAttachmentView)
                .ToList(),
            recipe.CurrentVersion,
            recipe.CreatedAt,
            recipe.UpdatedAt);

    private static AnimationRecipeVersionView AnimationRecipeVersionView(AnimationRecipeVersion version) =>
        new(
            version.Id,
            version.AnimationRecipeId,
            version.Version,
            version.Name,
            version.Notes,
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


    private static SpriteSheetRect RectView(int x, int y, int width, int height) =>
        new(Math.Max(0, x), Math.Max(0, y), Math.Max(1, width), Math.Max(1, height));

    private static SpriteSheetRect RectViewPreserveOrigin(int x, int y, int width, int height) =>
        new(x, y, Math.Max(1, width), Math.Max(1, height));


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

    private static string OutputAssetLabel(string batchLabel, int outputIndex, int outputCount, string fallback)
    {
        var label = Clean(batchLabel);
        if (string.IsNullOrWhiteSpace(label) || IsDefaultBatchLabel(label))
            return fallback;

        return outputCount > 1
            ? $"{label} {LabelForIndex(outputIndex)}"
            : label;
    }

    private static bool IsDefaultBatchLabel(string label) =>
        IsTimestampLabel(label, "Batch ") || IsTimestampLabel(label, "Edit ");

    private static bool IsTimestampLabel(string label, string prefix) =>
        label.StartsWith(prefix, StringComparison.Ordinal)
        && DateTime.TryParseExact(
            label[prefix.Length..],
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

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
            asset.SourceAnimationRecipeId,
            asset.SourceAnimationRecipeVersion,
            asset.IsFavorite,
            asset.ReviewStatus,
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
            batch.AnimationRecipeId,
            batch.AnimationRecipeVersion,
            batch.Status,
            batch.Error,
            batch.OutputErrorsJson,
            batch.OutputStatesJson,
            batch.CreatedAt,
            batch.ReviewCompletedBy,
            batch.ReviewCompletedAt);

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

    private static bool IsTerminalBatchStatus(GenerationBatchStatus status) =>
        status is GenerationBatchStatus.Succeeded or GenerationBatchStatus.CompletedWithErrors or GenerationBatchStatus.Failed;

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
        Guid? SourceAnimationRecipeId,
        int? SourceAnimationRecipeVersion,
        bool IsFavorite,
        AssetReviewStatus ReviewStatus,
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
        Guid? AnimationRecipeId,
        int? AnimationRecipeVersion,
        GenerationBatchStatus Status,
        string Error,
        string OutputErrorsJson,
        string OutputStatesJson,
        DateTime CreatedAt,
        AssetReviewActor? ReviewCompletedBy,
        DateTime? ReviewCompletedAt);

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

}
