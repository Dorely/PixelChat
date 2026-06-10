namespace PixelChat.Models;

public class SpriteSheetDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid SourceAssetId { get; set; }
    public ArtAsset SourceAsset { get; set; } = null!;

    public Guid? OutputAssetId { get; set; }
    public ArtAsset? OutputAsset { get; set; }

    public required string Label { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int Padding { get; set; }
    public int Gutter { get; set; }
    public int Fps { get; set; }
    public bool Loop { get; set; } = true;
    public string FramesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
