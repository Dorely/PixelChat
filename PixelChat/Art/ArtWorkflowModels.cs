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
    IReadOnlyList<SpriteSheetFrameRecordView> Frames,
    DateTime CreatedAt,
    DateTime UpdatedAt);

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
    double Duration);

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
    IReadOnlyList<SpriteSheetFrameDetectionView> Frames);

public sealed record SpriteSheetFrameDetectionView(
    int Index,
    SpriteSheetRect SourceRect,
    IReadOnlyList<SpriteSheetShapePath> ShapePaths);

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
    IReadOnlyList<SpriteSheetFrameView> Frames);

public sealed record NormalizeSpriteSheetRequest(
    Guid SpriteSheetId,
    string WorkingPngDataUrl,
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
    IReadOnlyList<SpriteSheetFrameView> Frames);

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
    IReadOnlyList<Guid> ExampleAssetIds,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes,
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
    IReadOnlyList<Guid> ExampleAssetIds,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes);

public sealed record UpdatePromptRecipeRequest(
    string Name,
    string AssetType,
    string PromptTemplate,
    IReadOnlyList<string> StyleRules,
    IReadOnlyList<string> AvoidRules,
    IReadOnlyList<Guid> ExampleAssetIds,
    string PreferredProvider,
    string PreferredModel,
    string PreferredSize,
    string Notes);
