using Microsoft.EntityFrameworkCore;
using PixelChat.Models;

namespace PixelChat.Persistence.Repositories;

public class AssistantConversationRepository(AppDbContext db) : IAssistantConversationRepository
{
    public Task<AssistantConversation?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        db.AssistantConversations
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AssistantConversation?> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        db.AssistantConversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken = default) =>
        await db.AssistantConversations.AddAsync(conversation, cancellationToken);

    public void RemoveConversation(AssistantConversation conversation) =>
        db.AssistantConversations.Remove(conversation);

    public async Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        await db.AssistantMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Order)
            .ToListAsync(cancellationToken);

    public Task<AssistantMessage?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        db.AssistantMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

    public async Task<int> GetMaxOrderAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        await db.AssistantMessages
            .Where(m => m.ConversationId == conversationId)
            .Select(m => (int?)m.Order)
            .MaxAsync(cancellationToken) ?? -1;

    public async Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken = default) =>
        await db.AssistantMessages.AddAsync(message, cancellationToken);

    public void UpdateMessage(AssistantMessage message) => db.AssistantMessages.Update(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
