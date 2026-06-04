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
                _error);
        }
    }

    public async Task StartTurnAsync(string userText, CancellationToken cancellationToken = default)
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
            _ = Task.Run(() => RunTurnAsync(text, turnCts, persisted), CancellationToken.None);
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

    public async Task ResetConversationAsync(CancellationToken cancellationToken = default)
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
        await chat.ResetAsync(cancellationToken);
        NotifyStateChanged();
    }

    public void ClearError()
    {
        lock (_gate)
            _error = null;

        NotifyStateChanged();
    }

    private async Task RunTurnAsync(
        string text,
        CancellationTokenSource turnCts,
        TaskCompletionSource persisted)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
            await foreach (var update in chat.SendAsync(text, turnCts.Token))
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
                case AssistantTurnError error:
                    _error = error.Message;
                    break;
            }
        }
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
