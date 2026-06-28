using System.Text.Json.Serialization;

namespace PixelChat.Chat;

public sealed record PersistedToolCall(
    string CallId,
    string Name,
    string ArgumentsJson,
    int? TextOffset = null,
    string? ExplicitDisplayTitle = null);

[JsonConverter(typeof(JsonStringEnumConverter<AssistantFormDraftTarget>))]
public enum AssistantFormDraftTarget
{
    Generate,
    Edit,
    Recipe
}

public sealed record AssistantFormDraft(
    AssistantFormDraftTarget Target,
    string? Prompt = null,
    string? NegativePrompt = null,
    string? Size = null,
    string? Background = null,
    int? Count = null,
    Guid? PromptRecipeId = null,
    Guid? AnimationRecipeId = null,
    IReadOnlyList<Guid>? ReferenceAssetIds = null,
    string? RecipeName = null,
    string? Notes = null);

public sealed record CompareReviewToolItem(
    string Kind,
    Guid RefId,
    string? Label = null,
    string? Notes = null);

public sealed record RecipeAttachmentToolItem(
    Guid AssetId,
    string Role = "example",
    string? Notes = null);

public sealed record AnimationFrameMark(
    int FrameNumber,
    string Status,
    string? Reason = null,
    bool ForceAccept = false);

public sealed class AssistantTurnGenerationBudget(int maxRounds)
{
    public int MaxRounds { get; } = Math.Max(0, maxRounds);
    public int RoundsUsed { get; private set; }
    public bool IsExhausted => RoundsUsed >= MaxRounds;
    public int Consume() => ++RoundsUsed;
}
