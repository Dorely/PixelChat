namespace PixelChat.Models;

public sealed class SpriteAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameSetId { get; set; }
    public long Revision { get; set; }
    public string Kind { get; set; } = "measurements";
    public string ResultJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
