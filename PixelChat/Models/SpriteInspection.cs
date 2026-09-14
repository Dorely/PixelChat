namespace PixelChat.Models;

public sealed class SpriteInspection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameSetId { get; set; }
    public long Revision { get; set; }
    public required string CacheKey { get; set; }
    public required string Label { get; set; }
    public required string BitmapHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
