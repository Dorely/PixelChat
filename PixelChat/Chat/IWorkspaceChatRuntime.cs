using PixelChat.Components.Chat;

namespace PixelChat.Chat;

public interface IWorkspaceChatRuntime
{
    event Action? StateChanged;
    bool IsRunning { get; }
    WorkspaceChatRuntimeSnapshot GetSnapshot();
    Task StartTurnAsync(Guid projectId, string userText, CancellationToken cancellationToken = default);
    Task StopTurnAsync();
    Task ResetConversationAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<AssistantToolExecutionResult> ConfirmToolCallAsync(Guid projectId, Guid assistantMessageId, string callId, CancellationToken cancellationToken = default);
    Task<AssistantToolExecutionResult> RejectToolCallAsync(Guid projectId, Guid assistantMessageId, string callId, CancellationToken cancellationToken = default);
    void ClearError();
}

public sealed record WorkspaceChatRuntimeSnapshot(
    bool Running,
    ChatLiveTurn? Live,
    string? PendingUserText,
    string? Error,
    int WorkspaceVersion);
