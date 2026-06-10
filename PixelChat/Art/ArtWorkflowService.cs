using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    ILogger<ArtWorkflowService> logger) : IArtWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record ImagePayload(string ContentType, byte[] Data, int Width, int Height);

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
            .ToListAsync(cancellationToken);
        var batches = await db.GenerationBatches
            .AsNoTracking()
            .Where(b => b.ProjectId == selected.Id)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        var recipes = await db.PromptRecipes
            .AsNoTracking()
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
            ? new List<SpriteSheetFrameRecord>()
            : await db.SpriteSheetFrameRecords
                .AsNoTracking()
                .Where(f => f.ProjectId == selected.Id && spriteSheetIds.Contains(f.SpriteSheetDefinitionId))
                .OrderBy(f => f.Index)
                .ToListAsync(cancellationToken);
        var masks = await db.ImageMasks
            .AsNoTracking()
            .Where(m => m.ProjectId == selected.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatContextAttachments
            .AsNoTracking()
            .Where(a => a.ProjectId == selected.Id)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var assetViews = assets.Select(AssetView).ToList();
        var batchViews = batches.Select(batch => BatchView(batch, assets)).ToList();
        var recipeViews = recipes.Select(RecipeView).ToList();
        var spriteSheetFramesBySheet = spriteSheetFrameRecords
            .GroupBy(frame => frame.SpriteSheetDefinitionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SpriteSheetFrameRecord>)group.ToList());
        var spriteSheetViews = spriteSheets
            .Select(sheet => SpriteSheetView(sheet, spriteSheetFramesBySheet.GetValueOrDefault(sheet.Id, [])))
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
            spriteSheetViews,
            maskViews,
            attachmentViews,
            batchViews.FirstOrDefault(b => b.Id == selected.ActiveBatchId),
            spriteSheetViews.FirstOrDefault(s => s.Id == selected.ActiveSpriteSheetId),
            providerStatus);
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
        if (existing is not null)
        {
            var project = await GetProjectAsync(projectId, cancellationToken);
            project.ActiveSpriteSheetId = existing.Id;
            project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
            project.UpdatedAt = now;
            existing.UpdatedAt = now;
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

    public async Task<SpriteSheetDefinitionView> AutosaveSpriteSheetLayoutAsync(
        Guid projectId,
        AutosaveSpriteSheetLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        UpdateSpriteSheetDefinition(definition, request.Rows, request.Columns, request.CellWidth, request.CellHeight, request.Padding, request.Gutter, request.Fps, request.Loop);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, request.Frames, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = definition.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> NormalizeSpriteSheetAsync(
        Guid projectId,
        NormalizeSpriteSheetRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.SpriteSheetDefinitions
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == request.SpriteSheetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet was not found.");
        if (definition.OutputAssetId is not Guid workingAssetId)
            throw new InvalidOperationException("Sprite sheet working asset was not found.");

        var working = await db.ArtAssets
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == workingAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite sheet working asset was not found.");
        var parsed = ParsePngDataUrl(request.WorkingPngDataUrl, "Sprite sheet working image must be a PNG data URL.");
        UpdateSpriteSheetDefinition(definition, request.Rows, request.Columns, request.CellWidth, request.CellHeight, request.Padding, request.Gutter, request.Fps, request.Loop);
        UpdateWorkingSpriteAsset(working, parsed, definition, DateTime.UtcNow);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, request.Frames, cancellationToken);

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
            throw new InvalidOperationException("Agent sprite-sheet normalization currently requires a PNG working image.");

        var records = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .OrderBy(frame => frame.Index)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            throw new InvalidOperationException("At least one sprite frame is required.");

        var updates = records
            .Select(frame => new SpriteSheetFrameUpdateView(
                frame.Index,
                frame.Label,
                RectView(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight)))
            .ToList();
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
        if (!SpriteSheetPngCodec.TryReadRgba(working.Data, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Agent sprite-sheet frame updates currently require a PNG working image.");

        var layout = SpriteSheetServerRenderer.BuildFramePreviews(
            rgba,
            width,
            height,
            request.Rows,
            request.Columns,
            request.CellWidth,
            request.CellHeight,
            request.Padding,
            request.Gutter,
            request.Fps,
            request.Frames);
        UpdateSpriteSheetDefinition(definition, request.Rows, request.Columns, request.CellWidth, request.CellHeight, request.Padding, request.Gutter, request.Fps, request.Loop);
        await UpsertSpriteSheetFrameRecordsAsync(projectId, definition, layout.Frames, cancellationToken);

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = definition.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return await LoadSpriteSheetViewAsync(projectId, definition.Id, cancellationToken);
    }

    public async Task<SpriteSheetDefinitionView> ResetSpriteSheetToOriginalAsync(
        Guid projectId,
        Guid spriteSheetId,
        CancellationToken cancellationToken = default)
    {
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
        definition.Rows = 1;
        definition.Columns = 1;
        definition.CellWidth = Math.Max(1, source.Width ?? 128);
        definition.CellHeight = Math.Max(1, source.Height ?? 128);
        definition.Padding = 8;
        definition.Gutter = 16;
        definition.Fps = 8;
        definition.Loop = true;
        definition.FramesJson = "[]";
        definition.UpdatedAt = now;

        var frames = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .ToListAsync(cancellationToken);
        if (frames.Count > 0)
        {
            var frameIds = frames.Select(frame => frame.Id).ToList();
            var frameAttachments = await db.ChatContextAttachments
                .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.SpriteFrame && frameIds.Contains(a.RefId))
                .ToListAsync(cancellationToken);
            db.ChatContextAttachments.RemoveRange(frameAttachments);
            db.SpriteSheetFrameRecords.RemoveRange(frames);
        }

        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return SpriteSheetView(definition, []);
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
            frames = frames.Select(frame => new
            {
                frame.Index,
                label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
                sourceRect = RectView(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
                cellRect = RectView(frame.CellX, frame.CellY, frame.CellWidth, frame.CellHeight),
                spriteRect = RectView(frame.SpriteX, frame.SpriteY, frame.SpriteWidth, frame.SpriteHeight),
                pivot = new { x = 0.5, y = 1.0 },
                duration = FrameDuration(definition.Fps),
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
        var references = await ResolveAssetsAsync(projectId, request.ReferenceAssetIds, cancellationToken);
        if (references.Count > imageOptions.Value.MaxReferenceImages)
            throw new InvalidOperationException($"Select no more than {imageOptions.Value.MaxReferenceImages} reference images.");

        PromptRecipe? recipe = null;
        if (request.PromptRecipeId is Guid recipeId)
            recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
                ?? throw new InvalidOperationException("Prompt recipe was not found.");

        var batch = new GenerationBatch
        {
            ProjectId = projectId,
            Label = $"Batch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel,
            ImageModel = imageOptions.Value.DefaultImageModel,
            Prompt = prompt,
            NegativePrompt = Clean(request.NegativePrompt),
            Size = NormalizeSize(request.Size),
            Background = NormalizeBackground(request.Background),
            Count = count,
            InputAssetIdsJson = SerializeIds(references.Select(a => a.Id)),
            ParentBatchId = request.ParentBatchId,
            PromptRecipeId = recipe?.Id,
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
        var recipe = batch.PromptRecipeId is Guid recipeId
            ? await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            : null;

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
                BuildPrompt(batch.Prompt, batch.NegativePrompt, recipe, batch.Background),
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
            promptRecipeId: recipe?.Id,
            prompt: batch.Prompt,
            metadata: new
            {
                OutputIndex = outputIndex,
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
        var references = await ResolveAssetsAsync(projectId, referenceIds, cancellationToken);
        var count = ClampCount(request.Count);

        var sourceImage = ResolveEditSourceImage(sourceAsset, request.SourcePngDataUrl);
        ImageMask? storedMask = null;
        if (!string.IsNullOrWhiteSpace(request.MaskPngDataUrl))
        {
            storedMask = await UpsertAssetMaskEntityAsync(
                projectId,
                sourceAsset,
                request.MaskPngDataUrl,
                $"{sourceAsset.Label} mask",
                requireEditableArea: true,
                cancellationToken);
            ValidateEditImageAndMask(sourceImage, storedMask);
        }

        var batch = new GenerationBatch
        {
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
            Status = GenerationBatchStatus.Running,
            OutputStatesJson = SerializeOutputStates(CreateInitialOutputStates(count)),
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
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
                BuildPrompt(batch.Prompt, string.Empty, null, batch.Background),
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
            sourceAsset.SourcePromptRecipeId,
            batch.Prompt,
            new
            {
                OutputIndex = outputIndex,
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
            parent.Prompt,
            new { ParentAssetId = parent.Id });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await SetWorkspaceModeAfterAssetMutationAsync(projectId, WorkspaceMode.Edit, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
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
            .Where(m => m.ProjectId == projectId && m.AssetId == assetId)
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
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe);
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
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe);
    }

    public async Task<PromptRecipeView> DuplicatePromptRecipeAsync(
        Guid projectId,
        Guid recipeId,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        var source = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
            ?? throw new InvalidOperationException("Prompt recipe was not found.");

        var duplicate = new PromptRecipe
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? $"{source.Name} Copy" : name.Trim(),
            AssetType = source.AssetType,
            PromptTemplate = source.PromptTemplate,
            StyleRulesJson = source.StyleRulesJson,
            AvoidRulesJson = source.AvoidRulesJson,
            ExampleAssetIdsJson = source.ExampleAssetIdsJson,
            PreferredProvider = source.PreferredProvider,
            PreferredModel = source.PreferredModel,
            PreferredSize = source.PreferredSize,
            ExportDefaultsJson = source.ExportDefaultsJson,
            Notes = source.Notes,
        };
        await db.PromptRecipes.AddAsync(duplicate, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(duplicate);
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
            asset.SourcePromptRecipeId = null;

        var linkedBatches = await db.GenerationBatches
            .Where(b => b.ProjectId == projectId && b.PromptRecipeId == recipeId)
            .ToListAsync(cancellationToken);
        foreach (var batch in linkedBatches)
            batch.PromptRecipeId = null;

        db.PromptRecipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);
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

        var recipes = await db.PromptRecipes
            .Where(r => r.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        foreach (var recipe in recipes)
        {
            var exampleIds = DeserializeIds(recipe.ExampleAssetIdsJson);
            if (!exampleIds.Remove(assetId))
                continue;
            recipe.ExampleAssetIdsJson = SerializeIds(exampleIds);
            recipe.UpdatedAt = DateTime.UtcNow;
        }

        db.ArtAssets.Remove(asset);
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = DateTime.UtcNow;
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

    public async Task<string> GetWorkspaceStateJsonAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var view = await GetWorkbenchAsync(projectId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            project = view.Project,
            activeBatch = view.ActiveBatch,
            chatAttachments = view.Attachments,
            recentAssets = view.Assets.Take(12).Select(CompactAsset),
            recentBatches = view.Batches.Take(6),
            recipes = view.Recipes.Take(12),
            spriteSheets = view.SpriteSheets.Take(12).Select(CompactSpriteSheet),
            activeSpriteSheet = view.ActiveSpriteSheet is null ? null : CompactSpriteSheet(view.ActiveSpriteSheet),
            provider = view.ImageProviderStatus,
        }, JsonOptions);
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
            .Where(m => m.ProjectId == projectId && m.AssetId == asset.Id)
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
        bool loop)
    {
        definition.Rows = Math.Clamp(rows, 1, 32);
        definition.Columns = Math.Clamp(columns, 1, 64);
        definition.CellWidth = Math.Clamp(cellWidth, 1, 8192);
        definition.CellHeight = Math.Clamp(cellHeight, 1, 8192);
        definition.Padding = Math.Clamp(padding, 0, 4096);
        definition.Gutter = Math.Clamp(gutter, 0, 4096);
        definition.Fps = Math.Clamp(fps, 1, 60);
        definition.Loop = loop;
        definition.UpdatedAt = DateTime.UtcNow;
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
            frameCount = definition.FrameRecords.Count,
        }, JsonOptions);
    }

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
            frame.CellRect,
            frame.SpriteRect,
        }), JsonOptions);

        var existing = await db.SpriteSheetFrameRecords
            .Where(frame => frame.ProjectId == projectId && frame.SpriteSheetDefinitionId == definition.Id)
            .ToListAsync(cancellationToken);
        var byIndex = existing.ToDictionary(frame => frame.Index);
        var keepIndexes = normalized.Select(frame => frame.Index).ToHashSet();
        var removed = existing.Where(frame => !keepIndexes.Contains(frame.Index)).ToList();
        if (removed.Count > 0)
        {
            var removedIds = removed.Select(frame => frame.Id).ToList();
            var removedAttachments = await db.ChatContextAttachments
                .Where(a => a.ProjectId == projectId && a.Type == ChatContextAttachmentType.SpriteFrame && removedIds.Contains(a.RefId))
                .ToListAsync(cancellationToken);
            db.ChatContextAttachments.RemoveRange(removedAttachments);
            db.SpriteSheetFrameRecords.RemoveRange(removed);
        }

        var now = DateTime.UtcNow;
        foreach (var frame in normalized)
        {
            var preview = ParsePngDataUrl(frame.PreviewPngDataUrl, "Sprite frame preview must be a PNG data URL.");
            if (!byIndex.TryGetValue(frame.Index, out var record))
            {
                record = new SpriteSheetFrameRecord
                {
                    ProjectId = projectId,
                    SpriteSheetDefinitionId = definition.Id,
                    Index = frame.Index,
                    CreatedAt = now,
                };
                await db.SpriteSheetFrameRecords.AddAsync(record, cancellationToken);
            }

            record.Index = frame.Index;
            record.Label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label.Trim();
            record.SourceX = frame.SourceRect.X;
            record.SourceY = frame.SourceRect.Y;
            record.SourceWidth = frame.SourceRect.Width;
            record.SourceHeight = frame.SourceRect.Height;
            record.CellX = frame.CellRect.X;
            record.CellY = frame.CellRect.Y;
            record.CellWidth = frame.CellRect.Width;
            record.CellHeight = frame.CellRect.Height;
            record.SpriteX = frame.SpriteRect.X;
            record.SpriteY = frame.SpriteRect.Y;
            record.SpriteWidth = frame.SpriteRect.Width;
            record.SpriteHeight = frame.SpriteRect.Height;
            record.PreviewContentType = preview.ContentType;
            record.PreviewData = preview.Data;
            record.PreviewWidth = preview.Width;
            record.PreviewHeight = preview.Height;
            record.UpdatedAt = now;
        }
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
        ?? throw new InvalidOperationException("Project was not found.");

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
            Prompt = prompt,
            SourceMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
        };
    }

    private static string BuildPrompt(string prompt, string negativePrompt, PromptRecipe? recipe, string? background)
    {
        var parts = new List<string>();
        if (recipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(recipe.PromptTemplate))
                parts.Add("Recipe style template:\n" + recipe.PromptTemplate.Trim());
        }

        parts.Add(recipe is null
            ? prompt.Trim()
            : "Asset-specific request:\n" + prompt.Trim());

        if (recipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(recipe.StyleRulesJson))
            {
                var styleRules = DeserializeStrings(recipe.StyleRulesJson);
                if (styleRules.Count > 0)
                    parts.Add("Style rules: " + string.Join("; ", styleRules));
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
        recipe.ExampleAssetIdsJson = SerializeIds(request.ExampleAssetIds);
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
        recipe.ExampleAssetIdsJson = SerializeIds(request.ExampleAssetIds);
        recipe.PreferredProvider = Clean(request.PreferredProvider);
        recipe.PreferredModel = Clean(request.PreferredModel);
        recipe.PreferredSize = Clean(request.PreferredSize);
        recipe.Notes = Clean(request.Notes);
    }

    private static ImageProviderReference ToProviderReference(ArtAsset asset) =>
        new(asset.FileName, asset.ContentType, asset.Data);

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
        frames = spriteSheet.Frames.Select(frame => new
        {
            frame.Id,
            frame.Index,
            frame.Label,
            frame.SourceRect,
            frame.CellRect,
            frame.SpriteRect,
            frame.PreviewWidth,
            frame.PreviewHeight,
        }),
    };

    private static ProjectView ProjectView(Project project) =>
        new(project.Id, project.Name, project.ActiveWorkspaceMode, project.ActiveBatchId, project.ActiveSpriteSheetId);

    private static ArtAssetView AssetView(ArtAsset asset) =>
        new(
            asset.Id,
            asset.Label,
            string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
            asset.Kind,
            asset.ContentType,
            DataUrl.ToDataUrl(asset.ContentType, asset.ThumbnailData is { Length: > 0 } thumb ? thumb : asset.Data),
            asset.Width,
            asset.Height,
            asset.ParentAssetId,
            asset.SourceBatchId,
            ReadBatchOutputIndex(asset),
            asset.SourcePromptRecipeId,
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
            frames
                .OrderBy(frame => frame.Index)
                .Select(frame => FrameRecordView(spriteSheet, frame))
                .ToList(),
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
        return frames
            .Where(frame => frame.Index >= 0 && frame.Index < maxFrames)
            .OrderBy(frame => frame.Index)
            .Take(maxFrames)
            .Select(frame =>
            {
                var index = frame.Index;
                var row = index / columns;
                var column = index % columns;
                var fallbackCell = new SpriteSheetRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                return new SpriteSheetFrameView(
                    index,
                    string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label.Trim(),
                    NormalizeSpriteRect(frame.SourceRect),
                    NormalizeSpriteRect(frame.CellRect, fallbackCell),
                    NormalizeSpriteRect(frame.SpriteRect, fallbackCell),
                    frame.PreviewPngDataUrl);
            })
            .ToList();
    }

    private static SpriteSheetRect NormalizeSpriteRect(SpriteSheetRect rect) =>
        new(
            Math.Max(0, rect.X),
            Math.Max(0, rect.Y),
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height));

    private static SpriteSheetRect NormalizeSpriteRect(SpriteSheetRect rect, SpriteSheetRect fallback) =>
        rect.Width <= 0 || rect.Height <= 0 ? fallback : NormalizeSpriteRect(rect);

    private static string NormalizeCacheString(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static GenerationBatchView BatchView(GenerationBatch batch, IReadOnlyList<ArtAsset> assets)
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
            batch.Status,
            displayError,
            outputStates,
            outputErrors,
            batch.CreatedAt);
    }

    private static PromptRecipeView RecipeView(PromptRecipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.AssetType,
            recipe.PromptTemplate,
            DeserializeStrings(recipe.StyleRulesJson),
            DeserializeStrings(recipe.AvoidRulesJson),
            DeserializeIds(recipe.ExampleAssetIdsJson),
            recipe.PreferredProvider,
            recipe.PreferredModel,
            recipe.PreferredSize,
            recipe.Notes,
            recipe.CreatedAt);

    private static ImageMaskView MaskView(ImageMask mask) =>
        new(
            mask.Id,
            mask.AssetId,
            mask.Label,
            mask.ContentType,
            DataUrl.ToDataUrl(mask.ContentType, mask.Data),
            mask.Width,
            mask.Height,
            mask.CreatedAt);

    private static ChatContextAttachmentView AttachmentView(ChatContextAttachment attachment) =>
        new(attachment.Id, attachment.Type, attachment.RefId, attachment.Label, attachment.SortOrder);

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
            .ToListAsync(cancellationToken);
        return SpriteSheetView(sheet, frames);
    }

    private static SpriteSheetFrameRecordView FrameRecordView(SpriteSheetDefinition spriteSheet, SpriteSheetFrameRecord frame) =>
        new(
            frame.Id,
            spriteSheet.Id,
            frame.Index,
            string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {frame.Index + 1}" : frame.Label,
            RectView(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight),
            RectView(frame.CellX, frame.CellY, frame.CellWidth, frame.CellHeight),
            RectView(frame.SpriteX, frame.SpriteY, frame.SpriteWidth, frame.SpriteHeight),
            DataUrl.ToDataUrl(frame.PreviewContentType, frame.PreviewData),
            frame.PreviewWidth,
            frame.PreviewHeight,
            FrameDuration(spriteSheet.Fps));

    private static SpriteSheetRect RectView(int x, int y, int width, int height) =>
        new(Math.Max(0, x), Math.Max(0, y), Math.Max(1, width), Math.Max(1, height));

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
}
