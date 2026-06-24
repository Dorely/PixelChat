namespace PixelChat.Models;

public class ActivityArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid ActivityRunId { get; set; }
    public ActivityRun ActivityRun { get; set; } = null!;

    public string Kind { get; set; } = string.Empty;
    public Guid RefId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
