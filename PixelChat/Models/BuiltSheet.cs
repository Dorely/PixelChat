namespace PixelChat.Models;

/// <summary>
/// A reassembled, opaque sprite sheet rendered from a <see cref="SheetLayout"/>. Retains
/// a per-frame placement manifest and links to the frames used so the sheet is a
/// rebuildable project asset rather than a dead-end bitmap. The pixels live in
/// <see cref="OutputAsset"/>.
/// </summary>
public class BuiltSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid SheetLayoutId { get; set; }
    public SheetLayout SheetLayout { get; set; } = null!;

    public Guid? OutputAssetId { get; set; }
    public ArtAsset? OutputAsset { get; set; }

    /// <summary>Per-frame placement manifest (rows, columns, cell size, content offsets).</summary>
    public string ManifestJson { get; set; } = "{}";

    /// <summary>Ids of the frames used to build this sheet (JSON array of GUIDs).</summary>
    public string LinkedFrameIdsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
