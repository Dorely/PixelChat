namespace PixelChat.Models;

public class PromptRecipe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public string StyleRulesJson { get; set; } = "[]";
    public string AvoidRulesJson { get; set; } = "[]";
    public string ExampleAssetIdsJson { get; set; } = "[]";
    public string PreferredProvider { get; set; } = string.Empty;
    public string PreferredModel { get; set; } = string.Empty;
    public string PreferredSize { get; set; } = string.Empty;
    public string ExportDefaultsJson { get; set; } = "{}";
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
