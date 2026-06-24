namespace PixelChat.Models;

public class ActivityStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid ActivityRunId { get; set; }
    public ActivityRun ActivityRun { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "completed";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
