using Microsoft.Extensions.AI;
using OpenAI.Chat;
using PixelChat.Models;

namespace PixelChat.Llm;

public static class ProviderThinkingModes
{
    public const string OpenAIModeNone = "none";
    public const string OpenAIModeMinimal = "minimal";
    public const string OpenAIModeLow = "low";
    public const string OpenAIModeMedium = "medium";
    public const string OpenAIModeHigh = "high";
    public const string OpenAIModeExtraHigh = "xhigh";

    public static readonly IReadOnlyList<(string Value, string Label)> OpenAIOptions =
    [
        (string.Empty, "Provider default"),
        (OpenAIModeNone, "None"),
        (OpenAIModeMinimal, "Minimal"),
        (OpenAIModeLow, "Low"),
        (OpenAIModeMedium, "Medium"),
        (OpenAIModeHigh, "High"),
        (OpenAIModeExtraHigh, "XHigh")
    ];

    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    public static bool UsesOpenAIThinkingDropdown(LlmProvider provider) =>
        OpenAIAccountProvider.IsOpenAIAccount(provider) || IsOpenAIEndpoint(provider.EndpointUrl);

    public static bool IsOpenAIEndpoint(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl) || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyToChatOptions(ChatOptions options, string? thinkingMode)
    {
        var normalized = Normalize(thinkingMode);
        if (normalized is null)
            return;

        options.RawRepresentationFactory ??= _ => new ChatCompletionOptions
        {
#pragma warning disable OPENAI001
            ReasoningEffortLevel = new ChatReasoningEffortLevel(normalized)
#pragma warning restore OPENAI001
        };

        options.Reasoning ??= new ReasoningOptions();
        options.Reasoning.Effort ??= ToReasoningEffort(normalized);
    }

    private static ReasoningEffort? ToReasoningEffort(string thinkingMode) =>
        thinkingMode switch
        {
            OpenAIModeNone => ReasoningEffort.None,
            OpenAIModeLow => ReasoningEffort.Low,
            OpenAIModeMedium => ReasoningEffort.Medium,
            OpenAIModeHigh => ReasoningEffort.High,
            OpenAIModeExtraHigh => ReasoningEffort.ExtraHigh,
            _ => null
        };
}
