namespace PixelChat.Models;

public class SpriteSheetFrameRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid SpriteSheetDefinitionId { get; set; }
    public SpriteSheetDefinition SpriteSheetDefinition { get; set; } = null!;

    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;

    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public string ShapeJson { get; set; } = "[]";

    public Guid? SourceImageAssetId { get; set; }
    public ArtAsset? SourceImageAsset { get; set; }
    public int SourceImageX { get; set; }
    public int SourceImageY { get; set; }
    public int SourceImageWidth { get; set; }
    public int SourceImageHeight { get; set; }

    public int CellX { get; set; }
    public int CellY { get; set; }
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }

    public int SpriteX { get; set; }
    public int SpriteY { get; set; }
    public int SpriteWidth { get; set; }
    public int SpriteHeight { get; set; }

    public string PreviewContentType { get; set; } = "image/png";
    public byte[] PreviewData { get; set; } = [];
    public int PreviewWidth { get; set; }
    public int PreviewHeight { get; set; }

    public string WorkingState { get; set; } = "none";
    public string WorkingContentType { get; set; } = "image/png";
    public byte[] WorkingData { get; set; } = [];
    public int WorkingWidth { get; set; }
    public int WorkingHeight { get; set; }
    public int WorkingMargin { get; set; }
    public DateTime? WorkingUpdatedAt { get; set; }

    public string PoseName { get; set; } = string.Empty;
    public double Phase { get; set; }
    public int RootOffsetX { get; set; }
    public int RootOffsetY { get; set; }
    public int DurationMs { get; set; }
    public string FootContactsJson { get; set; } = "[]";
    public bool IsKeyframe { get; set; }
    public int PivotX { get; set; }
    public int PivotY { get; set; }
    public Guid? SourceAnimationJobId { get; set; }
    public AssetAnimationJob? SourceAnimationJob { get; set; }
    public Guid? SourceAnimationCandidateId { get; set; }
    public AssetAnimationCandidate? SourceAnimationCandidate { get; set; }
    public double AppliedScale { get; set; } = 1d;
    public int TranslationX { get; set; }
    public int TranslationY { get; set; }
    public string QaStatus { get; set; } = string.Empty;
    public string RepairHistoryJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
