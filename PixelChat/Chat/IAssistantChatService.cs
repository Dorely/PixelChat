using PixelChat.Models;
using PixelChat.Tokens;

namespace PixelChat.Chat;

public interface IAssistantChatService
{
    Task<AssistantConversation> GetOrCreateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task RecoverInterruptedToolCallsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TokenContextEstimate?> EstimateNextRequestTokensAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<AssistantConversationCompactionResult> CompactAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
    Task ResetAsync(Guid projectId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AssistantTurnUpdate> SendAsync(
        Guid projectId,
        string userText,
        IReadOnlyList<AssistantChatImageInput>? pastedImages = null,
        CancellationToken cancellationToken = default);
}

public sealed record AssistantChatImageInput(
    string FileName,
    string ContentType,
    byte[] Data,
    string Label);

public sealed record AssistantConversationCompactionResult(
    Guid ConversationId,
    int RemovedToolCallCount,
    int RemovedMessageCount,
    bool SummaryCreated,
    bool Changed,
    TokenContextEstimate Before,
    TokenContextEstimate After);