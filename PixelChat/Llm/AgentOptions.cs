namespace PixelChat.Llm;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public int OpenAIAccountRequestTimeoutSeconds { get; set; } = 600;
}
