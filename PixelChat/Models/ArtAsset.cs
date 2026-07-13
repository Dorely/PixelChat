namespace PixelChat.Models;

public class ArtAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Label { get; set; }
    public string FileName { get; set; } = string.Empty;
    public ArtAssetKind Kind { get; set; }
    public required string ContentType { get; set; }
    public byte[] Data { get; set; } = [];
    public byte[]? ThumbnailData { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public Guid? ParentAssetId { get; set; }
    public ArtAsset? ParentAsset { get; set; }
    public ICollection<ArtAsset> ChildAssets { get; set; } = [];

    public Guid? SourceBatchId { get; set; }
    public GenerationBatch? SourceBatch { get; set; }

    public Guid? SourcePromptRecipeId { get; set; }
    public PromptRecipe? SourcePromptRecipe { get; set; }
    public int? SourcePromptRecipeVersion { get; set; }
    public Guid? SourceAnimationRecipeId { get; set; }
    public AnimationRecipe? SourceAnimationRecipe { get; set; }
    public int? SourceAnimationRecipeVersion { get; set; }

    public bool IsFavorite { get; set; }
    public AssetReviewStatus ReviewStatus { get; set; } = AssetReviewStatus.Kept;
    public string Notes { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string SourceMetadataJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AssetReviewDecision> ReviewDecisions { get; set; } = [];
}

public enum AssetReviewStatus
{
    Kept,
    Pending,
    Rejected
}

public enum ArtAssetKind
{
    Generated,
    Imported,
    Edited,
    Cropped,
    SpriteSheet,
    SpriteGuide,
    Extracted
}
