namespace PixelChat.Models;

/// <summary>
/// An ordered set of animation frames derived from regions. Replaces the legacy
/// <c>SpriteSheetDefinition</c> as the structural owner of frames; sheet geometry now
/// lives separately in <see cref="SheetLayout"/>.
/// </summary>
public class FrameSet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Originating source image, when the frame set came from a single sheet.</summary>
    public Guid? SourceAssetId { get; set; }
    public ArtAsset? SourceAsset { get; set; }

    public required string Name { get; set; }

    public long Revision { get; set; }
    public string DocumentJson { get; set; } = "";
    public string UndoStackJson { get; set; } = "[0]";
    public string RedoStackJson { get; set; } = "[]";

    /// <summary>Explicit playback/sheet ordering of frame ids (JSON array of GUIDs).</summary>
    public string OrderedFrameIdsJson { get; set; } = "[]";

    public int DefaultCellWidth { get; set; }
    public int DefaultCellHeight { get; set; }

    public ICollection<Frame> Frames { get; set; } = [];
    public ICollection<SheetLayout> SheetLayouts { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
