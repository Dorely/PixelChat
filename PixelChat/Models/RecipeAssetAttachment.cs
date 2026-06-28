namespace PixelChat.Models;

public class RecipeAssetAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? PromptRecipeId { get; set; }
    public PromptRecipe? PromptRecipe { get; set; }

    public Guid? AnimationRecipeId { get; set; }
    public AnimationRecipe? AnimationRecipe { get; set; }

    public Guid AssetId { get; set; }
    public ArtAsset Asset { get; set; } = null!;

    public string Role { get; set; } = RecipeAssetAttachmentRoles.Example;
    public int SortOrder { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class RecipeAssetAttachmentRoles
{
    public const string Example = "example";
    public const string Guide = "guide";
}
