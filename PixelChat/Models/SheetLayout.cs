namespace PixelChat.Models;

/// <summary>
/// Deterministic sheet geometry for a frame set: grid, cell size, spacing, ordering,
/// and playback/background defaults used when building an output sprite sheet.
/// </summary>
public class SheetLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid FrameSetId { get; set; }
    public FrameSet FrameSet { get; set; } = null!;

    public int Rows { get; set; } = 1;
    public int Columns { get; set; } = 1;
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int Padding { get; set; }
    public int Gutter { get; set; }
    public int OuterMargin { get; set; }

    /// <summary>rowMajor | columnMajor</summary>
    public string Ordering { get; set; } = "rowMajor";

    public int Fps { get; set; } = 8;
    public bool Loop { get; set; } = true;
    public string HorizontalAnchor { get; set; } = "center";
    public string VerticalAnchor { get; set; } = "bottom";

    public string? BackgroundMode { get; set; }
    public string? BackgroundColor { get; set; }

    public ICollection<BuiltSheet> BuiltSheets { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
