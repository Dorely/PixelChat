using PixelChat.Models;

namespace PixelChat.Art;

public sealed record WorkbenchView(
    ProjectView Project,
    IReadOnlyList<ProjectView> Projects,
    IReadOnlyList<ArtAssetView> Assets,
    IReadOnlyList<GenerationBatchView> Batches,
    IReadOnlyList<PromptRecipeView> Recipes,
    IReadOnlyList<AnimationRecipeView> AnimationRecipes,
    IReadOnlyList<SpriteSheetDefinitionView> SpriteSheets,
    IReadOnlyList<ImageMaskView> Masks,
    IReadOnlyList<ChatContextAttachmentView> Attachments,
    CompareReviewSetView? CompareReviewSet,
    GenerationBatchView? ActiveBatch,
    SpriteSheetDefinitionView? ActiveSpriteSheet,
    ProviderStatusView ImageProviderStatus);

public sealed record ProjectView(
    Guid Id,
    string Name,
    WorkspaceMode ActiveWorkspaceMode,
    Guid? ActiveBatchId,
    Guid? ActiveSpriteSheetId,
    Guid? ActiveFrameSetId,
    string ActiveSpriteMode,
    Guid? ActiveSpriteSourceAssetId,
    Guid? ActiveSpriteFrameId,
    string ActiveSpriteRegionIdsJson,
    DateTime UpdatedAt);

public sealed record ArtAssetView(
    Guid Id,
    string Label,
    string FileName,
    ArtAssetKind Kind,
    string ContentType,
    string PreviewImageUrl,
    string FullImageUrl,
    int? Width,
    int? Height,
    Guid? ParentAssetId,
    Guid? SourceBatchId,
    int? BatchOutputIndex,
    Guid? SourcePromptRecipeId,
    int? SourcePromptRecipeVersion,
    Guid? SourceAnimationRecipeId,
    int? SourceAnimationRecipeVersion,
    bool IsFavorite,
    string Notes,
    string Prompt,
    DateTime CreatedAt);

public sealed record ArtAssetExportView(
    Guid Id,
    string Label,
    string FileName,
    ArtAssetKind Kind,
    string ContentType,
    string DataUrl,
    int? Width,
    int? Height);

public sealed record SpriteSheetDefinitionView(
    Guid Id,
    Guid SourceAssetId,
    Guid? WorkingAssetId,
    string Label,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    int Padding,
    int Gutter,
    int Fps,
    bool Loop,
    string HorizontalAnchor,
    string VerticalAnchor,
    SpriteSheetBackground Background,
    IReadOnlyList<SpriteSheetFrameRecordView> Frames,
    SpriteSheetStabilizationView? Stabilization,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SpriteSheetBackground(
    string Mode,
    byte R,
    byte G,
    byte B,
    byte A);

public sealed record SpriteSheetFrameRecordView(
    Guid Id,
    Guid SpriteSheetId,
    int Index,
    string Label,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths,
    SpriteSheetRect CellRect,
    SpriteSheetRect SpriteRect,
    string PreviewImageUrl,
    int PreviewWidth,
    int PreviewHeight,
    string WorkingState,
    int WorkingWidth,
    int WorkingHeight,
    int WorkingMargin,
    DateTime? WorkingUpdatedAt,
    double Duration,
    Guid? SourceImageAssetId = null,
    SpriteSheetRect? SourceImageRect = null,
    string PoseName = "",
    double Phase = 0,
    int RootOffsetX = 0,
    int RootOffsetY = 0,
    int DurationMs = 0,
    IReadOnlyList<string>? FootContacts = null,
    bool IsKeyframe = false,
    int PivotX = 0,
    int PivotY = 0,
    double AppliedScale = 1,
    int TranslationX = 0,
    int TranslationY = 0,
    string QaStatus = "",
    IReadOnlyList<string>? RepairHistory = null);

public sealed record SpriteSheetStabilizationView(
    int ReferenceFrameNumber,
    int ReferenceFrameIndex,
    SpriteSheetRect AnchorRect,
    SpriteSheetRect ReferenceWorkingAnchorRect,
    SpriteSheetPoint ReferenceAnchorCenter,
    int NormalizedWidth,
    int NormalizedHeight,
    int SearchPadding,
    double MinScore,
    bool Applied,
    bool RequiresReassembly,
    DateTime UpdatedAt,
    IReadOnlyList<SpriteSheetStabilizationMatchView> Matches,
    IReadOnlyList<string> Warnings);

public sealed record SpriteSheetStabilizationMatchView(
    int FrameNumber,
    int Index,
    SpriteSheetRect SourceRect,
    SpriteSheetRect InputFrameRect,
    SpriteSheetRect MatchedAnchorRect,
    SpriteSheetRect PlacementRect,
    int DeltaX,
    int DeltaY,
    double Score,
    bool LowConfidence,
    bool Clipped,
    IReadOnlyList<string> Warnings);

public sealed record SpriteSheetStabilizationFrameView(
    int FrameNumber,
    int Index,
    string Label,
    int InputWidth,
    int InputHeight,
    int OutputWidth,
    int OutputHeight,
    bool UsedExistingWorkingImage,
    bool Applied,
    string WorkingState);

public sealed record StabilizeSpriteSheetFramesRequest(
    Guid SpriteSheetId,
    int ReferenceFrameNumber,
    SpriteSheetRect AnchorRect,
    int? SearchPadding,
    double? MinScore,
    IReadOnlyList<int>? TargetFrameNumbers,
    bool Apply);

public sealed record StabilizeSpriteSheetFramesResult(
    Guid SpriteSheetId,
    Guid SourceAssetId,
    Guid? WorkingAssetId,
    bool Applied,
    int ImageWidth,
    int ImageHeight,
    SpriteSheetStabilizationView Stabilization,
    IReadOnlyList<SpriteSheetStabilizationFrameView> Frames,
    IReadOnlyList<string> Warnings,
    SpriteAnimationReviewImageView DiagnosticImage,
    SpriteSheetDefinitionView? SavedSheet);

public sealed record SpriteFrameWorkingView(
    Guid FrameId,
    Guid SpriteSheetId,
    int Index,
    string Label,
    string State,
    string WorkingPngDataUrl,
    int WorkingWidth,
    int WorkingHeight,
    int WorkingMargin,
    DateTime? WorkingUpdatedAt);

public sealed record EditSpriteFrameRequest(
    Guid SpriteSheetId,
    int FrameIndex,
    string Prompt,
    string? Background);

public sealed record EraseSpriteFrameRegionsRequest(
    Guid SpriteSheetId,
    int FrameIndex,
    IReadOnlyList<SpriteSheetRect> Rects,
    IReadOnlyList<SpriteSheetShapePath>? Polygons,
    string? Mode = null);

public sealed record ReassembleSpriteSheetResult(
    SpriteSheetDefinitionView Sheet,
    IReadOnlyList<SpriteFrameReassemblyView> Frames,
    IReadOnlyList<string> Warnings);

public sealed record SpriteFrameReassemblyView(
    int Index,
    string Label,
    bool UsedWorkingImage,
    SpriteSheetRect DetectedRect,
    SpriteSheetRect PlacedRect,
    IReadOnlyList<string> Warnings);

public sealed record SpriteAnimationReviewView(
    Guid SpriteSheetId,
    int FrameCount,
    int Rows,
    int Columns,
    int Fps,
    bool Loop,
    SpriteAnimationMetricsView Metrics,
    IReadOnlyList<SpriteAnimationReviewImageView> Images);

public sealed record SpriteAnimationMetricsView(
    IReadOnlyList<SpriteAnimationFramePairMetricsView> FramePairs,
    double MeanCentroidDrift,
    double MaxCentroidDrift,
    double AreaVariancePercent);

public sealed record SpriteAnimationFramePairMetricsView(
    int FromFrame,
    int ToFrame,
    bool LoopSeam,
    double CentroidDeltaX,
    double CentroidDeltaY,
    double CentroidDistance,
    int BoundingBoxWidthDelta,
    int BoundingBoxHeightDelta,
    double SilhouetteAreaChangePercent,
    double ForegroundPixelDiffPercent);

public sealed record SpriteAnimationReviewImageView(
    string Label,
    string FileName,
    string ContentType,
    string DataUrl,
    string Kind,
    int? FrameIndex,
    int? FromFrame,
    int? ToFrame);

public sealed record SpriteSheetFrameView(
    int Index,
    string Label,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths,
    SpriteSheetRect CellRect,
    SpriteSheetRect SpriteRect,
    string PreviewPngDataUrl,
    Guid? SourceImageAssetId = null,
    SpriteSheetRect? SourceImageRect = null);

public sealed record SpriteSheetRect(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record SpriteSheetPoint(
    int X,
    int Y);

public sealed record SpriteSheetShapePath(
    IReadOnlyList<SpriteSheetPoint> Points);

public sealed record SpriteSheetDetectionRequest(
    Guid SourceAssetId,
    int? ExpectedFrames,
    string? LayoutHint,
    string? BackgroundMode);

public sealed record SpriteSheetDetectionResult(
    Guid SourceAssetId,
    int ImageWidth,
    int ImageHeight,
    int Rows,
    int Columns,
    SpriteSheetBackground Background,
    IReadOnlyList<SpriteSheetFrameDetectionView> Frames)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<SpriteSheetRejectedSegmentView> RejectedSegments { get; init; } = [];
    public IReadOnlyList<SpriteSheetFrameQualityView> FrameQuality { get; init; } = [];
}

public sealed record SpriteSheetFrameDetectionView(
    int Index,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths);

public sealed record SpriteSheetRejectedSegmentView(
    int Index,
    SpriteSheetRect Rect,
    string Reason);

public sealed record SpriteSheetFrameQualityView(
    int FrameNumber,
    int Index,
    SpriteSheetRect SourceRect,
    int ForegroundPixelCount,
    double ForegroundCoveragePercent,
    IReadOnlyList<string> Warnings);

public sealed record RepairSpriteSheetFramesRequest(
    Guid SpriteSheetId,
    int ExpectedFrames,
    string? LayoutHint,
    IReadOnlyList<int>? TargetFrameNumbers,
    bool Apply);

public sealed record RepairSpriteSheetFramesResult(
    Guid SpriteSheetId,
    Guid SourceAssetId,
    Guid? WorkingAssetId,
    bool Applied,
    int ImageWidth,
    int ImageHeight,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    int Padding,
    int Gutter,
    int Fps,
    bool Loop,
    string HorizontalAnchor,
    string VerticalAnchor,
    SpriteSheetBackground Background,
    IReadOnlyList<SpriteSheetFrameUpdateView> Frames,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SpriteSheetRejectedSegmentView> RejectedSegments,
    IReadOnlyList<SpriteSheetFrameQualityView> FrameQuality,
    SpriteSheetDefinitionView? SavedSheet);

public sealed record AutosaveSpriteSheetLayoutRequest(
    Guid SpriteSheetId,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    int Padding,
    int Gutter,
    int Fps,
    bool Loop,
    string HorizontalAnchor,
    string VerticalAnchor,
    IReadOnlyList<SpriteSheetFrameUpdateView> Frames);

public sealed record UpdateSpriteSheetFramesRequest(
    Guid SpriteSheetId,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    int Padding,
    int Gutter,
    int Fps,
    bool Loop,
    string HorizontalAnchor,
    string VerticalAnchor,
    IReadOnlyList<SpriteSheetFrameUpdateView> Frames);

public sealed record ComposeSpriteSheetFromImagesRequest(
    IReadOnlyList<Guid> AssetIds,
    Guid? SpriteSheetId = null,
    int? InsertAt = null,
    string? Label = null,
    int? Rows = null,
    int? Columns = null,
    int? Padding = null,
    int? Gutter = null,
    int? Fps = null,
    bool? Loop = null,
    string? HorizontalAnchor = null,
    string? VerticalAnchor = null);

public sealed record SpriteSheetFrameUpdateView(
    int Index,
    string Label,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths,
    Guid? SourceImageAssetId = null,
    SpriteSheetRect? SourceImageRect = null);

public sealed record BackgroundRemovalExportCacheRequest(
    string RemovalMethod,
    string ModelName,
    string RembgPackageVersion,
    bool AlphaMatting,
    string OptionsHash);

public sealed record SaveBackgroundRemovalExportCacheRequest(
    string RemovalMethod,
    string ModelName,
    string RembgPackageVersion,
    bool AlphaMatting,
    string OptionsHash,
    string ContentType,
    byte[] Data,
    int TransparentPixels,
    int SemiTransparentPixels,
    int OpaquePixels,
    string ActualBackend);

public sealed record BackgroundRemovalExportCacheView(
    Guid Id,
    Guid AssetId,
    string SourceImageSha256,
    string RemovalMethod,
    string ModelName,
    string RembgPackageVersion,
    bool AlphaMatting,
    string OptionsHash,
    string ContentType,
    string DataUrl,
    int TransparentPixels,
    int SemiTransparentPixels,
    int OpaquePixels,
    string ActualBackend,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SaveExportStepCacheRequest(
    int StepIndex,
    string ParentImageSha256,
    string Method,
    string OptionsHash,
    string ModelName,
    string ActualBackend,
    string ContentType,
    byte[] Data,
    int? Width,
    int? Height);

public sealed record ExportStepCacheView(
    Guid Id,
    Guid AssetId,
    string SourceImageSha256,
    int StepIndex,
    string ParentImageSha256,
    string OutputImageSha256,
    string Method,
    string OptionsHash,
    string ModelName,
    string ActualBackend,
    string ContentType,
    string DataUrl,
    int? Width,
    int? Height,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record GenerationBatchView(
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
    IReadOnlyList<Guid> InputAssetIds,
    IReadOnlyList<Guid> InputMaskIds,
    IReadOnlyList<Guid> OutputAssetIds,
    Guid? ParentBatchId,
    Guid? PromptRecipeId,
    int? PromptRecipeVersion,
    Guid? AnimationRecipeId,
    int? AnimationRecipeVersion,
    GenerationBatchStatus Status,
    string Error,
    IReadOnlyList<GenerationOutputStateView> OutputStates,
    IReadOnlyList<GenerationOutputErrorView> OutputErrors,
    DateTime CreatedAt);

public sealed record GenerationOutputErrorView(
    int OutputIndex,
    string Error,
    string? ErrorKind = null,
    string? RequestId = null,
    string? ResponseId = null,
    string? CallId = null,
    int? StatusCode = null,
    string? LastEventType = null,
    int EventCount = 0);

public sealed record GenerationOutputStateView(
    int OutputIndex,
    GenerationOutputStatus Status,
    int Attempt = 0,
    string Message = "",
    string Error = "",
    string? ErrorKind = null,
    string? RequestId = null,
    string? ResponseId = null,
    string? CallId = null,
    string? LastEventType = null,
    int EventCount = 0,
    DateTime? StartedAt = null,
    DateTime? UpdatedAt = null,
    DateTime? CompletedAt = null);

public enum GenerationOutputStatus
{
    Queued,
    Running,
    Generating,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record PromptRecipeView(
    Guid Id,
    string Name,
    string Prompt,
    string Notes,
    IReadOnlyList<RecipeAssetAttachmentView> Attachments,
    int CurrentVersion,
    DateTime CreatedAt);

public sealed record AnimationRecipeView(
    Guid Id,
    string Name,
    string Prompt,
    string Notes,
    IReadOnlyList<RecipeAssetAttachmentView> Attachments,
    int CurrentVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record RecipeAssetAttachmentView(
    Guid Id,
    Guid AssetId,
    string Role,
    int SortOrder,
    string Notes,
    string AssetLabel,
    ArtAssetKind AssetKind,
    string PreviewImageUrl,
    int? Width,
    int? Height,
    DateTime CreatedAt);

public sealed record ImageMaskView(
    Guid Id,
    Guid AssetId,
    string Label,
    string ContentType,
    string PreviewImageUrl,
    int Width,
    int Height,
    DateTime CreatedAt);

public sealed record ImageBinaryView(
    string ContentType,
    byte[] Data,
    string FileName,
    DateTime LastModified);

public sealed record ChatContextAttachmentView(
    Guid Id,
    ChatContextAttachmentType Type,
    Guid RefId,
    string Label,
    int SortOrder);

public sealed record CompareReviewSetView(
    Guid Id,
    string Title,
    string Summary,
    IReadOnlyList<CompareReviewSetItemView> Items,
    DateTime UpdatedAt);

public sealed record CompareReviewSetItemView(
    Guid Id,
    CompareReviewItemKind Kind,
    Guid RefId,
    string Label,
    string Notes,
    int SortOrder,
    DateTime CreatedAt);

public sealed record ProviderStatusView(
    bool Connected,
    string Message);

public sealed record GenerateImagesRequest(
    string Prompt,
    string NegativePrompt,
    string Size,
    int Count,
    string Background,
    Guid? PromptRecipeId,
    Guid? AnimationRecipeId,
    IReadOnlyList<Guid> ReferenceAssetIds,
    Guid? ParentBatchId,
    string? ImageModel = null);

public sealed record EditImageRequest(
    Guid SourceAssetId,
    string Prompt,
    string Size,
    int Count,
    string Background,
    Guid? PromptRecipeId,
    string? SourcePngDataUrl,
    string? MaskPngDataUrl,
    IReadOnlyList<Guid> ReferenceAssetIds,
    bool SwitchToCompare = true);

public sealed record CompareReviewSetItemRequest(
    CompareReviewItemKind Kind,
    Guid RefId,
    string? Label = null,
    string? Notes = null);

public sealed record SetCompareReviewSetRequest(
    string? Title,
    string? Summary,
    IReadOnlyList<CompareReviewSetItemRequest> Items,
    bool SwitchToCompare = true);

public sealed record AddCompareReviewItemsRequest(
    string? Title,
    string? Summary,
    IReadOnlyList<CompareReviewSetItemRequest> Items,
    bool SwitchToCompare = true);

public sealed record ImportAssetRequest(
    string FileName,
    string ContentType,
    byte[] Data,
    string Label);

public sealed record CropAssetRequest(
    Guid ParentAssetId,
    string CropDataUrl,
    string Label);

/// <summary>
/// Extracts a source-image region into a standalone, opaque project asset
/// (greenfield Source-view "Extract as Asset"). Padding/canvas areas are filled with
/// the source background color so the result stays opaque (transparency is an Export
/// concern only).
/// </summary>
public sealed record ExtractRegionAsAssetRequest(
    Guid SourceAssetId,
    int X,
    int Y,
    int Width,
    int Height,
    string? Name = null,
    int Padding = 0,
    int? FixedCanvasWidth = null,
    int? FixedCanvasHeight = null,
    bool CenterInCanvas = true,
    bool LinkToSource = true);

public sealed record ExtractRegionAsAssetResult(
    ArtAssetView Asset,
    Guid RegionId,
    Guid StandaloneAssetId,
    int LogicalWidth,
    int LogicalHeight,
    int ContentOffsetX,
    int ContentOffsetY);

public sealed record SavePromptRecipeRequest(
    string Name,
    string Prompt,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record UpdatePromptRecipeRequest(
    string Name,
    string Prompt,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record SaveAnimationRecipeRequest(
    string Name,
    string Prompt,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record UpdateAnimationRecipeRequest(
    string Name,
    string Prompt,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record RecipeAssetAttachmentRequest(
    Guid AssetId,
    string Role,
    string? Notes = null);

public sealed record GenerateAnimationGuideRequest(
    Guid? ReferenceAssetId,
    string AnimationKind,
    string? AssetType = null,
    string? StructureType = null,
    string? Facing = null,
    int? FrameCount = null,
    int? Fps = null,
    string? RootMotion = null,
    string? TargetCellSize = null,
    string? MotionClipId = null,
    string? Label = null,
    int? Rows = null,
    int? Columns = null,
    string? GuideCellSize = null,
    double? GuideCameraYawDegrees = null,
    double? GuideCameraPitchDegrees = null,
    bool? Loop = null,
    double? SafeMarginPercent = null);

public sealed record AnimationGuidePreviewView(
    string Label,
    string ImageDataUrl,
    string AnimationKind,
    string AssetType,
    string StructureType,
    string Facing,
    string RootMotion,
    int FrameCount,
    int Fps,
    bool Loop,
    int Rows,
    int Columns,
    int CanvasWidth,
    int CanvasHeight,
    int GuideCellWidth,
    int GuideCellHeight,
    int TargetCellWidth,
    int TargetCellHeight,
    double? GuideCameraYawDegrees,
    double? GuideCameraPitchDegrees,
    string Renderer,
    string RenderStyle,
    string? MotionClipId);

public sealed record AnimationGuideRenderView(
    Guid GuideAssetId,
    Guid DiagnosticGuideAssetId,
    string Label,
    string AnimationKind,
    string AssetType,
    string StructureType,
    string Facing,
    string RootMotion,
    int FrameCount,
    IReadOnlyList<int> FrameOrder,
    int Fps,
    bool Loop,
    int Rows,
    int Columns,
    int CanvasWidth,
    int CanvasHeight,
    int GuideCellWidth,
    int GuideCellHeight,
    int TargetCellWidth,
    int TargetCellHeight,
    IReadOnlyList<SpriteSheetRect> ExpectedFrameBoxes,
    string AnchorStrategy,
    string PromptScaffold,
    string ExportDefaultsJson,
    string Renderer,
    string RenderStyle,
    string? MotionClipId,
    double? GuideCameraYawDegrees,
    double? GuideCameraPitchDegrees,
    string? MotionSourcePackage,
    string? MotionSourceLicense,
    string? MotionSourceUrl,
    string Message);

public sealed record MotionClipView(
    string MotionClipId,
    string DisplayName,
    string AnimationName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> SupportedAnimationKinds,
    IReadOnlyList<string> SearchTags,
    bool LoopRecommended,
    string RecommendedRootMotion,
    int DefaultFps,
    IReadOnlyList<int> AllowedSampleCounts,
    IReadOnlyList<string> SupportedAssetTypes,
    string SourcePackage,
    string SourceUrl,
    string License);

public sealed record AdjustSpriteSheetFrameBoxRequest(
    Guid SpriteSheetId,
    int FrameIndex,
    SpriteSheetRect SourceRect,
    SpriteSheetRect? SourceImageRect = null,
    string? Label = null,
    bool FitCells = true);

public sealed record PromptRecipeVersionView(
    Guid Id,
    Guid RecipeId,
    int Version,
    string Name,
    string Notes,
    string Source,
    string ChangeSummary,
    DateTime CreatedAt);

public sealed record AnimationRecipeVersionView(
    Guid Id,
    Guid AnimationRecipeId,
    int Version,
    string Name,
    string Notes,
    string Source,
    string ChangeSummary,
    DateTime CreatedAt);
