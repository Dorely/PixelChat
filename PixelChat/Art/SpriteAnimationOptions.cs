namespace PixelChat.Art;

public sealed class SpriteAnimationOptions
{
    public const string SectionName = "SpriteAnimation";

    public string DefaultFrameCellSize { get; set; } = "192x192";
    public int DefaultFps { get; set; } = 12;
    public int MaxGenerationRoundsPerJob { get; set; } = 20;
    public int MaxCandidateSheets { get; set; } = 3;
    public int MaxRepairAttemptsPerFrame { get; set; } = 2;
    public string PreferredImageModelSnapshot { get; set; } = string.Empty;
}
