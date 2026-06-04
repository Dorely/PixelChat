namespace PixelChat.Llm;

public interface IOpenAIAccountAuthService
{
    (string AuthorizationUrl, string State) StartPkceFlow(int providerId);
    Task<int> HandleCallbackAsync(string code, string state, CancellationToken cancellationToken = default);
    Task<string?> GetValidTokenAsync(int providerId, CancellationToken cancellationToken = default);
    Task RevokeTokenAsync(int providerId, CancellationToken cancellationToken = default);
}
