using PixelChat.Components.Chat;
using PixelChat.Tokens;

namespace PixelChat.Chat;

public interface IWorkspaceChatRuntime
{
    event Action? StateChanged;
    event Action? WorkspaceChanged;
    event Action<WorkspaceChatTurnFinished>? TurnFinished;
    event Action<WorkspaceChatCompactionFinished>? CompactionFinished;
    bool IsRunning { get; }
    bool IsCompacting { get; }
    WorkspaceChatRuntimeSnapshot GetSnapshot();
    Task StartTurnAsync(
        Guid projectId,
        string userText,
        IReadOnlyList<AssistantChatImageInput>? pastedImages = null,
        CancellationToken cancellationToken = default);
    Task StartCompactionAsync(Guid projectId);
    Task StopAsync();
    Task ResetConversationAsync(Guid projectId, CancellationToken cancellationToken = default);
    void ClearError();
}

public sealed record WorkspaceChatRuntimeSnapshot(
    bool Running,
    bool Compacting,
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

public sealed record WorkspaceChatCompactionFinished(
    Guid ProjectId,
    AssistantConversationCompactionResult? Result,
    bool Cancelled,
    string? Error);