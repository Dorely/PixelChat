namespace PixelChat.Art;

public sealed class ImageGenerationOptions
{
    public const string SectionName = "Images";

    public string DefaultMainlineModel { get; set; } = "gpt-5.5";
    public string DefaultImageModel { get; set; } = "gpt-image-2";
    public string DefaultSize { get; set; } = "auto";
    public string DefaultQuality { get; set; } = "auto";
    public string DefaultOutputFormat { get; set; } = "png";
    public string DefaultBackground { get; set; } = "auto";
    public int MaxOutputs { get; set; } = 4;
    public int RequestTimeoutSeconds { get; set; } = 600;
    public int MaxReferenceImages { get; set; } = 4;
}
