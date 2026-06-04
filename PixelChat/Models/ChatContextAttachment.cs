namespace PixelChat.Models;

public class ChatContextAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public ChatContextAttachmentType Type { get; set; }
    public Guid RefId { get; set; }
    public required string Label { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ChatContextAttachmentType
{
    Asset,
    Mask,
    Crop,
    PromptRecipe,
    GenerationBatch
}
