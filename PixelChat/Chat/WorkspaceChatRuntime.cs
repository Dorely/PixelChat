using PixelChat.Components.Chat;
using PixelChat.Tokens;

namespace PixelChat.Chat;

public sealed class WorkspaceChatRuntime(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceChatRuntime> logger) : IWorkspaceChatRuntime
{
    private readonly object _gate = new();
    private ChatLiveTurn? _live;
    private CancellationTokenSource? _turnCts;
    private string? _pendingUserText;
    private TokenContextEstimate? _tokenCount;
    private string? _error;
    private bool _running;

    public event Action? StateChanged;
    public event Action? WorkspaceChanged;
    public event Action<AssistantFormDraft>? FormDraftProposed;
    public event Action<WorkspaceChatTurnFinished>? TurnFinished;

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
                _tokenCount,
                _error);
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
            _tokenCount = null;
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
            _tokenCount = null;
            _error = null;
        }

        using var scope = scopeFactory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
        await chat.ResetAsync(projectId, cancellationToken);
        NotifyStateChanged();
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
        var assistantMessageIds = new List<Guid>();
        Guid? userMessageId = null;
        var finalStatus = ChatMessageStatus.Completed;
        string? finalError = null;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IAssistantChatService>();
            await foreach (var update in chat.SendAsync(projectId, text, turnCts.Token))
            {
                if (update is AssistantUserMessagePersisted userPersisted)
                {
                    userMessageId = userPersisted.MessageId;
                    persisted.TrySetResult();
                    continue;
                }

                if (update is AssistantMessagePersisted assistantPersisted)
                {
                    assistantMessageIds.Add(assistantPersisted.MessageId);
                    continue;
                }

                if (update is AssistantTurnError turnError)
                {
                    finalStatus = turnError.Cancelled ? ChatMessageStatus.Cancelled : ChatMessageStatus.Failed;
                    finalError = turnError.Message;
                }

                ApplyUpdate(update);
                NotifyStateChanged();
            }

            persisted.TrySetResult();
        }
        catch (OperationCanceledException) when (turnCts.IsCancellationRequested)
        {
            finalStatus = ChatMessageStatus.Cancelled;
            finalError = "Cancelled.";
            persisted.TrySetResult();
            SetError("Cancelled.");
        }
        catch (Exception ex)
        {
            finalStatus = ChatMessageStatus.Failed;
            finalError = ex.Message;
            logger.LogError(ex, "Workspace chat turn failed.");
            persisted.TrySetException(ex);
            SetError(ex.Message);
        }
        finally
        {
            WorkspaceChatTurnFinished? finished = null;
            lock (_gate)
            {
                if (ReferenceEquals(_turnCts, turnCts))
                {
                    finished = new WorkspaceChatTurnFinished(
                        projectId,
                        userMessageId,
                        _live?.Clone() ?? new ChatLiveTurn(),
                        assistantMessageIds.ToList(),
                        finalStatus,
                        finalError);
                }
            }

            if (finished is not null)
                NotifyTurnFinished(finished);

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
        var notifyWorkspaceChanged = false;
        AssistantFormDraft? formDraft = null;
        lock (_gate)
        {
            switch (update)
            {
                case AssistantTextDelta delta:
                    _live?.AppendText(delta.Text);
                    break;

                case AssistantToolCallStarted started:
                    _live?.StartToolCall(
                        started.CallId,
                        started.ToolName,
                        started.ArgumentsJson,
                        started.ArgumentsComplete);
                    break;

                case AssistantToolCallArgumentsDelta delta:
                    _live?.AppendToolArguments(
                        delta.CallId,
                        delta.ArgumentsDelta,
                        delta.ArgumentsComplete);
                    break;

                case AssistantToolCallCompleted completed:
                    _live?.CompleteToolCall(
                        completed.CallId,
                        completed.Result,
                        completed.Error,
                        completed.DurationMs);
                    break;

                case AssistantFormDraftProposed draft:
                    formDraft = draft.Draft;
                    break;

                case AssistantWorkspaceMutated:
                    notifyWorkspaceChanged = true;
                    break;

                case AssistantTokenCountUpdated tokenCount:
                    _tokenCount = tokenCount.Estimate;
                    break;

                case AssistantTurnError error:
                    _error = error.Message;
                    break;
            }
        }

        if (formDraft is not null)
            NotifyFormDraftProposed(formDraft);

        if (notifyWorkspaceChanged)
            NotifyWorkspaceChanged();
    }

    private void SetError(string message)
    {
        lock (_gate)
            _error = message;

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => Notify(StateChanged, "Workspace chat state subscriber failed.");

    private void NotifyWorkspaceChanged() => Notify(WorkspaceChanged, "Workspace chat workspace subscriber failed.");

    private void NotifyTurnFinished(WorkspaceChatTurnFinished turn)
    {
        if (TurnFinished is null)
            return;

        foreach (Action<WorkspaceChatTurnFinished> handler in TurnFinished.GetInvocationList())
        {
            try
            {
                handler(turn);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Workspace chat turn-finished subscriber failed.");
            }
        }
    }

    private void NotifyFormDraftProposed(AssistantFormDraft draft)
    {
        if (FormDraftProposed is null)
            return;

        foreach (Action<AssistantFormDraft> handler in FormDraftProposed.GetInvocationList())
        {
            try
            {
                handler(draft);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Workspace chat form draft subscriber failed.");
            }
        }
    }

    private void Notify(Action? handlers, string failureMessage)
    {
        if (handlers is null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, failureMessage);
            }
        }
    }
}
