namespace PixelChat.Models;

/// <summary>
/// A region extracted into a standalone, reusable project asset (weapon, portrait,
/// prop, UI element, tile, VFX). The pixels live in <see cref="OutputAsset"/>
/// (an <see cref="ArtAsset"/> of kind <c>Extracted</c>); this record holds the
/// extraction metadata and the optional link back to the originating region.
/// </summary>
public class StandaloneAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? SourceRegionId { get; set; }
    public SpriteRegion? SourceRegion { get; set; }

    public Guid OutputAssetId { get; set; }
    public ArtAsset OutputAsset { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }
    public int ContentOffsetX { get; set; }
    public int ContentOffsetY { get; set; }

    public bool LinkedToSource { get; set; } = true;

    /// <summary>Pointer into the (future) bitmap-revision store; null until history lands.</summary>
    public Guid? BitmapRevisionAssetId { get; set; }
    public ArtAsset? BitmapRevisionAsset { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
