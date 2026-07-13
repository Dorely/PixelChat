namespace PixelChat.Models;

public class AssetReviewDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AssetId { get; set; }
    public ArtAsset Asset { get; set; } = null!;

    public Guid? SourceBatchId { get; set; }
    public GenerationBatch? SourceBatch { get; set; }

    public AssetReviewDecisionKind Decision { get; set; }
    public AssetReviewActor Actor { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AssetReviewDecisionKind
{
    Clear,
    Keep,
    Reject
}

public enum AssetReviewActor
{
    User,
    Assistant
}
