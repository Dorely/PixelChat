using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Art;

public sealed class SpriteWorkspaceActionService(
    AppDbContext db,
    IFrameSetService frameSets,
    IArtWorkflowService workflow) : ISpriteWorkspaceActionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SetFocusAsync(Guid projectId, SpriteWorkspaceFocusUpdate focus, CancellationToken cancellationToken = default)
    {
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Normalize(focus.Mode),
            focus.SourceAssetId,
            focus.FrameSetId,
            focus.FrameId,
            focus.SelectedRegionIds,
            cancellationToken);
    }

    public async Task<ExtractRegionAsAssetResult> ExtractRegionAsAssetAsync(
        Guid projectId,
        ExtractRegionAsAssetRequest request,
        IReadOnlyList<Guid>? selectedRegionIds = null,
        CancellationToken cancellationToken = default)
    {
        var result = await workflow.ExtractRegionAsAssetAsync(projectId, request, cancellationToken);
        var activeFrameSetId = await ActiveFrameSetForSourceAsync(projectId, request.SourceAssetId, cancellationToken);
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Source,
            request.SourceAssetId,
            activeFrameSetId,
            frameId: null,
            selectedRegionIds ?? [result.RegionId],
            cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<SourceRegionView>> DetectSourceRegionsAsync(
        Guid projectId,
        DetectSourceRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var regions = await frameSets.DetectSourceRegionsAsync(projectId, request, cancellationToken);
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Source,
            request.SourceAssetId,
            frameSetId: null,
            frameId: null,
            regions.Select(region => region.Id).ToList(),
            cancellationToken);
        return regions;
    }

    public async Task<IReadOnlyList<SourceRegionView>> SaveSourceRegionsAsync(
        Guid projectId,
        SaveSourceRegionsRequest request,
        IReadOnlyList<Guid>? selectedRegionIds = null,
        CancellationToken cancellationToken = default)
    {
        var regions = await frameSets.SaveSourceRegionsAsync(projectId, request, cancellationToken);
        var selected = selectedRegionIds is null
            ? regions.Select(region => region.Id).ToList()
            : regions.Where(region => selectedRegionIds.Contains(region.Id)).Select(region => region.Id).ToList();
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Source,
            request.SourceAssetId,
            frameSetId: null,
            frameId: null,
            selected,
            cancellationToken);
        return regions;
    }

    public async Task<FrameSetView> CreateFrameSetFromAssetAsync(
        Guid projectId,
        CreateFrameSetFromAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = await frameSets.CreateFrameSetFromAssetAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<FrameSetView> CreateFrameSetFromRegionsAsync(
        Guid projectId,
        CreateFrameSetFromRegionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = await frameSets.CreateFrameSetFromRegionsAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<FrameSetView> SetActiveFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.SetActiveFrameSetAsync(projectId, frameSetId, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<FrameSetView> SetCommonCellSizeAsync(Guid projectId, SetCommonCellSizeRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.SetCommonCellSizeAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<FrameSetView> AddFrameFromRegionAsync(Guid projectId, AddFrameFromRegionRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.AddFrameFromRegionAsync(projectId, request, cancellationToken);
        var frameId = request.SourceRegionId == Guid.Empty
            ? SelectedFrameId(view)
            : view.Frames.FirstOrDefault(frame => frame.SourceRegionId == request.SourceRegionId)?.Id ?? SelectedFrameId(view);
        await FocusFrameSetAsync(projectId, view, frameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> DuplicateFrameAsync(Guid projectId, DuplicateFrameRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.DuplicateFrameAsync(projectId, request, cancellationToken);
        var insertAt = Math.Clamp(request.InsertAt ?? (view.Frames.FirstOrDefault(frame => frame.Id == request.FrameId)?.Index + 1 ?? view.FrameCount - 1), 0, Math.Max(0, view.FrameCount - 1));
        var frameId = view.Frames.FirstOrDefault(frame => frame.Index == insertAt)?.Id ?? SelectedFrameId(view);
        await FocusFrameSetAsync(projectId, view, frameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> SetFrameLogicalCellAsync(Guid projectId, SetFrameLogicalCellRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.SetFrameLogicalCellAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, request.FrameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> UpdateFrameSourceBoundsAsync(Guid projectId, UpdateFrameSourceBoundsRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.UpdateFrameSourceBoundsAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, request.FrameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> TranslateFrameContentAsync(Guid projectId, TranslateFrameContentRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.TranslateFrameContentAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, request.FrameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> ApplyFrameEditCandidateAsync(Guid projectId, ApplyFrameEditCandidateRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.ApplyFrameEditCandidateAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, request.FrameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> ReorderFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, int targetIndex, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.ReorderFrameAsync(projectId, frameSetId, frameId, targetIndex, cancellationToken);
        await FocusFrameSetAsync(projectId, view, frameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> DeleteFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.DeleteFrameAsync(projectId, frameSetId, frameId, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<FrameSetView> SetFrameDurationAsync(Guid projectId, Guid frameSetId, Guid frameId, int durationMs, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.SetFrameDurationAsync(projectId, frameSetId, frameId, durationMs, cancellationToken);
        await FocusFrameSetAsync(projectId, view, frameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> SetFrameOnionSkinVisibilityAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        bool hideFromOnionSkin,
        CancellationToken cancellationToken = default)
    {
        var view = await frameSets.SetFrameOnionSkinVisibilityAsync(projectId, frameSetId, frameId, hideFromOnionSkin, cancellationToken);
        await FocusFrameSetAsync(projectId, view, frameId, cancellationToken);
        return view;
    }

    public async Task<FrameSetView> AlignFramesAsync(Guid projectId, AlignFramesRequest request, CancellationToken cancellationToken = default)
    {
        var view = await frameSets.AlignFramesAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, view, SelectedFrameId(view), cancellationToken);
        return view;
    }

    public async Task<AnchorAlignmentResult> AlignFramesByAnchorRectAsync(Guid projectId, AlignFramesByAnchorRectRequest request, CancellationToken cancellationToken = default)
    {
        var result = await frameSets.AlignFramesByAnchorRectAsync(projectId, request, cancellationToken);
        await FocusFrameSetAsync(projectId, result.FrameSet, request.ReferenceFrameId, cancellationToken);
        return result;
    }

    public async Task<ImageMaskView> UpsertFrameMaskAsync(Guid projectId, UpsertFrameMaskRequest request, CancellationToken cancellationToken = default)
    {
        var mask = await frameSets.UpsertFrameMaskAsync(projectId, request, cancellationToken);
        await FocusFrameAsync(projectId, request.FrameId, cancellationToken);
        return mask;
    }

    public async Task ClearFrameMaskAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default)
    {
        await frameSets.ClearFrameMaskAsync(projectId, frameId, cancellationToken);
        await FocusFrameAsync(projectId, frameId, cancellationToken);
    }

    public async Task<BuildSheetResult> BuildSheetAsync(Guid projectId, BuildSheetRequest request, CancellationToken cancellationToken = default)
    {
        var result = await frameSets.BuildSheetAsync(projectId, request, cancellationToken);
        var view = await frameSets.GetFrameSetAsync(projectId, request.FrameSetId, cancellationToken);
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Sheet,
            view.SourceAssetId,
            view.Id,
            SelectedFrameId(view),
            selectedRegionIds: [],
            cancellationToken);
        return result;
    }

    private async Task FocusFrameSetAsync(Guid projectId, FrameSetView view, Guid? frameId, CancellationToken cancellationToken)
    {
        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Frames,
            view.SourceAssetId,
            view.Id,
            frameId,
            selectedRegionIds: [],
            cancellationToken);
    }

    private async Task FocusFrameAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken)
    {
        var frame = await db.Frames
            .AsNoTracking()
            .Include(candidate => candidate.FrameSet)
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == frameId, cancellationToken);
        if (frame is null)
            return;

        await ApplyFocusAsync(
            projectId,
            SpriteWorkspaceModes.Frames,
            frame.FrameSet.SourceAssetId,
            frame.FrameSetId,
            frameId,
            selectedRegionIds: [],
            cancellationToken);
    }

    private async Task<Guid?> ActiveFrameSetForSourceAsync(Guid projectId, Guid sourceAssetId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == projectId, cancellationToken);
        if (project?.ActiveFrameSetId is not Guid activeFrameSetId)
            return null;

        var frameSet = await db.FrameSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == activeFrameSetId, cancellationToken);
        return frameSet?.SourceAssetId == sourceAssetId ? activeFrameSetId : null;
    }

    private async Task ApplyFocusAsync(
        Guid projectId,
        string mode,
        Guid? sourceAssetId,
        Guid? frameSetId,
        Guid? frameId,
        IReadOnlyList<Guid>? selectedRegionIds,
        CancellationToken cancellationToken)
    {
        var normalizedMode = SpriteWorkspaceModes.Normalize(mode);
        await workflow.SetWorkspaceModeAsync(projectId, WorkspaceMode.Sprites, cancellationToken);

        var project = await db.Projects.FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project was not found.");

        project.ActiveWorkspaceMode = WorkspaceMode.Sprites;
        project.ActiveSpriteMode = normalizedMode;
        project.ActiveSpriteSourceAssetId = sourceAssetId ?? project.ActiveSpriteSourceAssetId;
        project.ActiveFrameSetId = normalizedMode == SpriteWorkspaceModes.Source
            ? frameSetId
            : frameSetId ?? project.ActiveFrameSetId;
        project.ActiveSpriteFrameId = frameId;
        if (selectedRegionIds is not null)
            project.ActiveSpriteRegionIdsJson = JsonSerializer.Serialize(selectedRegionIds.Distinct().ToList(), JsonOptions);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid? SelectedFrameId(FrameSetView view) =>
        view.Frames.OrderBy(frame => frame.Index).FirstOrDefault()?.Id;
}
