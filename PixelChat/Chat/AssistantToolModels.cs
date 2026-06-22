using System.Text.Json.Serialization;

namespace PixelChat.Chat;

public sealed record PersistedToolCall(
    string CallId,
    string Name,
    string ArgumentsJson,
    int? TextOffset = null);

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
    IReadOnlyList<Guid>? ReferenceAssetIds = null,
    Guid? RecipeExampleAssetId = null,
    string? RecipeName = null,
    string? AssetType = null,
    string? PromptTemplate = null,
    IReadOnlyList<string>? StyleRules = null,
    IReadOnlyList<string>? AvoidRules = null,
    string? Notes = null,
    string? PreferredSize = null);

public sealed record CompareReviewToolItem(
    string Kind,
    Guid RefId,
    string? Label = null,
    string? Notes = null);

public sealed class AssistantTurnGenerationBudget(int maxRounds)
{
    public int MaxRounds { get; } = Math.Max(0, maxRounds);
    public int RoundsUsed { get; private set; }
    public bool IsExhausted => RoundsUsed >= MaxRounds;
    public int Consume() => ++RoundsUsed;
}
