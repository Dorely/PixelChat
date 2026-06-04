using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using PixelChat.Llm;
using PixelChat.Models;
using PixelChat.Persistence.Repositories;

namespace PixelChat.Chat;

public sealed class AssistantChatService(
    IAssistantConversationRepository conversations,
    ILlmProviderService providerService,
    IChatClientFactory chatClientFactory,
    ILogger<AssistantChatService> logger) : IAssistantChatService
{
    private const string InitialAssistantGreeting =
        "Tell me what kind of 2D game art you are working on. I can help shape style direction, asset prompts, iteration plans, and production-ready art specs.";

    public async Task<AssistantConversation> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await conversations.GetCurrentAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var conversation = new AssistantConversation();
        await conversations.AddConversationAsync(conversation, cancellationToken);

        var greeting = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Order = 0,
            Role = AssistantMessageRole.Assistant,
            Content = InitialAssistantGreeting,
            Status = AssistantMessageStatus.Completed,
        };
        await conversations.AddMessageAsync(greeting, cancellationToken);
        await conversations.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        await conversations.LoadMessagesAsync(conversationId, cancellationToken);

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var existing = await conversations.GetCurrentAsync(cancellationToken);
        if (existing is null)
            return;

        conversations.RemoveConversation(existing);
        await conversations.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<AssistantTurnUpdate> SendAsync(
        string userText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
            throw new ArgumentException("Message cannot be empty.", nameof(userText));

        var conversation = await GetOrCreateAsync(cancellationToken);
        var providerAvailability = await providerService.GetDefaultChatProviderAvailabilityAsync(cancellationToken);
        if (!providerAvailability.IsAvailable || providerAvailability.Provider is null)
        {
            yield return new AssistantTurnError(providerAvailability.Message, Cancelled: false);
            yield break;
        }

        var nextOrder = await conversations.GetMaxOrderAsync(conversation.Id, cancellationToken) + 1;

        var userMessage = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Order = nextOrder++,
            Role = AssistantMessageRole.User,
            Content = userText.Trim(),
            Status = AssistantMessageStatus.Completed,
        };
        await conversations.AddMessageAsync(userMessage, cancellationToken);
        conversation.UpdatedAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(cancellationToken);
        yield return new AssistantUserMessagePersisted(userMessage.Id);

        IChatClient chat = null!;
        string? setupError = null;
        try
        {
            chat = await chatClientFactory.CreateChatClientAsync(providerAvailability.Provider.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant turn setup failed.");
            await PersistFailedAssistantAsync(conversation.Id, nextOrder, ex.Message);
            setupError = ex.Message;
        }

        if (setupError is not null)
        {
            yield return new AssistantTurnError(setupError, Cancelled: false);
            yield break;
        }

        var history = await conversations.LoadMessagesAsync(conversation.Id, cancellationToken);
        var messages = new List<ChatMessage> { new(ChatRole.System, AssistantPromptBuilder.Build()) };
        messages.AddRange(history.Select(ToChatMessage));

        var activeAssistant = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Order = nextOrder++,
            Role = AssistantMessageRole.Assistant,
            Content = string.Empty,
            Status = AssistantMessageStatus.Pending,
        };
        await conversations.AddMessageAsync(activeAssistant, cancellationToken);
        await conversations.SaveChangesAsync(cancellationToken);

        var textBuilder = new StringBuilder();
        var streamFailed = false;
        string? streamError = null;
        var cancelled = false;

        var enumerator = chat.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Assistant streaming round failed.");
                    streamFailed = true;
                    streamError = ex.Message;
                    break;
                }

                if (!hasNext)
                    break;

                var contents = enumerator.Current?.Contents;
                if (contents is null)
                    continue;

                foreach (var content in contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        textBuilder.Append(textContent.Text);
                        yield return new AssistantTextDelta(textContent.Text);
                    }
                }
            }
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assistant streaming enumerator disposal failed.");
                streamFailed = true;
                streamError ??= ex.Message;
            }
        }

        if (cancelled)
        {
            activeAssistant.Content = textBuilder.ToString();
            activeAssistant.Status = AssistantMessageStatus.Cancelled;
            activeAssistant.ErrorMessage = "Cancelled by user.";
            await SafePersistAsync(activeAssistant);
            yield return new AssistantTurnError("Cancelled.", Cancelled: true);
            yield break;
        }

        if (streamFailed)
        {
            activeAssistant.Content = textBuilder.ToString();
            activeAssistant.Status = AssistantMessageStatus.Failed;
            activeAssistant.ErrorMessage = streamError;
            await SafePersistAsync(activeAssistant);
            yield return new AssistantTurnError(streamError ?? "Assistant streaming failed.", Cancelled: false);
            yield break;
        }

        activeAssistant.Content = textBuilder.ToString();
        activeAssistant.Status = AssistantMessageStatus.Completed;
        await SafePersistAsync(activeAssistant);
        conversation.UpdatedAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(CancellationToken.None);
        yield return new AssistantMessageCompleted(activeAssistant.Id);
    }

    private static ChatMessage ToChatMessage(AssistantMessage message) => message.Role switch
    {
        AssistantMessageRole.System => new ChatMessage(ChatRole.System, message.Content),
        AssistantMessageRole.User => new ChatMessage(ChatRole.User, message.Content),
        AssistantMessageRole.Assistant => new ChatMessage(ChatRole.Assistant, message.Content),
        _ => new ChatMessage(ChatRole.User, message.Content)
    };

    private async Task PersistFailedAssistantAsync(Guid conversationId, int order, string error)
    {
        try
        {
            var message = new AssistantMessage
            {
                ConversationId = conversationId,
                Order = order,
                Role = AssistantMessageRole.Assistant,
                Status = AssistantMessageStatus.Failed,
                ErrorMessage = error,
            };
            await conversations.AddMessageAsync(message, CancellationToken.None);
            await conversations.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist assistant setup failure.");
        }
    }

    private async Task SafePersistAsync(AssistantMessage message)
    {
        try
        {
            conversations.UpdateMessage(message);
            await conversations.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist assistant message {MessageId}", message.Id);
        }
    }
}
