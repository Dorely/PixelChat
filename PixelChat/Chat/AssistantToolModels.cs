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
