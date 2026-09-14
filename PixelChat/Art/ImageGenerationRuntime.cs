using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;
using Microsoft.Extensions.Options;

namespace PixelChat.Art;

public sealed class ImageGenerationRuntime(
    IServiceScopeFactory scopeFactory,
    IOptions<ImageGenerationOptions> imageOptions,
    ILogger<ImageGenerationRuntime> logger,
    IHostApplicationLifetime lifetime) : IImageGenerationRuntime
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, ImageGenerationBatchRuntimeView> _batches = [];
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _batchCompletions = [];
    private readonly HashSet<Guid> _reservedProjectStarts = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = [];

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

        _ = Task.Run(() => RunGenerationBatchAsync(projectId, batch.Id, Enumerable.Range(0, batch.Count).ToArray(), RuntimeBatchKind.Generate));
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

        _ = Task.Run(() => RunGenerationBatchAsync(projectId, batch.Id, Enumerable.Range(0, batch.Count).ToArray(), RuntimeBatchKind.Edit));
        return batch;
    }

    public async Task<bool> WaitForBatchCompletionAsync(
        Guid batchId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await WaitForCompletionAsync(batchId, timeout, cancellationToken);
    }

    public async Task<GenerationBatchView> StartSpriteGenerationAsync(Guid projectId, PixelChat.Sprites.SpriteGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ReserveProjectStart(projectId);
        GenerationBatchView batch;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            batch = await scope.ServiceProvider.GetRequiredService<IArtWorkflowService>().StartSpriteGenerationAsync(projectId, request, cancellationToken);
            RegisterStartedBatch(projectId, batch, followInBatches: false);
        }
        catch { ReleaseProjectStart(projectId); throw; }
        NotifyStateChanged();
        var kind = request.Kind == "reference" ? RuntimeBatchKind.Generate : RuntimeBatchKind.Edit;
        _ = Task.Run(() => RunGenerationBatchAsync(projectId, batch.Id, Enumerable.Range(0, batch.Count).ToArray(), kind));
        return batch;
    }

    private async Task<bool> WaitForCompletionAsync(
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
            foreach (var oldBatch in _batches.Values.Where(b => b.ProjectId == projectId && !b.IsRunning).ToList())
                _batches.Remove(oldBatch.BatchId);
            _batches[batch.Id] = runtimeBatch;
            _batchCompletions[batch.Id] = completion;
            _cancellations[batch.Id] = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        }
    }

    public async Task StopAsync(Guid projectId, Guid batchId, CancellationToken cancellationToken = default)
    {
        Task? completion = null;
        lock (_lock)
        {
            if (_batches.TryGetValue(batchId, out var batch) && batch.ProjectId == projectId && _cancellations.TryGetValue(batchId, out var source))
            {
                source.Cancel();
                completion = _batchCompletions.GetValueOrDefault(batchId)?.Task;
            }
        }
        if (completion is not null) await completion.WaitAsync(cancellationToken);
    }

    public async Task ResumeAsync(Guid projectId, Guid batchId, bool retryFailed = false, CancellationToken cancellationToken = default)
    {
        ReserveProjectStart(projectId);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.GenerationBatches.AsSplitQuery().FirstOrDefaultAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException("Batch was not found.");
            var targets = batch.Outputs.Where(o => retryFailed ? o.Status == GenerationOutputStatus.Failed
                : o.Status is GenerationOutputStatus.Queued or GenerationOutputStatus.Cancelled).OrderBy(o => o.OutputIndex).ToList();
            if (targets.Count == 0) throw new InvalidOperationException(retryFailed ? "No failed outputs to retry." : "No stopped outputs to resume. Use Retry failed for errors.");
            foreach (var target in targets)
                GenerationQueueState.Apply(target, GenerationQueueState.Read(target) with { Status = GenerationOutputStatus.Queued,
                    Message = "Waiting for an available worker.", Error = string.Empty, ErrorKind = null, CompletedAt = null, UpdatedAt = DateTime.UtcNow });
            batch.Status = GenerationBatchStatus.Running;
            batch.Error = string.Empty;
            batch.ReviewCompletedBy = null;
            batch.ReviewCompletedAt = null;
            var project = await db.Projects.SingleAsync(p => p.Id == projectId, cancellationToken);
            project.ActiveBatchId = batchId;
            if (string.IsNullOrEmpty(batch.SpriteTargetJson)) project.ActiveWorkspaceMode = WorkspaceMode.Batches;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            var workbench = await scope.ServiceProvider.GetRequiredService<IArtWorkflowService>().GetWorkbenchAsync(projectId, cancellationToken);
            RegisterStartedBatch(projectId, workbench.Batches.Single(b => b.Id == batchId), string.IsNullOrEmpty(batch.SpriteTargetJson));
            var kind = batch.EditSourceData is { Length: > 0 } ? RuntimeBatchKind.Edit : RuntimeBatchKind.Generate;
            _ = Task.Run(() => RunGenerationBatchAsync(projectId, batchId, targets.Select(o => o.OutputIndex).ToArray(), kind));
            NotifyStateChanged();
        }
        catch { ReleaseProjectStart(projectId); throw; }
    }

    private async Task RunGenerationBatchAsync(Guid projectId, Guid batchId, IReadOnlyList<int> indexes, RuntimeBatchKind kind)
    {
        CancellationToken token;
        lock (_lock) token = _cancellations[batchId].Token;
        var next = -1;
        var workers = Enumerable.Range(0, Math.Min(indexes.Count, Math.Max(1, imageOptions.Value.MaxParallelRequests)))
            .Select(async _ =>
            {
                while (!token.IsCancellationRequested)
                {
                    var position = Interlocked.Increment(ref next);
                    if (position >= indexes.Count) break;
                    var outputIndex = indexes[position];
                    try
                    {
                        var error = await GenerateBatchOutputWithRetriesAsync(projectId, batchId, outputIndex, kind, token);
                        if (error is not null) await PersistGenerationFailureAsync(projectId, batchId, outputIndex, error);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        await PersistOutputStateAsync(projectId, batchId, new(outputIndex, GenerationOutputStatus.Cancelled,
                            Message: "Stopped. Resume manually.", Error: "Stopped by user or application shutdown. Resume when ready.", ErrorKind: "cancelled", UpdatedAt: DateTime.UtcNow));
                        int attempt;
                        lock (_lock) attempt = _batches[batchId].Outputs.FirstOrDefault(o => o.OutputIndex == outputIndex)?.Attempt ?? 0;
                        UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Cancelled, attempt, "Stopped. Resume manually.", errorKind: "cancelled");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Could not finish image output {BatchId}/{OutputIndex}", batchId, outputIndex);
                        await PersistGenerationFailureAsync(projectId, batchId, outputIndex, ex);
                    }
                }
            }).ToArray();
        try { await Task.WhenAll(workers); }
        catch (Exception ex) { logger.LogError(ex, "Batch workers stopped unexpectedly: {BatchId}", batchId); }
        finally
        {
            await CompleteBatchAsync(projectId, batchId);
            TaskCompletionSource<bool>? completion;
            lock (_lock)
            {
                if (_cancellations.Remove(batchId, out var source)) source.Dispose();
                if (_batches.TryGetValue(batchId, out var batch)) _batches[batchId] = batch with { IsRunning = false };
                _batchCompletions.Remove(batchId, out completion);
            }
            completion?.TrySetResult(true);
            NotifyStateChanged();
        }
    }

    private async Task<Exception?> GenerateBatchOutputWithRetriesAsync(Guid projectId, Guid batchId, int outputIndex, RuntimeBatchKind kind, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(imageOptions.Value.MaxRequestAttempts, 1, 10);
        Exception? finalError = null;
        int previousAttempts;
        lock (_lock) previousAttempts = _batches[batchId].Outputs.FirstOrDefault(o => o.OutputIndex == outputIndex)?.Attempt ?? 0;

        for (var currentAttempt = 1; currentAttempt <= maxAttempts; currentAttempt++)
        {
            var attempt = previousAttempts + currentAttempt;
            try
            {
                await PersistOutputStateAsync(projectId, batchId, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Running,
                    attempt,
                    $"Starting image request attempt {attempt}.",
                    StartedAt: DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow));
                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Running, attempt, $"Starting image request attempt {attempt}.");

                var progress = new ActionProgress(update => HandleProviderProgress(projectId, batchId, outputIndex, attempt, update));
                await using var scope = scopeFactory.CreateAsyncScope();
                var workflow = scope.ServiceProvider.GetRequiredService<IArtWorkflowService>();
                if (kind == RuntimeBatchKind.Edit)
                    await workflow.GenerateEditBatchOutputAsync(projectId, batchId, outputIndex, cancellationToken, progress);
                else
                    await workflow.GenerateBatchOutputAsync(projectId, batchId, outputIndex, cancellationToken, progress);

                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Succeeded, attempt, "Image saved.", partialImageDataUrl: null);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                finalError = ex;
                if (currentAttempt >= maxAttempts || !IsTransientImageGenerationError(ex))
                    break;

                var delay = ImageGenerationRetryDelay(ex, attempt);
                var message = $"Retrying after provider error, attempt {attempt + 1}.";
                await PersistOutputStateAsync(projectId, batchId, new GenerationOutputStateView(
                    outputIndex,
                    GenerationOutputStatus.Running,
                    attempt,
                    message,
                    ex.Message,
                    ErrorKind: TryReadErrorKind(ex),
                    UpdatedAt: DateTime.UtcNow));
                UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Running, attempt, message, error: ex.Message, errorKind: TryReadErrorKind(ex));
                await Task.Delay(delay, cancellationToken);
            }
        }

        return finalError;
    }

    private void HandleProviderProgress(Guid projectId, Guid batchId, int outputIndex, int attempt, ImageProviderProgress update)
    {
        lock (_lock)
        {
            var current = _batches.GetValueOrDefault(batchId)?.Outputs.FirstOrDefault(o => o.OutputIndex == outputIndex);
            if (current is null || current.Attempt > attempt || current.Status is GenerationOutputStatus.Succeeded or GenerationOutputStatus.Cancelled or GenerationOutputStatus.Deleted)
                return;
        }
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

        // Live progress is serialized under the runtime lock. Only awaited lifecycle
        // transitions are persisted, so delayed callbacks cannot overwrite saved success.
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
        int attempt;
        lock (_lock) attempt = _batches.GetValueOrDefault(batchId)?.Outputs.FirstOrDefault(o => o.OutputIndex == outputIndex)?.Attempt ?? 0;
        UpdateRuntimeOutput(batchId, outputIndex, GenerationOutputStatus.Failed, attempt, "Image request failed.",
            error: outputError.Error, errorKind: outputError.ErrorKind, requestId: outputError.RequestId,
            responseId: outputError.ResponseId, callId: outputError.CallId, lastEventType: outputError.LastEventType, eventCount: outputError.EventCount);
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
                    status is GenerationOutputStatus.Succeeded or GenerationOutputStatus.Failed or GenerationOutputStatus.Cancelled ? null
                        : partialImageDataUrl ?? batch.Outputs.FirstOrDefault(output => output.OutputIndex == outputIndex)?.PartialImageDataUrl))
                .OrderBy(output => output.OutputIndex)
                .ToList();
            _batches[batchId] = batch with { Outputs = outputs };
        }

        NotifyStateChanged();
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
        if (exception is ImageProviderException provider)
        {
            if (provider.ErrorKind is "account_access" or "account_quota" or "invalid_request" or "image_model_invalid" || provider.StatusCode is 400 or 401 or 403) return false;
            if (provider.StatusCode is 408 or 429 or >= 500 || provider.ErrorKind == "timeout") return true;
        }
        if (exception is HttpRequestException) return true;
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
        if (exception is ImageProviderException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero)
            return retryAfter;
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
