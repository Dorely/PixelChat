namespace PixelChat.Art;

public interface IImageGenerationRuntime
{
    event EventHandler? StateChanged;

    ImageGenerationRuntimeSnapshot GetSnapshot();
    bool HasRunningBatch(Guid projectId);
    Task<GenerationBatchView> StartGenerateImagesAsync(Guid projectId, GenerateImagesRequest request, CancellationToken cancellationToken = default);
    Task<GenerationBatchView> StartEditImageAsync(Guid projectId, EditImageRequest request, CancellationToken cancellationToken = default);
    Task<bool> WaitForBatchCompletionAsync(Guid batchId, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task ReconcileInterruptedBatchesAsync(CancellationToken cancellationToken = default);
}

public sealed record ImageGenerationRuntimeSnapshot(
    IReadOnlyList<ImageGenerationBatchRuntimeView> Batches);

public sealed record ImageGenerationBatchRuntimeView(
    Guid ProjectId,
    Guid BatchId,
    bool IsRunning,
    IReadOnlyList<ImageGenerationOutputRuntimeView> Outputs);

public sealed record ImageGenerationOutputRuntimeView(
    int OutputIndex,
    GenerationOutputStatus Status,
    int Attempt,
    string Message,
    string Error,
    string? ErrorKind,
    string? RequestId,
    string? ResponseId,
    string? CallId,
    string? LastEventType,
    int EventCount,
    string? PartialImageDataUrl);
