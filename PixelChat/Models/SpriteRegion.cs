namespace PixelChat.Models;

/// <summary>
/// A user- or agent-defined region on a source image. Regions stay linked to the
/// source pixels so their bounds can be re-adjusted without losing source data, and
/// can be extracted as standalone assets or turned into animation frames.
/// </summary>
public class SpriteRegion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid SourceAssetId { get; set; }
    public ArtAsset SourceAsset { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    // Source-image coordinate space.
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Optional polygon outline (JSON array of points); empty for a rectangle.</summary>
    public string ShapeJson { get; set; } = "[]";

    /// <summary>frame | asset | tile | prop | ui | vfx</summary>
    public string RegionType { get; set; } = "frame";

    public int Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
