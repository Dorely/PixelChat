using System.Text;
using System.Text.Json;

namespace PixelChat.Components.Chat;

public enum ChatMessageRole
{
    User,
    Assistant
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
    List<ChatMessagePart> Parts);

public abstract class ChatMessagePart;

public sealed class ChatTextPart : ChatMessagePart
{
    public ChatTextPart(string text) => Text.Append(text);

    public StringBuilder Text { get; } = new();

    public void Append(string text) => Text.Append(text);
}

public sealed class ChatToolPart(ChatToolChip chip) : ChatMessagePart
{
    public ChatToolChip Chip { get; } = chip;
}

public sealed class ChatImagePart(ChatImageVisual visual) : ChatMessagePart
{
    public ChatImageVisual Visual { get; } = visual;
}

public sealed record ChatImageVisual(
    Guid Id,
    string Title,
    string Caption,
    string PreviewImageUrl,
    string FullImageUrl,
    int? Width,
    int? Height,
    string? ToolCallId = null);

public sealed record ChatPendingImageDraft(
    Guid Id,
    string FileName,
    string ContentType,
    byte[] Data,
    string PreviewImageUrl,
    long Size,
    int? Width,
    int? Height);

public sealed class ChatToolChip
{
    private readonly StringBuilder _arguments = new();

    public ChatToolChip(string callId, string name, string argumentsJson, bool argumentsComplete = true, string? explicitDisplayTitle = null)
    {
        CallId = callId;
        Name = name;
        ExplicitDisplayTitle = explicitDisplayTitle;
        SetArguments(argumentsJson);
        ArgumentsComplete = argumentsComplete;
    }

    public string CallId { get; }
    public string Name { get; private set; }
    public string? ExplicitDisplayTitle { get; private set; }
    public string ArgumentsJson => _arguments.ToString();
    public string? Result { get; set; }
    public string? Error { get; set; }
    public double? DurationMs { get; set; }
    public bool Completed { get; set; }
    public bool ArgumentsComplete { get; private set; }
    public List<ChatImageVisual> Visuals { get; } = [];
    public bool HasArguments => !string.IsNullOrWhiteSpace(ArgumentsJson) && ArgumentsJson != "{}";

    public void Rename(string name) => Name = name;

    public void SetExplicitDisplayTitle(string? explicitDisplayTitle)
    {
        if (!string.IsNullOrWhiteSpace(explicitDisplayTitle))
            ExplicitDisplayTitle = explicitDisplayTitle.Trim();
    }

    public void SetArguments(string argumentsJson)
    {
        _arguments.Clear();
        if (!string.IsNullOrEmpty(argumentsJson))
            _arguments.Append(argumentsJson);
    }

    public void AppendArguments(string argumentsDelta, bool argumentsComplete)
    {
        if (!string.IsNullOrEmpty(argumentsDelta))
            _arguments.Append(argumentsDelta);
        ArgumentsComplete = argumentsComplete || ArgumentsComplete;
    }

    public void MarkArgumentsComplete(string? argumentsJson = null)
    {
        if (argumentsJson is not null)
            SetArguments(argumentsJson);
        ArgumentsComplete = true;
    }

    public ChatToolChip Clone()
    {
        var clone = new ChatToolChip(CallId, Name, ArgumentsJson, ArgumentsComplete, ExplicitDisplayTitle)
        {
            Result = Result,
            Error = Error,
            DurationMs = DurationMs,
            Completed = Completed
        };
        clone.Visuals.AddRange(Visuals);
        return clone;
    }
}

public sealed class ChatLiveTurn
{
    private bool _startNewMessageOnNextPart;

    public List<ChatLiveMessage> Messages { get; } = [];
    public bool HasContent => Messages.Any(message => message.Parts.Count > 0);
    public bool IsThinking { get; private set; } = true;

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        IsThinking = false;
        CurrentMessage().AppendText(text);
    }

    public void StartToolCall(string callId, string name, string argumentsJson, bool argumentsComplete, string? explicitDisplayTitle = null)
    {
        IsThinking = false;
        var chip = FindToolChip(callId);
        if (chip is null)
        {
            chip = new ChatToolChip(callId, name, argumentsJson, argumentsComplete, explicitDisplayTitle);
            ToolMessage().Parts.Add(new ChatToolPart(chip));
            return;
        }

        chip.Rename(name);
        chip.SetExplicitDisplayTitle(explicitDisplayTitle);
        if (!string.IsNullOrEmpty(argumentsJson))
            chip.SetArguments(argumentsJson);
        if (argumentsComplete)
            chip.MarkArgumentsComplete();
    }

    public void AppendToolArguments(string callId, string argumentsDelta, bool argumentsComplete)
    {
        IsThinking = false;
        var chip = FindToolChip(callId);
        if (chip is null)
        {
            chip = new ChatToolChip(callId, callId, string.Empty, argumentsComplete: false);
            ToolMessage().Parts.Add(new ChatToolPart(chip));
        }

        chip.AppendArguments(argumentsDelta, argumentsComplete);
    }

    public void CompleteToolCall(
        string callId,
        string? result,
        string? error,
        double? durationMs = null,
        IReadOnlyList<ChatImageVisual>? visuals = null)
    {
        var chip = FindToolChip(callId);
        if (chip is not null)
        {
            chip.Result = result;
            chip.Error = error;
            chip.DurationMs = durationMs;
            chip.Completed = true;
            chip.MarkArgumentsComplete();
            if (visuals is { Count: > 0 })
                chip.Visuals.AddRange(visuals);
        }

        IsThinking = true;
        _startNewMessageOnNextPart = true;
    }

    public ChatLiveTurn Clone()
    {
        var clone = new ChatLiveTurn
        {
            IsThinking = IsThinking,
            _startNewMessageOnNextPart = _startNewMessageOnNextPart
        };
        foreach (var message in Messages)
            clone.Messages.Add(message.Clone());

        return clone;
    }

    private ChatToolChip? FindToolChip(string callId) => Messages
        .SelectMany(message => message.Parts)
        .OfType<ChatToolPart>()
        .Select(part => part.Chip)
        .FirstOrDefault(candidate => candidate.CallId == callId);

    private ChatLiveMessage CurrentMessage()
    {
        if (_startNewMessageOnNextPart || Messages.Count == 0)
        {
            var message = new ChatLiveMessage();
            Messages.Add(message);
            _startNewMessageOnNextPart = false;
            return message;
        }

        return Messages[^1];
    }

    private ChatLiveMessage ToolMessage()
    {
        if (_startNewMessageOnNextPart
            && Messages.LastOrDefault() is { } lastMessage
            && lastMessage.Parts.Count > 0
            && lastMessage.Parts.All(part => part is ChatToolPart))
        {
            _startNewMessageOnNextPart = false;
            return lastMessage;
        }

        return CurrentMessage();
    }
}

public sealed class ChatLiveMessage
{
    public List<ChatMessagePart> Parts { get; } = [];

    public void AppendText(string text)
    {
        if (Parts.LastOrDefault() is ChatTextPart textPart)
            textPart.Append(text);
        else
            Parts.Add(new ChatTextPart(text));
    }

    public ChatLiveMessage Clone()
    {
        var clone = new ChatLiveMessage();
        foreach (var part in Parts)
        {
            switch (part)
            {
                case ChatTextPart textPart:
                    clone.Parts.Add(new ChatTextPart(textPart.Text.ToString()));
                    break;
                case ChatToolPart toolPart:
                    clone.Parts.Add(new ChatToolPart(toolPart.Chip.Clone()));
                    break;
                case ChatImagePart imagePart:
                    clone.Parts.Add(new ChatImagePart(imagePart.Visual));
                    break;
            }
        }

        return clone;
    }

    public List<ChatMessagePart> CloneParts() => Clone().Parts;
}

public sealed record ChatPersistedToolCall(
    string CallId,
    string Name,
    string ArgumentsJson,
    int? TextOffset = null,
    string? ExplicitDisplayTitle = null);

public static class ChatTranscriptHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static List<ChatPersistedToolCall> ReadPersistedCalls(string toolCallsJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallsJson) || toolCallsJson == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<ChatPersistedToolCall>>(toolCallsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string TruncateInline(string value, int max)
    {
        value = value.Replace('\n', ' ').Replace('\r', ' ');
        return value.Length <= max ? value : value[..max] + "...";
    }
}
