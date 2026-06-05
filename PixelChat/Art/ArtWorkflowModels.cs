using PixelChat.Models;

namespace PixelChat.Art;

public sealed record WorkbenchView(
    ProjectView Project,
    IReadOnlyList<ProjectView> Projects,
    IReadOnlyList<ArtAssetView> Assets,
    IReadOnlyList<GenerationBatchView> Batches,
    IReadOnlyList<PromptRecipeView> Recipes,
    IReadOnlyList<ImageMaskView> Masks,
    IReadOnlyList<ChatContextAttachmentView> Attachments,
    ArtAssetView? ActiveAsset,
    GenerationBatchView? ActiveBatch,
    ProviderStatusView ImageProviderStatus);

public sealed record ProjectView(
    Guid Id,
    string Name,
    WorkspaceMode ActiveWorkspaceMode,
    Guid? ActiveAssetId,
    Guid? ActiveBatchId);

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
    Guid? SourcePromptRecipeId,
    bool IsFavorite,
    bool IsRejected,
    bool IsReference,
    string Notes,
    string Prompt,
    DateTime CreatedAt);

public sealed record GenerationBatchView(
    Guid Id,
    string Label,
    string Provider,
    string MainlineModel,
    string ImageModel,
    string Prompt,
    string NegativePrompt,
    string Size,
    int Count,
    IReadOnlyList<Guid> InputAssetIds,
    IReadOnlyList<Guid> InputMaskIds,
    IReadOnlyList<Guid> OutputAssetIds,
    Guid? ParentBatchId,
    Guid? PromptRecipeId,
    GenerationBatchStatus Status,
    string Error,
    DateTime CreatedAt);

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
    Guid? PromptRecipeId,
    IReadOnlyList<Guid> ReferenceAssetIds,
    Guid? ParentBatchId);

public sealed record EditImageRequest(
    Guid SourceAssetId,
    string Prompt,
    string Size,
    int Count,
    Guid? MaskId,
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

public sealed record SaveMaskRequest(
    Guid AssetId,
    string MaskDataUrl,
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
