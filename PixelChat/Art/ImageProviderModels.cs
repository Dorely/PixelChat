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
    IReadOnlyList<string> OutputFormats);
