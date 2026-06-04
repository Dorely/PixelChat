using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PixelChat.Llm;

public sealed class OpenAIAccountChatClient : IChatClient
{
    private const int MaxBufferedResponseAttempts = 2;

    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _model;
    private readonly string _accountId;
    private readonly ILogger<OpenAIAccountChatClient> _logger;

    public OpenAIAccountChatClient(HttpClient httpClient, string accessToken, string model, ILogger<OpenAIAccountChatClient> logger)
    {
        _httpClient = httpClient;
        _accessToken = accessToken;
        _model = string.IsNullOrWhiteSpace(model) ? OpenAIAccountProvider.DefaultChatModel : model;
        _accountId = OpenAIAccountProvider.ExtractAccountId(accessToken);
        _logger = logger;
    }

    public ChatClientMetadata Metadata => new("OpenAIAccountResponsesAPI", new Uri("https://chatgpt.com"));

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bufferedMessages = chatMessages as IReadOnlyList<ChatMessage> ?? chatMessages.ToList();

        for (var attempt = 1; attempt <= MaxBufferedResponseAttempts; attempt++)
        {
            var fullText = new StringBuilder();

            try
            {
                await foreach (var update in GetStreamingResponseAsync(bufferedMessages, options, cancellationToken))
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextContent textContent && textContent.Text is { Length: > 0 })
                            fullText.Append(textContent.Text);
                    }
                }
            }
            catch (HttpIOException ex) when (IsResponseEnded(ex)
                && attempt < MaxBufferedResponseAttempts
                && fullText.Length == 0
                && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "OpenAI account streaming response ended before assistant content on attempt {Attempt}; retrying once.",
                    attempt);
                continue;
            }

            return new ChatResponse([new ChatMessage(ChatRole.Assistant, fullText.ToString())]);
        }

        throw new HttpRequestException("OpenAI account streaming response ended prematurely before assistant content after retry.");
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var bufferedMessages = chatMessages as IReadOnlyCollection<ChatMessage> ?? chatMessages.ToList();
        var body = BuildRequestBody(bufferedMessages);
        var json = JsonSerializer.Serialize(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAIAccountProvider.ResponsesEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.TryAddWithoutValidation("chatgpt-account-id", _accountId);
        request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
        request.Headers.TryAddWithoutValidation("originator", "pi");
        request.Headers.TryAddWithoutValidation("User-Agent", "PixelChat");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        _logger.LogDebug(
            "OpenAI account request: POST {Endpoint}, account={AccountId}, model={Model}, messages={MessageCount}, bodyChars={BodyChars}",
            OpenAIAccountProvider.ResponsesEndpoint,
            _accountId,
            _model,
            bufferedMessages.Count,
            json.Length);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI account API error {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
            throw new HttpRequestException($"OpenAI account API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var eventCount = 0;
        var outputTextChars = 0;
        string? lastEventType = null;
        var lastEventData = string.Empty;

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (HttpIOException ex) when (IsResponseEnded(ex) && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "OpenAI account streaming response ended prematurely after {EventCount} SSE events; lastEvent={LastEventType}; textChars={TextChars}; model={Model}; bodyChars={BodyChars}",
                    eventCount,
                    lastEventType ?? "(none)",
                    outputTextChars,
                    _model,
                    json.Length);
                throw;
            }

            if (line is null)
                break;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;
            lastEventData = data;

            JsonElement evt;
            try
            {
                evt = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch
            {
                continue;
            }

            var type = evt.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            eventCount++;
            lastEventType = type ?? "(missing)";

            switch (type)
            {
                case "response.output_text.delta":
                    if (evt.TryGetProperty("delta", out var delta))
                    {
                        var text = delta.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            outputTextChars += text.Length;
                            yield return new ChatResponseUpdate
                            {
                                Role = ChatRole.Assistant,
                                Contents = [new TextContent(text)]
                            };
                        }
                    }
                    break;

                case "response.completed" or "response.done":
                    _logger.LogDebug(
                        "OpenAI account streaming response completed after {EventCount} SSE events. TextChars={TextChars}",
                        eventCount,
                        outputTextChars);
                    yield break;

                case "response.created":
                case "response.in_progress":
                case "response.content_part.added":
                case "response.content_part.done":
                case "response.output_item.added":
                case "response.output_item.done":
                case "response.output_text.done":
                case "response.reasoning_summary_part.added":
                case "response.reasoning_summary_part.done":
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_summary_text.done":
                case "keepalive":
                    break;

                case "response.failed":
                    _logger.LogError(
                        "OpenAI account streaming response failed after {EventCount} SSE events; lastEvent={LastEventType}; textChars={TextChars}; model={Model}; bodyChars={BodyChars}; event={EventData}",
                        eventCount,
                        lastEventType ?? "(none)",
                        outputTextChars,
                        _model,
                        json.Length,
                        Truncate(lastEventData, 2000));
                    throw new InvalidOperationException(ExtractErrorMessage(evt, "OpenAI account API response failed."));

                case "error":
                    _logger.LogError(
                        "OpenAI account streaming error after {EventCount} SSE events; lastEvent={LastEventType}; textChars={TextChars}; model={Model}; bodyChars={BodyChars}; event={EventData}",
                        eventCount,
                        lastEventType ?? "(none)",
                        outputTextChars,
                        _model,
                        json.Length,
                        Truncate(lastEventData, 2000));
                    throw new InvalidOperationException($"OpenAI account error: {ExtractErrorMessage(evt, "Unknown error")}");

                default:
                    _logger.LogWarning("Unhandled OpenAI account SSE event type: {EventType}", type);
                    break;
            }
        }

        _logger.LogDebug(
            "OpenAI account streaming response ended after {EventCount} SSE events without an explicit completion event. TextChars={TextChars}",
            eventCount,
            outputTextChars);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private Dictionary<string, object> BuildRequestBody(IEnumerable<ChatMessage> chatMessages)
    {
        var instructions = new List<string>();
        var inputItems = new List<object>();

        foreach (var message in chatMessages)
        {
            if (message.Role == ChatRole.System)
            {
                instructions.Add(message.Text ?? string.Empty);
                continue;
            }

            var text = message.Text ?? string.Empty;
            if (message.Role == ChatRole.User)
            {
                inputItems.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = new[] { new { type = "input_text", text } }
                });
            }
            else if (message.Role == ChatRole.Assistant)
            {
                inputItems.Add(new Dictionary<string, object>
                {
                    ["role"] = "assistant",
                    ["content"] = new[] { new { type = "output_text", text } }
                });
            }
        }

        return new Dictionary<string, object>
        {
            ["model"] = _model,
            ["stream"] = true,
            ["store"] = false,
            ["input"] = inputItems,
            ["instructions"] = instructions.Count > 0
                ? string.Join("\n\n", instructions)
                : "You are a helpful assistant."
        };
    }

    private static bool IsResponseEnded(HttpIOException exception) =>
        exception.HttpRequestError == HttpRequestError.ResponseEnded;

    private static string ExtractErrorMessage(JsonElement evt, string fallback)
    {
        foreach (var path in new[]
        {
            new[] { "message" },
            new[] { "error", "message" },
            new[] { "response", "error", "message" },
            new[] { "error", "code" },
            new[] { "response", "error", "code" },
        })
        {
            if (TryGetStringByPath(evt, path, out var value))
                return value;
        }

        return fallback;
    }

    private static bool TryGetStringByPath(JsonElement element, IReadOnlyList<string> path, out string value)
    {
        value = string.Empty;
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return false;
        }

        value = current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : current.GetRawText();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "...";
    }
}
