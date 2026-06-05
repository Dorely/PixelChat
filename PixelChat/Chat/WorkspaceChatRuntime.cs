using PixelChat.Components.Chat;

namespace PixelChat.Chat;

public sealed class WorkspaceChatRuntime(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceChatRuntime> logger) : IWorkspaceChatRuntime
{
    private readonly object _gate = new();
    private ChatLiveTurn? _live;
    private CancellationTokenSource? _turnCts;
    private string? _pendingUserText;
    private string? _error;
    private bool _running;
    private int _workspaceVersion;

    public event Action? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public WorkspaceChatRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new WorkspaceChatRuntimeSnapshot(
                _running,
                _live?.Clone(),
                _pendingUserText,
                _error,
                _workspaceVersion);
        }
    }

    public async Task StartTurnAsync(Guid projectId, string userText, CancellationToken cancellationToken = default)
    {
        var text = userText.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Message cannot be empty.", nameof(userText));

        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource turnCts;
        lock (_gate)
        {
            if (_running)
                return;

            _running = true;
            _live = new ChatLiveTurn();
            _pendingUserText = text;
            _error = null;
            _turnCts = new CancellationTokenSource();
            turnCts = _turnCts;
            _ = Task.Run(() => RunTurnAsync(projectId, text, turnCts, persisted), CancellationToken.None);
        }
        NotifyStateChanged();

        using var registration = cancellationToken.Register(() => persisted.TrySetCanceled(cancellationToken));
        await persisted.Task;
    }

    public Task StopTurnAsync()
    {
        CancellationTokenSource? cts;
        lock (_gate)
            cts = _turnCts;

        cts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task ResetConversationAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running)
                return;

            _live = null;
            _pendingUserText = null;
            _error = null;
        }

        using var scope = scopeFactory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
        await chat.ResetAsync(projectId, cancellationToken);
        BumpWorkspaceVersion();
        NotifyStateChanged();
    }

    public async Task<AssistantToolExecutionResult> ConfirmToolCallAsync(
        Guid projectId,
        Guid assistantMessageId,
        string callId,
        CancellationToken cancellationToken = default)
    {
        if (!TryStartBlockingOperation())
            throw new InvalidOperationException("A chat turn is already running.");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
            var result = await chat.ConfirmToolCallAsync(projectId, assistantMessageId, callId, cancellationToken);
            if (result.Status == PersistedToolCallStatus.Completed)
                BumpWorkspaceVersion();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetError(ex.Message);
            throw;
        }
        finally
        {
            FinishBlockingOperation();
        }
    }

    public async Task<AssistantToolExecutionResult> RejectToolCallAsync(
        Guid projectId,
        Guid assistantMessageId,
        string callId,
        CancellationToken cancellationToken = default)
    {
        if (!TryStartBlockingOperation())
            throw new InvalidOperationException("A chat turn is already running.");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
            return await chat.RejectToolCallAsync(projectId, assistantMessageId, callId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetError(ex.Message);
            throw;
        }
        finally
        {
            FinishBlockingOperation();
        }
    }

    public void ClearError()
    {
        lock (_gate)
            _error = null;

        NotifyStateChanged();
    }

    private async Task RunTurnAsync(
        Guid projectId,
        string text,
        CancellationTokenSource turnCts,
        TaskCompletionSource persisted)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
            await foreach (var update in chat.SendAsync(projectId, text, turnCts.Token))
            {
                if (update is AssistantUserMessagePersisted)
                {
                    persisted.TrySetResult();
                    continue;
                }

                ApplyUpdate(update);
                NotifyStateChanged();
            }

            persisted.TrySetResult();
        }
        catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
        {
            persisted.TrySetResult();
            SetError("Cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace chat turn failed.");
            persisted.TrySetException(ex);
            SetError(ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_turnCts, turnCts))
                {
                    _running = false;
                    _live = null;
                    _pendingUserText = null;
                    _turnCts = null;
                }
            }

            turnCts.Dispose();
            NotifyStateChanged();
        }
    }

    private void ApplyUpdate(AssistantTurnUpdate update)
    {
        lock (_gate)
        {
            switch (update)
            {
                case AssistantTextDelta delta:
                    _live?.AppendText(delta.Text);
                    break;

                case AssistantToolCallStarted started:
                    _live?.StartToolCall(started.CallId, started.ToolName, started.ArgumentsJson, started.ArgumentsComplete);
                    break;

                case AssistantToolCallArgumentsDelta delta:
                    _live?.AppendToolCallArguments(delta.CallId, delta.ArgumentsDelta, delta.ArgumentsComplete);
                    break;

                case AssistantToolCallPendingConfirmation pending:
                    _live?.MarkToolCallPendingConfirmation(
                        pending.AssistantMessageId,
                        pending.CallId,
                        pending.ToolName,
                        pending.ArgumentsJson);
                    break;

                case AssistantToolCallCompleted completed:
                    _live?.CompleteToolCall(
                        completed.CallId,
                        completed.Result,
                        completed.Error,
                        completed.DurationMs);
                    break;

                case AssistantWorkspaceMutated:
                    _workspaceVersion++;
                    break;

                case AssistantTurnError error:
                    _error = error.Message;
                    break;
            }
        }
    }

    private bool TryStartBlockingOperation()
    {
        lock (_gate)
        {
            if (_running)
                return false;

            _running = true;
            _live = null;
            _pendingUserText = null;
            _error = null;
            return true;
        }
    }

    private void FinishBlockingOperation()
    {
        lock (_gate)
            _running = false;

        NotifyStateChanged();
    }

    private void BumpWorkspaceVersion()
    {
        lock (_gate)
            _workspaceVersion++;
    }

    private void SetError(string message)
    {
        lock (_gate)
            _error = message;

        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        if (StateChanged is null)
            return;

        foreach (Action handler in StateChanged.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Workspace chat state subscriber failed.");
            }
        }
    }
}
