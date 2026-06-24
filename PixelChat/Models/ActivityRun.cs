namespace PixelChat.Models;

public class ActivityRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public string Actor { get; set; } = "system";
    public string WorkflowKind { get; set; } = string.Empty;
    public Guid? PrimaryArtifactId { get; set; }
    public string PrimaryArtifactKind { get; set; } = string.Empty;
    public ICollection<ActivityStep> Steps { get; set; } = [];
    public ICollection<ActivityArtifact> Artifacts { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
