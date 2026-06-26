namespace PixelChat.Models;

/// <summary>
/// A single animation frame. Coordinate spaces are kept explicit to avoid the legacy
/// ambiguity between source crops, logical cells, and content offsets:
/// <list type="bullet">
/// <item><c>Source*</c> — source-image space (which pixels are extracted).</item>
/// <item><c>Logical*</c> — the equal logical cell size for the frame.</item>
/// <item><c>ContentOffset*</c> — content position within its logical cell.</item>
/// </list>
/// Replaces the legacy <c>SpriteSheetFrameRecord</c>.
/// </summary>
public class Frame
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid FrameSetId { get; set; }
    public FrameSet FrameSet { get; set; } = null!;

    public Guid? SourceRegionId { get; set; }
    public SpriteRegion? SourceRegion { get; set; }

    public string Name { get; set; } = string.Empty;
    public int Index { get; set; }

    // Source-image coordinate space.
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }

    // Logical-frame (cell) coordinate space.
    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }

    // Frame-content coordinate space (offset of content within the cell).
    public int ContentOffsetX { get; set; }
    public int ContentOffsetY { get; set; }

    public int DurationMs { get; set; }
    public bool HideFromOnionSkin { get; set; }

    /// <summary>Optional polygon outline of the source region (JSON array of points).</summary>
    public string ShapeJson { get; set; } = "[]";

    // Working bitmap (isolated/edited/aligned image for this frame).
    public string WorkingState { get; set; } = "none";
    public string WorkingContentType { get; set; } = "image/png";
    public byte[] WorkingData { get; set; } = [];
    public int WorkingWidth { get; set; }
    public int WorkingHeight { get; set; }
    public int WorkingMargin { get; set; }
    public DateTime? WorkingUpdatedAt { get; set; }

    // Cached preview rendered from the source region.
    public string PreviewContentType { get; set; } = "image/png";
    public byte[] PreviewData { get; set; } = [];
    public int PreviewWidth { get; set; }
    public int PreviewHeight { get; set; }

    /// <summary>Pointer into the (future) bitmap-revision store; null until history lands.</summary>
    public Guid? BitmapRevisionAssetId { get; set; }
    public ArtAsset? BitmapRevisionAsset { get; set; }

    public ICollection<Anchor> Anchors { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
