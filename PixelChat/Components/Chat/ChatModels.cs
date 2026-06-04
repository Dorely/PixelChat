using System.Text;

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
    string Content);

public sealed class ChatLiveTurn
{
    private readonly StringBuilder _text = new();

    public bool HasContent => _text.Length > 0;
    public bool IsThinking { get; private set; } = true;
    public string Text => _text.ToString();

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        IsThinking = false;
        _text.Append(text);
    }

    public ChatLiveTurn Clone()
    {
        var clone = new ChatLiveTurn
        {
            IsThinking = IsThinking
        };
        clone._text.Append(_text);
        return clone;
    }
}
