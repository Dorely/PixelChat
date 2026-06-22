namespace PixelChat.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public WorkspaceMode ActiveWorkspaceMode { get; set; } = WorkspaceMode.Generate;
    public Guid? ActiveBatchId { get; set; }
    public Guid? ActiveSpriteSheetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ArtAsset> Assets { get; set; } = [];
    public ICollection<GenerationBatch> GenerationBatches { get; set; } = [];
    public ICollection<PromptRecipe> PromptRecipes { get; set; } = [];
    public ICollection<PromptRecipeVersion> PromptRecipeVersions { get; set; } = [];
    public ICollection<SpriteSheetDefinition> SpriteSheets { get; set; } = [];
    public ICollection<SpriteSheetFrameRecord> SpriteSheetFrameRecords { get; set; } = [];
    public ICollection<ImageMask> Masks { get; set; } = [];
    public ICollection<ChatContextAttachment> ChatContextAttachments { get; set; } = [];
    public ICollection<CompareReviewSet> CompareReviewSets { get; set; } = [];
    public ICollection<AssistantConversation> AssistantConversations { get; set; } = [];
}

public enum WorkspaceMode
{
    Generate,
    Compare,
    Edit,
    Sprites,
    Recipes,
    Assets
}
