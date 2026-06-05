using System.Net.Http.Headers;
using System.Text.Json;
using PixelChat.Models;

namespace PixelChat.Llm;

public static class OpenAIAccountProvider
{
    public const string Name = "openai-account";
    public const string ResponsesEndpoint = "https://chatgpt.com/backend-api/codex/responses";
    public const string DefaultChatModel = "gpt-5.4-mini";
    public const string CodexOriginator = "codex_cli_rs";

    private const string CodexUserAgent = "codex_cli_rs/0.0.0 (PixelChat)";

    public static bool IsOpenAIAccount(LlmProvider provider) =>
        string.Equals(provider.Name, Name, StringComparison.OrdinalIgnoreCase)
        && provider.AuthType == AuthType.OAuth;

    public static void ApplyCodexRequestHeaders(HttpRequestMessage request, string accessToken, string accountId)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", accountId);
        request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
        request.Headers.TryAddWithoutValidation("originator", CodexOriginator);
        request.Headers.TryAddWithoutValidation("User-Agent", CodexUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public static string ExtractAccountId(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("OAuth token is not a valid JWT.");

        var payload = parts[1];
        payload += new string('=', (4 - payload.Length % 4) % 4);
        var decoded = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
        var json = JsonSerializer.Deserialize<JsonElement>(decoded);

        if (json.TryGetProperty("https://api.openai.com/auth", out var authClaim) &&
            authClaim.TryGetProperty("chatgpt_account_id", out var accountId))
        {
            return accountId.GetString()
                ?? throw new InvalidOperationException("chatgpt_account_id is null in token.");
        }

        throw new InvalidOperationException("OAuth token does not contain chatgpt_account_id. Make sure you signed in with your OpenAI account.");
    }
}
