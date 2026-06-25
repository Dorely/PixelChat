namespace PixelChat.Models;

/// <summary>
/// Groups the operations of a single user/agent instruction into one undoable task.
/// Schema only for now — the reversible-command + bitmap-revision backend is deferred;
/// this table lets the UI history placeholders bind to an (initially empty) stream.
/// </summary>
public class HistoryTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>user | agent</summary>
    public string Source { get; set; } = "user";

    public Guid? CheckpointId { get; set; }

    /// <summary>Ordered child operations (JSON); populated when history is implemented.</summary>
    public string OperationsJson { get; set; } = "[]";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>running | completed | cancelled | failed</summary>
    public string Status { get; set; } = "running";
}
