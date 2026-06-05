namespace PixelChat.Models;

public class GenerationBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Label { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string MainlineModel { get; set; } = string.Empty;
    public string ImageModel { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Background { get; set; } = "auto";
    public int Count { get; set; }
    public string InputAssetIdsJson { get; set; } = "[]";
    public string InputMaskIdsJson { get; set; } = "[]";
    public Guid? ParentBatchId { get; set; }
    public GenerationBatch? ParentBatch { get; set; }
    public ICollection<GenerationBatch> ChildBatches { get; set; } = [];
    public Guid? PromptRecipeId { get; set; }
    public PromptRecipe? PromptRecipe { get; set; }
    public GenerationBatchStatus Status { get; set; } = GenerationBatchStatus.Queued;
    public string Error { get; set; } = string.Empty;
    public string OutputErrorsJson { get; set; } = "[]";
    public string AgentSummary { get; set; } = string.Empty;
    public string RawProviderResponseJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ArtAsset> OutputAssets { get; set; } = [];
}

public enum GenerationBatchStatus
{
    Queued,
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed
}
