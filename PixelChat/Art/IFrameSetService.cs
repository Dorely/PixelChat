namespace PixelChat.Art;

/// <summary>
/// Greenfield deterministic Source -> Frames -> Sheet pipeline over the
/// FrameSet/Frame/SheetLayout/BuiltSheet entities. Reuses the existing pixel engines
/// for detection and reassembly; produces opaque sheet assets with a linked manifest.
/// </summary>
public interface IFrameSetService
{
    Task<IReadOnlyList<SourceRegionView>> DetectSourceRegionsAsync(Guid projectId, DetectSourceRegionsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceRegionView>> ListSourceRegionsAsync(Guid projectId, Guid sourceAssetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceRegionView>> SaveSourceRegionsAsync(Guid projectId, SaveSourceRegionsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> CreateFrameSetFromAssetAsync(Guid projectId, CreateFrameSetFromAssetRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> CreateFrameSetFromRegionsAsync(Guid projectId, CreateFrameSetFromRegionsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetCommonCellSizeAsync(Guid projectId, SetCommonCellSizeRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> AddFrameFromRegionAsync(Guid projectId, AddFrameFromRegionRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> DuplicateFrameAsync(Guid projectId, DuplicateFrameRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetFrameLogicalCellAsync(Guid projectId, SetFrameLogicalCellRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> UpdateFrameSourceBoundsAsync(Guid projectId, UpdateFrameSourceBoundsRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> TranslateFrameContentAsync(Guid projectId, TranslateFrameContentRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> ApplyFrameEditCandidateAsync(Guid projectId, ApplyFrameEditCandidateRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> AlignFramesAsync(Guid projectId, AlignFramesRequest request, CancellationToken cancellationToken = default);
    Task<AnchorAlignmentResult> AlignFramesByAnchorRectAsync(Guid projectId, AlignFramesByAnchorRectRequest request, CancellationToken cancellationToken = default);
    Task<FrameSetView> GetFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default);
    Task<BuildSheetResult> BuildSheetAsync(Guid projectId, BuildSheetRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FrameSetSummaryView>> ListFrameSetsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<FrameSetView?> GetActiveFrameSetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetActiveFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken = default);
    Task<FrameSetView> ReorderFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, int targetIndex, CancellationToken cancellationToken = default);
    Task<FrameSetView> DeleteFrameAsync(Guid projectId, Guid frameSetId, Guid frameId, CancellationToken cancellationToken = default);
    Task<FrameSetView> SetFrameDurationAsync(Guid projectId, Guid frameSetId, Guid frameId, int durationMs, CancellationToken cancellationToken = default);
    Task<ImageMaskView> UpsertFrameMaskAsync(Guid projectId, UpsertFrameMaskRequest request, CancellationToken cancellationToken = default);
    Task ClearFrameMaskAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default);
    Task<SpriteEditSessionView?> GetPendingSpriteEditSessionAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<SpriteEditSessionView> SaveSpriteEditSessionAsync(Guid projectId, SaveSpriteEditSessionRequest request, CancellationToken cancellationToken = default);
    Task CompleteSpriteEditSessionAsync(Guid projectId, Guid sessionId, string status, CancellationToken cancellationToken = default);
    Task<(byte[] Data, string ContentType)?> GetFrameMaskImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default);
    Task<(byte[] Data, string ContentType)?> GetFrameContentImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default);
    Task<(byte[] Data, string ContentType)?> GetFramePreviewImageAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken = default);
}
