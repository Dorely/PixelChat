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
    WorkspaceActivityVisibleState? Activity,
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
    IReadOnlyList<WorkspaceBatchSummary> VisibleRecentBatches,
    WorkspaceCompareReviewSetSummary? ReviewSet);

public sealed record WorkspaceActivityVisibleState(
    int ActivityCount,
    int RecentBatchCount,
    IReadOnlyList<WorkspaceActivitySummary> RecentActivity);

public sealed record WorkspaceActivitySummary(
    Guid Id,
    string Title,
    string Summary,
    string Status,
    string Actor,
    string WorkflowKind,
    Guid? PrimaryArtifactId,
    string PrimaryArtifactKind,
    DateTime UpdatedAt);

public sealed record WorkspaceSpritesVisibleState(
    WorkspaceSpriteSheetSummary? ActiveSpriteSheet = null,
    WorkspaceFrameSetSummary? ActiveFrameSet = null,
    WorkspaceAssetSummary? SourceAsset = null,
    IReadOnlyList<WorkspaceSpriteRegionSummary>? SelectedRegions = null,
    IReadOnlyList<WorkspaceFrameSummary>? SelectedFrames = null,
    Guid? SelectedMaskId = null,
    WorkspaceBuiltSheetSummary? BuiltSheet = null,
    string Mode = "",
    WorkspaceSpriteEditModalSummary? EditModal = null,
    WorkspaceSpriteAnchorSummary? Anchor = null);

public sealed record WorkspaceSpriteEditModalSummary(
    string TargetKind,
    Guid? TargetId,
    Guid? BatchId,
    IReadOnlyList<Guid> CandidateAssetIds,
    Guid? SelectedCandidateAssetId,
    bool IsGenerating,
    int CandidateCount = 1,
    string CandidateSetState = "");

public sealed record WorkspaceSpriteAnchorSummary(
    Guid? ReferenceFrameId,
    SpriteSheetRect? AnchorRect,
    int SearchPadding,
    double MinScore,
    bool AxisX,
    bool AxisY,
    int MatchCount,
    int LowConfidenceCount,
    bool Applied);

public sealed record WorkspaceRecipeVisibleState(
    Guid? EditingRecipeId,
    string Name,
    string UseCase,
    string PromptTemplate,
    string StyleRules,
    string AvoidRules,
    string Notes,
    string PreferredSize,
    Guid? ExampleAssetId,
    WorkspaceAssetSummary? ExampleAsset);

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
    int? SourcePromptRecipeVersion,
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
    int? PromptRecipeVersion,
    IReadOnlyList<Guid> InputAssetIds,
    IReadOnlyList<Guid> OutputAssetIds,
    Guid? ParentBatchId,
    string PromptPreview,
    string Error,
    DateTime CreatedAt);

public sealed record WorkspaceCompareReviewSetSummary(
    Guid Id,
    string Title,
    string Summary,
    IReadOnlyList<WorkspaceCompareReviewItemSummary> Items,
    DateTime UpdatedAt);

public sealed record WorkspaceCompareReviewItemSummary(
    Guid Id,
    CompareReviewItemKind Kind,
    Guid RefId,
    string Label,
    string Notes,
    int SortOrder);

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
    SpriteSheetBackground Background,
    int FrameCount,
    DateTime UpdatedAt);

public sealed record WorkspaceFrameSetSummary(
    Guid Id,
    string Name,
    Guid? SourceAssetId,
    int DefaultCellWidth,
    int DefaultCellHeight,
    int FrameCount);

public sealed record WorkspaceSpriteRegionSummary(
    Guid Id,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    string RegionType,
    int Order);

public sealed record WorkspaceFrameSummary(
    Guid Id,
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
    bool HasMask);

public sealed record WorkspaceBuiltSheetSummary(
    Guid OutputAssetId,
    string ManifestJson);
