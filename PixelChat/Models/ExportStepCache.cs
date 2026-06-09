namespace PixelChat.Models;

public class ExportStepCache
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid AssetId { get; set; }
    public ArtAsset Asset { get; set; } = null!;

    public required string SourceImageSha256 { get; set; }
    public int StepIndex { get; set; }
    public required string ParentImageSha256 { get; set; }
    public required string OutputImageSha256 { get; set; }
    public required string Method { get; set; }
    public string OptionsHash { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ActualBackend { get; set; } = string.Empty;

    public string ContentType { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
    public int? Width { get; set; }
    public int? Height { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
