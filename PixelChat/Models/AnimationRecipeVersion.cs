namespace PixelChat.Models;

public class AnimationRecipeVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid AnimationRecipeId { get; set; }
    public AnimationRecipe AnimationRecipe { get; set; } = null!;

    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AnimationKind { get; set; } = string.Empty;
    public string Facing { get; set; } = string.Empty;
    public int FrameCount { get; set; }
    public string FrameOrderJson { get; set; } = "[]";
    public int Fps { get; set; } = 8;
    public bool Loop { get; set; } = true;
    public Guid? GuideAssetId { get; set; }
    public string ExpectedFrameBoxesJson { get; set; } = "[]";
    public string AnchorStrategy { get; set; } = "recipe-defined";
    public string PromptScaffold { get; set; } = string.Empty;
    public string ExportDefaultsJson { get; set; } = "{}";
    public string Notes { get; set; } = string.Empty;
    public Guid? PrimaryExampleSpriteSheetId { get; set; }
    public string Source { get; set; } = "user";
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
