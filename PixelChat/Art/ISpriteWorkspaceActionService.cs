namespace PixelChat.Art;

public static class SpriteWorkspaceModes
{
    public const string Source = "source";
    public const string Frames = "frames";
    public const string Sheet = "sheet";
    public const string Export = "export";

    public static string Normalize(string? mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            Frames => Frames,
            Sheet => Sheet,
            Export => Export,
            _ => Source,
        };
}

public sealed record SpriteWorkspaceFocusUpdate(
    string Mode,
    Guid? SourceAssetId = null,
    Guid? FrameSetId = null,
    Guid? FrameId = null,
    IReadOnlyList<Guid>? SelectedRegionIds = null);

public interface ISpriteWorkspaceActionService
{
    Task SetFocusAsync(Guid projectId, SpriteWorkspaceFocusUpdate focus, CancellationToken cancellationToken = default);
    Task<ExtractRegionAsAssetResult> ExtractRegionAsAssetAsync(Guid projectId, ExtractRegionAsAssetRequest request, IReadOnlyList<Guid>? selectedRegionIds = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceRegionView>> DetectSourceRegionsAsync(Guid projectId, DetectSourceRegionsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceRegionView>> SaveSourceRegionsAsync(Guid projectId, SaveSourceRegionsRequest request, IReadOnlyList<Guid>? selectedRegionIds = null, CancellationToken cancellationToken = default);
    Task<FrameSetView> CreateFrameSetFromAssetAsync(Guid projectId, CreateFrameSetFromAssetRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> CreateFrameSetFromRegionsAsync(Guid projectId, CreateFrameSetFromRegionsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetActiveFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetCommonCellSizeAsync(Guid projectId, SetCommonCellSizeRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> AddFrameFromRegionAsync(Guid projectId, AddFrameFromRegionRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> DuplicateFrameAsync(Guid projectId, DuplicateFrameRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetFrameLogicalCellAsync(Guid projectId, SetFrameLogicalCellRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> UpdateFrameSourceBoundsAsync(Guid projectId, UpdateFrameSourceBoundsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> TranslateFrameContentAsync(Guid projectId, TranslateFrameContentRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> ApplyFrameEditCandidateAsync(Guid projectId, ApplyFrameEditCandidateRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> EditFrameAsync(Guid projectId, EditFrameRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> EraseFrameRegionsAsync(Guid projectId, EraseFrameRegionsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> ComposeFrameSetFromAssetsAsync(Guid projectId, ComposeFrameSetFromAssetsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> ReorderFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, int targetIndex, CancellationToken cancellationToken = default);
    Task<FrameSetView> DeleteFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetFrameDurationAsync(Guid projectId, Guid frameSetId, Guid frameId, int durationMs, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetFrameOnionSkinVisibilityAsync(Guid projectId, Guid frameSetId, Guid frameId, bool hideFromOnionSkin, CancellationToken cancellationToken = default);
    Task<FrameSetView> AlignFramesAsync(Guid projectId, AlignFramesRequest request, CancellationToken cancellationToken = default);
    Task<AnchorAlignmentResult> AlignFramesByAnchorRectAsync(Guid projectId, AlignFramesByAnchorRectRequest request, CancellationToken cancellationToken = default);
    Task<ImageMaskView> UpsertFrameMaskAsync(Guid projectId, UpsertFrameMaskRequest request, CancellationToken cancellationToken = default);
    Task ClearFrameMaskAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default);
    Task<BuildSheetResult> BuildSheetAsync(Guid projectId, BuildSheetRequest request, CancellationToken cancellationToken = default);
}
