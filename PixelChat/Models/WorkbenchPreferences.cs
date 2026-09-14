namespace PixelChat.Models;

public sealed class WorkbenchPreferences
{
    public int Id { get; set; } = 1;
    public string ImageModel { get; set; } = "gpt-image-2.5-sunburst";
    public string ImageQuality { get; set; } = "auto";
}
