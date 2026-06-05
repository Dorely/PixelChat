using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PixelChat.Llm;

namespace PixelChat.Art;

public sealed class OpenAIAccountImageProvider(
    ILlmProviderService providerService,
    IOpenAIAccountAuthService openAIAuth,
    IHttpClientFactory httpClientFactory,
    IOptions<ImageGenerationOptions> options,
    ILogger<OpenAIAccountImageProvider> logger) : IImageProvider
{
    public ImageProviderCapabilities DescribeCapabilities() =>
        new(
            SupportsReferenceImages: true,
            SupportsMaskedEdit: true,
            MaxReferenceImages: Math.Max(1, options.Value.MaxReferenceImages),
            Sizes: ["auto", "1024x1024", "1024x1536", "1536x1024"],
            OutputFormats: ["png", "jpeg", "webp"]);

    public async Task<ImageProviderResult> GenerateAsync(ImageProviderGenerateRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await ResolveConnectionAsync(cancellationToken);
        var mainlineModel = CleanModel(request.MainlineModel, options.Value.DefaultMainlineModel);
        var imageModel = CleanModel(request.ImageModel, options.Value.DefaultImageModel);
        var images = new List<ImageProviderImage>();
        var metadata = new List<object?>();

        for (var i = 0; i < request.Count; i++)
        {
            var includedReferenceCount = Math.Min(request.ReferenceImages.Count, Math.Max(0, options.Value.MaxReferenceImages));
            var result = await SendImageRequestAsync(
                connection,
                mainlineModel,
                imageModel,
                BuildGeneratePayload(request, mainlineModel, imageModel),
                new OpenAIImageRequestDiagnostics(
                    "generate",
                    request.Size,
                    request.Quality,
                    request.OutputFormat,
                    request.Background,
                    includedReferenceCount,
                    includedReferenceCount,
                    HasMask: false),
                request.OutputFormat,
                cancellationToken);
            images.Add(result.Image);
            metadata.Add(result.Metadata);
        }

        return new ImageProviderResult(
            images,
            OpenAIAccountProvider.Name,
            mainlineModel,
            imageModel,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    public async Task<ImageProviderResult> EditAsync(ImageProviderEditRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await ResolveConnectionAsync(cancellationToken);
        var mainlineModel = CleanModel(request.MainlineModel, options.Value.DefaultMainlineModel);
        var imageModel = CleanModel(request.ImageModel, options.Value.DefaultImageModel);
        var images = new List<ImageProviderImage>();
        var metadata = new List<object?>();

        for (var i = 0; i < request.Count; i++)
        {
            var includedReferenceCount = Math.Min(request.ReferenceImages.Count, Math.Max(0, options.Value.MaxReferenceImages));
            var hasMask = request.Mask is not null;
            var result = await SendImageRequestAsync(
                connection,
                mainlineModel,
                imageModel,
                BuildEditPayload(request, mainlineModel, imageModel),
                new OpenAIImageRequestDiagnostics(
                    "edit",
                    request.Size,
                    request.Quality,
                    request.OutputFormat,
                    request.Background,
                    includedReferenceCount,
                    InputImageCount: 1 + includedReferenceCount,
                    HasMask: hasMask),
                request.OutputFormat,
                cancellationToken);
            images.Add(result.Image);
            metadata.Add(result.Metadata);
        }

        return new ImageProviderResult(
            images,
            OpenAIAccountProvider.Name,
            mainlineModel,
            imageModel,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<OpenAIImageConnection> ResolveConnectionAsync(CancellationToken cancellationToken)
    {
        var provider = await providerService.GetByNameAsync(OpenAIAccountProvider.Name, cancellationToken);
        if (provider is null || !OpenAIAccountProvider.IsOpenAIAccount(provider))
            throw new InvalidOperationException("No OpenAI account provider is configured. Connect OpenAI in Providers first.");

        var token = await openAIAuth.GetValidTokenAsync(provider.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Connect OpenAI in Providers before generating images.");

        return new OpenAIImageConnection(token, OpenAIAccountProvider.ExtractAccountId(token));
    }

    private async Task<OpenAIImageReadResult> SendImageRequestAsync(
        OpenAIImageConnection connection,
        string mainlineModel,
        string imageModel,
        Dictionary<string, object?> payload,
        OpenAIImageRequestDiagnostics diagnostics,
        string requestedOutputFormat,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAIAccountProvider.ResponsesEndpoint);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        OpenAIAccountProvider.ApplyCodexRequestHeaders(request, connection.Token, connection.AccountId);

        var httpClient = httpClientFactory.CreateClient();
        var timeoutSeconds = Math.Clamp(options.Value.RequestTimeoutSeconds, 1, 3600);
        httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        logger.LogDebug(
            "OpenAI account image request starting: action={Action}, model={MainlineModel}, imageModel={ImageModel}, size={Size}, quality={Quality}, outputFormat={OutputFormat}, background={Background}, referenceImages={ReferenceImageCount}, inputImages={InputImageCount}, hasMask={HasMask}, bodyChars={BodyChars}, timeoutSeconds={TimeoutSeconds}",
            diagnostics.Action,
            mainlineModel,
            imageModel,
            CleanForLog(diagnostics.Size),
            CleanForLog(diagnostics.Quality),
            CleanForLog(diagnostics.OutputFormat),
            CleanForLog(diagnostics.Background),
            diagnostics.ReferenceImageCount,
            diagnostics.InputImageCount,
            diagnostics.HasMask,
            json.Length,
            timeoutSeconds);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var requestId = ReadResponseRequestId(response);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorKind = ClassifyImageError(errorBody);
            logger.LogError(
                "OpenAI account image API error: statusCode={StatusCode}, requestId={RequestId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, elapsedMs={ElapsedMs}, errorKind={ErrorKind}, body={Body}",
                (int)response.StatusCode,
                requestId,
                diagnostics.Action,
                mainlineModel,
                imageModel,
                stopwatch.ElapsedMilliseconds,
                errorKind,
                TruncateForLog(errorBody, 4000));
            throw new HttpRequestException($"OpenAI account image request returned {(int)response.StatusCode} ({errorKind}): {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? responseId = null;
        string? lastEventType = null;
        var eventCount = 0;
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]")
                break;

            JsonElement evt;
            try
            {
                evt = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch (JsonException)
            {
                logger.LogDebug(
                    "OpenAI account image stream contained non-JSON SSE data: requestId={RequestId}, eventCount={EventCount}, dataChars={DataChars}",
                    requestId,
                    eventCount,
                    data.Length);
                continue;
            }

            var type = ReadString(evt, "type");
            lastEventType = type;
            eventCount++;
            if (type == "response.created" && evt.TryGetProperty("response", out var createdResponse))
                responseId = ReadString(createdResponse, "id");

            if (type == "response.failed")
            {
                var error = ReadResponseError(evt) ?? "OpenAI account image generation failed.";
                var errorKind = ClassifyImageError(error);
                logger.LogWarning(
                    "OpenAI account image response failed: requestId={RequestId}, responseId={ResponseId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, elapsedMs={ElapsedMs}, eventCount={EventCount}, lastEventType={LastEventType}, errorKind={ErrorKind}, error={Error}",
                    requestId,
                    responseId,
                    diagnostics.Action,
                    mainlineModel,
                    imageModel,
                    stopwatch.ElapsedMilliseconds,
                    eventCount,
                    lastEventType,
                    errorKind,
                    error);
                throw new InvalidOperationException($"OpenAI account image generation failed ({errorKind}): {error}");
            }
            if (type == "error")
            {
                var error = ReadResponseError(evt) ?? "Unknown error";
                var errorKind = ClassifyImageError(error);
                logger.LogWarning(
                    "OpenAI account image stream error: requestId={RequestId}, responseId={ResponseId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, elapsedMs={ElapsedMs}, eventCount={EventCount}, lastEventType={LastEventType}, errorKind={ErrorKind}, error={Error}",
                    requestId,
                    responseId,
                    diagnostics.Action,
                    mainlineModel,
                    imageModel,
                    stopwatch.ElapsedMilliseconds,
                    eventCount,
                    lastEventType,
                    errorKind,
                    error);
                throw new InvalidOperationException($"OpenAI account image error ({errorKind}): {error}");
            }

            if (type == "response.output_item.done"
                && evt.TryGetProperty("item", out var item))
            {
                var imageResult = TryReadImageResult(
                    item,
                    requestedOutputFormat,
                    responseId,
                    requestId,
                    diagnostics,
                    mainlineModel,
                    imageModel,
                    stopwatch.Elapsed,
                    eventCount,
                    lastEventType);
                if (imageResult is not null)
                    return imageResult;
            }

            if (type == "response.completed"
                && evt.TryGetProperty("response", out var completedResponse))
            {
                responseId ??= ReadString(completedResponse, "id");
                var imageResult = TryReadImageResultFromResponse(
                    completedResponse,
                    requestedOutputFormat,
                    responseId,
                    requestId,
                    diagnostics,
                    mainlineModel,
                    imageModel,
                    stopwatch.Elapsed,
                    eventCount,
                    lastEventType);
                if (imageResult is not null)
                    return imageResult;
            }
        }

        logger.LogWarning(
            "OpenAI account image generation completed without image: requestId={RequestId}, responseId={ResponseId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, elapsedMs={ElapsedMs}, eventCount={EventCount}, lastEventType={LastEventType}",
            requestId,
            responseId,
            diagnostics.Action,
            mainlineModel,
            imageModel,
            stopwatch.ElapsedMilliseconds,
            eventCount,
            lastEventType);
        throw new InvalidOperationException($"OpenAI account image generation completed without returning an image. responseId={responseId ?? "unknown"}, requestId={requestId ?? "unknown"}, events={eventCount}, lastEvent={lastEventType ?? "none"}.");
    }

    private OpenAIImageReadResult? TryReadImageResultFromResponse(
        JsonElement response,
        string requestedOutputFormat,
        string? responseId,
        string? requestId,
        OpenAIImageRequestDiagnostics diagnostics,
        string mainlineModel,
        string imageModel,
        TimeSpan elapsed,
        int eventCount,
        string? lastEventType)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in output.EnumerateArray())
        {
            var imageResult = TryReadImageResult(
                item,
                requestedOutputFormat,
                responseId,
                requestId,
                diagnostics,
                mainlineModel,
                imageModel,
                elapsed,
                eventCount,
                lastEventType);
            if (imageResult is not null)
                return imageResult;
        }

        return null;
    }

    private OpenAIImageReadResult? TryReadImageResult(
        JsonElement item,
        string requestedOutputFormat,
        string? responseId,
        string? requestId,
        OpenAIImageRequestDiagnostics diagnostics,
        string mainlineModel,
        string imageModel,
        TimeSpan elapsed,
        int eventCount,
        string? lastEventType)
    {
        if (ReadString(item, "type") != "image_generation_call")
            return null;

        var result = ReadString(item, "result");
        if (string.IsNullOrWhiteSpace(result))
            return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(result);
        }
        catch (FormatException ex)
        {
            logger.LogError(
                ex,
                "OpenAI account returned invalid base64 image result: requestId={RequestId}, responseId={ResponseId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, callId={CallId}, elapsedMs={ElapsedMs}, eventCount={EventCount}, lastEventType={LastEventType}, resultChars={ResultChars}",
                requestId,
                responseId,
                diagnostics.Action,
                mainlineModel,
                imageModel,
                ReadString(item, "id"),
                elapsed.TotalMilliseconds,
                eventCount,
                lastEventType,
                result.Length);
            throw new InvalidOperationException("OpenAI account returned an image_generation_call result that was not valid base64.", ex);
        }

        var outputFormat = NormalizeOutputFormat(ReadString(item, "output_format") ?? requestedOutputFormat);
        var callId = ReadString(item, "id");
        logger.LogDebug(
            "OpenAI account image result received: requestId={RequestId}, responseId={ResponseId}, action={Action}, model={MainlineModel}, imageModel={ImageModel}, callId={CallId}, outputFormat={OutputFormat}, imageBytes={ImageBytes}, elapsedMs={ElapsedMs}, eventCount={EventCount}, lastEventType={LastEventType}",
            requestId,
            responseId,
            diagnostics.Action,
            mainlineModel,
            imageModel,
            callId,
            outputFormat,
            bytes.Length,
            elapsed.TotalMilliseconds,
            eventCount,
            lastEventType);

        return new OpenAIImageReadResult(
            new ImageProviderImage(
                bytes,
                $"image/{outputFormat}",
                outputFormat,
                ReadString(item, "revised_prompt"),
                responseId,
                callId),
            new
            {
                ResponseId = responseId,
                CallId = callId,
                RevisedPrompt = ReadString(item, "revised_prompt"),
                OutputFormat = outputFormat,
            });
    }

    private Dictionary<string, object?> BuildGeneratePayload(ImageProviderGenerateRequest request, string mainlineModel, string imageModel)
    {
        var content = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = "input_text",
                ["text"] = request.Prompt,
            },
        };

        foreach (var reference in request.ReferenceImages.Take(options.Value.MaxReferenceImages))
            content.Add(InputImage(reference));

        var tool = BaseImageTool(request.Size, request.Quality, request.OutputFormat, request.Background, imageModel);

        return BasePayload(mainlineModel, content, tool, "Use the image_generation tool to create one game-ready 2D art asset from the user's prompt.");
    }

    private Dictionary<string, object?> BuildEditPayload(ImageProviderEditRequest request, string mainlineModel, string imageModel)
    {
        var content = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = "input_text",
                ["text"] = request.Prompt,
            },
            InputImage(request.SourceImage),
        };

        foreach (var reference in request.ReferenceImages.Take(options.Value.MaxReferenceImages))
            content.Add(InputImage(reference));

        var tool = BaseImageTool(request.Size, request.Quality, request.OutputFormat, request.Background, imageModel);
        if (request.Mask is not null)
        {
            tool["input_image_mask"] = new Dictionary<string, object?>
            {
                ["image_url"] = ToDataUrl(request.Mask),
            };
        }

        return BasePayload(mainlineModel, content, tool, "Use the image_generation tool to edit the first supplied image. If a mask is supplied, apply it to guide the targeted edit.");
    }

    private static Dictionary<string, object?> BasePayload(
        string mainlineModel,
        IReadOnlyList<Dictionary<string, object?>> content,
        Dictionary<string, object?> tool,
        string instructions) =>
        new()
        {
            ["model"] = mainlineModel,
            ["instructions"] = instructions,
            ["input"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content,
                }
            },
            ["tools"] = new[] { tool },
            ["tool_choice"] = new Dictionary<string, object?> { ["type"] = "image_generation" },
            ["stream"] = true,
            ["store"] = false,
        };

    private static Dictionary<string, object?> BaseImageTool(
        string size,
        string quality,
        string outputFormat,
        string background,
        string imageModel)
    {
        var tool = new Dictionary<string, object?>
        {
            ["type"] = "image_generation",
            ["model"] = imageModel,
            ["size"] = string.IsNullOrWhiteSpace(size) ? "auto" : size.Trim(),
            ["output_format"] = NormalizeOutputFormat(outputFormat),
            ["background"] = NormalizeBackground(background),
        };

        var cleanQuality = quality.Trim();
        if (!string.IsNullOrWhiteSpace(cleanQuality)
            && !string.Equals(cleanQuality, "auto", StringComparison.OrdinalIgnoreCase))
        {
            tool["quality"] = cleanQuality;
        }

        return tool;
    }

    private static Dictionary<string, object?> InputImage(ImageProviderReference reference) =>
        new()
        {
            ["type"] = "input_image",
            ["image_url"] = ToDataUrl(reference),
            ["detail"] = "auto",
        };

    private static string ToDataUrl(ImageProviderReference reference) =>
        $"data:{reference.ContentType};base64,{Convert.ToBase64String(reference.Data)}";

    private static string CleanModel(string? requested, string fallback) =>
        string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();

    private static string NormalizeOutputFormat(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "jpg" => "jpeg",
            "jpeg" => "jpeg",
            "webp" => "webp",
            _ => "png",
        };

    private static string NormalizeBackground(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "transparent" => "transparent",
            "opaque" => "opaque",
            _ => "auto",
        };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadResponseRequestId(HttpResponseMessage response) =>
        ReadHeader(response, "x-request-id")
        ?? ReadHeader(response, "openai-request-id")
        ?? ReadHeader(response, "request-id");

    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();
        return null;
    }

    private static string CleanForLog(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();

    private static string TruncateForLog(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;
        return value[..maxChars] + "...";
    }

    private static string ClassifyImageError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "unknown";

        if (IsCodexImageInputRateLimit(message))
            return "codex_image_input_rate_limit";

        if (message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Please try again", StringComparison.OrdinalIgnoreCase))
        {
            return "rate_limit";
        }

        if (message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cf-mitigated", StringComparison.OrdinalIgnoreCase))
        {
            return "codex_transport_forbidden";
        }

        if (message.Contains("invalid_value", StringComparison.OrdinalIgnoreCase)
            && message.Contains("gpt-image", StringComparison.OrdinalIgnoreCase))
        {
            return "image_model_invalid";
        }

        return "api_error";
    }

    private static bool IsCodexImageInputRateLimit(string message) =>
        message.Contains("input-images", StringComparison.OrdinalIgnoreCase)
        && message.Contains("per min", StringComparison.OrdinalIgnoreCase)
        && message.Contains("gpt-image", StringComparison.OrdinalIgnoreCase);

    private static string? ReadResponseError(JsonElement evt)
    {
        if (evt.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            return message.GetString();
        if (evt.TryGetProperty("error", out var error)
            && error.TryGetProperty("message", out var errorMessage)
            && errorMessage.ValueKind == JsonValueKind.String)
            return errorMessage.GetString();
        if (evt.TryGetProperty("response", out var response)
            && response.TryGetProperty("error", out error)
            && error.TryGetProperty("message", out errorMessage)
            && errorMessage.ValueKind == JsonValueKind.String)
            return errorMessage.GetString();
        return null;
    }

    private sealed record OpenAIImageConnection(string Token, string AccountId);

    private sealed record OpenAIImageRequestDiagnostics(
        string Action,
        string Size,
        string Quality,
        string OutputFormat,
        string Background,
        int ReferenceImageCount,
        int InputImageCount,
        bool HasMask);

    private sealed record OpenAIImageReadResult(ImageProviderImage Image, object Metadata);
}
