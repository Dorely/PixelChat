namespace PixelChat.Art;

public sealed class ImageGenerationOptions
{
    public const string SectionName = "Images";

    public string DefaultMainlineModel { get; set; } = "gpt-5.5";
    public string DefaultImageModel { get; set; } = "gpt-image-2";
    public string DefaultSize { get; set; } = "auto";
    public string DefaultQuality { get; set; } = "auto";
    public string DefaultOutputFormat { get; set; } = "png";
    public string DefaultBackground { get; set; } = "removable";
    public int MaxOutputs { get; set; } = 4;
    public int MaxParallelRequests { get; set; } = 4;
    public int MaxRequestAttempts { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 600;
    public int PartialImages { get; set; } = 2;
    public int MaxReferenceImages { get; set; } = 4;
    public long OpenAIAccountReliableEditMaximumPixels { get; set; } = 1_572_864;
}
