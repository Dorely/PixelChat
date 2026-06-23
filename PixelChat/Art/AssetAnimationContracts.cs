using PixelChat.Models;

namespace PixelChat.Art;

public sealed record CreateAssetProfileRequest(
    Guid CanonicalAssetId,
    Guid? StyleAssetId,
    string? Label,
    string? AssetType,
    string? StructureType,
    IReadOnlyList<string>? RequiredFeatures,
    IReadOnlyList<string>? ForbiddenChanges,
    bool Frozen = true);

public sealed record PlanAssetAnimationRequest(
    Guid AssetProfileId,
    string AnimationKind,
    string? Facing = null,
    string? Strategy = null,
    int? FrameCount = null,
    int? Fps = null,
    string? RootMotion = null,
    string? PromptSummary = null,
    string? TargetCellSize = null,
    string? MotionClipId = null);

public sealed record RunAnimationCandidatesRequest(
    Guid AssetAnimationJobId,
    int? CandidateCount = null);

public sealed record MarkAnimationFramesRequest(
    Guid AssetAnimationJobId,
    IReadOnlyList<MarkAnimationFrameRequest> Frames);

public sealed record MarkAnimationFrameRequest(
    int FrameNumber,
    string Status,
    string? Reason = null,
    bool ForceAccept = false);

public sealed record RegenerateAnimationFramesRequest(
    Guid AssetAnimationJobId,
    IReadOnlyList<int> FrameNumbers,
    string? Prompt = null);

public sealed record ExtractAnimationFixedSlotsRequest(
    Guid AssetAnimationJobId,
    Guid? CandidateId = null);

public sealed record AssetProfileView(
    Guid Id,
    Guid CanonicalAssetId,
    Guid? StyleAssetId,
    string Label,
    string AssetType,
    string StructureType,
    string ChromaColor,
    IReadOnlyList<string> Palette,
    IReadOnlyList<string> RequiredFeatures,
    IReadOnlyList<string> ForbiddenChanges,
    bool Frozen,
    DateTime CreatedAt);

public sealed record AssetAnimationJobView(
    Guid Id,
    Guid AssetProfileId,
    Guid? GuideAssetId,
    Guid? DiagnosticGuideAssetId,
    Guid? OutputSpriteSheetId,
    Guid? SelectedCandidateId,
    string Status,
    string AnimationKind,
    string Strategy,
    string PromptSummary,
    string RecommendedAction,
    int MaxGenerationRounds,
    int GenerationRoundsUsed,
    int MaxRepairAttemptsPerFrame,
    AnimationSpec AnimationSpec,
    LayoutSpec LayoutSpec,
    IReadOnlyList<AssetAnimationCandidateView> Candidates,
    IReadOnlyList<AssetAnimationFrameStatusView> FrameStatuses,
    string LatestError,
    DateTime UpdatedAt);

public sealed record AssetAnimationCandidateView(
    Guid Id,
    Guid? GenerationBatchId,
    Guid? OutputAssetId,
    int CandidateIndex,
    string State,
    string RawQaStatus);

public sealed record AssetAnimationFrameStatusView(
    int FrameNumber,
    int Index,
    string Status,
    string Reason,
    string RecommendedAction,
    Guid? SourceAssetId = null);

public sealed record AnimationSpec(
    string AnimationKind,
    string AssetType,
    string StructureType,
    string Facing,
    string RootMotion,
    int FrameCount,
    int Fps,
    int TargetCellWidth,
    int TargetCellHeight,
    bool Loop,
    IReadOnlyList<FrameSpec> Frames,
    string? GuideRenderer = null,
    string? GuideRenderStyle = null,
    string? MotionClipId = null,
    double? GuideCameraYawDegrees = null,
    string? MotionValidationProfile = null,
    string? GuideSourcePackage = null,
    string? GuideSourceLicense = null);

public sealed record FrameSpec(
    int Index,
    string PoseName,
    double Phase,
    IReadOnlyList<string> Contacts,
    int RootOffsetX,
    int RootOffsetY,
    int DurationMs,
    bool IsKeyframe,
    string GuideShape);

public sealed record LayoutSpec(
    int CanvasWidth,
    int CanvasHeight,
    int Rows,
    int Columns,
    int GuideCellWidth,
    int GuideCellHeight,
    int TargetCellWidth,
    int TargetCellHeight,
    string BackgroundColor,
    IReadOnlyList<SlotSpec> Slots);

public sealed record SlotSpec(
    int FrameIndex,
    SpriteSheetRect Rect,
    SpriteSheetPoint Root,
    int BaselineY,
    SpriteSheetRect SafeRect);

public sealed record FrameQaStatus(
    int FrameIndex,
    string Status,
    IReadOnlyList<SpriteFailure> Failures,
    SpriteRepairAction RecommendedAction);

public sealed record FrameProvenance(
    Guid AssetAnimationJobId,
    Guid? AssetAnimationCandidateId,
    Guid? SourceAssetId,
    SpriteSheetRect SourceRect,
    double AppliedScale,
    int TranslationX,
    int TranslationY,
    string QaStatus,
    IReadOnlyList<string> RepairHistory);

public enum SpriteFailure
{
    None,
    MissingFrame,
    SlotCrossing,
    Clipped,
    GuideLeakage,
    DirtyBackground,
    WrongFacing,
    DuplicateFrame,
    RootDrift,
    ScaleDrift,
    IdentityDrift,
    DetachedArtifact,
    LowMotion,
}

public enum SpriteRepairAction
{
    None,
    ReextractFixedSlots,
    RegenerateFrame,
    RegenerateAdjacentFrames,
    RegenerateStrip,
    SwitchStrategy,
    UserReview,
    AcceptWithWarnings,
}
