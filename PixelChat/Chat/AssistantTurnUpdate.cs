using PixelChat.Tokens;

namespace PixelChat.Chat;

public abstract record AssistantTurnUpdate;

public sealed record AssistantMessageVisualUpdate(
    Guid Id,
    string? ToolCallId,
    string Title,
    string Caption,
    string PreviewImageUrl,
    string FullImageUrl,
    int? Width,
    int? Height);

public sealed record AssistantUserMessagePersisted(
    Guid MessageId,
    IReadOnlyList<AssistantMessageVisualUpdate> Visuals) : AssistantTurnUpdate;

public sealed record AssistantTextDelta(string Text) : AssistantTurnUpdate;

public sealed record AssistantToolCallStarted(
    string CallId,
    string ToolName,
    string ArgumentsJson,
    bool ArgumentsComplete,
    string? DisplayTitle) : AssistantTurnUpdate;

public sealed record AssistantToolCallArgumentsDelta(string CallId, string ArgumentsDelta, bool ArgumentsComplete) : AssistantTurnUpdate;

public sealed record AssistantToolCallCompleted(
    string CallId,
    string ToolName,
    string? Result,
    string? Error,
    double DurationMs,
    IReadOnlyList<AssistantMessageVisualUpdate> Visuals) : AssistantTurnUpdate;

public sealed record AssistantFormDraftProposed(AssistantFormDraft Draft) : AssistantTurnUpdate;

public sealed record AssistantMessagePersisted(Guid MessageId) : AssistantTurnUpdate;

public sealed record AssistantWorkspaceMutated : AssistantTurnUpdate;

public sealed record AssistantTokenCountUpdated(TokenContextEstimate Estimate) : AssistantTurnUpdate;

public sealed record AssistantTurnError(string Message, bool Cancelled) : AssistantTurnUpdate;
