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
    Recipe,
    SpriteSheet
}

public sealed record AssistantSpriteRect(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record AssistantSpriteSheetFrameDraft(
    int Index,
    AssistantSpriteRect SourceRect,
    int OffsetX = 0,
    int OffsetY = 0);

public sealed record AssistantFormDraft(
    AssistantFormDraftTarget Target,
    string? Prompt = null,
    string? NegativePrompt = null,
    string? Size = null,
    string? Background = null,
    int? Count = null,
    Guid? PromptRecipeId = null,
    IReadOnlyList<Guid>? ReferenceAssetIds = null,
    string? RecipeName = null,
    string? AssetType = null,
    string? PromptTemplate = null,
    IReadOnlyList<string>? StyleRules = null,
    IReadOnlyList<string>? AvoidRules = null,
    string? Notes = null,
    string? PreferredSize = null,
    Guid? SourceAssetId = null,
    int? Rows = null,
    int? Columns = null,
    int? CellWidth = null,
    int? CellHeight = null,
    int? Padding = null,
    int? Gutter = null,
    int? Fps = null,
    bool? Loop = null,
    string? Anchor = null,
    IReadOnlyList<AssistantSpriteSheetFrameDraft>? SpriteFrames = null);
