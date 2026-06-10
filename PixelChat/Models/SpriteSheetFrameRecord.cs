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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
