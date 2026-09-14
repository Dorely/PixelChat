namespace PixelChat.Models;

public sealed class SpriteExport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameSetId { get; set; }
    public long Revision { get; set; }
    public string SpecificationJson { get; set; } = "{}";
    public string FileName { get; set; } = "sprite.zip";
    public string ContentType { get; set; } = "application/zip";
    public byte[] Data { get; set; } = [];
    public string ManifestJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
