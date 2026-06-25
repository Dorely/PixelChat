namespace PixelChat.Models;

public class ImageMask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AssetId { get; set; }
    public ArtAsset Asset { get; set; } = null!;

    public string Label { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>asset | frame — what this mask is attached to.</summary>
    public string OwnerKind { get; set; } = "asset";

    /// <summary>Id of the owning asset or frame (mirrors <see cref="AssetId"/> for asset masks).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Coordinate space the mask pixels are expressed in (source | frameContent | sheet).</summary>
    public string CoordinateSpace { get; set; } = "source";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
