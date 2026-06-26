namespace PixelChat.Art;

// Greenfield Source -> Frames -> Sheet view/request models. These operate on the
// FrameSet/Frame/SheetLayout/BuiltSheet entities and reuse the existing pixel engines
// (SpriteSheetImageAnalyzer / SpriteSheetServerRenderer / SpriteSheetPngCodec).

public sealed record FrameView(
    Guid Id,
    Guid? SourceRegionId,
    int Index,
    string Name,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int LogicalWidth,
    int LogicalHeight,
    int ContentOffsetX,
    int ContentOffsetY,
    int DurationMs,
    string WorkingState,
    int WorkingWidth,
    int WorkingHeight,
    bool HideFromOnionSkin,
    bool HasMask,
    Guid? MaskId);

public sealed record FrameSetView(
    Guid Id,
    string Name,
    Guid? SourceAssetId,
    int DefaultCellWidth,
    int DefaultCellHeight,
    int FrameCount,
    Guid? LatestBuiltSheetAssetId,
    string? LatestBuiltSheetManifest,
    IReadOnlyList<FrameView> Frames);

public sealed record FrameSetSummaryView(
    Guid Id,
    string Name,
    Guid? SourceAssetId,
    int DefaultCellWidth,
    int DefaultCellHeight,
    int FrameCount,
    DateTime UpdatedAt);

public sealed record CreateFrameSetFromAssetRequest(
    Guid SourceAssetId,
    string? Name = null,
    int? ExpectedFrames = null,
    string? LayoutHint = null);

public sealed record DetectSourceRegionsRequest(
    Guid SourceAssetId,
    int? ExpectedFrames = null,
    string? LayoutHint = null,
    bool ReplaceExisting = true);

public sealed record SourceRegionView(
    Guid Id,
    Guid SourceAssetId,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths,
    string RegionType,
    int Order);

public sealed record SourceRegionEditRequest(
    Guid? Id,
    string? Name,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<SpriteSheetShapePath>? ShapePaths = null,
    string RegionType = "frame",
    int Order = 0);

public sealed record SaveSourceRegionsRequest(
    Guid SourceAssetId,
    IReadOnlyList<SourceRegionEditRequest> Regions);

public sealed record CreateFrameSetFromRegionsRequest(
    Guid SourceAssetId,
    IReadOnlyList<Guid> RegionIds,
    string? Name = null);

public sealed record UpdateFrameSourceBoundsRequest(
    Guid FrameSetId,
    Guid FrameId,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<SpriteSheetShapePath>? ShapePaths = null);

public sealed record AddFrameFromRegionRequest(
    Guid FrameSetId,
    Guid SourceRegionId,
    int? InsertAt = null,
    string? Name = null);

public sealed record DuplicateFrameRequest(
    Guid FrameSetId,
    Guid FrameId,
    int? InsertAt = null,
    string? Name = null);

public sealed record SetFrameLogicalCellRequest(
    Guid FrameSetId,
    Guid FrameId,
    int Width,
    int Height);

public sealed record TranslateFrameContentRequest(
    Guid FrameSetId,
    Guid FrameId,
    int ContentOffsetX,
    int ContentOffsetY);

public sealed record ApplyFrameEditCandidateRequest(
    Guid FrameSetId,
    Guid FrameId,
    Guid CandidateAssetId,
    int EditSourceWidth,
    int EditSourceHeight,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight);

public sealed record UpsertFrameMaskRequest(
    Guid FrameId,
    string MaskDataUrl,
    string? Label = null,
    string CoordinateSpace = "logicalFrame");

public sealed record SetCommonCellSizeRequest(
    Guid FrameSetId,
    int Width = 0,
    int Height = 0);

public sealed record AlignFramesRequest(
    Guid FrameSetId,
    string Anchor = "feet",
    bool AxisX = true,
    bool AxisY = true);

public sealed record AlignFramesByAnchorRectRequest(
    Guid FrameSetId,
    Guid ReferenceFrameId,
    SpriteSheetRect AnchorRect,
    int SearchPadding = 24,
    double MinScore = 0.68d,
    bool AxisX = true,
    bool AxisY = true,
    bool Apply = false);

public sealed record AnchorAlignmentResult(
    FrameSetView FrameSet,
    Guid ReferenceFrameId,
    SpriteSheetRect AnchorRect,
    int SearchPadding,
    double MinScore,
    bool Applied,
    IReadOnlyList<AnchorAlignmentMatchView> Matches,
    IReadOnlyList<string> Warnings);

public sealed record AnchorAlignmentMatchView(
    Guid FrameId,
    int FrameIndex,
    string FrameName,
    SpriteSheetRect MatchedAnchorRect,
    int DeltaX,
    int DeltaY,
    double Score,
    bool LowConfidence,
    IReadOnlyList<string> Warnings);

public sealed record BuildSheetRequest(
    Guid FrameSetId,
    int Rows = 1,
    int Columns = 0,
    int Padding = 0,
    int Gutter = 0,
    int OuterMargin = 0,
    string Ordering = "rowMajor",
    string HorizontalAnchor = "center",
    string VerticalAnchor = "bottom",
    string? Name = null);

public sealed record BuildSheetResult(
    Guid BuiltSheetId,
    Guid SheetLayoutId,
    Guid OutputAssetId,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    int Width,
    int Height,
    string ManifestJson,
    IReadOnlyList<string> Warnings);

public sealed record SpriteEditSessionCrop(
    int EditSourceWidth,
    int EditSourceHeight,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight);

public sealed record SpriteEditSessionView(
    Guid Id,
    string Status,
    bool ModalOpen,
    string TargetKind,
    Guid? TargetSourceAssetId,
    Guid? TargetFrameSetId,
    Guid? TargetFrameId,
    Guid? BatchId,
    Guid? MaskId,
    Guid? SelectedCandidateAssetId,
    int? SelectedOutputIndex,
    bool PreviewOverlayActive,
    string Prompt,
    int Count,
    SpriteEditSessionCrop? Crop,
    IReadOnlyList<Guid> CandidateAssetIds,
    IReadOnlyList<GenerationOutputStateView> OutputStates,
    DateTime UpdatedAt);

public sealed record SaveSpriteEditSessionRequest(
    bool ModalOpen,
    string TargetKind,
    Guid? TargetSourceAssetId,
    Guid? TargetFrameSetId,
    Guid? TargetFrameId,
    Guid? BatchId,
    Guid? MaskId,
    Guid? SelectedCandidateAssetId,
    int? SelectedOutputIndex,
    bool PreviewOverlayActive,
    string Prompt,
    int Count,
    SpriteEditSessionCrop? Crop,
    IReadOnlyList<Guid> CandidateAssetIds,
    IReadOnlyList<GenerationOutputStateView> OutputStates);
