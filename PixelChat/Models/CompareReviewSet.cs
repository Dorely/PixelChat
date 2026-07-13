namespace PixelChat.Models;

public class CompareReviewSet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Title { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompareReviewSetItem> Items { get; set; } = [];
}

public class CompareReviewSetItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompareReviewSetId { get; set; }
    public CompareReviewSet CompareReviewSet { get; set; } = null!;

    public CompareReviewItemKind Kind { get; set; }
    public Guid RefId { get; set; }
    public required string Label { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum CompareReviewItemKind
{
    Asset = 0,
    Frame = 7,
    Animation = 8,
}
