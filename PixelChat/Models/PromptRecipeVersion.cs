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
    public string AssetType { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string StyleRulesJson { get; set; } = "[]";
    public string AvoidRulesJson { get; set; } = "[]";
    public Guid? ExampleAssetId { get; set; }
    public string PreferredProvider { get; set; } = string.Empty;
    public string PreferredModel { get; set; } = string.Empty;
    public string PreferredSize { get; set; } = string.Empty;
    public string ExportDefaultsJson { get; set; } = "{}";
    public string Notes { get; set; } = string.Empty;
    public string Source { get; set; } = "user";
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
