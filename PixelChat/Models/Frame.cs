namespace PixelChat.Models;

/// <summary>Relational identity and geometry projected from a native sprite document.</summary>
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

    // Original import placement, retained as provenance; native cels already contain this offset.
    public int ContentOffsetX { get; set; }
    public int ContentOffsetY { get; set; }

    public int DurationMs { get; set; }
    public bool HideFromOnionSkin { get; set; }

    /// <summary>Optional polygon outline of the source region (JSON array of points).</summary>
    public string ShapeJson { get; set; } = "[]";

    public bool IsDeleted { get; set; }

    public ICollection<Anchor> Anchors { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
