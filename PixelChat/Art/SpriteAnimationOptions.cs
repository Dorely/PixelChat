namespace PixelChat.Art;

public sealed class SpriteAnimationOptions
{
    public const string SectionName = "SpriteAnimation";

    public string DefaultFrameCellSize { get; set; } = "192x192";
    public int DefaultFps { get; set; } = 12;
}
