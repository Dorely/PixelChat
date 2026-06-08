namespace PixelChat.Art;

public interface IBackgroundRemovalService
{
    Task<BackgroundRemovalResult> RemoveBackgroundAsync(
        BackgroundRemovalRequest request,
        IProgress<BackgroundRemovalProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record BackgroundRemovalRequest(
    string FileName,
    string ContentType,
    byte[] Data,
    string? ModelName = null,
    string? RequestedBackend = null);

public sealed record BackgroundRemovalResult(
    byte[] Data,
    string ContentType,
    string Method,
    string Model,
    string Message,
    string ActualBackend,
    TimeSpan Elapsed);

public sealed record BackgroundRemovalProgress(
    string Stage,
    string Message);
