namespace PixelChat.Art;

public interface IImageProvider
{
    Task<ImageProviderResult> GenerateAsync(ImageProviderGenerateRequest request, CancellationToken cancellationToken = default);
    Task<ImageProviderResult> EditAsync(ImageProviderEditRequest request, CancellationToken cancellationToken = default);
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

public sealed record ImageProviderCapabilities(
    bool SupportsReferenceImages,
    bool SupportsMaskedEdit,
    int MaxReferenceImages,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> OutputFormats);
