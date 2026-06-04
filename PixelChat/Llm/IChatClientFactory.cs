using Microsoft.Extensions.AI;
using PixelChat.Models;

namespace PixelChat.Llm;

public interface IChatClientFactory
{
    Task<IChatClient> CreateChatClientAsync(int providerId, CancellationToken cancellationToken = default);
    Task TestModelAsync(int providerId, CancellationToken cancellationToken = default);
    Task TestModelAsync(LlmProvider provider, string? apiKeyOverride = null, CancellationToken cancellationToken = default);
}
