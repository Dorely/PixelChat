namespace PixelChat.Models;

/// <summary>
/// A named alignment point on a frame (feet, root, center, custom, …) used to align
/// frames in a frame set. Stored in frame-content coordinate space.
/// </summary>
public class Anchor
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid FrameId { get; set; }
    public Frame Frame { get; set; } = null!;

    /// <summary>feet | root | center | top | bottom | left | right | custom</summary>
    public string Name { get; set; } = "custom";

    public int X { get; set; }
    public int Y { get; set; }

    public double Confidence { get; set; } = 1d;

    /// <summary>detected | manual</summary>
    public string Source { get; set; } = "manual";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
