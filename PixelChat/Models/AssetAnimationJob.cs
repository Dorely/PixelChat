namespace PixelChat.Models;

public class AssetAnimationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AssetProfileId { get; set; }
    public AssetProfile AssetProfile { get; set; } = null!;

    public Guid? GuideAssetId { get; set; }
    public ArtAsset? GuideAsset { get; set; }

    public Guid? DiagnosticGuideAssetId { get; set; }
    public ArtAsset? DiagnosticGuideAsset { get; set; }

    public Guid? OutputSpriteSheetId { get; set; }
    public SpriteSheetDefinition? OutputSpriteSheet { get; set; }

    public Guid? SelectedCandidateId { get; set; }
    public AssetAnimationCandidate? SelectedCandidate { get; set; }

    public string Status { get; set; } = "planned";
    public string AnimationKind { get; set; } = string.Empty;
    public string Strategy { get; set; } = "hybrid";
    public string PromptSummary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = "render_guide";
    public int MaxGenerationRounds { get; set; }
    public int GenerationRoundsUsed { get; set; }
    public int MaxRepairAttemptsPerFrame { get; set; }

    public string AnimationSpecJson { get; set; } = "{}";
    public string LayoutSpecJson { get; set; } = "{}";
    public string RawQaSummaryJson { get; set; } = "{}";
    public string FrameQaSummaryJson { get; set; } = "{}";
    public string MotionQaSummaryJson { get; set; } = "{}";
    public string FrameStatusesJson { get; set; } = "[]";

    public ICollection<AssetAnimationCandidate> Candidates { get; set; } = [];
    public ICollection<AssetAnimationFrameAttempt> FrameAttempts { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AssetAnimationCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AssetAnimationJobId { get; set; }
    public AssetAnimationJob AssetAnimationJob { get; set; } = null!;

    public Guid? GenerationBatchId { get; set; }
    public GenerationBatch? GenerationBatch { get; set; }

    public Guid? OutputAssetId { get; set; }
    public ArtAsset? OutputAsset { get; set; }

    public int CandidateIndex { get; set; }
    public string State { get; set; } = "generated";
    public string RawQaStatus { get; set; } = "pending";
    public string RawQaSummaryJson { get; set; } = "{}";

    public ICollection<AssetAnimationFrameAttempt> FrameAttempts { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AssetAnimationFrameAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AssetAnimationJobId { get; set; }
    public AssetAnimationJob AssetAnimationJob { get; set; } = null!;

    public Guid? AssetAnimationCandidateId { get; set; }
    public AssetAnimationCandidate? AssetAnimationCandidate { get; set; }

    public int FrameIndex { get; set; }
    public int AttemptNumber { get; set; }
    public string AttemptKind { get; set; } = "mark";
    public string Status { get; set; } = "pending";
    public string FailureReason { get; set; } = string.Empty;
    public string RepairHistoryJson { get; set; } = "[]";

    public Guid? SourceAssetId { get; set; }
    public ArtAsset? SourceAsset { get; set; }

    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
