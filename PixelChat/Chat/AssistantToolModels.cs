using System.Text.Json.Serialization;

namespace PixelChat.Chat;

public sealed record PersistedToolCall(
    string CallId,
    string Name,
    string ArgumentsJson,
    int? TextOffset,
    PersistedToolCallStatus Status,
    string? Result = null,
    string? Error = null);

[JsonConverter(typeof(JsonStringEnumConverter<PersistedToolCallStatus>))]
public enum PersistedToolCallStatus
{
    Pending,
    Completed,
    Rejected,
    Failed
}

public sealed record AssistantToolExecutionResult(
    string CallId,
    string ToolName,
    PersistedToolCallStatus Status,
    string? Result,
    string? Error);

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
    int? Count = null,
    Guid? PromptRecipeId = null,
    IReadOnlyList<Guid>? ReferenceAssetIds = null,
    string? RecipeName = null,
    string? AssetType = null,
    string? PromptTemplate = null,
    IReadOnlyList<string>? StyleRules = null,
    IReadOnlyList<string>? AvoidRules = null,
    string? Notes = null,
    string? PreferredSize = null);
