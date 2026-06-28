namespace PixelChat.Models;

public class PromptRecipeVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid RecipeId { get; set; }
    public PromptRecipe Recipe { get; set; } = null!;

    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Source { get; set; } = "user";
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
