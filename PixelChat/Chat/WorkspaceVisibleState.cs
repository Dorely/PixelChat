using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Chat;

public interface IWorkspaceVisibleStateStore
{
    WorkspaceVisibleStateSnapshot? Get(Guid projectId);
    void Publish(WorkspaceVisibleStateSnapshot snapshot);
    void Clear(Guid projectId);
}

public sealed class WorkspaceVisibleStateStore : IWorkspaceVisibleStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, WorkspaceVisibleStateSnapshot> _snapshots = [];

    public WorkspaceVisibleStateSnapshot? Get(Guid projectId)
    {
        lock (_gate)
            return _snapshots.GetValueOrDefault(projectId);
    }

    public void Publish(WorkspaceVisibleStateSnapshot snapshot)
    {
        lock (_gate)
            _snapshots[snapshot.ProjectId] = snapshot;
    }

    public void Clear(Guid projectId)
    {
        lock (_gate)
            _snapshots.Remove(projectId);
    }
}

public sealed record WorkspaceVisibleStateSnapshot(
    Guid ProjectId,
    string ProjectName,
    WorkspaceMode ActiveMode,
    ProviderStatusView Provider,
    IReadOnlyList<ChatContextAttachmentView> ChatAttachments,
    WorkspaceGenerateVisibleState? Generate,
    WorkspaceEditVisibleState? Edit,
    WorkspaceCompareVisibleState? Compare,
    WorkspaceSpritesVisibleState? Sprites,
    WorkspaceRecipeVisibleState? Recipes,
    WorkspaceAssetsVisibleState? Assets,
    DateTime CapturedAt);

public sealed record WorkspaceGenerateVisibleState(
    string Prompt,
    string NegativePrompt,
    string Size,
    string Background,
    int Count,
    Guid? PromptRecipeId,
    IReadOnlyList<Guid> ReferenceAssetIds,
    IReadOnlyList<WorkspaceAssetSummary> ReferenceAssets);

public sealed record WorkspaceEditVisibleState(
    WorkspaceAssetSummary? SourceAsset,
    string Prompt,
    string Size,
    string Background,
    int Count,
    Guid? PromptRecipeId,
    IReadOnlyList<Guid> ReferenceAssetIds,
    IReadOnlyList<WorkspaceAssetSummary> ReferenceAssets,
    bool HasMask,
    Guid? MaskId,
    string EditorTool,
    int BrushSize);

public sealed record WorkspaceCompareVisibleState(
    WorkspaceBatchSummary? ActiveBatch,
    IReadOnlyList<WorkspaceBatchSummary> VisibleRecentBatches);

public sealed record WorkspaceSpritesVisibleState(
    WorkspaceSpriteSheetSummary? ActiveSpriteSheet);

public sealed record WorkspaceRecipeVisibleState(
    Guid? EditingRecipeId,
    string Name,
    string UseCase,
    string PromptTemplate,
    string StyleRules,
    string AvoidRules,
    string Notes,
    string PreferredSize,
    IReadOnlyList<Guid> ExampleAssetIds,
    IReadOnlyList<WorkspaceAssetSummary> ExampleAssets);

public sealed record WorkspaceAssetsVisibleState(
    string Organization,
    IReadOnlyList<WorkspaceAssetSummary> VisibleAssets);

public sealed record WorkspaceAssetSummary(
    Guid Id,
    string Label,
    ArtAssetKind Kind,
    string ContentType,
    int? Width,
    int? Height,
    Guid? ParentAssetId,
    Guid? SourceBatchId,
    int? BatchOutputIndex,
    Guid? SourcePromptRecipeId,
    bool IsFavorite,
    string Notes,
    string PromptPreview,
    DateTime CreatedAt);

public sealed record WorkspaceBatchSummary(
    Guid Id,
    string Label,
    GenerationBatchStatus Status,
    string Size,
    string Background,
    int Count,
    Guid? PromptRecipeId,
    IReadOnlyList<Guid> InputAssetIds,
    IReadOnlyList<Guid> OutputAssetIds,
    Guid? ParentBatchId,
    string PromptPreview,
    string Error,
    DateTime CreatedAt);

public sealed record WorkspaceSpriteSheetSummary(
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
    int FrameCount,
    DateTime UpdatedAt);
