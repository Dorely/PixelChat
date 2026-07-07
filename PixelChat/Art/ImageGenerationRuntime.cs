using System.Globalization;
using Microsoft.Extensions.Options;

namespace PixelChat.Art;

public sealed class ImageGenerationRuntime(
    IServiceScopeFactory scopeFactory,
    IOptions<ImageGenerationOptions> imageOptions,
    ILogger<ImageGenerationRuntime> logger) : IImageGenerationRuntime
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, ImageGenerationBatchRuntimeView> _batches = [];
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _batchCompletions = [];
    private readonly HashSet<Guid> _reservedProjectStarts = [];

    public event EventHandler? StateChanged;

    private enum RuntimeBatchKind
    {
        Generate,
        Edit
    }

    public ImageGenerationRuntimeSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new ImageGenerationRuntimeSnapshot(_batches.Values.ToList());
        }
    }

    public bool HasRunningBatch(Guid projectId)
    {
        lock (_lock)
        {
            return _reservedProjectStarts.Contains(projectId)
                || _batches.Values.Any(batch => batch.ProjectId == projectId && batch.IsRunning);
        }
    }

    public async Task<GenerationBatchView> StartGenerateImagesAsync(
        Guid projectId,
        GenerateImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ReserveProjectStart(projectId);
        GenerationBatchView batch;
        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
                batch = await workflow.StartGenerateImagesAsync(projectId, request, cancellationToken);
            }

            RegisterStartedBatch(projectId, batch, followInBatches: true);
        }
        catch
        {
            ReleaseProjectStart(projectId);
            throw;
        }
        NotifyStateChanged();

        _ = Task.Run(() => RunGenerationBatchAsync(projectId, batch.Id, batch.Count, RuntimeBatchKind.Generate));
        return batch;
    }

    public async Task<GenerationBatchView> StartEditImageAsync(
        Guid projectId,
        EditImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ReserveProjectStart(projectId);
        GenerationBatchView batch;
        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
                batch = await workflow.StartEditImageAsync(projectId, request, cancellationToken);
            }

            RegisterStartedBatch(projectId, batch, request.SwitchToBatches);
        }
        catch
        {
            ReleaseProjectStart(projectId);
            throw;
        }
        NotifyStateChanged();

        _ = Task.Run(() => RunGenerationBatchAsync(projectId, batch.Id, batch.Count, RuntimeBatchKind.Edit));
        return batch;
    }

    public async Task<bool> WaitForBatchCompletionAsync(
        Guid batchId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool>? completion;
        lock (_lock)
        {
            if (!_batches.TryGetValue(batchId, out var batch) || !batch.IsRunning)
                return true;

            if (!_batchCompletions.TryGetValue(batchId, out completion))
                return true;
        }

        try
        {
            await completion.Task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // The caller (e.g. an assistant tool) was cancelled mid-request. Treat it like a
            // non-completion and let the caller observe cancellation cooperatively rather than
            // letting the cancellation exception propagate up through the tool-invocation framework.
            return false;
        }
    }

    public async Task ReconcileInterruptedBatchesAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
        await workflow.ReconcileInterruptedGenerationBatchesAsync(cancellationToken);
        NotifyStateChanged();
    }

    private void ReserveProjectStart(Guid projectId)
    {
        lock (_lock)
        {
            if (_reservedProjectStarts.Contains(projectId)
                || _batches.Values.Any(batch => batch.ProjectId == projectId && batch.IsRunning))
            {
                throw new InvalidOperationException("An image generation batch is already running for this project.");
            }

            _reservedProjectStarts.Add(projectId);
        }
    }

    private void ReleaseProjectStart(Guid projectId)
    {
        lock (_lock)
        {
            _reservedProjectStarts.Remove(projectId);
        }
    }

    private void RegisterStartedBatch(Guid projectId, GenerationBatchView batch, bool followInBatches)
    {
        var runtimeBatch = new ImageGenerationBatchRuntimeView(
            projectId,
            batch.Id,
            IsRunning: true,
            followInBatches,
            batch.OutputStates.Select(ToRuntimeOutput).ToList());
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _reservedProjectStarts.Remove(projectId);
            _batches[batch.Id] = runtimeBatch;
            _batchCompletions[batch.Id] = completion;
        }
    }

    private async Task RunGenerationBatchAsync(Guid projectId, Guid batchId, int count, RuntimeBatchKind kind)
    {
        var parallelLimit = Math.Clamp(imageOptions.Value.MaxParallelRequests, 1, Math.Max(1, imageOptions.Value.MaxOutputs));
        logger.LogDebug(
            "Image generation runtime batch starting: projectId={ProjectId}, batchId={BatchId}, count={Count}, parallelLimit={ParallelLimit}",
            projectId,
            batchId,
            count,
            parallelLimit);

        using var throttler = new SemaphoreSlim(parallelLimit, parallelLimit);
        var tasks = Enumerable.Range(0, count)
            .Select(outputIndex => RunGenerationOutputAsync(projectId, batchId, outputIndex, throttler, kind))
            .ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image generation runtime batch failed unexpectedly: projectId={ProjectId}, batchId={BatchId}", projectId, batchId);
        }
        finally
        {
            await CompleteBatchAsync(projectId, batchId);
            MarkBatchNotRunning(batchId);
            CompleteBatchWaiter(batchId);
            NotifyStateChanged();
            logger.LogDebug("Image generation runtime batch finished: projectId={ProjectId}, batchId={BatchId}", projectId, batchId);
        }
    }

    private async Task RunGenerationOutputAsync(Guid projectId, Guid batchId, int outputIndex, SemaphoreSlim throttler, RuntimeBatchKind kind)
    {
        await throttler.WaitAsync();
        try
        {
            var finalError = await GenerateBatchOutputWithRetriesAsync(projectId, batchId, outputIndex, kind);
            if (finalError is null)
                return;

            logger.LogWarning(
                finalError,
                "Image generation runtime output failed after retries: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}",
                projectId,
                batchId,
                outputIndex);
            await PersistGenerationFailureAsync(projectId, batchId, outputIndex, finalError);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task<Exception?> GenerateBatchOutputWithRetriesAsync(Guid projectId, Guid batchId, int outputIndex, RuntimeBatchKind kind)
    {
        var maxAttempts = Math.Clamp(imageOptions.Value.MaxRequestAttempts, 1, 10);
        Exception? finalError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await PersistOutputStateAsync(projectId, batchId, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Running,
                    attempt,
                    $"Starting image request attempt {attempt} of {maxAttempts}.",
                    StartedAt: DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow));
                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Running, attempt, $"Starting image request attempt {attempt} of {maxAttempts}.");

                var progress = new ActionProgress(update => HandleProviderProgress(projectId, batchId, outputIndex, attempt, update));
                await using var scope = scopeFactory.CreateAsyncScope();
                var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
                if (kind == RuntimeBatchKind.Edit)
                    await workflow.GenerateEditBatchOutputAsync(projectId, batchId, outputIndex, CancellationToken.None, progress);
                else
                    await workflow.GenerateBatchOutputAsync(projectId, batchId, outputIndex, CancellationToken.None, progress);

                await PersistOutputStateAsync(projectId, batchId, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Succeeded,
                    attempt,
                    "Image saved.",
                    UpdatedAt: DateTime.UtcNow,
                    CompletedAt: DateTime.UtcNow));
                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Succeeded, attempt, "Image saved.", partialImageDataUrl: null);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                finalError = ex;
                if (attempt >= maxAttempts || !IsTransientImageGenerationError(ex))
                    break;

                var delay = ImageGenerationRetryDelay(ex, attempt);
                var message = $"Retrying after provider error, attempt {attempt + 1} of {maxAttempts}.";
                await PersistOutputStateAsync(projectId, batchId, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Running,
                    attempt,
                    message,
                    ex.Message,
                    ErrorKind: TryReadErrorKind(ex),
                    UpdatedAt: DateTime.UtcNow));
                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Running, attempt, message, error: ex.Message, errorKind: TryReadErrorKind(ex));
                await Task.Delay(delay);
            }
        }

        return finalError;
    }

    private void HandleProviderProgress(Guid projectId, Guid batchId, int outputIndex, int attempt, ImageProviderProgress update)
    {
        var status = update.Kind switch
        {
            ImageProviderProgressKind.Generating or ImageProviderProgressKind.PartialImage => GenerationOutputStatus.Generating,
            ImageProviderProgressKind.Failed or ImageProviderProgressKind.StreamEndedWithoutImage => GenerationOutputStatus.Failed,
            _ => GenerationOutputStatus.Running,
        };
        var message = string.IsNullOrWhiteSpace(update.Message) ? StatusMessage(status) : update.Message;
        UpdateRuntimeOutput(
            batchId,
            outputIndex,
            status,
            attempt,
            message,
            error: status == GenerationOutputStatus.Failed ? update.Message : null,
            errorKind: update.ErrorKind,
            requestId: update.RequestId,
            responseId: update.ResponseId,
            callId: update.CallId,
            lastEventType: update.LastEventType,
            eventCount: update.EventCount,
            partialImageDataUrl: update.PartialImageDataUrl);

        if (update.Kind != ImageProviderProgressKind.PartialImage)
        {
            PersistOutputStateFireAndForget(projectId, batchId, new GenerationOutputStateView(
                outputIndex,
                status,
                attempt,
                message,
                status == GenerationOutputStatus.Failed ? update.Message : "",
                update.ErrorKind,
                update.RequestId,
                update.ResponseId,
                update.CallId,
                update.LastEventType,
                update.EventCount,
                UpdatedAt: DateTime.UtcNow,
                CompletedAt: status == GenerationOutputStatus.Failed ? DateTime.UtcNow : null));
        }
    }

    private async Task PersistGenerationFailureAsync(Guid projectId, Guid batchId, int outputIndex, Exception exception)
    {
        var outputError = exception is ImageProviderException providerException
            ? new GenerationOutputErrorView(
                outputIndex,
                providerException.Message,
                providerException.ErrorKind,
                providerException.RequestId,
                providerException.ResponseId,
                providerException.CallId,
                providerException.StatusCode,
                providerException.LastEventType,
                providerException.EventCount)
            : new GenerationOutputErrorView(outputIndex, exception.Message);

        await using var scope = scopeFactory.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
        await workflow.MarkGenerationBatchOutputFailedAsync(projectId, batchId, outputError, CancellationToken.None);
        UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Failed, attempt: 0, "Image request failed.", error: outputError.Error, errorKind: outputError.ErrorKind);
    }

    private async Task CompleteBatchAsync(Guid projectId, Guid batchId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
            await workflow.CompleteGenerationBatchAsync(projectId, batchId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image generation runtime could not complete batch: projectId={ProjectId}, batchId={BatchId}", projectId, batchId);
        }
    }

    private async Task PersistOutputStateAsync(Guid projectId, Guid batchId, GenerationOutputStateView state)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
        await workflow.MarkGenerationBatchOutputStateAsync(projectId, batchId, state, CancellationToken.None);
    }

    private void PersistOutputStateFireAndForget(Guid projectId, Guid batchId, GenerationOutputStateView state)
    {
        _ = PersistOutputStateAsync(projectId, batchId, state).ContinueWith(
            task => logger.LogWarning(task.Exception, "Image generation runtime could not persist output state: projectId={ProjectId}, batchId={BatchId}, outputIndex={OutputIndex}", projectId, batchId, state.OutputIndex),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void UpdateRuntimeOutput(
        Guid batchId,
        int outputIndex,
        GenerationOutputStatus status,
        int attempt,
        string message,
        string? error = null,
        string? errorKind = null,
        string? requestId = null,
        string? responseId = null,
        string? callId = null,
        string? lastEventType = null,
        int eventCount = 0,
        string? partialImageDataUrl = null)
    {
        lock (_lock)
        {
            if (!_batches.TryGetValue(batchId, out var batch))
                return;

            var outputs = batch.Outputs
                .Where(output => output.OutputIndex != outputIndex)
                .Append(new ImageGenerationOutputRuntimeView(
                    outputIndex,
                    status,
                    attempt,
                    message,
                    error ?? string.Empty,
                    errorKind,
                    requestId,
                    responseId,
                    callId,
                    lastEventType,
                    eventCount,
                    partialImageDataUrl ?? batch.Outputs.FirstOrDefault(output => output.OutputIndex == outputIndex)?.PartialImageDataUrl))
                .OrderBy(output => output.OutputIndex)
                .ToList();
            _batches[batchId] = batch with { Outputs = outputs };
        }

        NotifyStateChanged();
    }

    private void MarkBatchNotRunning(Guid batchId)
    {
        lock (_lock)
        {
            if (_batches.TryGetValue(batchId, out var batch))
                _batches[batchId] = batch with { IsRunning = false };
        }
    }

    private void CompleteBatchWaiter(Guid batchId)
    {
        TaskCompletionSource<bool>? completion;
        lock (_lock)
        {
            if (!_batchCompletions.Remove(batchId, out completion))
                return;
        }

        completion.TrySetResult(true);
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static ImageGenerationOutputRuntimeView ToRuntimeOutput(GenerationOutputStateView state) =>
        new(
            state.OutputIndex,
            state.Status,
            state.Attempt,
            state.Message,
            state.Error,
            state.ErrorKind,
            state.RequestId,
            state.ResponseId,
            state.CallId,
            state.LastEventType,
            state.EventCount,
            PartialImageDataUrl: null);

    private static string StatusMessage(GenerationOutputStatus status) =>
        status switch
        {
            GenerationOutputStatus.Generating => "Generating image.",
            GenerationOutputStatus.Running => "Image request running.",
            GenerationOutputStatus.Failed => "Image request failed.",
            GenerationOutputStatus.Succeeded => "Image saved.",
            _ => "Waiting for earlier image requests.",
        };

    private static string? TryReadErrorKind(Exception exception) =>
        exception is ImageProviderException providerException ? providerException.ErrorKind : null;

    private static bool IsTransientImageGenerationError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)
                || message.Contains("429", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Please try again", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan ImageGenerationRetryDelay(Exception exception, int failedAttempt)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var providerDelay = TryReadProviderRetryDelay(current.Message);
            if (providerDelay is { } delay && delay > TimeSpan.Zero)
            {
                var minimumDelay = IsImagePerMinuteRateLimit(current.Message)
                    ? TimeSpan.FromSeconds(10)
                    : TimeSpan.FromMilliseconds(500);
                return delay < minimumDelay ? minimumDelay : delay;
            }
        }

        if (ContainsImagePerMinuteRateLimit(exception))
            return TimeSpan.FromSeconds(Math.Min(30, 10 * failedAttempt));

        var backoffMilliseconds = Math.Min(5000, 250 * Math.Pow(2, Math.Max(0, failedAttempt - 1)));
        return TimeSpan.FromMilliseconds(backoffMilliseconds);
    }

    private static TimeSpan? TryReadProviderRetryDelay(string message)
    {
        const string marker = "Please try again in ";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        var valueStart = markerIndex + marker.Length;
        while (valueStart < message.Length && char.IsWhiteSpace(message[valueStart]))
            valueStart++;

        var valueEnd = valueStart;
        while (valueEnd < message.Length && (char.IsDigit(message[valueEnd]) || message[valueEnd] == '.'))
            valueEnd++;

        if (valueEnd == valueStart
            || !double.TryParse(message[valueStart..valueEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = message[valueEnd..].TrimStart();
        if (unit.StartsWith("ms", StringComparison.OrdinalIgnoreCase)
            || unit.StartsWith("millisecond", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMilliseconds(value);
        }

        if (unit.StartsWith("s", StringComparison.OrdinalIgnoreCase)
            || unit.StartsWith("second", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromSeconds(value);
        }

        return null;
    }

    private static bool ContainsImagePerMinuteRateLimit(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (IsImagePerMinuteRateLimit(current.Message))
                return true;
        }

        return false;
    }

    private static bool IsImagePerMinuteRateLimit(string message) =>
        message.Contains("per min", StringComparison.OrdinalIgnoreCase)
        && (message.Contains("gpt-image", StringComparison.OrdinalIgnoreCase)
            || message.Contains("input-images", StringComparison.OrdinalIgnoreCase)
            || message.Contains("image", StringComparison.OrdinalIgnoreCase));

    private sealed class ActionProgress(Action<ImageProviderProgress> report) : IProgress<ImageProviderProgress>
    {
        public void Report(ImageProviderProgress value) => report(value);
    }
}
