using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PixelChat.Models;
using PixelChat.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PixelChat.Art;

/// <inheritdoc />
public sealed class FrameSetService(
    AppDbContext db,
    IImageProvider imageProvider,
    IOptions<ImageGenerationOptions> imageOptions,
    ILogger<FrameSetService> logger) : IFrameSetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<SourceRegionView>> DetectSourceRegionsAsync(
        Guid projectId,
        DetectSourceRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadSourceAssetAsync(projectId, request.SourceAssetId, cancellationToken);
        var detection = SpriteSheetImageAnalyzer.Detect(
            source.Id,
            source.Data,
            source.ContentType,
            source.Width,
            source.Height,
            request.ExpectedFrames,
            request.LayoutHint,
            null);

        if (detection.Frames.Count == 0)
            throw new InvalidOperationException("No source regions were detected.");

        if (request.ReplaceExisting)
        {
            var existing = await db.SpriteRegions
                .Where(r => r.ProjectId == projectId && r.SourceAssetId == source.Id)
                .ToListAsync(cancellationToken);
            db.SpriteRegions.RemoveRange(existing);
        }

        var order = 0;
        var regions = detection.Frames
            .OrderBy(frame => frame.Index)
            .Select(frame => new SpriteRegion
            {
                ProjectId = projectId,
                SourceAssetId = source.Id,
                Name = $"Region {frame.Index + 1}",
                X = frame.SourceRect.X,
                Y = frame.SourceRect.Y,
                Width = frame.SourceRect.Width,
                Height = frame.SourceRect.Height,
                ShapeJson = JsonSerializer.Serialize(frame.ShapePaths, JsonOptions),
                RegionType = "frame",
                Order = order++,
            })
            .ToList();

        await db.SpriteRegions.AddRangeAsync(regions, cancellationToken);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return regions.Select(RegionView).ToList();
    }

    public async Task<IReadOnlyList<SourceRegionView>> ListSourceRegionsAsync(
        Guid projectId,
        Guid sourceAssetId,
        CancellationToken cancellationToken = default)
    {
        _ = await LoadSourceAssetAsync(projectId, sourceAssetId, cancellationToken);
        var regions = await db.SpriteRegions
            .Where(r => r.ProjectId == projectId && r.SourceAssetId == sourceAssetId)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        return regions.Select(RegionView).ToList();
    }

    public async Task<IReadOnlyList<SourceRegionView>> SaveSourceRegionsAsync(
        Guid projectId,
        SaveSourceRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadSourceAssetAsync(projectId, request.SourceAssetId, cancellationToken);
        var (sourceWidth, sourceHeight, _) = DecodeSource(source);

        var existing = await db.SpriteRegions
            .Where(r => r.ProjectId == projectId && r.SourceAssetId == source.Id)
            .ToListAsync(cancellationToken);
        var existingById = existing.ToDictionary(region => region.Id);
        var kept = new HashSet<Guid>();
        var now = DateTime.UtcNow;

        foreach (var edit in request.Regions.OrderBy(region => region.Order))
        {
            var rect = ClampRect(edit.X, edit.Y, edit.Width, edit.Height, sourceWidth, sourceHeight);
            SpriteRegion region;
            if (edit.Id is Guid id && existingById.TryGetValue(id, out var found))
            {
                region = found;
                kept.Add(region.Id);
            }
            else
            {
                region = new SpriteRegion
                {
                    ProjectId = projectId,
                    SourceAssetId = source.Id,
                    CreatedAt = now,
                };
                await db.SpriteRegions.AddAsync(region, cancellationToken);
            }

            region.Name = string.IsNullOrWhiteSpace(edit.Name) ? $"Region {edit.Order + 1}" : edit.Name.Trim();
            region.X = rect.X;
            region.Y = rect.Y;
            region.Width = rect.Width;
            region.Height = rect.Height;
            region.ShapeJson = JsonSerializer.Serialize(NormalizeShapePaths(edit.ShapePaths, sourceWidth, sourceHeight), JsonOptions);
            region.RegionType = string.IsNullOrWhiteSpace(edit.RegionType) ? "frame" : edit.RegionType.Trim();
            region.Order = edit.Order;
            region.UpdatedAt = now;
        }

        var removed = existing.Where(region => !kept.Contains(region.Id) && !request.Regions.Any(edit => edit.Id == region.Id)).ToList();
        if (removed.Count > 0)
            db.SpriteRegions.RemoveRange(removed);

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ListSourceRegionsAsync(projectId, source.Id, cancellationToken);
    }

    public async Task<FrameSetView> CreateFrameSetFromAssetAsync(
        Guid projectId,
        CreateFrameSetFromAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        var regions = await DetectSourceRegionsAsync(projectId, new DetectSourceRegionsRequest(
            request.SourceAssetId,
            request.ExpectedFrames,
            request.LayoutHint,
            ReplaceExisting: true), cancellationToken);

        return await CreateFrameSetFromRegionsAsync(projectId, new CreateFrameSetFromRegionsRequest(
            request.SourceAssetId,
            regions.Select(region => region.Id).ToList(),
            request.Name), cancellationToken);
    }

    public async Task<FrameSetView> CreateFrameSetFromRegionsAsync(
        Guid projectId,
        CreateFrameSetFromRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadSourceAssetAsync(projectId, request.SourceAssetId, cancellationToken);
        var (_, _, sourceRgba) = DecodeSource(source);

        var regions = await db.SpriteRegions
            .Where(r => r.ProjectId == projectId && r.SourceAssetId == source.Id && request.RegionIds.Contains(r.Id))
            .OrderBy(r => r.Order)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        if (regions.Count == 0)
            throw new InvalidOperationException("Select at least one source region before creating frames.");

        var maxWidth = regions.Max(region => Math.Max(1, region.Width));
        var maxHeight = regions.Max(region => Math.Max(1, region.Height));
        var frameSet = new FrameSet
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Label} frames" : request.Name.Trim(),
            SourceAssetId = source.Id,
            DefaultCellWidth = maxWidth,
            DefaultCellHeight = maxHeight,
        };
        await db.FrameSets.AddAsync(frameSet, cancellationToken);

        var frames = new List<Frame>();
        var index = 0;
        foreach (var region in regions)
        {
            var rect = new SpriteSheetRect(region.X, region.Y, region.Width, region.Height);
            var (png, previewW, previewH) = CropToPng(sourceRgba, source.Width ?? 1, source.Height ?? 1, rect);
            frames.Add(new Frame
            {
                ProjectId = projectId,
                FrameSetId = frameSet.Id,
                SourceRegionId = region.Id,
                Index = index,
                Name = string.IsNullOrWhiteSpace(region.Name) ? $"Frame {index + 1}" : region.Name,
                SourceX = region.X,
                SourceY = region.Y,
                SourceWidth = region.Width,
                SourceHeight = region.Height,
                LogicalWidth = maxWidth,
                LogicalHeight = maxHeight,
                ContentOffsetX = Math.Max(0, (maxWidth - region.Width) / 2),
                ContentOffsetY = Math.Max(0, maxHeight - region.Height),
                DurationMs = 125,
                ShapeJson = region.ShapeJson,
                PreviewContentType = "image/png",
                PreviewData = png,
                PreviewWidth = previewW,
                PreviewHeight = previewH,
            });
            index++;
        }

        await db.Frames.AddRangeAsync(frames, cancellationToken);
        frameSet.OrderedFrameIdsJson = JsonSerializer.Serialize(frames.Select(frame => frame.Id), JsonOptions);
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveFrameSetId = frameSet.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> SetCommonCellSizeAsync(
        Guid projectId,
        SetCommonCellSizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSet.Id, cancellationToken);

        var width = request.Width;
        var height = request.Height;
        if (width <= 0 || height <= 0)
        {
            width = frames.Count > 0 ? frames.Max(f => Math.Max(1, f.SourceWidth)) : frameSet.DefaultCellWidth;
            height = frames.Count > 0 ? frames.Max(f => Math.Max(1, f.SourceHeight)) : frameSet.DefaultCellHeight;
        }

        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);

        var now = DateTime.UtcNow;
        frameSet.DefaultCellWidth = width;
        frameSet.DefaultCellHeight = height;
        frameSet.UpdatedAt = now;
        foreach (var frame in frames)
        {
            frame.LogicalWidth = width;
            frame.LogicalHeight = height;
            frame.ContentOffsetX = Math.Clamp(frame.ContentOffsetX, -width, width);
            frame.ContentOffsetY = Math.Clamp(frame.ContentOffsetY, -height, height);
            if (frame.WorkingState == "none")
            {
                frame.ContentOffsetX = Math.Max(0, (width - frame.SourceWidth) / 2);
                frame.ContentOffsetY = Math.Max(0, height - frame.SourceHeight);
            }
            frame.UpdatedAt = now;
        }

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> AddFrameFromRegionAsync(
        Guid projectId,
        AddFrameFromRegionRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var sourceAssetId = frameSet.SourceAssetId
            ?? throw new InvalidOperationException("Frame set has no source asset.");
        var region = await db.SpriteRegions.FirstOrDefaultAsync(
                r => r.ProjectId == projectId && r.SourceAssetId == sourceAssetId && r.Id == request.SourceRegionId,
                cancellationToken)
            ?? throw new InvalidOperationException("Source region was not found.");
        var source = await LoadSourceAssetAsync(projectId, sourceAssetId, cancellationToken);
        var (_, _, sourceRgba) = DecodeSource(source);
        var rect = new SpriteSheetRect(region.X, region.Y, region.Width, region.Height);
        var (png, previewW, previewH) = CropToPng(sourceRgba, source.Width ?? 1, source.Height ?? 1, rect);
        var logicalWidth = Math.Max(frameSet.DefaultCellWidth, Math.Max(1, region.Width));
        var logicalHeight = Math.Max(frameSet.DefaultCellHeight, Math.Max(1, region.Height));

        var frames = await LoadFramesAsync(projectId, frameSet.Id, cancellationToken);
        var frame = new Frame
        {
            ProjectId = projectId,
            FrameSetId = frameSet.Id,
            SourceRegionId = region.Id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? region.Name : request.Name.Trim(),
            SourceX = region.X,
            SourceY = region.Y,
            SourceWidth = Math.Max(1, region.Width),
            SourceHeight = Math.Max(1, region.Height),
            LogicalWidth = logicalWidth,
            LogicalHeight = logicalHeight,
            ContentOffsetX = Math.Max(0, (logicalWidth - region.Width) / 2),
            ContentOffsetY = Math.Max(0, logicalHeight - region.Height),
            DurationMs = 125,
            ShapeJson = region.ShapeJson,
            PreviewContentType = "image/png",
            PreviewData = png,
            PreviewWidth = previewW,
            PreviewHeight = previewH,
        };

        await db.Frames.AddAsync(frame, cancellationToken);
        frames.Insert(Math.Clamp(request.InsertAt ?? frames.Count, 0, frames.Count), frame);
        frameSet.DefaultCellWidth = Math.Max(frameSet.DefaultCellWidth, logicalWidth);
        frameSet.DefaultCellHeight = Math.Max(frameSet.DefaultCellHeight, logicalHeight);
        ReindexAndPersistOrder(frameSet, frames);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> DuplicateFrameAsync(
        Guid projectId,
        DuplicateFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSet.Id, cancellationToken);
        var source = frames.FirstOrDefault(frame => frame.Id == request.FrameId)
            ?? throw new InvalidOperationException("Frame was not found.");
        var copy = new Frame
        {
            ProjectId = projectId,
            FrameSetId = frameSet.Id,
            SourceRegionId = source.SourceRegionId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} copy" : request.Name.Trim(),
            SourceX = source.SourceX,
            SourceY = source.SourceY,
            SourceWidth = source.SourceWidth,
            SourceHeight = source.SourceHeight,
            LogicalWidth = source.LogicalWidth,
            LogicalHeight = source.LogicalHeight,
            ContentOffsetX = source.ContentOffsetX,
            ContentOffsetY = source.ContentOffsetY,
            DurationMs = source.DurationMs,
            ShapeJson = source.ShapeJson,
            WorkingState = source.WorkingState,
            WorkingContentType = source.WorkingContentType,
            WorkingData = source.WorkingData.ToArray(),
            WorkingWidth = source.WorkingWidth,
            WorkingHeight = source.WorkingHeight,
            WorkingMargin = source.WorkingMargin,
            WorkingUpdatedAt = source.WorkingUpdatedAt,
            PreviewContentType = source.PreviewContentType,
            PreviewData = source.PreviewData.ToArray(),
            PreviewWidth = source.PreviewWidth,
            PreviewHeight = source.PreviewHeight,
        };

        await db.Frames.AddAsync(copy, cancellationToken);
        var sourceIndex = frames.FindIndex(frame => frame.Id == source.Id);
        frames.Insert(Math.Clamp(request.InsertAt ?? sourceIndex + 1, 0, frames.Count), copy);
        ReindexAndPersistOrder(frameSet, frames);

        var masks = await db.ImageMasks
            .Where(mask => mask.ProjectId == projectId && mask.OwnerKind == "frame" && mask.OwnerId == source.Id)
            .ToListAsync(cancellationToken);
        foreach (var mask in masks)
        {
            await db.ImageMasks.AddAsync(new ImageMask
            {
                ProjectId = projectId,
                AssetId = mask.AssetId,
                Label = string.IsNullOrWhiteSpace(mask.Label) ? $"{copy.Name} mask" : $"{mask.Label} copy",
                ContentType = mask.ContentType,
                Data = mask.Data.ToArray(),
                Width = mask.Width,
                Height = mask.Height,
                OwnerKind = "frame",
                OwnerId = copy.Id,
                CoordinateSpace = mask.CoordinateSpace,
            }, cancellationToken);
        }

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> SetFrameLogicalCellAsync(
        Guid projectId,
        SetFrameLogicalCellRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        frame.LogicalWidth = Math.Clamp(request.Width, 1, 8192);
        frame.LogicalHeight = Math.Clamp(request.Height, 1, 8192);
        frameSet.DefaultCellWidth = Math.Max(frameSet.DefaultCellWidth, frame.LogicalWidth);
        frameSet.DefaultCellHeight = Math.Max(frameSet.DefaultCellHeight, frame.LogicalHeight);
        frame.UpdatedAt = DateTime.UtcNow;
        frameSet.UpdatedAt = frame.UpdatedAt;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> UpdateFrameSourceBoundsAsync(
        Guid projectId,
        UpdateFrameSourceBoundsRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var rect = ClampRect(request.X, request.Y, request.Width, request.Height, sourceWidth, sourceHeight);
        var shapePaths = NormalizeShapePaths(request.ShapePaths, sourceWidth, sourceHeight);
        var (preview, previewW, previewH) = CropToPng(sourceRgba, sourceWidth, sourceHeight, rect);
        var now = DateTime.UtcNow;

        frame.SourceX = rect.X;
        frame.SourceY = rect.Y;
        frame.SourceWidth = rect.Width;
        frame.SourceHeight = rect.Height;
        frame.ShapeJson = JsonSerializer.Serialize(shapePaths, JsonOptions);
        frame.PreviewData = preview;
        frame.PreviewContentType = "image/png";
        frame.PreviewWidth = previewW;
        frame.PreviewHeight = previewH;
        frame.WorkingData = [];
        frame.WorkingState = "none";
        frame.WorkingWidth = 0;
        frame.WorkingHeight = 0;
        frame.WorkingUpdatedAt = null;
        frame.UpdatedAt = now;

        if (frame.SourceRegionId is Guid regionId
            && await db.SpriteRegions.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == regionId, cancellationToken) is { } region)
        {
            region.X = rect.X;
            region.Y = rect.Y;
            region.Width = rect.Width;
            region.Height = rect.Height;
            region.ShapeJson = frame.ShapeJson;
            region.UpdatedAt = now;
        }

        frameSet.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> TranslateFrameContentAsync(
        Guid projectId,
        TranslateFrameContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        frame.ContentOffsetX = Math.Clamp(request.ContentOffsetX, -8192, 8192);
        frame.ContentOffsetY = Math.Clamp(request.ContentOffsetY, -8192, 8192);
        frame.UpdatedAt = DateTime.UtcNow;
        frameSet.UpdatedAt = frame.UpdatedAt;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> ApplyFrameEditCandidateAsync(
        Guid projectId,
        ApplyFrameEditCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        var candidate = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.CandidateAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Edit candidate asset was not found.");
        if (!ImageRgbaDecoder.TryReadRgba(candidate, out var candidateWidth, out var candidateHeight, out var candidateRgba))
            throw new InvalidOperationException("Edit candidate image could not be read.");

        var cellWidth = Math.Clamp(frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth, 1, 8192);
        var cellHeight = Math.Clamp(frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight, 1, 8192);
        var editSourceWidth = Math.Max(1, request.EditSourceWidth);
        var editSourceHeight = Math.Max(1, request.EditSourceHeight);
        var scaleX = candidateWidth / (double)editSourceWidth;
        var scaleY = candidateHeight / (double)editSourceHeight;
        var cropX = (int)Math.Round(request.CropX * scaleX);
        var cropY = (int)Math.Round(request.CropY * scaleY);
        var cropWidth = Math.Clamp((int)Math.Round(Math.Max(1, request.CropWidth) * scaleX), 1, 8192);
        var cropHeight = Math.Clamp((int)Math.Round(Math.Max(1, request.CropHeight) * scaleY), 1, 8192);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(candidateRgba, candidateWidth, candidateHeight);
        var cropped = CropToRgbaWithFill(candidateRgba, candidateWidth, candidateHeight, cropX, cropY, cropWidth, cropHeight, background);
        if (cropWidth != cellWidth || cropHeight != cellHeight)
        {
            cropped = ResizeNearest(cropped, cropWidth, cropHeight, cellWidth, cellHeight);
            cropWidth = cellWidth;
            cropHeight = cellHeight;
        }

        var cellBackground = SpriteSheetImageAnalyzer.ResolveBackground(cropped, cropWidth, cropHeight);
        var bounds = SpriteSheetImageAnalyzer.ForegroundBounds(cropped, cropWidth, cropHeight, cellBackground)
            ?? new SpriteSheetRect(0, 0, cropWidth, cropHeight);
        bounds = ClampRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, cropWidth, cropHeight);
        var (workingRgba, workingWidth, workingHeight) = CropToRgba(cropped, cropWidth, cropHeight, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var now = DateTime.UtcNow;

        frame.WorkingData = SpriteSheetPngCodec.EncodeRgba(workingWidth, workingHeight, workingRgba);
        frame.WorkingContentType = "image/png";
        frame.WorkingWidth = workingWidth;
        frame.WorkingHeight = workingHeight;
        frame.WorkingMargin = 0;
        frame.WorkingState = "edited";
        frame.WorkingUpdatedAt = now;
        frame.ContentOffsetX = bounds.X;
        frame.ContentOffsetY = bounds.Y;
        frame.UpdatedAt = now;
        frameSet.UpdatedAt = now;

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> EditFrameAsync(
        Guid projectId,
        EditFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = (request.Prompt ?? string.Empty).Trim();
        if (prompt.Length == 0)
            throw new InvalidOperationException("Frame edit prompt is required.");

        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = OrderFrames(frameSet, await LoadFramesAsync(projectId, frameSet.Id, cancellationToken)).ToList();
        var frame = frames.FirstOrDefault(f => f.Id == request.FrameId)
            ?? throw new InvalidOperationException("Frame was not found.");
        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var cellWidth = Math.Clamp(frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth, 1, 8192);
        var cellHeight = Math.Clamp(frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight, 1, 8192);
        var cellRgba = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
        var cellPng = SpriteSheetPngCodec.EncodeRgba(cellWidth, cellHeight, cellRgba);
        var (references, referenceRoleLines) = await BuildEditFrameReferenceImagesAsync(
            projectId,
            request,
            frames,
            frame,
            frameSet,
            sourceRgba,
            sourceWidth,
            sourceHeight,
            background,
            cancellationToken);
        var editPrompt = BuildEditFramePrompt(prompt, referenceRoleLines);
        var mask = request.UseFrameMask
            ? await ResolveFrameEditMaskReferenceAsync(projectId, frame, cellWidth, cellHeight, cancellationToken)
            : null;

        var options = imageOptions.Value;
        var backgroundMode = NormalizeBackgroundMode(request.Background);
        var providerResult = await imageProvider.EditAsync(new ImageProviderEditRequest(
            editPrompt,
            NormalizeSize(options.DefaultSize),
            1,
            options.DefaultMainlineModel,
            options.DefaultImageModel,
            new ImageProviderReference($"frame-{frame.Index + 1}.png", "image/png", cellPng),
            mask,
            references,
            OutputFormat: "png",
            Quality: options.DefaultQuality,
            Background: backgroundMode), cancellationToken);
        var image = providerResult.Images.FirstOrDefault()
            ?? throw new InvalidOperationException("Image provider completed without returning a frame image.");
        if (!SpriteSheetPngCodec.TryReadRgba(image.Data, out var editedWidth, out var editedHeight, out var editedRgba))
            throw new InvalidOperationException("Image provider returned a non-PNG frame image.");

        var now = DateTime.UtcNow;
        StoreEditedCellWorkingImage(frame, editedRgba, editedWidth, editedHeight, cellWidth, cellHeight, now);
        frameSet.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> EraseFrameRegionsAsync(
        Guid projectId,
        EraseFrameRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var cellWidth = Math.Clamp(frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth, 1, 8192);
        var cellHeight = Math.Clamp(frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight, 1, 8192);
        var cellRgba = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);

        var result = SpriteSheetServerRenderer.EraseRegions(
            cellRgba,
            cellWidth,
            cellHeight,
            background,
            request.Rects ?? [],
            request.Polygons,
            request.KeepSelection);
        if (!SpriteSheetPngCodec.TryReadRgba(result.PngData, out var erasedWidth, out var erasedHeight, out var erasedRgba))
            throw new InvalidOperationException("Frame erase produced an unreadable image.");

        var now = DateTime.UtcNow;
        StoreEditedCellWorkingImage(frame, erasedRgba, erasedWidth, erasedHeight, cellWidth, cellHeight, now);
        frameSet.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> ComposeFrameSetFromAssetsAsync(
        Guid projectId,
        ComposeFrameSetFromAssetsRequest request,
        CancellationToken cancellationToken = default)
    {
        var ids = (request.AssetIds ?? [])
            .Where(id => id != Guid.Empty)
            .ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Provide at least one image asset to compose into a frame set.");

        var assets = await db.ArtAssets
            .Where(a => a.ProjectId == projectId && ids.Contains(a.Id))
            .ToListAsync(cancellationToken);
        var ordered = ids
            .Select(id => assets.FirstOrDefault(a => a.Id == id))
            .Where(a => a is not null)
            .Cast<ArtAsset>()
            .ToList();
        if (ordered.Count == 0)
            throw new InvalidOperationException("None of the requested assets were found.");

        var decoded = new List<(ArtAsset Asset, int Width, int Height, byte[] Rgba)>();
        foreach (var asset in ordered)
        {
            if (!ImageRgbaDecoder.TryReadRgba(asset, out var width, out var height, out var rgba))
                throw new InvalidOperationException($"Asset '{asset.Label}' is not a readable image.");
            decoded.Add((asset, width, height, rgba));
        }

        var cellWidth = decoded.Max(item => Math.Max(1, item.Width));
        var cellHeight = decoded.Max(item => Math.Max(1, item.Height));
        var columns = decoded.Count;
        var sheetWidth = checked(columns * cellWidth);
        var sheetHeight = cellHeight;
        ValidateCanvasSize(sheetWidth, sheetHeight, "Composed sprite sheet is too large.");

        var background = SpriteSheetImageAnalyzer.ResolveBackground(decoded[0].Rgba, decoded[0].Width, decoded[0].Height);
        var sheetRgba = BuildOpaqueCell(sheetWidth, sheetHeight, background);
        for (var index = 0; index < decoded.Count; index++)
        {
            var item = decoded[index];
            var offsetX = (index * cellWidth) + Math.Max(0, (cellWidth - item.Width) / 2);
            var offsetY = Math.Max(0, cellHeight - item.Height);
            BlitInto(sheetRgba, sheetWidth, sheetHeight, item.Rgba, item.Width, item.Height, offsetX, offsetY);
        }

        var now = DateTime.UtcNow;
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Composed sprite sheet" : request.Name.Trim();
        var sheetAsset = new ArtAsset
        {
            ProjectId = projectId,
            Label = name,
            FileName = $"composed-{now:yyyyMMddHHmmss}.png",
            Kind = ArtAssetKind.SpriteSheet,
            ContentType = "image/png",
            Data = SpriteSheetPngCodec.EncodeRgba(sheetWidth, sheetHeight, sheetRgba),
            Width = sheetWidth,
            Height = sheetHeight,
            SourceMetadataJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.ArtAssets.AddAsync(sheetAsset, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var regionRequests = new List<SourceRegionEditRequest>();
        for (var index = 0; index < decoded.Count; index++)
        {
            regionRequests.Add(new SourceRegionEditRequest(
                Id: null,
                Name: decoded[index].Asset.Label,
                X: index * cellWidth,
                Y: 0,
                Width: cellWidth,
                Height: cellHeight,
                ShapePaths: null,
                RegionType: "frame",
                Order: index));
        }

        var regions = await SaveSourceRegionsAsync(projectId, new SaveSourceRegionsRequest(sheetAsset.Id, regionRequests), cancellationToken);
        return await CreateFrameSetFromRegionsAsync(
            projectId,
            new CreateFrameSetFromRegionsRequest(sheetAsset.Id, regions.Select(region => region.Id).ToList(), name),
            cancellationToken);
    }

    public async Task<NormalizeFrameScaleResult> NormalizeFrameScaleAsync(
        Guid projectId,
        NormalizeFrameScaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = OrderFrames(frameSet, await LoadFramesAsync(projectId, frameSet.Id, cancellationToken)).ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("The frame set has no frames to normalize.");

        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var measurements = frames
            .Select(frame =>
            {
                var cellWidth = Math.Clamp(frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth, 1, 8192);
                var cellHeight = Math.Clamp(frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight, 1, 8192);
                var cellRgba = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
                var bounds = SpriteSheetImageAnalyzer.ForegroundBounds(cellRgba, cellWidth, cellHeight, background);
                (double X, double Y)? centroid = bounds is null
                    ? null
                    : ForegroundCentroid(cellRgba, cellWidth, cellHeight, background, bounds);
                return new FrameScaleMeasurement(frame, cellRgba, cellWidth, cellHeight, bounds, centroid);
            })
            .ToList();

        var targetHeight = request.TargetHeight > 0
            ? Math.Clamp(request.TargetHeight, 1, 8192)
            : (int)Math.Round(Median(measurements
                .Where(measurement => measurement.Bounds is not null && measurement.Bounds.Height > 0)
                .Select(measurement => measurement.Bounds!.Height)
                .ToList()), MidpointRounding.AwayFromZero);
        var tolerancePercent = Math.Clamp(double.IsFinite(request.TolerancePercent) ? request.TolerancePercent : 2.0d, 0d, 100d);
        var anchor = NormalizeScaleAnchor(request.Anchor);
        var warnings = new List<string>();
        var results = new List<NormalizeFrameScaleFrameResult>();

        if (targetHeight <= 0)
        {
            warnings.Add("No foreground was detected in the frame set; scale normalization was skipped.");
            var unchanged = await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
            return new NormalizeFrameScaleResult(unchanged, targetHeight, tolerancePercent, anchor, results, warnings);
        }

        var now = DateTime.UtcNow;
        foreach (var measurement in measurements)
        {
            var frame = measurement.Frame;
            var oldOffsetX = frame.ContentOffsetX;
            var oldOffsetY = frame.ContentOffsetY;
            if (measurement.Bounds is not { } bounds || bounds.Height <= 0)
            {
                results.Add(new NormalizeFrameScaleFrameResult(
                    frame.Id,
                    frame.Index,
                    frame.Name,
                    null,
                    null,
                    1d,
                    Applied: false,
                    oldOffsetX,
                    oldOffsetY,
                    oldOffsetX,
                    oldOffsetY,
                    "Skipped because no foreground was detected."));
                continue;
            }

            var scaleFactor = targetHeight / (double)bounds.Height;
            var deviation = Math.Abs(bounds.Height - targetHeight) / (double)Math.Max(1, targetHeight) * 100d;
            if (deviation <= tolerancePercent)
            {
                results.Add(new NormalizeFrameScaleFrameResult(
                    frame.Id,
                    frame.Index,
                    frame.Name,
                    bounds,
                    bounds,
                    RoundMetric(scaleFactor),
                    Applied: false,
                    oldOffsetX,
                    oldOffsetY,
                    oldOffsetX,
                    oldOffsetY,
                    "Skipped because foreground height was within tolerance."));
                continue;
            }

            var (contentRgba, contentWidth, contentHeight) = CropToRgba(
                measurement.CellRgba,
                measurement.CellWidth,
                measurement.CellHeight,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
            var scaledWidth = Math.Clamp((int)Math.Round(contentWidth * scaleFactor, MidpointRounding.AwayFromZero), 1, 8192);
            var scaledHeight = Math.Clamp((int)Math.Round(contentHeight * scaleFactor, MidpointRounding.AwayFromZero), 1, 8192);
            ValidateCanvasSize(scaledWidth, scaledHeight, "Scaled frame content is too large.");
            var scaledRgba = ResizeSmoothRgba(contentRgba, contentWidth, contentHeight, scaledWidth, scaledHeight);
            var (newOffsetX, newOffsetY) = ScalePlacementOffset(bounds, measurement.Centroid, scaledWidth, scaledHeight, anchor);
            newOffsetX = Math.Clamp(newOffsetX, -8192, 8192);
            newOffsetY = Math.Clamp(newOffsetY, -8192, 8192);

            var previewCell = BuildOpaqueCell(measurement.CellWidth, measurement.CellHeight, background);
            BlitInto(previewCell, measurement.CellWidth, measurement.CellHeight, scaledRgba, scaledWidth, scaledHeight, newOffsetX, newOffsetY);
            var newBounds = SpriteSheetImageAnalyzer.ForegroundBounds(previewCell, measurement.CellWidth, measurement.CellHeight, background);

            frame.WorkingData = SpriteSheetPngCodec.EncodeRgba(scaledWidth, scaledHeight, scaledRgba);
            frame.WorkingContentType = "image/png";
            frame.WorkingWidth = scaledWidth;
            frame.WorkingHeight = scaledHeight;
            frame.WorkingMargin = 0;
            frame.WorkingState = "scaled";
            frame.WorkingUpdatedAt = now;
            frame.ContentOffsetX = newOffsetX;
            frame.ContentOffsetY = newOffsetY;
            frame.UpdatedAt = now;

            results.Add(new NormalizeFrameScaleFrameResult(
                frame.Id,
                frame.Index,
                frame.Name,
                bounds,
                newBounds,
                RoundMetric(scaleFactor),
                Applied: true,
                oldOffsetX,
                oldOffsetY,
                newOffsetX,
                newOffsetY,
                "Scaled foreground height toward the target."));
        }

        if (!results.Any(result => result.Applied))
            warnings.Add("All frames were already within the requested scale tolerance.");

        frameSet.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var view = await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
        return new NormalizeFrameScaleResult(view, targetHeight, tolerancePercent, anchor, results, warnings);
    }

    public async Task<FrameSetAnimationReviewView> BuildAnimationReviewAsync(
        Guid projectId,
        Guid frameSetId,
        int maxFrames = 12,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frames = OrderFrames(frameSet, await LoadFramesAsync(projectId, frameSet.Id, cancellationToken)).ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("The frame set has no frames to review.");

        var frameLimit = Math.Clamp(maxFrames <= 0 ? 12 : maxFrames, 1, 24);
        var limited = frames.Take(frameLimit).ToList();
        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);

        var cellWidth = Math.Max(1, limited.Max(frame => frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth));
        var cellHeight = Math.Max(1, limited.Max(frame => frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight));
        var columns = limited.Count;
        var sheetWidth = checked(columns * cellWidth);
        var sheetHeight = cellHeight;
        ValidateCanvasSize(sheetWidth, sheetHeight, "Animation review sheet is too large.");

        var sheetRgba = BuildOpaqueCell(sheetWidth, sheetHeight, background);
        var updates = new List<SpriteSheetFrameUpdateView>();
        for (var index = 0; index < limited.Count; index++)
        {
            var frame = limited[index];
            var cellRgba = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
            BlitInto(sheetRgba, sheetWidth, sheetHeight, cellRgba, cellWidth, cellHeight, index * cellWidth, 0);
            updates.Add(new SpriteSheetFrameUpdateView(
                index,
                frame.Name,
                new SpriteSheetRect(index * cellWidth, 0, cellWidth, cellHeight),
                [],
                null,
                null));
        }

        const bool loop = true;
        var reviewRender = SpriteSheetServerRenderer.BuildAnimationReview(
            sheetRgba,
            sheetWidth,
            sheetHeight,
            1,
            columns,
            cellWidth,
            cellHeight,
            0,
            0,
            "center",
            "bottom",
            background,
            updates,
            loop,
            frameLimit);
        var metrics = SpriteSheetImageAnalyzer.ComputeMotionMetrics(reviewRender.MetricFrames, background, loop);
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

        for (var index = 0; index < limited.Count; index++)
        {
            var frame = limited[index];
            if (frame.WorkingData.Length == 0)
                continue;

            var sourceCell = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight, ignoreWorking: true);
            var workingCell = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
            var overlay = SpriteSheetServerRenderer.RenderRemovedPixelsOverlay(sourceCell, background, workingCell, background, cellWidth, cellHeight);
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

        var averageDuration = limited.Average(frame => Math.Max(1, frame.DurationMs));
        var fps = Math.Clamp((int)Math.Round(1000d / averageDuration, MidpointRounding.AwayFromZero), 1, 60);
        return new FrameSetAnimationReviewView(frameSet.Id, limited.Count, 1, columns, fps, loop, metrics, images);
    }

    public async Task<AnchorAlignmentResult> AlignFramesByAnchorRectAsync(
        Guid projectId,
        AlignFramesByAnchorRectRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = OrderFrames(frameSet, await LoadFramesAsync(projectId, frameSet.Id, cancellationToken)).ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("The frame set has no frames to align.");

        var reference = frames.FirstOrDefault(frame => frame.Id == request.ReferenceFrameId)
            ?? throw new InvalidOperationException("Reference frame was not found.");
        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var referenceContent = FrameContentPixels(reference, sourceRgba, sourceWidth, sourceHeight);
        var anchor = NormalizeAnchorRect(request.AnchorRect, referenceContent.Width, referenceContent.Height);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(referenceContent.Rgba, referenceContent.Width, referenceContent.Height);
        var template = BuildAnchorTemplate(referenceContent.Rgba, referenceContent.Width, referenceContent.Height, anchor, background);
        var searchPadding = Math.Clamp(request.SearchPadding, 0, 512);
        var minScore = Math.Clamp(request.MinScore, -1d, 1d);
        var targetCenterX = reference.ContentOffsetX + anchor.X + (anchor.Width / 2);
        var targetCenterY = reference.ContentOffsetY + anchor.Y + (anchor.Height / 2);
        var warnings = template.Warnings.ToList();
        var matches = new List<AnchorAlignmentMatchView>();
        var now = DateTime.UtcNow;

        if (request.Apply)
        {
            var frameIds = frames.Select(frame => frame.Id).ToList();
            var existingAnchors = await db.Anchors
                .Where(anchorEntity => anchorEntity.ProjectId == projectId && anchorEntity.Name == "template" && frameIds.Contains(anchorEntity.FrameId))
                .ToListAsync(cancellationToken);
            db.Anchors.RemoveRange(existingAnchors);
        }

        foreach (var frame in frames)
        {
            var content = FrameContentPixels(frame, sourceRgba, sourceWidth, sourceHeight);
            var match = frame.Id == reference.Id
                ? new AnchorMatch(anchor, 1d, LowConfidence: false, [])
                : MatchAnchor(content.Rgba, content.Width, content.Height, template, anchor.X, anchor.Y, searchPadding, minScore);
            if (match.LowConfidence)
                warnings.Add($"Frame {frame.Index + 1} matched below min score {minScore:0.###}.");

            var matchCenterX = match.Rect.X + (match.Rect.Width / 2);
            var matchCenterY = match.Rect.Y + (match.Rect.Height / 2);
            var nextOffsetX = targetCenterX - matchCenterX;
            var nextOffsetY = targetCenterY - matchCenterY;
            var deltaX = nextOffsetX - frame.ContentOffsetX;
            var deltaY = nextOffsetY - frame.ContentOffsetY;

            if (request.Apply)
            {
                if (request.AxisX)
                    frame.ContentOffsetX = Math.Clamp(nextOffsetX, -8192, 8192);
                if (request.AxisY)
                    frame.ContentOffsetY = Math.Clamp(nextOffsetY, -8192, 8192);
                frame.UpdatedAt = now;

                await db.Anchors.AddAsync(new Anchor
                {
                    ProjectId = projectId,
                    FrameId = frame.Id,
                    Name = "template",
                    X = frame.ContentOffsetX + matchCenterX,
                    Y = frame.ContentOffsetY + matchCenterY,
                    Confidence = Math.Clamp(match.Score, -1d, 1d),
                    Source = match.LowConfidence ? "matched-low-confidence" : "matched",
                    CreatedAt = now,
                    UpdatedAt = now,
                }, cancellationToken);
            }

            matches.Add(new AnchorAlignmentMatchView(
                frame.Id,
                frame.Index,
                frame.Name,
                match.Rect,
                deltaX,
                deltaY,
                RoundMetric(match.Score),
                match.LowConfidence,
                match.Warnings));
        }

        if (request.Apply)
        {
            frameSet.UpdatedAt = now;
            await TouchProjectAsync(projectId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AnchorAlignmentResult(
            await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken),
            reference.Id,
            anchor,
            searchPadding,
            RoundMetric(minScore),
            request.Apply,
            matches.OrderBy(match => match.FrameIndex).ToList(),
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    public Task<FrameSetView> GetFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default) =>
        BuildFrameSetViewAsync(projectId, frameSetId, cancellationToken);

    public async Task<BuildSheetResult> BuildSheetAsync(
        Guid projectId,
        BuildSheetRequest request,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSet.Id, cancellationToken);
        if (frames.Count == 0)
            throw new InvalidOperationException("The frame set has no frames to build.");

        var source = await LoadSourceAssetAsync(projectId, frameSet.SourceAssetId ?? Guid.Empty, cancellationToken);
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var ordered = OrderFrames(frameSet, frames);
        var rows = Math.Clamp(request.Rows, 1, 32);
        var columns = request.Columns > 0
            ? Math.Clamp(request.Columns, 1, 64)
            : Math.Clamp((int)Math.Ceiling(ordered.Count / (double)rows), 1, 64);
        if (rows * columns < ordered.Count)
            rows = Math.Clamp((int)Math.Ceiling(ordered.Count / (double)columns), 1, 32);
        if (rows * columns < ordered.Count)
            throw new InvalidOperationException("Frame count exceeds the configured sheet grid.");

        var cellWidth = Math.Max(1, ordered.Max(frame => frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth));
        var cellHeight = Math.Max(1, ordered.Max(frame => frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight));
        var gutter = Math.Clamp(request.Gutter, 0, 4096);
        var outer = Math.Clamp(request.OuterMargin, 0, 4096);
        var outputWidth = checked((outer * 2) + (columns * cellWidth) + (Math.Max(0, columns - 1) * gutter));
        var outputHeight = checked((outer * 2) + (rows * cellHeight) + (Math.Max(0, rows - 1) * gutter));
        ValidateCanvasSize(outputWidth, outputHeight, "Built sprite sheet is too large.");

        var outputRgba = BuildOpaqueCell(outputWidth, outputHeight, background);
        var warnings = new List<string>();
        var manifestFrames = new List<object>();
        var ordering = NormalizeOrdering(request.Ordering);

        for (var ordinal = 0; ordinal < ordered.Count; ordinal++)
        {
            var frame = ordered[ordinal];
            var (row, column) = SheetSlot(ordinal, rows, columns, ordering);
            var destX = outer + (column * (cellWidth + gutter));
            var destY = outer + (row * (cellHeight + gutter));
            var cellRgba = RenderFrameCell(frame, frameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
            BlitInto(outputRgba, outputWidth, outputHeight, cellRgba, cellWidth, cellHeight, destX, destY);

            if (frame.ContentOffsetX < 0
                || frame.ContentOffsetY < 0
                || frame.ContentOffsetX + frame.SourceWidth > cellWidth
                || frame.ContentOffsetY + frame.SourceHeight > cellHeight)
            {
                warnings.Add($"Frame {frame.Index + 1}: content extends outside the logical cell.");
            }

            manifestFrames.Add(new
            {
                frameId = frame.Id,
                frame.Index,
                row,
                column,
                sheetX = destX,
                sheetY = destY,
                cellWidth,
                cellHeight,
                contentOffsetX = frame.ContentOffsetX,
                contentOffsetY = frame.ContentOffsetY,
                source = new { frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight },
            });
        }

        var png = SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba);
        var now = DateTime.UtcNow;
        var asset = new ArtAsset
        {
            ProjectId = projectId,
            Label = string.IsNullOrWhiteSpace(request.Name) ? $"{frameSet.Name} sheet" : request.Name.Trim(),
            FileName = $"sheet-{now:yyyyMMddHHmmss}.png",
            Kind = ArtAssetKind.SpriteSheet,
            ContentType = "image/png",
            Data = png,
            Width = outputWidth,
            Height = outputHeight,
            ParentAssetId = source.Id,
            Prompt = source.Prompt,
            SourceMetadataJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.ArtAssets.AddAsync(asset, cancellationToken);

        var layout = new SheetLayout
        {
            ProjectId = projectId,
            FrameSetId = frameSet.Id,
            Rows = rows,
            Columns = columns,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            Padding = Math.Clamp(request.Padding, 0, 4096),
            Gutter = gutter,
            OuterMargin = outer,
            Ordering = ordering,
            HorizontalAnchor = request.HorizontalAnchor,
            VerticalAnchor = request.VerticalAnchor,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.SheetLayouts.AddAsync(layout, cancellationToken);

        var manifest = JsonSerializer.Serialize(new
        {
            builtAt = now,
            frameSetId = frameSet.Id,
            rows,
            columns,
            cellWidth,
            cellHeight,
            padding = layout.Padding,
            gutter,
            outerMargin = outer,
            ordering,
            frames = manifestFrames,
        }, JsonOptions);
        var built = new BuiltSheet
        {
            ProjectId = projectId,
            SheetLayoutId = layout.Id,
            OutputAssetId = asset.Id,
            ManifestJson = manifest,
            LinkedFrameIdsJson = JsonSerializer.Serialize(ordered.Select(frame => frame.Id), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.BuiltSheets.AddAsync(built, cancellationToken);

        frameSet.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new BuildSheetResult(
            built.Id,
            layout.Id,
            asset.Id,
            rows,
            columns,
            cellWidth,
            cellHeight,
            outputWidth,
            outputHeight,
            manifest,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task<IReadOnlyList<FrameSetSummaryView>> ListFrameSetsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await db.FrameSets
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FrameSetSummaryView(
                f.Id,
                f.Name,
                f.SourceAssetId,
                f.DefaultCellWidth,
                f.DefaultCellHeight,
                f.Frames.Count,
                f.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<FrameSetView?> GetActiveFrameSetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var frameSet = project.ActiveFrameSetId is Guid activeId
            ? await db.FrameSets.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == activeId, cancellationToken)
            : null;
        frameSet ??= await db.FrameSets
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (frameSet is null)
            return null;
        if (project.ActiveFrameSetId != frameSet.Id)
        {
            project.ActiveFrameSetId = frameSet.Id;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> SetActiveFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default)
    {
        _ = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveFrameSetId = frameSetId;
        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSetId, cancellationToken);
    }

    public async Task<FrameSetView> ReorderFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, int targetIndex, CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSetId, cancellationToken);
        var moving = frames.FirstOrDefault(f => f.Id == frameId)
            ?? throw new InvalidOperationException("Frame was not found.");
        frames.Remove(moving);
        frames.Insert(Math.Clamp(targetIndex, 0, frames.Count), moving);
        ReindexAndPersistOrder(frameSet, frames);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> DeleteFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSetId, cancellationToken);
        var target = frames.FirstOrDefault(f => f.Id == frameId)
            ?? throw new InvalidOperationException("Frame was not found.");
        frames.Remove(target);
        var masks = await db.ImageMasks
            .Where(mask => mask.ProjectId == projectId && mask.OwnerKind == "frame" && mask.OwnerId == target.Id)
            .ToListAsync(cancellationToken);
        db.ImageMasks.RemoveRange(masks);
        db.Frames.Remove(target);
        ReindexAndPersistOrder(frameSet, frames);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> SetFrameDurationAsync(Guid projectId, Guid frameSetId, Guid frameId, int durationMs, CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == frameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        frame.DurationMs = Math.Clamp(durationMs, 1, 10000);
        frame.UpdatedAt = DateTime.UtcNow;
        frameSet.UpdatedAt = frame.UpdatedAt;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<FrameSetView> SetFrameOnionSkinVisibilityAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        bool hideFromOnionSkin,
        CancellationToken cancellationToken = default)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frame = await db.Frames.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.FrameSetId == frameSet.Id && f.Id == frameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        frame.HideFromOnionSkin = hideFromOnionSkin;
        frame.UpdatedAt = DateTime.UtcNow;
        frameSet.UpdatedAt = frame.UpdatedAt;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildFrameSetViewAsync(projectId, frameSet.Id, cancellationToken);
    }

    public async Task<ImageMaskView> UpsertFrameMaskAsync(
        Guid projectId,
        UpsertFrameMaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var frame = await db.Frames.Include(f => f.FrameSet).FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == request.FrameId, cancellationToken)
            ?? throw new InvalidOperationException("Frame was not found.");
        var sourceAssetId = frame.FrameSet.SourceAssetId
            ?? throw new InvalidOperationException("Frame masks require a source asset.");
        _ = await LoadSourceAssetAsync(projectId, sourceAssetId, cancellationToken);
        var parsed = DataUrl.Parse(request.MaskDataUrl);
        if (!parsed.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Frame masks must be PNG data URLs.");
        if (!SpriteSheetPngCodec.TryReadRgba(parsed.Data, out var width, out var height, out _))
            throw new InvalidOperationException("Frame mask PNG could not be read.");
        var logicalW = Math.Max(1, frame.LogicalWidth);
        var logicalH = Math.Max(1, frame.LogicalHeight);
        if (width != logicalW || height != logicalH)
            throw new InvalidOperationException($"Frame mask dimensions must match the logical frame size ({logicalW} x {logicalH}).");

        var masks = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.OwnerKind == "frame" && m.OwnerId == frame.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var mask = masks.FirstOrDefault() ?? new ImageMask
        {
            ProjectId = projectId,
            AssetId = sourceAssetId,
            OwnerKind = "frame",
            OwnerId = frame.Id,
            CreatedAt = now,
        };
        if (mask.Id == Guid.Empty)
            mask.Id = Guid.NewGuid();
        if (!db.ImageMasks.Local.Any(local => local.Id == mask.Id) && masks.Count == 0)
            await db.ImageMasks.AddAsync(mask, cancellationToken);

        mask.AssetId = sourceAssetId;
        mask.OwnerKind = "frame";
        mask.OwnerId = frame.Id;
        mask.CoordinateSpace = string.IsNullOrWhiteSpace(request.CoordinateSpace) ? "logicalFrame" : request.CoordinateSpace.Trim();
        mask.Label = string.IsNullOrWhiteSpace(request.Label) ? $"{frame.Name} mask" : request.Label.Trim();
        mask.ContentType = "image/png";
        mask.Data = parsed.Data;
        mask.Width = width;
        mask.Height = height;
        mask.UpdatedAt = now;

        if (masks.Count > 1)
            db.ImageMasks.RemoveRange(masks.Skip(1));

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return MaskView(mask);
    }

    public async Task ClearFrameMaskAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var masks = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.OwnerKind == "frame" && m.OwnerId == frameId)
            .ToListAsync(cancellationToken);
        if (masks.Count == 0)
            return;
        db.ImageMasks.RemoveRange(masks);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SpriteEditSessionView?> GetPendingSpriteEditSessionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var session = await db.SpriteEditSessions
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.Status == "pending")
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return session is null ? null : ToSpriteEditSessionView(session);
    }

    public async Task<SpriteEditSessionView> SaveSpriteEditSessionAsync(
        Guid projectId,
        SaveSpriteEditSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = await db.Projects
            .Where(project => project.Id == projectId)
            .Select(project => project.Id)
            .FirstAsync(cancellationToken);

        var session = await db.SpriteEditSessions
            .Where(item => item.ProjectId == projectId && item.Status == "pending")
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        if (session is null)
        {
            session = new SpriteEditSession
            {
                ProjectId = projectId,
                Status = "pending",
                CreatedAt = now,
            };
            await db.SpriteEditSessions.AddAsync(session, cancellationToken);
        }

        session.ModalOpen = request.ModalOpen;
        session.TargetKind = NormalizeEditTargetKind(request.TargetKind);
        session.TargetSourceAssetId = request.TargetSourceAssetId;
        session.TargetFrameSetId = request.TargetFrameSetId;
        session.TargetFrameId = request.TargetFrameId;
        session.BatchId = request.BatchId;
        session.MaskId = request.MaskId;
        session.SelectedCandidateAssetId = request.SelectedCandidateAssetId;
        session.SelectedOutputIndex = request.SelectedOutputIndex;
        session.PreviewOverlayActive = request.PreviewOverlayActive;
        session.Prompt = request.Prompt;
        session.Count = Math.Clamp(request.Count, 1, 16);
        session.CropJson = request.Crop is null ? "{}" : JsonSerializer.Serialize(request.Crop, JsonOptions);
        session.CandidateAssetIdsJson = JsonSerializer.Serialize(request.CandidateAssetIds.Distinct().ToList(), JsonOptions);
        session.OutputStatesJson = JsonSerializer.Serialize(request.OutputStates.OrderBy(state => state.OutputIndex).ToList(), JsonOptions);
        session.UpdatedAt = now;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToSpriteEditSessionView(session);
    }

    public async Task CompleteSpriteEditSessionAsync(
        Guid projectId,
        Guid sessionId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not ("accepted" or "rejected"))
            throw new InvalidOperationException("Sprite edit session status must be accepted or rejected.");

        var session = await db.SpriteEditSessions
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == sessionId && item.Status == "pending", cancellationToken);
        if (session is null)
            return;

        session.Status = normalized;
        session.ModalOpen = false;
        session.UpdatedAt = DateTime.UtcNow;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(byte[] Data, string ContentType)?> GetFrameMaskImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var mask = await db.ImageMasks
            .Where(m => m.ProjectId == projectId && m.OwnerKind == "frame" && m.OwnerId == frameId)
            .OrderByDescending(m => m.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return mask is null ? null : (mask.Data, mask.ContentType);
    }

    public async Task<(byte[] Data, string ContentType)?> GetFrameContentImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var frame = await db.Frames.Include(f => f.FrameSet).FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == frameId, cancellationToken);
        if (frame is null)
            return null;
        if (frame.WorkingData.Length > 0)
            return (frame.WorkingData, string.IsNullOrWhiteSpace(frame.WorkingContentType) ? "image/png" : frame.WorkingContentType);
        if (frame.PreviewData.Length > 0)
            return (frame.PreviewData, string.IsNullOrWhiteSpace(frame.PreviewContentType) ? "image/png" : frame.PreviewContentType);

        var source = frame.FrameSet.SourceAssetId is Guid sourceId
            ? await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceId, cancellationToken)
            : null;
        if (source is null)
            return null;
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var rect = ClampRect(frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight, sourceWidth, sourceHeight);
        var (png, _, _) = CropToPng(sourceRgba, sourceWidth, sourceHeight, rect);
        return (png, "image/png");
    }

    public async Task<(byte[] Data, string ContentType)?> GetFramePreviewImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var frame = await db.Frames.Include(f => f.FrameSet).FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == frameId, cancellationToken);
        if (frame is null)
            return null;
        var source = frame.FrameSet.SourceAssetId is Guid sourceId
            ? await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceId, cancellationToken)
            : null;
        if (source is null)
            return frame.PreviewData.Length > 0 ? (frame.PreviewData, frame.PreviewContentType) : null;
        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var width = Math.Max(1, frame.LogicalWidth > 0 ? frame.LogicalWidth : frame.FrameSet.DefaultCellWidth);
        var height = Math.Max(1, frame.LogicalHeight > 0 ? frame.LogicalHeight : frame.FrameSet.DefaultCellHeight);
        var rgba = RenderFrameCell(frame, frame.FrameSet, sourceRgba, sourceWidth, sourceHeight, background, width, height);
        return (SpriteSheetPngCodec.EncodeRgba(width, height, rgba), "image/png");
    }

    public async Task<(byte[] Data, string ContentType)?> InspectFrameAsync(
        Guid projectId,
        Guid frameId,
        SpriteSheetRect? rect,
        int scale,
        CancellationToken cancellationToken = default)
    {
        var frame = await db.Frames.Include(f => f.FrameSet).FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == frameId, cancellationToken);
        if (frame is null)
            return null;
        var source = frame.FrameSet.SourceAssetId is Guid sourceId
            ? await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceId, cancellationToken)
            : null;
        if (source is null)
            return null;

        var (sourceWidth, sourceHeight, sourceRgba) = DecodeSource(source);
        var background = SpriteSheetImageAnalyzer.ResolveBackground(sourceRgba, sourceWidth, sourceHeight);
        var cellWidth = Math.Max(1, frame.LogicalWidth > 0 ? frame.LogicalWidth : frame.FrameSet.DefaultCellWidth);
        var cellHeight = Math.Max(1, frame.LogicalHeight > 0 ? frame.LogicalHeight : frame.FrameSet.DefaultCellHeight);
        var cellRgba = RenderFrameCell(frame, frame.FrameSet, sourceRgba, sourceWidth, sourceHeight, background, cellWidth, cellHeight);
        var crop = rect is null
            ? new SpriteSheetRect(0, 0, cellWidth, cellHeight)
            : ClampRect(rect.X, rect.Y, rect.Width, rect.Height, cellWidth, cellHeight);
        var (croppedRgba, croppedWidth, croppedHeight) = CropToRgba(cellRgba, cellWidth, cellHeight, crop.X, crop.Y, crop.Width, crop.Height);
        var requestedScale = Math.Clamp(scale <= 0 ? 4 : scale, 1, 8);
        var outputWidth = checked(croppedWidth * requestedScale);
        var outputHeight = checked(croppedHeight * requestedScale);
        var longEdge = Math.Max(outputWidth, outputHeight);
        if (longEdge > 1024)
        {
            var ratio = 1024d / longEdge;
            outputWidth = Math.Max(1, (int)Math.Floor(outputWidth * ratio));
            outputHeight = Math.Max(1, (int)Math.Floor(outputHeight * ratio));
        }

        ValidateCanvasSize(outputWidth, outputHeight, "Inspected frame image is too large.");
        var outputRgba = outputWidth == croppedWidth && outputHeight == croppedHeight
            ? croppedRgba
            : ResizeNearest(croppedRgba, croppedWidth, croppedHeight, outputWidth, outputHeight);
        return (SpriteSheetPngCodec.EncodeRgba(outputWidth, outputHeight, outputRgba), "image/png");
    }

    private async Task<FrameSetView> BuildFrameSetViewAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken)
    {
        var frameSet = await LoadFrameSetAsync(projectId, frameSetId, cancellationToken);
        var frames = await LoadFramesAsync(projectId, frameSetId, cancellationToken);
        var frameIds = frames.Select(frame => frame.Id).ToList();
        var masks = await db.ImageMasks
            .Where(mask => mask.ProjectId == projectId && mask.OwnerKind == "frame" && frameIds.Contains(mask.OwnerId))
            .GroupBy(mask => mask.OwnerId)
            .Select(group => group.OrderByDescending(mask => mask.UpdatedAt).First())
            .ToDictionaryAsync(mask => mask.OwnerId, cancellationToken);
        var latestBuilt = await db.BuiltSheets
            .Where(sheet => sheet.ProjectId == projectId && sheet.SheetLayout.FrameSetId == frameSetId)
            .Include(sheet => sheet.SheetLayout)
            .OrderByDescending(sheet => sheet.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new FrameSetView(
            frameSet.Id,
            frameSet.Name,
            frameSet.SourceAssetId,
            frameSet.DefaultCellWidth,
            frameSet.DefaultCellHeight,
            frames.Count,
            latestBuilt?.OutputAssetId,
            latestBuilt?.ManifestJson,
            OrderFrames(frameSet, frames)
                .Select(frame =>
                {
                    masks.TryGetValue(frame.Id, out var mask);
                    return new FrameView(
                        frame.Id,
                        frame.SourceRegionId,
                        frame.Index,
                        frame.Name,
                        frame.SourceX,
                        frame.SourceY,
                        frame.SourceWidth,
                        frame.SourceHeight,
                        frame.LogicalWidth,
                        frame.LogicalHeight,
                        frame.ContentOffsetX,
                        frame.ContentOffsetY,
                        frame.DurationMs,
                        string.IsNullOrWhiteSpace(frame.WorkingState) ? "none" : frame.WorkingState,
                        frame.WorkingWidth,
                        frame.WorkingHeight,
                        frame.HideFromOnionSkin,
                        mask is not null,
                        mask?.Id);
                })
                .ToList());
    }

    private static IReadOnlyList<Frame> OrderFrames(FrameSet frameSet, IReadOnlyList<Frame> frames)
    {
        var byId = frames.ToDictionary(frame => frame.Id);
        var orderedIds = DeserializeIds(frameSet.OrderedFrameIdsJson);
        var ordered = orderedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        ordered.AddRange(frames.Where(frame => !orderedIds.Contains(frame.Id)).OrderBy(frame => frame.Index));
        return ordered;
    }

    private void ReindexAndPersistOrder(FrameSet frameSet, List<Frame> frames)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            frames[i].Index = i;
            frames[i].UpdatedAt = DateTime.UtcNow;
        }

        frameSet.OrderedFrameIdsJson = JsonSerializer.Serialize(frames.Select(frame => frame.Id), JsonOptions);
        frameSet.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project was not found.");

    private async Task TouchProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<ArtAsset> LoadSourceAssetAsync(Guid projectId, Guid sourceAssetId, CancellationToken cancellationToken)
    {
        if (sourceAssetId == Guid.Empty)
            throw new InvalidOperationException("Source asset was not found.");
        return await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == sourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
    }

    private async Task<FrameSet> LoadFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken) =>
        await db.FrameSets.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Id == frameSetId, cancellationToken)
            ?? throw new InvalidOperationException("Frame set was not found.");

    private async Task<List<Frame>> LoadFramesAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken) =>
        await db.Frames
            .Where(f => f.ProjectId == projectId && f.FrameSetId == frameSetId)
            .OrderBy(f => f.Index)
            .ToListAsync(cancellationToken);

    private static SourceRegionView RegionView(SpriteRegion region) =>
        new(
            region.Id,
            region.SourceAssetId,
            region.Name,
            region.X,
            region.Y,
            region.Width,
            region.Height,
            DeserializeShapePaths(region.ShapeJson),
            region.RegionType,
            region.Order);

    private static ImageMaskView MaskView(ImageMask mask) =>
        new(
            mask.Id,
            mask.AssetId,
            mask.Label,
            mask.ContentType,
            $"/media/projects/{mask.ProjectId:D}/masks/{mask.Id:D}?v={mask.UpdatedAt.Ticks}",
            mask.Width,
            mask.Height,
            mask.CreatedAt);

    private static SpriteEditSessionView ToSpriteEditSessionView(SpriteEditSession session) =>
        new(
            session.Id,
            session.Status,
            session.ModalOpen,
            NormalizeEditTargetKind(session.TargetKind),
            session.TargetSourceAssetId,
            session.TargetFrameSetId,
            session.TargetFrameId,
            session.BatchId,
            session.MaskId,
            session.SelectedCandidateAssetId,
            session.SelectedOutputIndex,
            session.PreviewOverlayActive,
            session.Prompt,
            Math.Max(1, session.Count),
            DeserializeSpriteEditCrop(session.CropJson),
            DeserializeGuidList(session.CandidateAssetIdsJson),
            DeserializeOutputStates(session.OutputStatesJson),
            session.UpdatedAt);

    private static string NormalizeEditTargetKind(string? value) =>
        string.Equals(value, "frame", StringComparison.OrdinalIgnoreCase) ? "frame" : "source";

    private static SpriteEditSessionCrop? DeserializeSpriteEditCrop(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<SpriteEditSessionCrop>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<Guid> DeserializeGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<GenerationOutputStateView> DeserializeOutputStates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<GenerationOutputStateView>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static (int Width, int Height, byte[] Rgba) DecodeSource(ArtAsset source)
    {
        if (!ImageRgbaDecoder.TryReadRgba(source, out var width, out var height, out var rgba))
            throw new InvalidOperationException("Sprite source editing requires a readable PNG or JPEG image.");
        return (width, height, rgba);
    }

    private async Task<(List<ImageProviderReference> References, List<string> RoleLines)> BuildEditFrameReferenceImagesAsync(
        Guid projectId,
        EditFrameRequest request,
        IReadOnlyList<Frame> frames,
        Frame frame,
        FrameSet frameSet,
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetBackground background,
        CancellationToken cancellationToken)
    {
        var maxReferences = Math.Max(0, imageOptions.Value.MaxReferenceImages);
        var references = new List<ImageProviderReference>();
        var roleLines = new List<string>();
        if (maxReferences == 0)
            return (references, roleLines);

        void AddReference(ImageProviderReference reference, string roleLine)
        {
            if (references.Count >= maxReferences)
                return;

            references.Add(reference);
            roleLines.Add($"Reference image {references.Count} {roleLine}");
        }

        var explicitIds = (request.ReferenceAssetIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(maxReferences)
            .ToList();
        if (explicitIds.Count > 0)
        {
            var assets = await db.ArtAssets
                .Where(asset => asset.ProjectId == projectId && explicitIds.Contains(asset.Id))
                .ToListAsync(cancellationToken);
            var byId = assets.ToDictionary(asset => asset.Id);
            for (var index = 0; index < explicitIds.Count; index++)
            {
                if (!byId.TryGetValue(explicitIds[index], out var asset))
                    continue;

                AddReference(
                    new ImageProviderReference(asset.FileName, asset.ContentType, asset.Data),
                    index == 0
                        ? "(character identity anchor): match palette, outfit, proportions exactly."
                        : "(additional identity/style reference): match only the intended identity or style facts; do not redesign the frame.");
            }
        }

        if (request.IncludeAdjacentFrames && references.Count < maxReferences)
        {
            var orderedIndex = frames.ToList().FindIndex(item => item.Id == frame.Id);
            if (orderedIndex > 0)
                AddAdjacentFrameReference(frames[orderedIndex - 1], "previous animation frame", "match character scale and motion continuity");
            if (orderedIndex >= 0 && orderedIndex < frames.Count - 1)
                AddAdjacentFrameReference(frames[orderedIndex + 1], "next animation frame", "match character scale and motion continuity");
        }

        return (references, roleLines);

        void AddAdjacentFrameReference(Frame adjacent, string role, string instruction)
        {
            if (references.Count >= maxReferences)
                return;

            var adjacentWidth = Math.Clamp(adjacent.LogicalWidth > 0 ? adjacent.LogicalWidth : frameSet.DefaultCellWidth, 1, 8192);
            var adjacentHeight = Math.Clamp(adjacent.LogicalHeight > 0 ? adjacent.LogicalHeight : frameSet.DefaultCellHeight, 1, 8192);
            var adjacentRgba = RenderFrameCell(adjacent, frameSet, sourceRgba, sourceWidth, sourceHeight, background, adjacentWidth, adjacentHeight);
            var adjacentPng = SpriteSheetPngCodec.EncodeRgba(adjacentWidth, adjacentHeight, adjacentRgba);
            AddReference(
                new ImageProviderReference($"{role.Replace(' ', '-')}-{adjacent.Index + 1}.png", "image/png", adjacentPng),
                $"({role}): {instruction}.");
        }
    }

    private async Task<ImageProviderReference?> ResolveFrameEditMaskReferenceAsync(
        Guid projectId,
        Frame frame,
        int cellWidth,
        int cellHeight,
        CancellationToken cancellationToken)
    {
        var mask = await db.ImageMasks
            .Where(item => item.ProjectId == projectId && item.OwnerKind == "frame" && item.OwnerId == frame.Id)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (mask is null)
            return null;
        if (mask.Width != cellWidth || mask.Height != cellHeight)
        {
            logger.LogWarning(
                "Skipping stale frame mask {MaskId} for frame {FrameId}: mask is {MaskWidth}x{MaskHeight} but the logical cell is {CellWidth}x{CellHeight}.",
                mask.Id,
                frame.Id,
                mask.Width,
                mask.Height,
                cellWidth,
                cellHeight);
            return null;
        }

        var fileName = string.IsNullOrWhiteSpace(mask.Label)
            ? $"frame-{frame.Index + 1}-mask.png"
            : mask.Label.Trim();
        var contentType = string.IsNullOrWhiteSpace(mask.ContentType) ? "image/png" : mask.ContentType;
        return new ImageProviderReference(fileName, contentType, mask.Data);
    }

    private static string BuildEditFramePrompt(string prompt, IReadOnlyList<string> referenceRoleLines)
    {
        if (referenceRoleLines.Count == 0)
            return prompt;

        var lines = new List<string>
        {
            "The first image is the frame cell to edit.",
        };
        lines.AddRange(referenceRoleLines);
        lines.Add("Edit only what the instruction changes.");
        lines.Add(string.Empty);
        lines.Add(prompt);
        return string.Join("\n", lines);
    }

    private static (byte[] Rgba, int Width, int Height) FrameContentPixels(
        Frame frame,
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        bool ignoreWorking = false)
    {
        if (!ignoreWorking
            && frame.WorkingData.Length > 0
            && SpriteSheetPngCodec.TryReadRgba(frame.WorkingData, out var workingW, out var workingH, out var workingRgba))
        {
            return (workingRgba, workingW, workingH);
        }

        return CropToRgba(sourceRgba, sourceWidth, sourceHeight, frame.SourceX, frame.SourceY, frame.SourceWidth, frame.SourceHeight);
    }

    private static byte[] RenderFrameCell(
        Frame frame,
        FrameSet frameSet,
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        SpriteSheetBackground background,
        int? forcedWidth = null,
        int? forcedHeight = null,
        bool ignoreWorking = false)
    {
        var cellWidth = Math.Max(1, forcedWidth ?? (frame.LogicalWidth > 0 ? frame.LogicalWidth : frameSet.DefaultCellWidth));
        var cellHeight = Math.Max(1, forcedHeight ?? (frame.LogicalHeight > 0 ? frame.LogicalHeight : frameSet.DefaultCellHeight));
        var cell = BuildOpaqueCell(cellWidth, cellHeight, background);
        var (contentRgba, contentWidth, contentHeight) = FrameContentPixels(frame, sourceRgba, sourceWidth, sourceHeight, ignoreWorking);
        var copyWholeCell = !ignoreWorking && frame.WorkingData.Length > 0 && contentWidth == cellWidth && contentHeight == cellHeight;
        BlitInto(
            cell,
            cellWidth,
            cellHeight,
            contentRgba,
            contentWidth,
            contentHeight,
            copyWholeCell ? 0 : frame.ContentOffsetX,
            copyWholeCell ? 0 : frame.ContentOffsetY);
        return cell;
    }

    private static void StoreEditedCellWorkingImage(
        Frame frame,
        byte[] rgba,
        int width,
        int height,
        int cellWidth,
        int cellHeight,
        DateTime now)
    {
        var cell = (width == cellWidth && height == cellHeight)
            ? rgba
            : ResizeNearest(rgba, width, height, cellWidth, cellHeight);
        frame.WorkingData = SpriteSheetPngCodec.EncodeRgba(cellWidth, cellHeight, cell);
        frame.WorkingContentType = "image/png";
        frame.WorkingWidth = cellWidth;
        frame.WorkingHeight = cellHeight;
        frame.WorkingMargin = 0;
        frame.WorkingState = "edited";
        frame.WorkingUpdatedAt = now;
        frame.ContentOffsetX = 0;
        frame.ContentOffsetY = 0;
        frame.UpdatedAt = now;
    }

    private static string NormalizeBackgroundMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "removable" or "removablecolor" or "removable-color" or "transparent" or "chroma" or "chromakey" or "chroma-key" => "removable",
            "auto" => "auto",
            _ => "opaque",
        };

    private static string NormalizeSize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim();

    private static (int Row, int Column) SheetSlot(int ordinal, int rows, int columns, string ordering) =>
        ordering == "columnMajor"
            ? (ordinal % rows, ordinal / rows)
            : (ordinal / columns, ordinal % columns);

    private static IReadOnlyList<Guid> DeserializeIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SpriteSheetShapePath> DeserializeShapePaths(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<SpriteSheetShapePath>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<SpriteSheetShapePath> NormalizeShapePaths(
        IReadOnlyList<SpriteSheetShapePath>? paths,
        int width,
        int height) =>
        (paths ?? [])
            .Select(path => new SpriteSheetShapePath(
                path.Points
                    .Select(point => new SpriteSheetPoint(
                        Math.Clamp(point.X, 0, Math.Max(0, width - 1)),
                        Math.Clamp(point.Y, 0, Math.Max(0, height - 1))))
                    .ToList()))
            .Where(path => path.Points.Count >= 3)
            .ToList();

    private static SpriteSheetRect ClampRect(int x, int y, int width, int height, int sourceWidth, int sourceHeight)
    {
        var rectX = Math.Clamp(x, 0, Math.Max(0, sourceWidth - 1));
        var rectY = Math.Clamp(y, 0, Math.Max(0, sourceHeight - 1));
        return new SpriteSheetRect(
            rectX,
            rectY,
            Math.Clamp(width, 1, Math.Max(1, sourceWidth - rectX)),
            Math.Clamp(height, 1, Math.Max(1, sourceHeight - rectY)));
    }

    private static string NormalizeOrdering(string? value) =>
        string.Equals(value?.Trim(), "columnMajor", StringComparison.OrdinalIgnoreCase) ? "columnMajor" : "rowMajor";

    private static byte[] BuildOpaqueCell(int width, int height, SpriteSheetBackground background)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[(i * 4) + 0] = background.R;
            rgba[(i * 4) + 1] = background.G;
            rgba[(i * 4) + 2] = background.B;
            rgba[(i * 4) + 3] = 255;
        }

        return rgba;
    }

    private static void BlitInto(
        byte[] destRgba,
        int destWidth,
        int destHeight,
        byte[] srcRgba,
        int srcWidth,
        int srcHeight,
        int offsetX,
        int offsetY)
    {
        for (var y = 0; y < srcHeight; y++)
        {
            var destY = offsetY + y;
            if (destY < 0 || destY >= destHeight)
                continue;
            for (var x = 0; x < srcWidth; x++)
            {
                var destX = offsetX + x;
                if (destX < 0 || destX >= destWidth)
                    continue;
                var srcIndex = ((y * srcWidth) + x) * 4;
                var destIndex = ((destY * destWidth) + destX) * 4;
                destRgba[destIndex + 0] = srcRgba[srcIndex + 0];
                destRgba[destIndex + 1] = srcRgba[srcIndex + 1];
                destRgba[destIndex + 2] = srcRgba[srcIndex + 2];
                destRgba[destIndex + 3] = 255;
            }
        }
    }

    private static (byte[] Png, int Width, int Height) CropToPng(byte[] sourceRgba, int sourceWidth, int sourceHeight, SpriteSheetRect rect)
    {
        var (rgba, width, height) = CropToRgba(sourceRgba, sourceWidth, sourceHeight, rect.X, rect.Y, rect.Width, rect.Height);
        return (SpriteSheetPngCodec.EncodeRgba(width, height, rgba), width, height);
    }

    private static (byte[] Rgba, int Width, int Height) CropToRgba(byte[] sourceRgba, int sourceWidth, int sourceHeight, int rectX, int rectY, int rectWidth, int rectHeight)
    {
        var x = Math.Clamp(rectX, 0, Math.Max(0, sourceWidth - 1));
        var y = Math.Clamp(rectY, 0, Math.Max(0, sourceHeight - 1));
        var width = Math.Clamp(rectWidth, 1, sourceWidth - x);
        var height = Math.Clamp(rectHeight, 1, sourceHeight - y);
        var outRgba = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Array.Copy(sourceRgba, (((y + row) * sourceWidth) + x) * 4, outRgba, row * width * 4, width * 4);
        return (outRgba, width, height);
    }

    private static byte[] CropToRgbaWithFill(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        SpriteSheetBackground background)
    {
        var outRgba = BuildOpaqueCell(rectWidth, rectHeight, background);
        for (var y = 0; y < rectHeight; y++)
        {
            var sourceY = rectY + y;
            if (sourceY < 0 || sourceY >= sourceHeight)
                continue;
            for (var x = 0; x < rectWidth; x++)
            {
                var sourceX = rectX + x;
                if (sourceX < 0 || sourceX >= sourceWidth)
                    continue;
                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                var destIndex = ((y * rectWidth) + x) * 4;
                outRgba[destIndex + 0] = sourceRgba[sourceIndex + 0];
                outRgba[destIndex + 1] = sourceRgba[sourceIndex + 1];
                outRgba[destIndex + 2] = sourceRgba[sourceIndex + 2];
                outRgba[destIndex + 3] = 255;
            }
        }

        return outRgba;
    }

    private static byte[] ResizeSmoothRgba(byte[] sourceRgba, int sourceWidth, int sourceHeight, int destWidth, int destHeight)
    {
        if (sourceWidth == destWidth && sourceHeight == destHeight)
            return (byte[])sourceRgba.Clone();

        using var image = Image.LoadPixelData<Rgba32>(sourceRgba, sourceWidth, sourceHeight);
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(destWidth, destHeight),
            Sampler = KnownResamplers.Bicubic,
        }));
        var output = new byte[destWidth * destHeight * 4];
        image.CopyPixelDataTo(output);
        for (var index = 3; index < output.Length; index += 4)
            output[index] = 255;
        return output;
    }

    private static byte[] ResizeNearest(byte[] sourceRgba, int sourceWidth, int sourceHeight, int destWidth, int destHeight)
    {
        var output = new byte[destWidth * destHeight * 4];
        for (var y = 0; y < destHeight; y++)
        {
            var sourceY = Math.Clamp((int)Math.Floor(y * (sourceHeight / (double)destHeight)), 0, sourceHeight - 1);
            for (var x = 0; x < destWidth; x++)
            {
                var sourceX = Math.Clamp((int)Math.Floor(x * (sourceWidth / (double)destWidth)), 0, sourceWidth - 1);
                var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                var destIndex = ((y * destWidth) + x) * 4;
                output[destIndex + 0] = sourceRgba[sourceIndex + 0];
                output[destIndex + 1] = sourceRgba[sourceIndex + 1];
                output[destIndex + 2] = sourceRgba[sourceIndex + 2];
                output[destIndex + 3] = 255;
            }
        }

        return output;
    }

    private static (double X, double Y) ForegroundCentroid(
        byte[] rgba,
        int width,
        int height,
        SpriteSheetBackground background,
        SpriteSheetRect bounds)
    {
        double sumX = 0;
        double sumY = 0;
        var count = 0;
        var right = Math.Min(width, bounds.X + bounds.Width);
        var bottom = Math.Min(height, bounds.Y + bounds.Height);
        for (var y = Math.Max(0, bounds.Y); y < bottom; y++)
        {
            for (var x = Math.Max(0, bounds.X); x < right; x++)
            {
                var offset = ((y * width) + x) * 4;
                if (!SpriteSheetImageAnalyzer.IsForeground(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3], background))
                    continue;

                count++;
                sumX += x + 0.5d;
                sumY += y + 0.5d;
            }
        }

        return count == 0
            ? (bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d))
            : (sumX / count, sumY / count);
    }

    private static (int X, int Y) ScalePlacementOffset(
        SpriteSheetRect bounds,
        (double X, double Y)? centroid,
        int scaledWidth,
        int scaledHeight,
        string anchor)
    {
        if (anchor == "center")
        {
            var center = centroid ?? (bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));
            var relativeX = center.X - bounds.X;
            var relativeY = center.Y - bounds.Y;
            var scaleX = scaledWidth / (double)Math.Max(1, bounds.Width);
            var scaleY = scaledHeight / (double)Math.Max(1, bounds.Height);
            return (
                (int)Math.Round(center.X - (relativeX * scaleX), MidpointRounding.AwayFromZero),
                (int)Math.Round(center.Y - (relativeY * scaleY), MidpointRounding.AwayFromZero));
        }

        var bottomCenterX = bounds.X + (bounds.Width / 2d);
        var bottomY = bounds.Y + bounds.Height;
        return (
            (int)Math.Round(bottomCenterX - (scaledWidth / 2d), MidpointRounding.AwayFromZero),
            (int)Math.Round((double)bottomY - scaledHeight, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeScaleAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "center" or "centroid" => "center",
            _ => "bottom",
        };

    private static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static SpriteSheetRect NormalizeAnchorRect(SpriteSheetRect anchor, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(anchor.X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(anchor.Y, 0, Math.Max(0, imageHeight - 1));
        var width = Math.Clamp(anchor.Width, 1, Math.Max(1, imageWidth - x));
        var height = Math.Clamp(anchor.Height, 1, Math.Max(1, imageHeight - y));
        if (width < 4 || height < 4)
            throw new InvalidOperationException("Anchor rectangle must be at least 4 x 4 pixels.");
        return new SpriteSheetRect(x, y, width, height);
    }

    private static AnchorTemplate BuildAnchorTemplate(
        byte[] rgba,
        int imageWidth,
        int imageHeight,
        SpriteSheetRect anchor,
        SpriteSheetBackground background)
    {
        var values = new double[anchor.Width * anchor.Height];
        var weights = new double[values.Length];
        var weightSum = 0d;
        var warnings = new List<string>();

        for (var y = 0; y < anchor.Height; y++)
        {
            for (var x = 0; x < anchor.Width; x++)
            {
                var sourceX = anchor.X + x;
                var sourceY = anchor.Y + y;
                if (sourceX < 0 || sourceY < 0 || sourceX >= imageWidth || sourceY >= imageHeight)
                    continue;

                var pixel = (y * anchor.Width) + x;
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

        var usefulPixels = weights.Count(weight => weight > 0);
        if (usefulPixels < Math.Max(8, values.Length / 10))
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

        return new AnchorTemplate(anchor.Width, anchor.Height, values, weights, weightSum, mean, denominator, warnings);
    }

    private static AnchorMatch MatchAnchor(
        byte[] rgba,
        int imageWidth,
        int imageHeight,
        AnchorTemplate template,
        int expectedX,
        int expectedY,
        int searchPadding,
        double minScore)
    {
        var warnings = new List<string>();
        var maxTemplateX = imageWidth - template.Width;
        var maxTemplateY = imageHeight - template.Height;
        if (maxTemplateX < 0 || maxTemplateY < 0)
        {
            warnings.Add("Frame content is smaller than the anchor rectangle.");
            return new AnchorMatch(new SpriteSheetRect(0, 0, template.Width, template.Height), -1d, LowConfidence: true, warnings);
        }

        var minX = Math.Max(0, expectedX - searchPadding);
        var minY = Math.Max(0, expectedY - searchPadding);
        var maxX = Math.Min(maxTemplateX, expectedX + searchPadding);
        var maxY = Math.Min(maxTemplateY, expectedY + searchPadding);
        if (maxX < minX || maxY < minY)
        {
            minX = 0;
            minY = 0;
            maxX = maxTemplateX;
            maxY = maxTemplateY;
            warnings.Add("Search area was too small for the anchor and was expanded.");
        }

        var bestX = Math.Clamp(expectedX, 0, maxTemplateX);
        var bestY = Math.Clamp(expectedY, 0, maxTemplateY);
        var bestScore = double.NegativeInfinity;
        for (var candidateY = minY; candidateY <= maxY; candidateY++)
        {
            for (var candidateX = minX; candidateX <= maxX; candidateX++)
            {
                var score = MatchScore(rgba, imageWidth, imageHeight, template, candidateX, candidateY);
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

        return new AnchorMatch(
            new SpriteSheetRect(bestX, bestY, template.Width, template.Height),
            bestScore,
            bestScore < minScore,
            warnings);
    }

    private static double MatchScore(byte[] rgba, int imageWidth, int imageHeight, AnchorTemplate template, int startX, int startY)
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

    private static double Luminance(byte r, byte g, byte b) =>
        (0.2126d * r) + (0.7152d * g) + (0.0722d * b);

    private static double RoundMetric(double value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static void ValidateCanvasSize(int width, int height, string message)
    {
        const int maxPixels = 8192 * 8192;
        if (width <= 0 || height <= 0 || (long)width * height > maxPixels)
            throw new InvalidOperationException(message);
    }

    private sealed record AnchorTemplate(
        int Width,
        int Height,
        double[] Values,
        double[] Weights,
        double WeightSum,
        double Mean,
        double Denominator,
        IReadOnlyList<string> Warnings);

    private sealed record AnchorMatch(
        SpriteSheetRect Rect,
        double Score,
        bool LowConfidence,
        IReadOnlyList<string> Warnings);

    private sealed record FrameScaleMeasurement(
        Frame Frame,
        byte[] CellRgba,
        int CellWidth,
        int CellHeight,
        SpriteSheetRect? Bounds,
        (double X, double Y)? Centroid);
}
