using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Art;

public sealed class AssetAnimationWorkflowService(
    AppDbContext db,
    IImageGenerationRuntime imageRuntime,
    IWebHostEnvironment environment,
    IOptions<SpriteAnimationOptions> animationOptions,
    IOptions<ImageGenerationOptions> imageOptions) : IAssetAnimationWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<AssetProfileView> CreateAssetProfileAsync(
        Guid projectId,
        CreateAssetProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var canonical = await db.ArtAssets
            .FirstOrDefaultAsync(asset => asset.ProjectId == projectId && asset.Id == request.CanonicalAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Canonical asset was not found.");
        ArtAsset? style = null;
        if (request.StyleAssetId is Guid styleId)
        {
            style = await db.ArtAssets
                .FirstOrDefaultAsync(asset => asset.ProjectId == projectId && asset.Id == styleId, cancellationToken)
                ?? throw new InvalidOperationException("Style/turnaround asset was not found.");
        }

        var (chroma, palette) = SpriteChromaSelector.Select(canonical.Data);
        var now = DateTime.UtcNow;
        var profile = new AssetProfile
        {
            ProjectId = projectId,
            CanonicalAssetId = canonical.Id,
            StyleAssetId = style?.Id,
            Label = Clean(request.Label) is { Length: > 0 } label ? label : $"{canonical.Label} profile",
            AssetType = NormalizeToken(request.AssetType, "unit"),
            StructureType = NormalizeToken(request.StructureType, DefaultStructure(request.AssetType)),
            ChromaColor = chroma,
            PaletteJson = JsonSerializer.Serialize(palette, JsonOptions),
            RequiredFeaturesJson = JsonSerializer.Serialize(request.RequiredFeatures ?? [], JsonOptions),
            ForbiddenChangesJson = JsonSerializer.Serialize(request.ForbiddenChanges ?? [], JsonOptions),
            Frozen = request.Frozen,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.AssetProfiles.AddAsync(profile, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToProfileView(profile);
    }

    public async Task<AssetAnimationJobView> PlanAssetAnimationAsync(
        Guid projectId,
        PlanAssetAnimationRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await db.AssetProfiles
            .Include(item => item.CanonicalAsset)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == request.AssetProfileId, cancellationToken)
            ?? throw new InvalidOperationException("Asset profile was not found.");

        var (targetWidth, targetHeight) = ParseSize(request.TargetCellSize, animationOptions.Value.DefaultFrameCellSize, 192, 192);
        var fps = Math.Clamp(request.Fps ?? animationOptions.Value.DefaultFps, 1, 60);
        var spec = SpriteMotionArchetypes.Build(
            profile.AssetType,
            profile.StructureType,
            request.AnimationKind,
            request.Facing ?? DefaultFacing(profile.AssetType),
            request.RootMotion ?? "in_place",
            request.FrameCount,
            fps,
            targetWidth,
            targetHeight);
        spec = ResolveMotionClipSpec(spec, request);
        var layout = BuildLayout(spec, profile.ChromaColor);
        var now = DateTime.UtcNow;
        var job = new AssetAnimationJob
        {
            ProjectId = projectId,
            AssetProfileId = profile.Id,
            Status = "planned",
            AnimationKind = spec.AnimationKind,
            Strategy = NormalizeToken(request.Strategy, "hybrid"),
            PromptSummary = Clean(request.PromptSummary),
            RecommendedAction = "render_animation_guide",
            MaxGenerationRounds = Math.Max(1, animationOptions.Value.MaxGenerationRoundsPerJob),
            MaxRepairAttemptsPerFrame = Math.Max(1, animationOptions.Value.MaxRepairAttemptsPerFrame),
            AnimationSpecJson = JsonSerializer.Serialize(spec, JsonOptions),
            LayoutSpecJson = JsonSerializer.Serialize(layout, JsonOptions),
            FrameStatusesJson = JsonSerializer.Serialize(spec.Frames.Select(frame => new AssetAnimationFrameStatusView(
                frame.Index + 1,
                frame.Index,
                "planned",
                frame.PoseName,
                "render_guide")).ToList(), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await db.AssetAnimationJobs.AddAsync(job, cancellationToken);
        await AddEventAsync(projectId, job.Id, "planned", "info", "Animation plan compiled.", new
        {
            job.AnimationKind,
            spec.FrameCount,
            spec.Fps,
            layout.Rows,
            layout.Columns,
            canvas = $"{layout.CanvasWidth}x{layout.CanvasHeight}",
            guideRenderer = spec.GuideRenderer ?? "sprite_guide_renderer",
            motionClipId = spec.MotionClipId,
            sourcePackage = spec.GuideSourcePackage,
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> RenderAnimationGuideAsync(
        Guid projectId,
        Guid assetAnimationJobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadJobEntityAsync(projectId, assetAnimationJobId, includeProfile: true, cancellationToken);
        var spec = ReadAnimationSpec(job);
        var layout = ReadLayoutSpec(job);
        var motionRender = RenderMotionGuideIfAvailable(spec, layout);
        if (motionRender is not null)
        {
            spec = ApplyMotionSampleMetadata(spec, motionRender.Samples);
            job.AnimationSpecJson = JsonSerializer.Serialize(spec, JsonOptions);
        }

        var guide = motionRender?.GuidePng ?? SpriteGuideRenderer.Render(layout, spec);
        var diagnostic = motionRender?.DiagnosticPng ?? SpriteGuideRenderer.Render(layout, spec, diagnostic: true);
        var now = DateTime.UtcNow;
        var renderer = motionRender?.Metadata.Renderer ?? "sprite_guide_renderer";
        var guideAsset = CreateAsset(
            projectId,
            $"{job.AnimationKind} structure guide",
            $"animation-guide-{now:yyyyMMddHHmmss}.png",
            ArtAssetKind.SpriteGuide,
            "image/png",
            guide,
            job.AssetProfile.CanonicalAssetId,
            sourceBatchId: null,
            prompt: string.Empty,
            new
            {
                Source = "asset-animation-guide",
                Renderer = renderer,
                AssetAnimationJobId = job.Id,
                job.AnimationKind,
                MotionClipId = motionRender?.Metadata.ClipId,
                MotionClipAnimation = motionRender?.Metadata.AnimationName,
                MotionClipSourcePackage = motionRender?.Metadata.SourcePackage,
                MotionClipSourceUrl = motionRender?.Metadata.SourceUrl,
                MotionClipSourceLicense = motionRender?.Metadata.SourceLicense,
                MotionClipSampleCount = motionRender?.Metadata.SampleCount,
                CameraYawDegrees = motionRender?.Metadata.CameraYawDegrees,
                layout.Rows,
                layout.Columns,
            });
        var diagnosticAsset = CreateAsset(
            projectId,
            $"{job.AnimationKind} diagnostic guide",
            $"animation-guide-diagnostic-{now:yyyyMMddHHmmss}.png",
            ArtAssetKind.SpriteGuide,
            "image/png",
            diagnostic,
            job.AssetProfile.CanonicalAssetId,
            sourceBatchId: null,
            prompt: string.Empty,
            new
            {
                Source = "asset-animation-diagnostic-guide",
                Renderer = renderer,
                AssetAnimationJobId = job.Id,
                MotionClipId = motionRender?.Metadata.ClipId,
                MotionClipSampleCount = motionRender?.Metadata.SampleCount,
                CameraYawDegrees = motionRender?.Metadata.CameraYawDegrees,
            });
        await db.ArtAssets.AddRangeAsync([guideAsset, diagnosticAsset], cancellationToken);
        job.GuideAssetId = guideAsset.Id;
        job.DiagnosticGuideAssetId = diagnosticAsset.Id;
        job.Status = "guides_ready";
        job.RecommendedAction = "run_animation_candidates";
        job.UpdatedAt = now;
        await AddEventAsync(projectId, job.Id, "guide_rendered", "info", "Guide and diagnostic guide rendered.", new
        {
            guideAssetId = guideAsset.Id,
            diagnosticGuideAssetId = diagnosticAsset.Id,
            canvas = $"{layout.CanvasWidth}x{layout.CanvasHeight}",
            renderer,
            motionClipId = motionRender?.Metadata.ClipId,
            sourcePackage = motionRender?.Metadata.SourcePackage,
            sourceLicense = motionRender?.Metadata.SourceLicense,
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> RunAnimationCandidatesAsync(
        Guid projectId,
        RunAnimationCandidatesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (imageRuntime.HasRunningBatch(projectId))
            throw new InvalidOperationException("An image generation batch is already running for this project.");

        var job = await LoadJobEntityAsync(projectId, request.AssetAnimationJobId, includeProfile: true, cancellationToken);
        if (job.GuideAssetId is not Guid guideAssetId)
            throw new InvalidOperationException("Render an animation guide before generating candidates.");
        if (job.GenerationRoundsUsed >= job.MaxGenerationRounds)
            throw new InvalidOperationException("The animation job generation budget is exhausted.");

        var spec = ReadAnimationSpec(job);
        var layout = ReadLayoutSpec(job);
        var referenceIds = new List<Guid> { guideAssetId, job.AssetProfile.CanonicalAssetId };
        if (job.AssetProfile.StyleAssetId is Guid styleAssetId)
            referenceIds.Add(styleAssetId);
        var count = Math.Clamp(request.CandidateCount ?? animationOptions.Value.MaxCandidateSheets, 1, Math.Max(1, animationOptions.Value.MaxCandidateSheets));
        count = Math.Min(count, Math.Max(1, job.MaxGenerationRounds - job.GenerationRoundsUsed));
        var batch = await imageRuntime.StartGenerateImagesAsync(projectId, new GenerateImagesRequest(
            BuildCandidatePrompt(job, spec, layout),
            string.Empty,
            $"{layout.CanvasWidth}x{layout.CanvasHeight}",
            count,
            "opaque",
            PromptRecipeId: null,
            referenceIds,
            ParentBatchId: null,
            ImageModel: PreferredAnimationModel()), cancellationToken);
        job.GenerationRoundsUsed += count;
        job.Status = "generating";
        job.RecommendedAction = "wait_for_candidates";
        job.UpdatedAt = DateTime.UtcNow;
        await AddEventAsync(projectId, job.Id, "candidate_batch_started", "info", "Candidate batch started.", new
        {
            batchId = batch.Id,
            count,
            requestedSize = $"{layout.CanvasWidth}x{layout.CanvasHeight}",
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await imageRuntime.WaitForBatchCompletionAsync(batch.Id, TimeSpan.FromSeconds(Math.Max(60, imageOptions.Value.RequestTimeoutSeconds)), cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return await LoadJobViewAsync(projectId, job.Id, CancellationToken.None);
        var outputAssets = await db.ArtAssets
            .Where(asset => asset.ProjectId == projectId && asset.SourceBatchId == batch.Id)
            .OrderBy(asset => asset.CreatedAt)
            .ToListAsync(cancellationToken);
        var existingCandidateCount = await db.AssetAnimationCandidates
            .Where(candidate => candidate.ProjectId == projectId && candidate.AssetAnimationJobId == job.Id)
            .CountAsync(cancellationToken);
        var candidates = new List<AssetAnimationCandidate>();
        foreach (var (asset, index) in outputAssets.Select((asset, index) => (asset, index)))
        {
            var frameStatuses = SpriteQualityInspector.InspectRawCandidate(asset.Data, layout);
            var rawStatus = frameStatuses.Any(frame => frame.Status is "reject" or "repair_requested") ? "warning" : "pass";
            var dimensions = ActualDimensions(asset);
            var scaledLayout = SpriteLayoutScaler.ScaleToSource(layout, dimensions.Width, dimensions.Height);
            candidates.Add(new AssetAnimationCandidate
            {
                ProjectId = projectId,
                AssetAnimationJobId = job.Id,
                GenerationBatchId = batch.Id,
                OutputAssetId = asset.Id,
                CandidateIndex = existingCandidateCount + index,
                State = index == 0 ? "selected" : "generated",
                RawQaStatus = rawStatus,
                RawQaSummaryJson = JsonSerializer.Serialize(frameStatuses, JsonOptions),
            });
            await AddEventAsync(projectId, job.Id, "candidate_completed", scaledLayout.Scaled ? "warning" : "info", scaledLayout.Scaled ? scaledLayout.Warning : "Candidate completed.", new
            {
                assetId = asset.Id,
                batchId = batch.Id,
                candidateIndex = existingCandidateCount + index,
                rawStatus,
                actualSize = $"{dimensions.Width}x{dimensions.Height}",
                requestedSize = $"{layout.CanvasWidth}x{layout.CanvasHeight}",
            }, cancellationToken);
        }

        if (candidates.Count > 0)
        {
            await db.AssetAnimationCandidates.AddRangeAsync(candidates, cancellationToken);
            job.SelectedCandidateId ??= candidates[0].Id;
            job.FrameStatusesJson = candidates[0].RawQaSummaryJson;
            job.Status = "raw_qa";
            job.RecommendedAction = candidates[0].RawQaStatus == "pass" ? "extract_animation_fixed_slots" : "mark_animation_frames";
        }
        else
        {
            job.Status = "repair_required";
            job.RecommendedAction = "run_animation_candidates";
            await AddEventAsync(projectId, job.Id, "candidate_batch_failed", "error", "Candidate batch completed without usable outputs.", new
            {
                batchId = batch.Id,
            }, cancellationToken);
        }

        job.UpdatedAt = DateTime.UtcNow;
        await AddEventAsync(projectId, job.Id, "raw_qa", candidates.Count == 0 ? "error" : "info", candidates.Count == 0 ? "No candidate outputs available." : "Raw candidate QA recorded.", new
        {
            candidateCount = candidates.Count,
            selectedCandidateId = job.SelectedCandidateId,
            next = job.RecommendedAction,
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> MarkAnimationFramesAsync(
        Guid projectId,
        MarkAnimationFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadJobEntityAsync(projectId, request.AssetAnimationJobId, includeProfile: false, cancellationToken);
        var spec = ReadAnimationSpec(job);
        var existing = ReadFrameStatuses(job).ToDictionary(status => status.Index);
        foreach (var frame in request.Frames)
        {
            var index = Math.Max(0, frame.FrameNumber - 1);
            existing[index] = new AssetAnimationFrameStatusView(
                index + 1,
                index,
                NormalizeFrameStatus(frame.Status),
                Clean(frame.Reason),
                RecommendedActionForStatus(frame.Status, frame.Reason, spec));
            var attemptNumber = await NextAttemptNumberAsync(projectId, job.Id, index, cancellationToken);
            await db.AssetAnimationFrameAttempts.AddAsync(new AssetAnimationFrameAttempt
            {
                ProjectId = projectId,
                AssetAnimationJobId = job.Id,
                FrameIndex = index,
                AttemptNumber = attemptNumber,
                AttemptKind = "mark",
                Status = NormalizeFrameStatus(frame.Status),
                FailureReason = Clean(frame.Reason),
                RepairHistoryJson = JsonSerializer.Serialize(new[] { Clean(frame.Reason) }, JsonOptions),
            }, cancellationToken);
            await AddEventAsync(projectId, job.Id, "frame_marked", "info", $"Frame {index + 1} marked {NormalizeFrameStatus(frame.Status)}.", new
            {
                frameNumber = index + 1,
                status = NormalizeFrameStatus(frame.Status),
                reason = Clean(frame.Reason),
            }, cancellationToken);
        }

        var statuses = existing.Values.OrderBy(status => status.Index).ToList();
        job.FrameStatusesJson = JsonSerializer.Serialize(statuses, JsonOptions);
        job.Status = statuses.Any(status => status.Status == "repair_requested") ? "repair_required" : "frame_qa";
        job.RecommendedAction = statuses.Any(status => status.RecommendedAction == "run_animation_candidates")
            ? "run_animation_candidates"
            : statuses.Any(status => status.Status == "repair_requested")
                ? "regenerate_animation_frames"
                : "extract_animation_fixed_slots";
        job.UpdatedAt = DateTime.UtcNow;
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> RegenerateAnimationFramesAsync(
        Guid projectId,
        RegenerateAnimationFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (imageRuntime.HasRunningBatch(projectId))
            throw new InvalidOperationException("An image generation batch is already running for this project.");

        var job = await LoadJobEntityAsync(projectId, request.AssetAnimationJobId, includeProfile: true, cancellationToken);
        var spec = ReadAnimationSpec(job);
        var layout = ReadLayoutSpec(job);
        var frames = request.FrameNumbers
            .Where(number => number > 0 && number <= spec.FrameCount)
            .Distinct()
            .Take(8)
            .ToList();
        if (frames.Count == 0)
            throw new InvalidOperationException("At least one valid frame number is required.");

        var selectedCandidateReferenceId = job.SelectedCandidateId is Guid selectedCandidateId
            ? await db.AssetAnimationCandidates
                .AsNoTracking()
                .Where(candidate => candidate.ProjectId == projectId && candidate.Id == selectedCandidateId)
                .Select(candidate => candidate.OutputAssetId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var repairedAssets = new Dictionary<int, Guid>();
        foreach (var frameNumber in frames)
        {
            if (job.GenerationRoundsUsed >= job.MaxGenerationRounds)
                break;
            var frameSpec = spec.Frames[frameNumber - 1];
            var singleLayout = BuildSingleFrameLayout(spec, layout, frameSpec);
            var guideBytes = SpriteGuideRenderer.Render(singleLayout, spec with { Frames = [frameSpec with { Index = 0 }] });
            var now = DateTime.UtcNow;
            var guideAsset = CreateAsset(
                projectId,
                $"{job.AnimationKind} frame {frameNumber} repair guide",
                $"animation-frame-{frameNumber}-guide-{now:yyyyMMddHHmmss}.png",
                ArtAssetKind.SpriteGuide,
                "image/png",
                guideBytes,
                job.AssetProfile.CanonicalAssetId,
                sourceBatchId: null,
                prompt: string.Empty,
                new
                {
                    Source = "asset-animation-frame-repair-guide",
                    AssetAnimationJobId = job.Id,
                    FrameNumber = frameNumber,
                });
            await db.ArtAssets.AddAsync(guideAsset, cancellationToken);
            await AddEventAsync(projectId, job.Id, "repair_guide_rendered", "info", $"Frame {frameNumber} repair guide rendered.", new
            {
                frameNumber,
                guideAssetId = guideAsset.Id,
            }, cancellationToken);
            await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var references = new List<Guid> { guideAsset.Id, job.AssetProfile.CanonicalAssetId };
            if (selectedCandidateReferenceId is Guid candidateReferenceId)
                references.Add(candidateReferenceId);
            if (job.AssetProfile.StyleAssetId is Guid styleAssetId)
                references.Add(styleAssetId);
            var batch = await imageRuntime.StartGenerateImagesAsync(projectId, new GenerateImagesRequest(
                BuildFrameRepairPrompt(job, spec, frameSpec, layout, request.Prompt),
                string.Empty,
                $"{singleLayout.CanvasWidth}x{singleLayout.CanvasHeight}",
                1,
                "opaque",
                PromptRecipeId: null,
                references,
                ParentBatchId: null,
                ImageModel: PreferredAnimationModel()), cancellationToken);
            job.GenerationRoundsUsed++;
            job.Status = "generating";
            job.RecommendedAction = "wait_for_frame_repair";
            job.UpdatedAt = DateTime.UtcNow;
            await AddEventAsync(projectId, job.Id, "repair_batch_started", "info", $"Frame {frameNumber} repair generation started.", new
            {
                frameNumber,
                batchId = batch.Id,
                requestedSize = $"{singleLayout.CanvasWidth}x{singleLayout.CanvasHeight}",
            }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await imageRuntime.WaitForBatchCompletionAsync(batch.Id, TimeSpan.FromSeconds(Math.Max(60, imageOptions.Value.RequestTimeoutSeconds)), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                break;
            var output = await db.ArtAssets
                .Where(asset => asset.ProjectId == projectId && asset.SourceBatchId == batch.Id)
                .OrderBy(asset => asset.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (output is null)
            {
                await AddEventAsync(projectId, job.Id, "repair_batch_failed", "error", $"Frame {frameNumber} repair generation produced no output.", new
                {
                    frameNumber,
                    batchId = batch.Id,
                }, cancellationToken);
                continue;
            }
            repairedAssets[frameNumber - 1] = output.Id;

            var attemptNumber = await NextAttemptNumberAsync(projectId, job.Id, frameNumber - 1, cancellationToken);
            await db.AssetAnimationFrameAttempts.AddAsync(new AssetAnimationFrameAttempt
            {
                ProjectId = projectId,
                AssetAnimationJobId = job.Id,
                AssetAnimationCandidateId = job.SelectedCandidateId,
                FrameIndex = frameNumber - 1,
                AttemptNumber = attemptNumber,
                AttemptKind = "regenerate_frame",
                Status = "repair_generated",
                SourceAssetId = output.Id,
                SourceX = 0,
                SourceY = 0,
                SourceWidth = output.Width ?? singleLayout.CanvasWidth,
                SourceHeight = output.Height ?? singleLayout.CanvasHeight,
                RepairHistoryJson = JsonSerializer.Serialize(new[] { "single-frame regeneration" }, JsonOptions),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }, cancellationToken);
            await AddEventAsync(projectId, job.Id, "repair_frame_generated", "info", $"Frame {frameNumber} repair output saved.", new
            {
                frameNumber,
                assetId = output.Id,
                batchId = batch.Id,
                actualSize = $"{output.Width ?? singleLayout.CanvasWidth}x{output.Height ?? singleLayout.CanvasHeight}",
            }, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
            return await LoadJobViewAsync(projectId, job.Id, CancellationToken.None);

        var statuses = ReadFrameStatuses(job)
            .Select(status =>
            {
                if (!frames.Contains(status.FrameNumber))
                    return status;
                if (repairedAssets.TryGetValue(status.Index, out var sourceAssetId))
                {
                    return status with
                    {
                        Status = "repair_generated",
                        Reason = "single-frame regeneration available",
                        RecommendedAction = "extract_animation_fixed_slots",
                        SourceAssetId = sourceAssetId,
                    };
                }

                return status with
                {
                    Status = "repair_requested",
                    Reason = "single-frame regeneration did not produce an output",
                    RecommendedAction = "regenerate_animation_frames",
                    SourceAssetId = null,
                };
            })
            .ToList();
        job.FrameStatusesJson = JsonSerializer.Serialize(statuses, JsonOptions);
        job.Status = "repair_required";
        job.RecommendedAction = "extract_animation_fixed_slots";
        job.UpdatedAt = DateTime.UtcNow;
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> ExtractAnimationFixedSlotsAsync(
        Guid projectId,
        ExtractAnimationFixedSlotsRequest request,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadJobEntityAsync(projectId, request.AssetAnimationJobId, includeProfile: true, cancellationToken);
        var candidate = request.CandidateId is Guid requestedCandidateId
            ? await db.AssetAnimationCandidates.FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == requestedCandidateId, cancellationToken)
            : await ResolveSelectedCandidateAsync(projectId, job, cancellationToken);
        if (candidate?.OutputAssetId is not Guid outputAssetId)
            throw new InvalidOperationException("No generated candidate image is available for fixed-slot extraction.");
        var source = await db.ArtAssets.FirstOrDefaultAsync(asset => asset.ProjectId == projectId && asset.Id == outputAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Candidate output asset was not found.");

        var spec = ReadAnimationSpec(job);
        var layout = ReadLayoutSpec(job);
        var overrides = await LoadFrameOverridesAsync(projectId, job.Id, cancellationToken);
        SpriteFixedSlotExtractionResult extraction;
        try
        {
            extraction = SpriteFrameExtractor.Extract(source.Data, layout, spec, overrides);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            var failedStatuses = spec.Frames
                .Select(frame => new AssetAnimationFrameStatusView(
                    frame.Index + 1,
                    frame.Index,
                    "repair_requested",
                    $"extraction failed: {ex.Message}",
                    "run_animation_candidates"))
                .ToList();
            job.SelectedCandidateId = candidate.Id;
            job.FrameQaSummaryJson = JsonSerializer.Serialize(failedStatuses, JsonOptions);
            job.FrameStatusesJson = JsonSerializer.Serialize(failedStatuses, JsonOptions);
            job.Status = "repair_required";
            job.RecommendedAction = "run_animation_candidates";
            job.UpdatedAt = DateTime.UtcNow;
            await AddEventAsync(projectId, job.Id, "extraction_failed", "error", ex.Message, new
            {
                candidateId = candidate.Id,
                outputAssetId,
                actualSize = $"{source.Width ?? 0}x{source.Height ?? 0}",
                plannedSize = $"{layout.CanvasWidth}x{layout.CanvasHeight}",
            }, cancellationToken);
            await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
        }
        var sheet = await SaveFixedSlotSpriteSheetAsync(projectId, job, candidate, source, extraction, cancellationToken);
        var statuses = SpriteQualityInspector.InspectExtracted(extraction);
        job.OutputSpriteSheetId = sheet.Id;
        job.SelectedCandidateId = candidate.Id;
        job.FrameQaSummaryJson = JsonSerializer.Serialize(statuses, JsonOptions);
        job.FrameStatusesJson = JsonSerializer.Serialize(statuses, JsonOptions);
        job.Status = statuses.Any(status => status.Status == "repair_requested") ? "repair_required" : "motion_qa";
        job.RecommendedAction = statuses.Any(status => status.Status == "repair_requested")
            ? MotionClipCatalog.IsExternalMotionSpec(spec) ? "run_animation_candidates" : "regenerate_animation_frames"
            : "review_animation_job";
        job.UpdatedAt = DateTime.UtcNow;
        await AddEventAsync(projectId, job.Id, "extraction_completed", string.IsNullOrWhiteSpace(extraction.LayoutWarning) ? "info" : "warning", string.IsNullOrWhiteSpace(extraction.LayoutWarning) ? "Fixed-slot extraction completed." : extraction.LayoutWarning, new
        {
            candidateId = candidate.Id,
            outputSpriteSheetId = sheet.Id,
            frameCount = extraction.Frames.Count,
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> ReviewAnimationJobAsync(
        Guid projectId,
        Guid assetAnimationJobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadJobEntityAsync(projectId, assetAnimationJobId, includeProfile: false, cancellationToken);
        if (job.OutputSpriteSheetId is null)
            throw new InvalidOperationException("Extract fixed slots before reviewing motion.");

        var spec = ReadAnimationSpec(job);
        var motionQa = await InspectMotionAsync(projectId, job, spec, cancellationToken);
        job.Status = "motion_qa";
        job.RecommendedAction = motionQa.Status == "pass" ? "package_animation_job" : motionQa.RecommendedAction;
        job.MotionQaSummaryJson = JsonSerializer.Serialize(motionQa, JsonOptions);
        job.UpdatedAt = DateTime.UtcNow;
        await AddEventAsync(projectId, job.Id, "motion_qa", motionQa.Status == "pass" ? "info" : "warning", motionQa.Summary, new
        {
            job.OutputSpriteSheetId,
            next = job.RecommendedAction,
            motionQa.Status,
            motionQa.Failures,
        }, cancellationToken);
        await SetProjectModeAsync(projectId, WorkspaceMode.Runs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public async Task<AssetAnimationJobView> PackageAnimationJobAsync(
        Guid projectId,
        Guid assetAnimationJobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadJobEntityAsync(projectId, assetAnimationJobId, includeProfile: false, cancellationToken);
        if (job.OutputSpriteSheetId is null)
            throw new InvalidOperationException("Extract fixed slots before packaging.");
        var spec = ReadAnimationSpec(job);
        if (MotionClipCatalog.IsExternalMotionSpec(spec) && !MotionQaAllowsPackaging(job.MotionQaSummaryJson))
            throw new InvalidOperationException("Run review_animation_job and resolve motion QA before packaging this sampled-motion animation.");

        await SetCompareReviewAsync(projectId, job, cancellationToken);
        var project = await db.Projects.FirstAsync(project => project.Id == projectId, cancellationToken);
        project.ActiveSpriteSheetId = job.OutputSpriteSheetId;
        project.ActiveWorkspaceMode = WorkspaceMode.Compare;
        project.UpdatedAt = DateTime.UtcNow;
        job.Status = "packaged";
        job.RecommendedAction = "done";
        job.UpdatedAt = DateTime.UtcNow;
        await AddEventAsync(projectId, job.Id, "package_completed", "info", "Animation packaged for review.", new
        {
            job.OutputSpriteSheetId,
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadJobViewAsync(projectId, job.Id, cancellationToken);
    }

    public Task<string> ListMotionClipsJsonAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            renderer = MotionClipCatalog.RendererId,
            defaultHumanoidWalkClipId = MotionClipCatalog.DefaultHumanoidWalkClipId,
            clips = catalog.Clips.Select(clip => new
            {
                clip.ClipId,
                clip.Aliases,
                clip.DisplayName,
                clip.AnimationName,
                clip.SourcePackage,
                clip.SourceUrl,
                clip.License,
                supportedAnimationKinds = clip.SupportedAnimationKinds,
                supportedAssetTypes = clip.SupportedAssetTypes,
                allowedSampleCounts = clip.AllowedSampleCounts,
                defaultFps = clip.DefaultFps,
                available = File.Exists(catalog.ResolveAssetPath(clip)),
            }),
        }, JsonOptions));
    }

    public async Task<string> ListAnimationJobsJsonAsync(Guid projectId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit ?? 12, 1, 50);
        var jobs = await db.AssetAnimationJobs
            .AsNoTracking()
            .Where(job => job.ProjectId == projectId)
            .OrderByDescending(job => job.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        var jobIds = jobs.Select(job => job.Id).ToList();
        var latestErrors = new Dictionary<Guid, string>();
        if (jobIds.Count > 0)
        {
            var errorEvents = await db.AssetAnimationEvents
                .AsNoTracking()
                .Where(item => item.ProjectId == projectId && jobIds.Contains(item.AssetAnimationJobId) && item.Severity == "error")
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new { item.AssetAnimationJobId, item.Summary })
                .ToListAsync(cancellationToken);
            latestErrors = errorEvents
                .GroupBy(item => item.AssetAnimationJobId)
                .ToDictionary(group => group.Key, group => group.First().Summary);
        }

        return JsonSerializer.Serialize(new
        {
            jobs = jobs.Select(job =>
            {
                var spec = TryReadAnimationSpec(job);
                var layout = TryReadLayoutSpec(job);
                return new
                {
                    jobId = job.Id,
                    profileId = job.AssetProfileId,
                    job.Status,
                    job.AnimationKind,
                    job.Strategy,
                    job.RecommendedAction,
                    job.SelectedCandidateId,
                    job.OutputSpriteSheetId,
                    budget = new
                    {
                        used = job.GenerationRoundsUsed,
                        max = job.MaxGenerationRounds,
                        remaining = Math.Max(0, job.MaxGenerationRounds - job.GenerationRoundsUsed),
                    },
                    frameCount = spec?.FrameCount ?? 0,
                    fps = spec?.Fps ?? 0,
                    guide = spec is null ? null : new
                    {
                        renderer = spec.GuideRenderer ?? "sprite_guide_renderer",
                        motionClipId = spec.MotionClipId,
                        yaw = spec.GuideCameraYawDegrees,
                        sourcePackage = spec.GuideSourcePackage,
                        sourceLicense = spec.GuideSourceLicense,
                    },
                    layout = layout is null ? "" : $"{layout.Columns}x{layout.Rows} {layout.CanvasWidth}x{layout.CanvasHeight}",
                    latestError = latestErrors.GetValueOrDefault(job.Id, string.Empty),
                    job.UpdatedAt,
                };
            }),
        }, JsonOptions);
    }

    public async Task<string> ReadAnimationJobJsonAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadJobViewAsync(projectId, assetAnimationJobId, cancellationToken);
        return JsonSerializer.Serialize(CompactJob(job), JsonOptions);
    }

    private async Task<SpriteSheetDefinition> SaveFixedSlotSpriteSheetAsync(
        Guid projectId,
        AssetAnimationJob job,
        AssetAnimationCandidate candidate,
        ArtAsset source,
        SpriteFixedSlotExtractionResult extraction,
        CancellationToken cancellationToken)
    {
        var layout = ReadLayoutSpec(job);
        var now = DateTime.UtcNow;
        var label = $"{job.AnimationKind} animation";
        var definition = new SpriteSheetDefinition
        {
            ProjectId = projectId,
            SourceAssetId = source.Id,
            Label = label,
            Rows = layout.Rows,
            Columns = layout.Columns,
            CellWidth = layout.TargetCellWidth,
            CellHeight = layout.TargetCellHeight,
            Padding = 0,
            Gutter = 0,
            Fps = ReadAnimationSpec(job).Fps,
            Loop = ReadAnimationSpec(job).Loop,
            HorizontalAnchor = "center",
            VerticalAnchor = "middle",
            BackgroundMode = "alpha",
            FramesJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var working = CreateAsset(
            projectId,
            label,
            $"asset-animation-sheet-{now:yyyyMMddHHmmss}.png",
            ArtAssetKind.SpriteSheet,
            "image/png",
            extraction.PngData,
            source.Id,
            sourceBatchId: source.SourceBatchId,
            source.Prompt,
            new
            {
                Source = "asset-animation-fixed-slot-sheet",
                AssetAnimationJobId = job.Id,
                AssetAnimationCandidateId = candidate.Id,
            });
        definition.OutputAssetId = working.Id;
        await db.SpriteSheetDefinitions.AddAsync(definition, cancellationToken);
        await db.ArtAssets.AddAsync(working, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var records = new List<SpriteSheetFrameRecord>();
        foreach (var frame in extraction.Frames)
        {
            var preview = ParsePngDataUrl(frame.PreviewPngDataUrl);
            records.Add(new SpriteSheetFrameRecord
            {
                ProjectId = projectId,
                SpriteSheetDefinitionId = definition.Id,
                Index = frame.Index,
                Label = frame.Label,
                SourceX = frame.SourceRect.X,
                SourceY = frame.SourceRect.Y,
                SourceWidth = frame.SourceRect.Width,
                SourceHeight = frame.SourceRect.Height,
                ShapeJson = "[]",
                SourceImageAssetId = frame.SourceImageAssetId ?? source.Id,
                SourceImageX = frame.SourceImageRect.X,
                SourceImageY = frame.SourceImageRect.Y,
                SourceImageWidth = frame.SourceImageRect.Width,
                SourceImageHeight = frame.SourceImageRect.Height,
                CellX = frame.CellRect.X,
                CellY = frame.CellRect.Y,
                CellWidth = frame.CellRect.Width,
                CellHeight = frame.CellRect.Height,
                SpriteX = frame.SpriteRect.X,
                SpriteY = frame.SpriteRect.Y,
                SpriteWidth = frame.SpriteRect.Width,
                SpriteHeight = frame.SpriteRect.Height,
                PreviewContentType = preview.ContentType,
                PreviewData = preview.Data,
                PreviewWidth = preview.Width,
                PreviewHeight = preview.Height,
                PoseName = frame.FrameSpec.PoseName,
                Phase = frame.FrameSpec.Phase,
                RootOffsetX = frame.FrameSpec.RootOffsetX,
                RootOffsetY = frame.FrameSpec.RootOffsetY,
                DurationMs = frame.FrameSpec.DurationMs,
                FootContactsJson = JsonSerializer.Serialize(frame.FrameSpec.Contacts, JsonOptions),
                IsKeyframe = frame.FrameSpec.IsKeyframe,
                PivotX = layout.TargetCellWidth / 2,
                PivotY = layout.TargetCellHeight * 4 / 5,
                SourceAnimationJobId = job.Id,
                SourceAnimationCandidateId = candidate.Id,
                AppliedScale = Math.Min(
                    layout.TargetCellWidth / (double)Math.Max(1, layout.GuideCellWidth),
                    layout.TargetCellHeight / (double)Math.Max(1, layout.GuideCellHeight)),
                QaStatus = "pending",
                RepairHistoryJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SpriteSheetFrameRecords.AddRangeAsync(records, cancellationToken);
        definition.FramesJson = JsonSerializer.Serialize(records.Select(record => new
        {
            record.Index,
            record.Label,
            SourceRect = new SpriteSheetRect(record.SourceX, record.SourceY, record.SourceWidth, record.SourceHeight),
            ShapePaths = Array.Empty<SpriteSheetShapePath>(),
            record.SourceImageAssetId,
            SourceImageRect = new SpriteSheetRect(record.SourceImageX, record.SourceImageY, record.SourceImageWidth, record.SourceImageHeight),
            CellRect = new SpriteSheetRect(record.CellX, record.CellY, record.CellWidth, record.CellHeight),
            SpriteRect = new SpriteSheetRect(record.SpriteX, record.SpriteY, record.SpriteWidth, record.SpriteHeight),
        }), JsonOptions);
        var project = await db.Projects.FirstAsync(project => project.Id == projectId, cancellationToken);
        project.ActiveSpriteSheetId = definition.Id;
        project.ActiveWorkspaceMode = WorkspaceMode.Runs;
        project.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return definition;
    }

    private async Task<Dictionary<int, SpriteFrameOverride>> LoadFrameOverridesAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var attempts = await db.AssetAnimationFrameAttempts
            .Where(attempt => attempt.ProjectId == projectId
                && attempt.AssetAnimationJobId == jobId
                && attempt.SourceAssetId != null
                && attempt.Status != "reject"
                && attempt.Status != "rejected")
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .ToListAsync(cancellationToken);
        var sourceIds = attempts.Select(attempt => attempt.SourceAssetId!.Value).Distinct().ToList();
        var assets = sourceIds.Count == 0
            ? new Dictionary<Guid, ArtAsset>()
            : await db.ArtAssets
                .Where(asset => asset.ProjectId == projectId && sourceIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, cancellationToken);

        return attempts
            .Where(attempt => attempt.SourceAssetId is Guid id && assets.ContainsKey(id))
            .GroupBy(attempt => attempt.FrameIndex)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var attempt = group.First();
                    var asset = assets[attempt.SourceAssetId!.Value];
                    return new SpriteFrameOverride(
                        asset.Id,
                        asset.Data,
                        new SpriteSheetRect(
                            attempt.SourceX,
                            attempt.SourceY,
                            attempt.SourceWidth <= 0 ? asset.Width ?? 1 : attempt.SourceWidth,
                            attempt.SourceHeight <= 0 ? asset.Height ?? 1 : attempt.SourceHeight));
                });
    }

    private async Task SetCompareReviewAsync(Guid projectId, AssetAnimationJob job, CancellationToken cancellationToken)
    {
        var reviewSet = await db.CompareReviewSets.FirstOrDefaultAsync(set => set.ProjectId == projectId, cancellationToken);
        if (reviewSet is null)
        {
            reviewSet = new CompareReviewSet
            {
                ProjectId = projectId,
                Title = $"{job.AnimationKind} animation",
                Summary = "Final packaged asset animation.",
            };
            await db.CompareReviewSets.AddAsync(reviewSet, cancellationToken);
        }

        reviewSet.Title = $"{job.AnimationKind} animation";
        reviewSet.Summary = "Final packaged asset animation.";
        reviewSet.UpdatedAt = DateTime.UtcNow;
        var existing = await db.CompareReviewSetItems
            .Where(item => item.CompareReviewSetId == reviewSet.Id)
            .ToListAsync(cancellationToken);
        db.CompareReviewSetItems.RemoveRange(existing);
        await db.CompareReviewSetItems.AddRangeAsync([
            new CompareReviewSetItem
            {
                CompareReviewSetId = reviewSet.Id,
                Kind = CompareReviewItemKind.SpriteAnimation,
                RefId = job.OutputSpriteSheetId!.Value,
                Label = $"{job.AnimationKind} animation",
                SortOrder = 0,
            },
            new CompareReviewSetItem
            {
                CompareReviewSetId = reviewSet.Id,
                Kind = CompareReviewItemKind.SpriteSheet,
                RefId = job.OutputSpriteSheetId!.Value,
                Label = $"{job.AnimationKind} sheet",
                SortOrder = 1,
            }
        ], cancellationToken);
    }

    private async Task<AssetAnimationCandidate?> ResolveSelectedCandidateAsync(Guid projectId, AssetAnimationJob job, CancellationToken cancellationToken)
    {
        if (job.SelectedCandidateId is Guid selected)
        {
            var selectedCandidate = await db.AssetAnimationCandidates
                .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == selected, cancellationToken);
            if (selectedCandidate is not null)
                return selectedCandidate;
        }

        return await db.AssetAnimationCandidates
            .Where(candidate => candidate.ProjectId == projectId && candidate.AssetAnimationJobId == job.Id && candidate.OutputAssetId != null)
            .OrderBy(candidate => candidate.CandidateIndex)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<MotionQaSummary> InspectMotionAsync(Guid projectId, AssetAnimationJob job, AnimationSpec spec, CancellationToken cancellationToken)
    {
        var frameFailures = ReadFrameStatuses(job)
            .Where(status => string.Equals(status.Status, "repair_requested", StringComparison.OrdinalIgnoreCase))
            .Select(status => $"frame_{status.FrameNumber}:{status.Reason}")
            .ToList();
        if (frameFailures.Count > 0)
        {
            return new MotionQaSummary(
                "warning",
                "Frame QA still has repair-requested frames.",
                job.OutputSpriteSheetId,
                frameFailures,
                MotionClipCatalog.IsExternalMotionSpec(spec) ? "run_animation_candidates" : "regenerate_animation_frames");
        }

        if (job.OutputSpriteSheetId is not Guid spriteSheetId)
        {
            return new MotionQaSummary(
                "warning",
                "No extracted sprite sheet is available for motion QA.",
                null,
                ["missing_sprite_sheet"],
                "extract_animation_fixed_slots");
        }

        var records = await db.SpriteSheetFrameRecords
            .AsNoTracking()
            .Where(record => record.ProjectId == projectId && record.SpriteSheetDefinitionId == spriteSheetId)
            .OrderBy(record => record.Index)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return new MotionQaSummary(
                "warning",
                "No extracted frame records are available for motion QA.",
                spriteSheetId,
                ["missing_frame_records"],
                "extract_animation_fixed_slots");
        }

        var failures = new List<string>();
        foreach (var record in records)
        {
            var edgeMargin = Math.Max(1, Math.Min(record.CellWidth, record.CellHeight) / 32);
            if (record.SpriteWidth <= 1 || record.SpriteHeight <= 1)
                failures.Add($"frame_{record.Index + 1}:missing_foreground");
            if (record.SpriteX <= edgeMargin
                || record.SpriteY <= edgeMargin
                || record.SpriteX + record.SpriteWidth >= record.CellWidth - edgeMargin
                || record.SpriteY + record.SpriteHeight >= record.CellHeight - edgeMargin)
            {
                failures.Add($"frame_{record.Index + 1}:cell_crossing_or_clipping");
            }
        }

        var usable = records.Where(record => record.SpriteWidth > 1 && record.SpriteHeight > 1).ToList();
        if (usable.Count >= 2)
        {
            var minHeight = usable.Min(record => record.SpriteHeight);
            var maxHeight = usable.Max(record => record.SpriteHeight);
            var minArea = usable.Min(record => record.SpriteWidth * record.SpriteHeight);
            var maxArea = usable.Max(record => record.SpriteWidth * record.SpriteHeight);
            if (minHeight > 0 && maxHeight / (double)minHeight > 1.35d)
                failures.Add("scale_drift:height_ratio_over_1.35");
            if (minArea > 0 && maxArea / (double)minArea > 1.7d)
                failures.Add("scale_drift:area_ratio_over_1.70");

            var bottomRange = usable.Max(record => record.SpriteY + record.SpriteHeight) - usable.Min(record => record.SpriteY + record.SpriteHeight);
            var centerXRange = usable.Max(CenterX) - usable.Min(CenterX);
            var centerYRange = usable.Max(CenterY) - usable.Min(CenterY);
            var averageCellHeight = usable.Average(record => record.CellHeight);
            var averageCellWidth = usable.Average(record => record.CellWidth);
            if (bottomRange > averageCellHeight * 0.18d)
                failures.Add("root_drift:bottom_range_over_18_percent");
            if (centerXRange > averageCellWidth * 0.45d)
                failures.Add("root_drift:center_x_range_over_45_percent");
            if (centerXRange + centerYRange < Math.Max(4d, (averageCellWidth + averageCellHeight) * 0.015d))
                failures.Add("low_frame_to_frame_motion");
        }

        return failures.Count == 0
            ? new MotionQaSummary("pass", "Motion QA passed deterministic checks.", spriteSheetId, [], "package_animation_job")
            : new MotionQaSummary(
                "warning",
                "Motion QA found severe strip-level issues.",
                spriteSheetId,
                failures.Distinct(StringComparer.Ordinal).ToList(),
                MotionClipCatalog.IsExternalMotionSpec(spec) ? "run_animation_candidates" : "regenerate_animation_frames");

        static double CenterX(SpriteSheetFrameRecord record) => record.SpriteX + (record.SpriteWidth / 2d);
        static double CenterY(SpriteSheetFrameRecord record) => record.SpriteY + (record.SpriteHeight / 2d);
    }

    private static bool MotionQaAllowsPackaging(string motionQaSummaryJson)
    {
        if (string.IsNullOrWhiteSpace(motionQaSummaryJson))
            return false;
        try
        {
            using var document = JsonDocument.Parse(motionQaSummaryJson);
            return document.RootElement.TryGetProperty("status", out var status)
                && string.Equals(status.GetString(), "pass", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private AnimationSpec ResolveMotionClipSpec(AnimationSpec spec, PlanAssetAnimationRequest request)
    {
        var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
        MotionClipDefinition? clip = null;
        if (!string.IsNullOrWhiteSpace(request.MotionClipId))
        {
            clip = catalog.Find(request.MotionClipId)
                ?? throw new InvalidOperationException($"Motion clip '{request.MotionClipId}' was not found in the catalog.");
            if (!clip.Supports(spec))
                throw new InvalidOperationException($"Motion clip '{request.MotionClipId}' does not support {spec.AssetType}/{spec.StructureType}/{spec.AnimationKind}.");
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
        var kind = NormalizeToken(spec.AnimationKind, string.Empty);
        if (!kind.Contains("walk", StringComparison.Ordinal))
            return false;

        var assetType = NormalizeToken(spec.AssetType, string.Empty);
        var structure = NormalizeToken(spec.StructureType, string.Empty);
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

    private static LayoutSpec BuildLayout(AnimationSpec spec, string chromaColor)
    {
        var (columns, rows, canvasWidth, canvasHeight) = spec.FrameCount switch
        {
            <= 1 => (1, 1, 1024, 1024),
            2 => (2, 1, 1536, 1024),
            <= 4 => (2, 2, 1024, 1024),
            <= 6 => (3, 2, 1536, 1024),
            <= 8 => (4, 2, 1536, 1024),
            <= 12 => (4, 3, 1536, 1024),
            _ => (4, 4, 1536, 1024),
        };
        var guideCellWidth = Math.Max(1, canvasWidth / columns);
        var guideCellHeight = Math.Max(1, canvasHeight / rows);
        var slots = Enumerable.Range(0, spec.FrameCount)
            .Select(index =>
            {
                var row = index / columns;
                var col = index % columns;
                var rect = CellRectForGrid(index, columns, rows, canvasWidth, canvasHeight);
                var margin = Math.Max(16, Math.Min(rect.Width, rect.Height) / 10);
                var safe = new SpriteSheetRect(rect.X + margin, rect.Y + margin, Math.Max(1, rect.Width - (margin * 2)), Math.Max(1, rect.Height - (margin * 2)));
                var baseline = rect.Y + rect.Height * 5 / 6;
                var root = new SpriteSheetPoint(rect.X + rect.Width / 2, baseline);
                return new SlotSpec(index, rect, root, baseline, safe);
            })
            .ToList();
        return new LayoutSpec(canvasWidth, canvasHeight, rows, columns, guideCellWidth, guideCellHeight, spec.TargetCellWidth, spec.TargetCellHeight, chromaColor, slots);
    }

    private static LayoutSpec BuildSingleFrameLayout(AnimationSpec originalSpec, LayoutSpec originalLayout, FrameSpec frame)
    {
        const int cell = 1024;
        var safe = new SpriteSheetRect(96, 96, cell - 192, cell - 192);
        var root = new SpriteSheetPoint(cell / 2, cell * 5 / 6);
        var slot = new SlotSpec(0, new SpriteSheetRect(0, 0, cell, cell), root, root.Y, safe);
        return new LayoutSpec(cell, cell, 1, 1, cell, cell, originalSpec.TargetCellWidth, originalSpec.TargetCellHeight, originalLayout.BackgroundColor, [slot]);
    }

    private static SpriteSheetRect CellRectForGrid(int index, int columns, int rows, int canvasWidth, int canvasHeight)
    {
        var row = index / columns;
        var col = index % columns;
        var x0 = col * canvasWidth / columns;
        var x1 = (col + 1) * canvasWidth / columns;
        var y0 = row * canvasHeight / rows;
        var y1 = (row + 1) * canvasHeight / rows;
        return new SpriteSheetRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private async Task<AssetAnimationJob> LoadJobEntityAsync(Guid projectId, Guid id, bool includeProfile, CancellationToken cancellationToken)
    {
        IQueryable<AssetAnimationJob> query = db.AssetAnimationJobs;
        if (includeProfile)
            query = query.Include(job => job.AssetProfile);
        return await query.FirstOrDefaultAsync(job => job.ProjectId == projectId && job.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Asset animation job was not found.");
    }

    private async Task<AssetAnimationJobView> LoadJobViewAsync(Guid projectId, Guid id, CancellationToken cancellationToken)
    {
        var job = await db.AssetAnimationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Asset animation job was not found.");
        var candidates = await db.AssetAnimationCandidates
            .AsNoTracking()
            .Where(candidate => candidate.ProjectId == projectId && candidate.AssetAnimationJobId == job.Id)
            .OrderBy(candidate => candidate.CandidateIndex)
            .Select(candidate => new AssetAnimationCandidateView(
                candidate.Id,
                candidate.GenerationBatchId,
                candidate.OutputAssetId,
                candidate.CandidateIndex,
                candidate.State,
                candidate.RawQaStatus))
            .ToListAsync(cancellationToken);
        var latestError = await db.AssetAnimationEvents
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.AssetAnimationJobId == job.Id && item.Severity == "error")
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => item.Summary)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        return new AssetAnimationJobView(
            job.Id,
            job.AssetProfileId,
            job.GuideAssetId,
            job.DiagnosticGuideAssetId,
            job.OutputSpriteSheetId,
            job.SelectedCandidateId,
            job.Status,
            job.AnimationKind,
            job.Strategy,
            job.PromptSummary,
            job.RecommendedAction,
            job.MaxGenerationRounds,
            job.GenerationRoundsUsed,
            job.MaxRepairAttemptsPerFrame,
            ReadAnimationSpec(job),
            ReadLayoutSpec(job),
            candidates,
            ReadFrameStatuses(job),
            latestError,
            job.UpdatedAt);
    }

    private static object CompactJob(AssetAnimationJobView job) => new
    {
        jobId = job.Id,
        profileId = job.AssetProfileId,
        job.Status,
        job.AnimationKind,
        job.Strategy,
        job.RecommendedAction,
        budget = new
        {
            used = job.GenerationRoundsUsed,
            max = job.MaxGenerationRounds,
            remaining = Math.Max(0, job.MaxGenerationRounds - job.GenerationRoundsUsed),
            repairAttemptsPerFrame = job.MaxRepairAttemptsPerFrame,
        },
        guideAssetId = job.GuideAssetId,
        diagnosticGuideAssetId = job.DiagnosticGuideAssetId,
        outputSpriteSheetId = job.OutputSpriteSheetId,
        selectedCandidateId = job.SelectedCandidateId,
        latestError = job.LatestError,
        animation = new
        {
            job.AnimationSpec.AssetType,
            job.AnimationSpec.StructureType,
            job.AnimationSpec.AnimationKind,
            job.AnimationSpec.Facing,
            job.AnimationSpec.FrameCount,
            job.AnimationSpec.Fps,
            targetCell = $"{job.AnimationSpec.TargetCellWidth}x{job.AnimationSpec.TargetCellHeight}",
        },
        guide = new
        {
            renderer = job.AnimationSpec.GuideRenderer ?? "sprite_guide_renderer",
            motionClipId = job.AnimationSpec.MotionClipId,
            cameraYawDegrees = job.AnimationSpec.GuideCameraYawDegrees,
            sourcePackage = job.AnimationSpec.GuideSourcePackage,
            sourceLicense = job.AnimationSpec.GuideSourceLicense,
        },
        layout = new
        {
            job.LayoutSpec.Rows,
            job.LayoutSpec.Columns,
            canvas = $"{job.LayoutSpec.CanvasWidth}x{job.LayoutSpec.CanvasHeight}",
            chroma = job.LayoutSpec.BackgroundColor,
        },
        candidates = job.Candidates.Select(candidate => new
        {
            candidate.Id,
            candidate.OutputAssetId,
            candidate.CandidateIndex,
            candidate.State,
            candidate.RawQaStatus,
        }),
        frames = job.FrameStatuses,
    };

    private async Task AddEventAsync(
        Guid projectId,
        Guid jobId,
        string eventType,
        string severity,
        string summary,
        object? payload,
        CancellationToken cancellationToken)
    {
        var cleanSeverity = NormalizeToken(severity, "info");
        if (cleanSeverity is not ("info" or "warning" or "error"))
            cleanSeverity = "info";
        await db.AssetAnimationEvents.AddAsync(new AssetAnimationEvent
        {
            ProjectId = projectId,
            AssetAnimationJobId = jobId,
            EventType = NormalizeToken(eventType, "event"),
            Severity = cleanSeverity,
            Summary = TrimForEvent(Clean(summary), 260),
            PayloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
    }

    private async Task SetProjectModeAsync(Guid projectId, WorkspaceMode mode, CancellationToken cancellationToken)
    {
        var project = await db.Projects.FirstAsync(project => project.Id == projectId, cancellationToken);
        project.ActiveWorkspaceMode = mode;
        project.UpdatedAt = DateTime.UtcNow;
    }

    private static (int Width, int Height) ActualDimensions(ArtAsset asset)
    {
        if (asset.Width is > 0 && asset.Height is > 0)
            return (asset.Width.Value, asset.Height.Value);
        var (width, height) = ImageMetadataReader.TryReadSize(asset.Data, asset.ContentType);
        return (Math.Max(1, width ?? 1), Math.Max(1, height ?? 1));
    }

    private static string TrimForEvent(string value, int maxLength)
    {
        var cleaned = value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    private static AssetProfileView ToProfileView(AssetProfile profile) =>
        new(
            profile.Id,
            profile.CanonicalAssetId,
            profile.StyleAssetId,
            profile.Label,
            profile.AssetType,
            profile.StructureType,
            profile.ChromaColor,
            DeserializeStrings(profile.PaletteJson),
            DeserializeStrings(profile.RequiredFeaturesJson),
            DeserializeStrings(profile.ForbiddenChangesJson),
            profile.Frozen,
            profile.CreatedAt);

    private static AnimationSpec ReadAnimationSpec(AssetAnimationJob job) =>
        JsonSerializer.Deserialize<AnimationSpec>(job.AnimationSpecJson, JsonOptions)
        ?? throw new InvalidOperationException("Animation spec is invalid.");

    private static LayoutSpec ReadLayoutSpec(AssetAnimationJob job) =>
        JsonSerializer.Deserialize<LayoutSpec>(job.LayoutSpecJson, JsonOptions)
        ?? throw new InvalidOperationException("Layout spec is invalid.");

    private static AnimationSpec? TryReadAnimationSpec(AssetAnimationJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<AnimationSpec>(job.AnimationSpecJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LayoutSpec? TryReadLayoutSpec(AssetAnimationJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<LayoutSpec>(job.LayoutSpecJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<AssetAnimationFrameStatusView> ReadFrameStatuses(AssetAnimationJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AssetAnimationFrameStatusView>>(job.FrameStatusesJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> DeserializeStrings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<int> NextAttemptNumberAsync(Guid projectId, Guid jobId, int frameIndex, CancellationToken cancellationToken)
    {
        var current = await db.AssetAnimationFrameAttempts
            .Where(attempt => attempt.ProjectId == projectId && attempt.AssetAnimationJobId == jobId && attempt.FrameIndex == frameIndex)
            .Select(attempt => (int?)attempt.AttemptNumber)
            .MaxAsync(cancellationToken);
        return (current ?? 0) + 1;
    }

    private string? PreferredAnimationModel()
    {
        var snapshot = animationOptions.Value.PreferredImageModelSnapshot?.Trim();
        return string.IsNullOrWhiteSpace(snapshot) ? null : snapshot;
    }

    private static string BuildCandidatePrompt(AssetAnimationJob job, AnimationSpec spec, LayoutSpec layout)
    {
        var guideRole = MotionClipCatalog.IsExternalMotionSpec(spec)
            ? $"Image 1 is a sampled real Quaternius glTF animation guide from clip {spec.MotionClipId}. It controls exact slot positions, frame order, root/pivot anchors, safe margins, sampled skeleton/capsule poses, foot contacts, and camera yaw. Do not reproduce guide marks."
            : "Image 1 controls exact slot positions, frame order, structure guide, root/pivot anchors, safe margins, and motion layout. Do not reproduce its guide marks.";
        var motionSource = MotionClipCatalog.IsExternalMotionSpec(spec)
            ? $"Motion source: {spec.MotionClipId} from {spec.GuideSourcePackage} ({spec.GuideSourceLicense}), sampled to {spec.FrameCount} frames at yaw {(spec.GuideCameraYawDegrees ?? 0d):0.#} degrees."
            : "Follow each guide cell's pose or structure state.";

        return $"""
        GOAL
        Render a {spec.FrameCount}-frame {spec.AssetType} {spec.AnimationKind} animation as a {layout.Columns} column by {layout.Rows} row grid.

        INPUT ROLES
        {guideRole}
        Image 2 controls the asset identity and visual design.
        Image 3, when present, controls style/material details.

        MOTION CONTRACT
        Facing/direction: {spec.Facing}. Root motion: {spec.RootMotion}. {motionSource}

        OUTPUT CONTRACT
        Exactly {spec.FrameCount} complete frames, one subject per cell, no text, no labels, no borders, no extra poses, no overlap between cells, no cropping. Keep identity, apparent scale, camera, line style, and pivot/root position stable. Keep weapons, shields, hands, feet, and accessories inside the guided safe region.

        BACKGROUND
        Use one flat opaque chroma color: {layout.BackgroundColor}. No shadows, floors, scenery, gradients, transparent checkerboards, glow-only backgrounds, or detached effects outside the guided subject.
        """;
    }

    private static string BuildFrameRepairPrompt(AssetAnimationJob job, AnimationSpec spec, FrameSpec frame, LayoutSpec layout, string? extraPrompt)
    {
        var motionNote = MotionClipCatalog.IsExternalMotionSpec(spec)
            ? "This is a Quaternius-sampled motion job. Use single-frame repair only for local clipping, chroma leakage, or small artifact cleanup. If gait, pose timing, root alignment, scale, facing, foot contact, or frame order is wrong, the correct repair is a full-strip regeneration, not a new invented pose for this one frame."
            : "Repair this one frame without changing the animation's intended timing.";

        return $"""
        GOAL
        Render only frame {frame.Index + 1} for the {job.AnimationKind} animation.

        INPUT ROLES
        Image 1 controls the single-frame structure guide, root/pivot anchor, safe margin, and intended pose/state. Do not reproduce guide marks.
        Image 2 controls identity.
        Image 3, when present, is the current or accepted strip context for scale, camera, neighboring frames, and registration.
        Image 4, when present, controls style/material details.

        FRAME CONTRACT
        Pose/state: {frame.PoseName}. Keep the same identity, scale, camera, line style, and pivot/root alignment as the animation. {motionNote}

        BACKGROUND
        Use one flat opaque chroma color: {layout.BackgroundColor}. No text, labels, borders, shadows, scenery, gradients, or extra subjects.

        {(string.IsNullOrWhiteSpace(extraPrompt) ? string.Empty : "Additional repair note: " + extraPrompt.Trim())}
        """;
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
            Prompt = prompt,
            SourceMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static (string ContentType, byte[] Data, int Width, int Height) ParsePngDataUrl(string dataUrl)
    {
        var (contentType, data) = DataUrl.Parse(dataUrl);
        var (width, height) = ImageMetadataReader.TryReadSize(data, contentType);
        return (contentType, data, width ?? 0, height ?? 0);
    }

    private static (int Width, int Height) ParseSize(string? preferred, string fallback, int defaultWidth, int defaultHeight)
    {
        var value = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        var parts = value.Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height)
            ? (Math.Clamp(width, 16, 4096), Math.Clamp(height, 16, 4096))
            : (defaultWidth, defaultHeight);
    }

    private static string DefaultFacing(string assetType) =>
        NormalizeToken(assetType, "unit") switch
        {
            "tower" or "vfx" => "center",
            _ => "right",
        };

    private static string DefaultStructure(string? assetType) =>
        NormalizeToken(assetType, "unit") switch
        {
            "tower" => "tower_pivot",
            "projectile" => "directional_projectile",
            "vfx" => "radial_vfx",
            _ => "biped",
        };

    private static string NormalizeToken(string? value, string fallback)
    {
        var cleaned = value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string NormalizeFrameStatus(string? value) =>
        NormalizeToken(value, "warning") switch
        {
            "accept" or "accepted" or "pass" => "accepted",
            "reject" or "rejected" or "fail" => "rejected",
            "repair" or "repair_requested" or "fix" => "repair_requested",
            _ => "warning",
        };

    private static string RecommendedActionForStatus(string? value, string? reason, AnimationSpec spec) =>
        NormalizeFrameStatus(value) switch
        {
            "accepted" => "extract_animation_fixed_slots",
            "rejected" => MotionClipCatalog.IsExternalMotionSpec(spec) && IsStripLevelMotionReason(reason) ? "run_animation_candidates" : "regenerate_animation_frames",
            "repair_requested" => MotionClipCatalog.IsExternalMotionSpec(spec) && IsStripLevelMotionReason(reason) ? "run_animation_candidates" : "regenerate_animation_frames",
            _ => "review_animation_job",
        };

    private static bool IsStripLevelMotionReason(string? reason)
    {
        var normalized = NormalizeToken(reason, string.Empty);
        return normalized.Contains("gait", StringComparison.Ordinal)
            || normalized.Contains("pose", StringComparison.Ordinal)
            || normalized.Contains("root", StringComparison.Ordinal)
            || normalized.Contains("scale", StringComparison.Ordinal)
            || normalized.Contains("motion", StringComparison.Ordinal)
            || normalized.Contains("contact", StringComparison.Ordinal)
            || normalized.Contains("foot", StringComparison.Ordinal)
            || normalized.Contains("walk", StringComparison.Ordinal)
            || normalized.Contains("drift", StringComparison.Ordinal)
            || normalized.Contains("cell_cross", StringComparison.Ordinal);
    }

    private sealed record MotionQaSummary(
        string Status,
        string Summary,
        Guid? OutputSpriteSheetId,
        IReadOnlyList<string> Failures,
        string RecommendedAction);

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
