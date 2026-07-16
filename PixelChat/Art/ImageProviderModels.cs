namespace PixelChat.Art;

public interface IImageProvider
{
    Task<ImageProviderResult> GenerateAsync(
        ImageProviderGenerateRequest request,
        CancellationToken cancellationToken = default,
        IProgress<ImageProviderProgress>? progress = null);

    Task<ImageProviderResult> EditAsync(
        ImageProviderEditRequest request,
        CancellationToken cancellationToken = default,
        IProgress<ImageProviderProgress>? progress = null);

    ImageProviderCapabilities DescribeCapabilities();
}

public sealed record ImageProviderGenerateRequest(
    string Prompt,
    string NegativePrompt,
    string Size,
    int Count,
    string? MainlineModel,
    string? ImageModel,
    IReadOnlyList<ImageProviderReference> ReferenceImages,
    string OutputFormat,
    string Quality,
    string Background);

public sealed record ImageProviderEditRequest(
    string Prompt,
    string Size,
    int Count,
    string? MainlineModel,
    string? ImageModel,
    ImageProviderReference SourceImage,
    ImageProviderReference? Mask,
    IReadOnlyList<ImageProviderReference> ReferenceImages,
    string OutputFormat,
    string Quality,
    string Background);

public sealed record ImageProviderReference(
    string FileName,
    string ContentType,
    byte[] Data);

public sealed record ImageProviderResult(
    IReadOnlyList<ImageProviderImage> Images,
    string Provider,
    string MainlineModel,
    string ImageModel,
    string RawMetadataJson);

public sealed record ImageProviderImage(
    byte[] Data,
    string ContentType,
    string OutputFormat,
    string? RevisedPrompt,
    string? ResponseId,
    string? CallId);

public sealed record ImageProviderProgress(
    ImageProviderProgressKind Kind,
    string Message,
    string? RequestId = null,
    string? ResponseId = null,
    string? CallId = null,
    string? ItemId = null,
    int? ProviderOutputIndex = null,
    int? PartialImageIndex = null,
    string? PartialImageDataUrl = null,
    string? ErrorKind = null,
    int? StatusCode = null,
    string? LastEventType = null,
    int EventCount = 0);

public enum ImageProviderProgressKind
{
    Started,
    InProgress,
    Generating,
    PartialImage,
    Completed,
    Failed,
    StreamEndedWithoutImage
}

public sealed class ImageProviderException : InvalidOperationException
{
    public ImageProviderException(
        string message,
        string errorKind,
        string? requestId = null,
        string? responseId = null,
        string? callId = null,
        int? statusCode = null,
        string? lastEventType = null,
        int eventCount = 0,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = string.IsNullOrWhiteSpace(errorKind) ? "unknown" : errorKind.Trim();
        RequestId = requestId;
        ResponseId = responseId;
        CallId = callId;
        StatusCode = statusCode;
        LastEventType = lastEventType;
        EventCount = eventCount;
    }

    public string ErrorKind { get; }
    public string? RequestId { get; }
    public string? ResponseId { get; }
    public string? CallId { get; }
    public int? StatusCode { get; }
    public string? LastEventType { get; }
    public int EventCount { get; }
}

public sealed record ImageProviderCapabilities(
    bool SupportsReferenceImages,
    bool SupportsMaskedEdit,
    int MaxReferenceImages,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> OutputFormats,
    ImageSizeConstraints SizeConstraints,
    long? ReliableEditMaximumPixels = null,
    bool MayReturnNoncanonicalEditDimensions = false);

public sealed record ImageSizeConstraints(
    int DimensionMultiple,
    int MaximumEdge,
    long MinimumPixels,
    long MaximumPixels,
    double MaximumAspectRatio);

public static class ImageSizeValidator
{
    public static readonly ImageSizeConstraints GptImage2Constraints = new(
        DimensionMultiple: 16,
        MaximumEdge: 3840,
        MinimumPixels: 655_360,
        MaximumPixels: 8_294_400,
        MaximumAspectRatio: 3d);

    private static readonly string[] PreferredSizes = ["1024x1024", "1536x1024", "1024x1536"];

    public static bool TryValidate(string? size, out string error, out string suggestedSize)
        => TryValidate(size, GptImage2Constraints, out error, out suggestedSize);

    public static bool TryValidate(
        string? size,
        ImageSizeConstraints constraints,
        out string error,
        out string suggestedSize)
    {
        error = string.Empty;
        suggestedSize = PreferredSizes[0];
        var trimmed = size?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = trimmed.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
        {
            error = $"Size '{trimmed}' is not valid. Use 'auto' or '{{width}}x{{height}}' such as {string.Join(", ", PreferredSizes)}.";
            return false;
        }

        if (width <= 0 || height <= 0 || width > constraints.MaximumEdge || height > constraints.MaximumEdge)
        {
            error = $"Size '{trimmed}' must use positive dimensions no larger than {constraints.MaximumEdge}px per edge.";
            suggestedSize = SuggestFor(width, height);
            return false;
        }

        if (width % constraints.DimensionMultiple != 0 || height % constraints.DimensionMultiple != 0)
        {
            error = $"Size '{trimmed}' must use dimensions that are multiples of {constraints.DimensionMultiple}px.";
            suggestedSize = SuggestFor(width, height);
            return false;
        }

        if (width > height * constraints.MaximumAspectRatio || height > width * constraints.MaximumAspectRatio)
        {
            error = $"Size '{trimmed}' exceeds the maximum supported aspect ratio of {constraints.MaximumAspectRatio:0.##}:1.";
            suggestedSize = SuggestFor(width, height);
            return false;
        }

        var pixels = (long)width * height;
        if (pixels < constraints.MinimumPixels || pixels > constraints.MaximumPixels)
        {
            error = $"Size '{trimmed}' must contain between {constraints.MinimumPixels:N0} and {constraints.MaximumPixels:N0} pixels.";
            suggestedSize = SuggestFor(width, height);
            return false;
        }

        return true;
    }

    private static string SuggestFor(int width, int height) =>
        width == height ? PreferredSizes[0] : width > height ? PreferredSizes[1] : PreferredSizes[2];
}
