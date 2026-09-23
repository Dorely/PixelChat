namespace PixelChat.Llm;

/// <summary>Account transport defaults, deliberately bounded below long-context usage.</summary>
public static class OpenAIModelCatalog
{
    public const int ContextTokens = 272_000;
    public const int EffectiveInputTokens = 258_400;
    public static readonly string[] ChatModels = ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna"];
    public static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];
    public static bool IsBuiltIn(string model) => ChatModels.Contains(model, StringComparer.Ordinal);
}
