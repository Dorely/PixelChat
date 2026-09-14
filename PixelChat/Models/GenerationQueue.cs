using PixelChat.Art;

namespace PixelChat.Models;

public sealed class GenerationPrompt
{
    public Guid BatchId { get; set; }
    public GenerationBatch Batch { get; set; } = null!;
    public int Index { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? OutputName { get; set; }
}

public sealed class GenerationOutput
{
    public Guid BatchId { get; set; }
    public GenerationBatch Batch { get; set; } = null!;
    public int OutputIndex { get; set; }
    public int PromptIndex { get; set; }
    public int OutputWithinPrompt { get; set; }
    public GenerationOutputStatus Status { get; set; } = GenerationOutputStatus.Queued;
    public Guid? AssetId { get; set; }
    public string StateJson { get; set; } = "{}";
    public string? ErrorJson { get; set; }
}

public sealed class GenerationReference
{
    public Guid BatchId { get; set; }
    public GenerationBatch Batch { get; set; } = null!;
    public int Index { get; set; }
    public Guid SourceAssetId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
}
