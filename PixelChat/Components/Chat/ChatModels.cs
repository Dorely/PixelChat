using System.Text;

namespace PixelChat.Components.Chat;

public enum ChatMessageRole
{
    User,
    Assistant,
    Tool
}

public enum ChatMessageStatus
{
    Pending,
    Completed,
    Failed,
    Cancelled
}

public sealed record ChatRenderableMessage(
    Guid Id,
    ChatMessageRole Role,
    ChatMessageStatus Status,
    string Content,
    IReadOnlyList<ChatToolCallView>? ToolCalls = null,
    string? ToolName = null,
    string? ToolCallId = null);

public sealed record ChatToolCallView(
    Guid AssistantMessageId,
    string CallId,
    string Name,
    string ArgumentsJson,
    ChatToolCallStatus Status,
    string? Result,
    string? Error);

public enum ChatToolCallStatus
{
    Pending,
    Completed,
    Rejected,
    Failed
}

public sealed record ChatToolCallAction(
    Guid AssistantMessageId,
    string CallId);

public sealed record ChatLiveToolCall(
    string CallId,
    string Name,
    string ArgumentsJson,
    bool ArgumentsComplete,
    bool Complete,
    string? Error);

public sealed class ChatLiveTurn
{
    private readonly StringBuilder _text = new();
    private readonly List<ChatLiveToolCall> _toolCalls = [];

    public bool HasContent => _text.Length > 0;
    public bool IsThinking { get; private set; } = true;
    public string Text => _text.ToString();
    public IReadOnlyList<ChatLiveToolCall> ToolCalls => _toolCalls;

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        IsThinking = false;
        _text.Append(text);
    }

    public void StartToolCall(string callId, string name, string argumentsJson, bool argumentsComplete)
    {
        IsThinking = false;
        var index = _toolCalls.FindIndex(call => call.CallId == callId);
        var call = new ChatLiveToolCall(callId, name, argumentsJson, argumentsComplete, Complete: false, Error: null);
        if (index < 0)
            _toolCalls.Add(call);
        else
            _toolCalls[index] = call;
    }

    public void AppendToolCallArguments(string callId, string delta, bool argumentsComplete)
    {
        var index = _toolCalls.FindIndex(call => call.CallId == callId);
        if (index < 0)
        {
            _toolCalls.Add(new ChatLiveToolCall(callId, "tool", delta, argumentsComplete, Complete: false, Error: null));
            return;
        }

        var current = _toolCalls[index];
        _toolCalls[index] = current with
        {
            ArgumentsJson = current.ArgumentsJson + delta,
            ArgumentsComplete = argumentsComplete,
        };
    }

    public void CompleteToolCall(string callId, string? error)
    {
        var index = _toolCalls.FindIndex(call => call.CallId == callId);
        if (index < 0)
            return;

        _toolCalls[index] = _toolCalls[index] with
        {
            Complete = true,
            Error = error,
        };
    }

    public ChatLiveTurn Clone()
    {
        var clone = new ChatLiveTurn
        {
            IsThinking = IsThinking
        };
        clone._text.Append(_text);
        clone._toolCalls.AddRange(_toolCalls);
        return clone;
    }
}
