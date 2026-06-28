namespace PixelChat.Models;

public class AnimationRecipe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int CurrentVersion { get; set; } = 1;
    public ICollection<AnimationRecipeVersion> Versions { get; set; } = [];
    public ICollection<RecipeAssetAttachment> Attachments { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
