namespace PixelChat.Models;

/// <summary>Immutable revision, independent of chat retention. Undo appends another revision.</summary>
public sealed class SpriteRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FrameSetId { get; set; }
    public long Number { get; set; }
    public required string DocumentJson { get; set; }
    public required string Label { get; set; }
    public string Source { get; set; } = "user";
    public string? TaskId { get; set; }
    public string OperationsJson { get; set; } = "[]";
    public string? Script { get; set; }
    public long? UndoTarget { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
