using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;

namespace PixelChat.Tokens;

public interface IChatTokenEstimator
{
    TokenContextEstimate Count(IReadOnlyList<ChatMessage> messages, string? modelName);
}

public sealed record TokenContextEstimate(
    int TokenCount,
    bool IsExact,
    string Method,
    string? Warning = null);

public sealed class ChatTokenEstimator(
    ITokenCounter tokenCounter,
    IImageTokenEstimator imageTokenEstimator) : IChatTokenEstimator
{
    private const int FallbackImageWidth = 512;
    private const int FallbackImageHeight = 512;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TokenContextEstimate Count(IReadOnlyList<ChatMessage> messages, string? modelName)
    {
        var request = new TokenCountRequest(ModelName: modelName);
        var total = 0;
        var exact = true;
        var methods = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var message in messages)
        {
            CountText(message.Role.ToString(), request, ref total, ref exact, methods, warnings);

            if (message.Contents.Count == 0)
            {
                CountText(message.Text, request, ref total, ref exact, methods, warnings);
                continue;
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent textContent:
                        CountText(textContent.Text, request, ref total, ref exact, methods, warnings);
                        break;

                    case DataContent dataContent when dataContent.HasTopLevelMediaType("image"):
                        var imageEstimate = CountImage(dataContent, modelName);
                        total += imageEstimate.TokenCount;
                        exact = false;
                        methods.Add(imageEstimate.Method);
                        AddWarning(warnings, imageEstimate.Warning);
                        break;

                    case FunctionCallContent functionCall:
                        CountText(functionCall.Name, request, ref total, ref exact, methods, warnings);
                        CountText(functionCall.CallId, request, ref total, ref exact, methods, warnings);
                        CountText(JsonSerializer.Serialize(functionCall.Arguments ?? new Dictionary<string, object?>(), JsonOptions), request, ref total, ref exact, methods, warnings);
                        break;

                    case FunctionResultContent functionResult:
                        CountText(functionResult.CallId, request, ref total, ref exact, methods, warnings);
                        CountText(functionResult.Result?.ToString(), request, ref total, ref exact, methods, warnings);
                        break;
                }
            }
        }

        return new TokenContextEstimate(
            total,
            exact,
            methods.Count == 0 ? "none" : string.Join("+", methods.Order(StringComparer.Ordinal)),
            warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct(StringComparer.Ordinal)));
    }

    private void CountText(
        string? text,
        TokenCountRequest request,
        ref int total,
        ref bool exact,
        ISet<string> methods,
        ICollection<string> warnings)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var result = tokenCounter.Count(text, request);
        total += result.TokenCount;
        exact = exact && result.IsExact;
        methods.Add(result.Method);
        AddWarning(warnings, result.Warning);
    }

    private ImageTokenEstimate CountImage(DataContent dataContent, string? modelName)
    {
        var imageUrl = dataContent.Uri?.ToString();
        if (!string.IsNullOrWhiteSpace(imageUrl) && imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parsed = DataUrl.Parse(imageUrl);
                var size = ImageMetadataReader.TryReadSize(parsed.Data, parsed.ContentType);
                if (size.Width is int width && size.Height is int height)
                    return imageTokenEstimator.Count(width, height, modelName);

                var fallbackEstimate = imageTokenEstimator.Count(FallbackImageWidth, FallbackImageHeight, modelName);
                return fallbackEstimate with
                {
                    Warning = CombineWarnings(
                        fallbackEstimate.Warning,
                        "Image dimensions could not be read; estimated as 512x512.")
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                var fallbackEstimate = imageTokenEstimator.Count(FallbackImageWidth, FallbackImageHeight, modelName);
                return fallbackEstimate with
                {
                    Warning = CombineWarnings(
                        fallbackEstimate.Warning,
                        "Image data URL could not be parsed; estimated as 512x512.")
                };
            }
        }

        var remoteEstimate = imageTokenEstimator.Count(FallbackImageWidth, FallbackImageHeight, modelName);
        return remoteEstimate with
        {
            Warning = CombineWarnings(
                remoteEstimate.Warning,
                "Image dimensions were not locally available; estimated as 512x512.")
        };
    }

    private static void AddWarning(ICollection<string> warnings, string? warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
            warnings.Add(warning.Trim());
    }

    private static string CombineWarnings(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";
}
