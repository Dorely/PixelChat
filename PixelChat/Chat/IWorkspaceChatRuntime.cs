using PixelChat.Components.Chat;

namespace PixelChat.Chat;

public interface IWorkspaceChatRuntime
{
    event Action? StateChanged;
    event Action? WorkspaceChanged;
    event Action<AssistantFormDraft>? FormDraftProposed;
    bool IsRunning { get; }
    WorkspaceChatRuntimeSnapshot GetSnapshot();
    Task StartTurnAsync(Guid projectId, string userText, CancellationToken cancellationToken = default);
    Task StopTurnAsync();
    Task ResetConversationAsync(Guid projectId, CancellationToken cancellationToken = default);
    void ClearError();
}

public sealed record WorkspaceChatRuntimeSnapshot(
    bool Running,
    ChatLiveTurn? Live,
    string? PendingUserText,
    string? Error);
