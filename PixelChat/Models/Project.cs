namespace PixelChat.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public WorkspaceMode ActiveWorkspaceMode { get; set; } = WorkspaceMode.Generate;
    public Guid? ActiveBatchId { get; set; }
    public Guid? ActiveSpriteSheetId { get; set; }
    public Guid? ActiveFrameSetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ArtAsset> Assets { get; set; } = [];
    public ICollection<GenerationBatch> GenerationBatches { get; set; } = [];
    public ICollection<PromptRecipe> PromptRecipes { get; set; } = [];
    public ICollection<PromptRecipeVersion> PromptRecipeVersions { get; set; } = [];
    public ICollection<AnimationRecipe> AnimationRecipes { get; set; } = [];
    public ICollection<AnimationRecipeVersion> AnimationRecipeVersions { get; set; } = [];
    public ICollection<ActivityRun> ActivityRuns { get; set; } = [];
    public ICollection<ActivityStep> ActivitySteps { get; set; } = [];
    public ICollection<ActivityArtifact> ActivityArtifacts { get; set; } = [];
    public ICollection<SpriteSheetDefinition> SpriteSheets { get; set; } = [];
    public ICollection<SpriteSheetFrameRecord> SpriteSheetFrameRecords { get; set; } = [];
    public ICollection<SpriteRegion> SpriteRegions { get; set; } = [];
    public ICollection<StandaloneAsset> StandaloneAssets { get; set; } = [];
    public ICollection<FrameSet> FrameSets { get; set; } = [];
    public ICollection<Frame> Frames { get; set; } = [];
    public ICollection<Anchor> Anchors { get; set; } = [];
    public ICollection<SheetLayout> SheetLayouts { get; set; } = [];
    public ICollection<BuiltSheet> BuiltSheets { get; set; } = [];
    public ICollection<HistoryTask> HistoryTasks { get; set; } = [];
    public ICollection<ImageMask> Masks { get; set; } = [];
    public ICollection<ChatContextAttachment> ChatContextAttachments { get; set; } = [];
    public ICollection<CompareReviewSet> CompareReviewSets { get; set; } = [];
    public ICollection<AssistantConversation> AssistantConversations { get; set; } = [];
}

public enum WorkspaceMode
{
    Generate,
    Runs,
    Compare,
    Edit,
    Sprites,
    Recipes,
    Assets
}
