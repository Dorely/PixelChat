namespace PixelChat.Models;

public class BackgroundRemovalExportCache
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid AssetId { get; set; }
    public ArtAsset Asset { get; set; } = null!;

    public required string SourceImageSha256 { get; set; }
    public string RemovalMethod { get; set; } = "local-ai";
    public required string ModelName { get; set; }
    public required string RembgPackageVersion { get; set; }
    public bool AlphaMatting { get; set; }
    public string OptionsHash { get; set; } = string.Empty;

    public string ContentType { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
    public int TransparentPixels { get; set; }
    public int SemiTransparentPixels { get; set; }
    public int OpaquePixels { get; set; }
    public required string ActualBackend { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
