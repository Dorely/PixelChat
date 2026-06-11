namespace PixelChat.Llm;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public int OpenAIAccountRequestTimeoutSeconds { get; set; } = 600;
    public int MaxToolIterations { get; set; } = 30;
    public int MaxToolResultCharsForModel { get; set; } = 6000;
    public int MaxGenerationRoundsPerTurn { get; set; } = 5;
    public int MaxImagesPerGenerationRound { get; set; } = 2;
    public int GenerationRoundWaitTimeoutSeconds { get; set; } = 600;
}
