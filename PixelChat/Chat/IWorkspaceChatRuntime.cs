using PixelChat.Components.Chat;

namespace PixelChat.Chat;

public interface IWorkspaceChatRuntime
{
    event Action? StateChanged;
    bool IsRunning { get; }
    WorkspaceChatRuntimeSnapshot GetSnapshot();
    Task StartTurnAsync(string userText, CancellationToken cancellationToken = default);
    Task StopTurnAsync();
    Task ResetConversationAsync(CancellationToken cancellationToken = default);
    void ClearError();
}

public sealed record WorkspaceChatRuntimeSnapshot(
    bool Running,
    ChatLiveTurn? Live,
    string? PendingUserText,
    string? Error);
