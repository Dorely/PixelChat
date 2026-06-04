namespace PixelChat.Llm;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public int OpenAIAccountRequestTimeoutSeconds { get; set; } = 600;
    public int MaxToolIterations { get; set; } = 3;
    public int MaxToolResultCharsForModel { get; set; } = 6000;
}
