namespace PixelChat.Chat;

public abstract record AssistantTurnUpdate;

public sealed record AssistantUserMessagePersisted(Guid MessageId) : AssistantTurnUpdate;

public sealed record AssistantTextDelta(string Text) : AssistantTurnUpdate;

public sealed record AssistantToolCallStarted(string CallId, string ToolName, string ArgumentsJson, bool ArgumentsComplete) : AssistantTurnUpdate;

public sealed record AssistantToolCallArgumentsDelta(string CallId, string ArgumentsDelta, bool ArgumentsComplete) : AssistantTurnUpdate;

public sealed record AssistantToolCallPendingConfirmation(Guid AssistantMessageId, string CallId, string ToolName, string ArgumentsJson) : AssistantTurnUpdate;

public sealed record AssistantToolCallCompleted(string CallId, string ToolName, string? Result, string? Error, double DurationMs) : AssistantTurnUpdate;

public sealed record AssistantFormDraftProposed(AssistantFormDraft Draft) : AssistantTurnUpdate;

public sealed record AssistantMessageCompleted(Guid MessageId) : AssistantTurnUpdate;

public sealed record AssistantWorkspaceMutated : AssistantTurnUpdate;

public sealed record AssistantTurnError(string Message, bool Cancelled) : AssistantTurnUpdate;
