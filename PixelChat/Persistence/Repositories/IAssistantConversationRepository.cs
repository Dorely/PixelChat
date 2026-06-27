using PixelChat.Models;

namespace PixelChat.Persistence.Repositories;

public interface IAssistantConversationRepository
{
    Task<AssistantConversation?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<AssistantConversation?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken = default);
    void RemoveConversation(AssistantConversation conversation);
    Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<AssistantMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<int> GetMaxOrderAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken = default);
    Task AddMessageVisualsAsync(IEnumerable<AssistantMessageVisual> visuals, CancellationToken cancellationToken = default);
    void UpdateMessage(AssistantMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
