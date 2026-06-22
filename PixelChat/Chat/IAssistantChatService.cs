using PixelChat.Models;
using PixelChat.Tokens;

namespace PixelChat.Chat;

public interface IAssistantChatService
{
    Task<AssistantConversation> GetOrCreateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task RecoverInterruptedToolCallsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TokenContextEstimate?> EstimateNextRequestTokensAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task ResetAsync(Guid projectId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AssistantTurnUpdate> SendAsync(Guid projectId, string userText, CancellationToken cancellationToken = default);
}
