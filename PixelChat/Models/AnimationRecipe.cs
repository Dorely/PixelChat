namespace PixelChat.Models;

public class AnimationRecipe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string AnimationKind { get; set; } = string.Empty;
    public string Facing { get; set; } = string.Empty;
    public int FrameCount { get; set; }
    public string FrameOrderJson { get; set; } = "[]";
    public int Fps { get; set; } = 8;
    public bool Loop { get; set; } = true;
    public Guid? GuideAssetId { get; set; }
    public ArtAsset? GuideAsset { get; set; }
    public string ExpectedFrameBoxesJson { get; set; } = "[]";
    public string AnchorStrategy { get; set; } = "recipe-defined";
    public string PromptScaffold { get; set; } = string.Empty;
    public string ExportDefaultsJson { get; set; } = "{}";
    public string Notes { get; set; } = string.Empty;
    public Guid? PrimaryExampleSpriteSheetId { get; set; }
    public SpriteSheetDefinition? PrimaryExampleSpriteSheet { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public ICollection<AnimationRecipeVersion> Versions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
