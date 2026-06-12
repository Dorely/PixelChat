using PixelChat.Models;

namespace PixelChat.Art;

public sealed record WorkbenchView(
    ProjectView Project,
    IReadOnlyList<ProjectView> Projects,
    IReadOnlyList<ArtAssetView> Assets,
    IReadOnlyList<GenerationBatchView> Batches,
    IReadOnlyList<PromptRecipeView> Recipes,
    IReadOnlyList<SpriteSheetDefinitionView> SpriteSheets,
    IReadOnlyList<ImageMaskView> Masks,
    IReadOnlyList<ChatContextAttachmentView> Attachments,
    GenerationBatchView? ActiveBatch,
    SpriteSheetDefinitionView? ActiveSpriteSheet,
    ProviderStatusView ImageProviderStatus);

public sealed record ProjectView(
    Guid Id,
    string Name,
    WorkspaceMode ActiveWorkspaceMode,
    Guid? ActiveBatchId,
    Guid? ActiveSpriteSheetId);

public sealed record ArtAssetView(
    Guid Id,
    string Label,
    string FileName,
    ArtAssetKind Kind,
    string ContentType,
    string PreviewDataUrl,
    int? Width,
    int? Height,
    Guid? ParentAssetId,
    Guid? SourceBatchId,
    int? BatchOutputIndex,
    Guid? SourcePromptRecipeId,
    int? SourcePromptRecipeVersion,
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
    string PreviewDataUrl,
    int PreviewWidth,
    int PreviewHeight,
    string WorkingState,
    int WorkingWidth,
    int WorkingHeight,
    int WorkingMargin,
    DateTime? WorkingUpdatedAt,
    double Duration);

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
    IReadOnlyList<SpriteSheetShapePath>? Polygons);

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
    string PreviewPngDataUrl);

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

public sealed record SpriteSheetFrameUpdateView(
    int Index,
    string Label,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths);

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
    string AssetType,
    string PromptTemplate,
    IReadOnlyList<string> StyleRules,
    IReadOnlyList<string> AvoidRules,
    Guid? ExampleAssetId,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes,
    int CurrentVersion,
    DateTime CreatedAt);

public sealed record ImageMaskView(
    Guid Id,
    Guid AssetId,
    string Label,
    string ContentType,
    string PreviewDataUrl,
    int Width,
    int Height,
    DateTime CreatedAt);

public sealed record ChatContextAttachmentView(
    Guid Id,
    ChatContextAttachmentType Type,
    Guid RefId,
    string Label,
    int SortOrder);

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
    IReadOnlyList<Guid> ReferenceAssetIds,
    Guid? ParentBatchId);

public sealed record EditImageRequest(
    Guid SourceAssetId,
    string Prompt,
    string Size,
    int Count,
    string Background,
    Guid? PromptRecipeId,
    string? SourcePngDataUrl,
    string? MaskPngDataUrl,
    IReadOnlyList<Guid> ReferenceAssetIds);

public sealed record ImportAssetRequest(
    string FileName,
    string ContentType,
    byte[] Data,
    string Label);

public sealed record CropAssetRequest(
    Guid ParentAssetId,
    string CropDataUrl,
    string Label);

public sealed record SavePromptRecipeRequest(
    string Name,
    string AssetType,
    string PromptTemplate,
    IReadOnlyList<string> StyleRules,
    IReadOnlyList<string> AvoidRules,
    Guid? ExampleAssetId,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record UpdatePromptRecipeRequest(
    string Name,
    string AssetType,
    string PromptTemplate,
    IReadOnlyList<string> StyleRules,
    IReadOnlyList<string> AvoidRules,
    Guid? ExampleAssetId,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes,
    string Source = "user",
    string ChangeSummary = "");

public sealed record PromptRecipeVersionView(
    Guid Id,
    Guid RecipeId,
    int Version,
    string Name,
    string Source,
    string ChangeSummary,
    Guid? ExampleAssetId,
    DateTime CreatedAt);
