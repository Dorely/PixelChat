using PixelChat.Components.Chat;
using PixelChat.Tokens;

namespace PixelChat.Chat;

public interface IWorkspaceChatRuntime
{
    event Action? StateChanged;
    event Action? WorkspaceChanged;
    event Action<AssistantFormDraft>? FormDraftProposed;
    event Action<WorkspaceChatTurnFinished>? TurnFinished;
    bool IsRunning { get; }
    WorkspaceChatRuntimeSnapshot GetSnapshot();
    Task StartTurnAsync(
        Guid projectId,
        string userText,
        IReadOnlyList<AssistantChatImageInput>? pastedImages = null,
        CancellationToken cancellationToken = default);
    Task StopTurnAsync();
    Task ResetConversationAsync(Guid projectId, CancellationToken cancellationToken = default);
    void ClearError();
}

public sealed record WorkspaceChatRuntimeSnapshot(
    bool Running,
    ChatLiveTurn? Live,
    string? PendingUserText,
    TokenContextEstimate? TokenCount,
    string? Error);

public sealed record WorkspaceChatTurnFinished(
    Guid ProjectId,
    Guid? UserMessageId,
    IReadOnlyList<ChatImageVisual> UserMessageVisuals,
    ChatLiveTurn Live,
    IReadOnlyList<Guid> AssistantMessageIds,
    ChatMessageStatus Status,
    string? Error);
