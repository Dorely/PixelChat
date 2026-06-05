using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PixelChat.Art;
using PixelChat.Llm;
using PixelChat.Models;
using PixelChat.Persistence.Repositories;

namespace PixelChat.Chat;

public sealed class AssistantChatService(
    IAssistantConversationRepository conversations,
    ILlmProviderService providerService,
    IChatClientFactory chatClientFactory,
    AssistantToolRegistry toolRegistry,
    IArtWorkflowService workflow,
    IOptions<AgentOptions> agentOptions,
    ILogger<AssistantChatService> logger) : IAssistantChatService
{
    private const string InitialAssistantGreeting =
        "Tell me what kind of 2D game art you are working on. I can analyze the active or attached images, shape style direction, draft generation and edit forms, and help build reusable prompt recipes for you to review.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public async Task<AssistantConversation> GetOrCreateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var existing = await conversations.GetCurrentAsync(projectId, cancellationToken);
        if (existing is not null)
            return existing;

        var conversation = new AssistantConversation
        {
            ProjectId = projectId,
        };
        await conversations.AddConversationAsync(conversation, cancellationToken);

        var greeting = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Order = 0,
            Role = AssistantMessageRole.Assistant,
            Content = InitialAssistantGreeting,
            Status = AssistantMessageStatus.Completed,
        };
        await conversations.AddMessageAsync(greeting, cancellationToken);
        await conversations.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<IReadOnlyList<AssistantMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        await conversations.LoadMessagesAsync(conversationId, cancellationToken);

    public async Task ResetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var existing = await conversations.GetCurrentAsync(projectId, cancellationToken);
        if (existing is null)
            return;

        conversations.RemoveConversation(existing);
        await conversations.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<AssistantTurnUpdate> SendAsync(
        Guid projectId,
        string userText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
            throw new ArgumentException("Message cannot be empty.", nameof(userText));

        var conversation = await GetOrCreateAsync(projectId, cancellationToken);
        var providerAvailability = await providerService.GetDefaultChatProviderAvailabilityAsync(cancellationToken);
        if (!providerAvailability.IsAvailable || providerAvailability.Provider is null)
        {
            yield return new AssistantTurnError(providerAvailability.Message, Cancelled: false);
            yield break;
        }

        var nextOrder = await conversations.GetMaxOrderAsync(conversation.Id, cancellationToken) + 1;

        var userMessage = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Order = nextOrder++,
            Role = AssistantMessageRole.User,
            Content = userText.Trim(),
            Status = AssistantMessageStatus.Completed,
        };
        await conversations.AddMessageAsync(userMessage, cancellationToken);
        conversation.UpdatedAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(cancellationToken);
        yield return new AssistantUserMessagePersisted(userMessage.Id);

        IChatClient chat = null!;
        string? setupError = null;
        Guid? setupFailureMessageId = null;
        try
        {
            chat = await chatClientFactory.CreateChatClientAsync(providerAvailability.Provider.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant turn setup failed.");
            setupFailureMessageId = await PersistFailedAssistantAsync(conversation.Id, nextOrder, ex.Message);
            setupError = ex.Message;
        }

        if (setupError is not null)
        {
            if (setupFailureMessageId is Guid messageId)
                yield return new AssistantMessagePersisted(messageId);

            yield return new AssistantTurnError(setupError, Cancelled: false);
            yield break;
        }

        var aiTools = toolRegistry.Build(projectId);
        var chatOptions = new ChatOptions
        {
            Tools = aiTools,
            ToolMode = ChatToolMode.Auto,
        };

        var history = await conversations.LoadMessagesAsync(conversation.Id, cancellationToken);
        var messages = new List<ChatMessage> { new(ChatRole.System, AssistantPromptBuilder.Build()) };
        messages.AddRange(await BuildModelHistoryAsync(history, userMessage.Id, projectId, cancellationToken));

        var maxIterations = Math.Max(1, agentOptions.Value.MaxToolIterations);
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var activeAssistant = new AssistantMessage
            {
                ConversationId = conversation.Id,
                Order = nextOrder++,
                Role = AssistantMessageRole.Assistant,
                Content = string.Empty,
                Status = AssistantMessageStatus.Pending,
            };
            await conversations.AddMessageAsync(activeAssistant, cancellationToken);
            await conversations.SaveChangesAsync(cancellationToken);

            var textBuilder = new StringBuilder();
            var pendingCalls = new List<PendingToolCall>();
            var toolCallTracker = new StreamingToolCallTracker();
            var streamFailed = false;
            string? streamError = null;
            var cancelled = false;

            var enumerator = chat.GetStreamingResponseAsync(messages, chatOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Assistant streaming round failed.");
                        streamFailed = true;
                        streamError = ex.Message;
                        break;
                    }

                    if (!hasNext)
                        break;

                    var updatesToYield = new List<AssistantTurnUpdate>();
                    try
                    {
                        var contents = enumerator.Current?.Contents;
                        if (contents is null)
                            continue;

                        foreach (var content in contents)
                        {
                            if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                            {
                                textBuilder.Append(textContent.Text);
                                updatesToYield.Add(new AssistantTextDelta(textContent.Text));
                                continue;
                            }

                            foreach (var toolUpdate in toolCallTracker.Process(content, textBuilder.Length))
                            {
                                switch (toolUpdate)
                                {
                                    case StreamingToolCallStartedUpdate started:
                                        updatesToYield.Add(new AssistantToolCallStarted(
                                            started.CallId,
                                            started.ToolName,
                                            started.ArgumentsJson,
                                            started.ArgumentsComplete));
                                        break;

                                    case StreamingToolCallArgumentsDeltaUpdate delta:
                                        updatesToYield.Add(new AssistantToolCallArgumentsDelta(
                                            delta.CallId,
                                            delta.ArgumentsDelta,
                                            delta.ArgumentsComplete));
                                        break;

                                    case StreamingToolCallReadyUpdate ready:
                                        pendingCalls.Add(new PendingToolCall(
                                            ready.Content,
                                            ready.CallId,
                                            ready.ToolName,
                                            ready.ArgumentsJson,
                                            ready.TextOffset));
                                        break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Assistant streaming update processing failed.");
                        streamFailed = true;
                        streamError = ex.Message;
                        break;
                    }

                    foreach (var updateToYield in updatesToYield)
                        yield return updateToYield;
                }
            }
            finally
            {
                try
                {
                    await enumerator.DisposeAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Assistant streaming enumerator disposal failed.");
                    streamFailed = true;
                    streamError ??= ex.Message;
                }
            }

            if (cancelled)
            {
                activeAssistant.Content = textBuilder.ToString();
                activeAssistant.Status = AssistantMessageStatus.Cancelled;
                activeAssistant.ErrorMessage = "Cancelled by user.";
                await SafePersistAsync(activeAssistant);
                yield return new AssistantMessagePersisted(activeAssistant.Id);
                yield return new AssistantTurnError("Cancelled.", Cancelled: true);
                yield break;
            }

            if (streamFailed)
            {
                activeAssistant.Content = textBuilder.ToString();
                activeAssistant.Status = AssistantMessageStatus.Failed;
                activeAssistant.ErrorMessage = streamError;
                await SafePersistAsync(activeAssistant);
                yield return new AssistantMessagePersisted(activeAssistant.Id);
                yield return new AssistantTurnError(streamError ?? "Assistant streaming failed.", Cancelled: false);
                yield break;
            }

            if (pendingCalls.Count == 0)
            {
                activeAssistant.Content = textBuilder.ToString();
                activeAssistant.Status = AssistantMessageStatus.Completed;
                await SafePersistAsync(activeAssistant);
                conversation.UpdatedAt = DateTime.UtcNow;
                await conversations.SaveChangesAsync(CancellationToken.None);
                yield return new AssistantMessagePersisted(activeAssistant.Id);
                yield break;
            }

            var manifest = pendingCalls
                .Select(pendingCall => new PersistedToolCall(
                    pendingCall.CallId,
                    pendingCall.Name,
                    pendingCall.ArgumentsJson,
                    pendingCall.TextOffset))
                .ToList();

            activeAssistant.Content = textBuilder.ToString();
            activeAssistant.ToolCallsJson = JsonSerializer.Serialize(manifest, JsonOptions);
            activeAssistant.Status = AssistantMessageStatus.Completed;
            await SafePersistAsync(activeAssistant);
            conversation.UpdatedAt = DateTime.UtcNow;
            await conversations.SaveChangesAsync(CancellationToken.None);
            yield return new AssistantMessagePersisted(activeAssistant.Id);

            messages.Add(new ChatMessage(ChatRole.Assistant, BuildAssistantContents(textBuilder.ToString(), manifest)));

            var resultContents = new List<AIContent>();
            foreach (var pendingCall in pendingCalls)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new AssistantTurnError("Cancelled.", Cancelled: true);
                    yield break;
                }

                var outcome = await InvokeToolAsync(aiTools, pendingCall, cancellationToken);
                if (outcome.Cancelled)
                {
                    yield return new AssistantTurnError("Cancelled.", Cancelled: true);
                    yield break;
                }

                await PersistToolResultAsync(
                    conversation.Id,
                    nextOrder++,
                    pendingCall.CallId,
                    pendingCall.Name,
                    outcome.Result,
                    outcome.Error);

                resultContents.Add(new FunctionResultContent(
                    pendingCall.CallId,
                    BuildToolResultForModel(pendingCall.Name, outcome.Result, EffectiveMaxToolResultCharsForModel())));
                yield return new AssistantToolCallCompleted(
                    pendingCall.CallId,
                    pendingCall.Name,
                    outcome.Result,
                    outcome.Error,
                    outcome.DurationMs);

                if (outcome.Error is null && TryReadFormDraft(pendingCall.Name, outcome.Result, out var draft))
                    yield return new AssistantFormDraftProposed(draft);

                if (outcome.Error is null && toolRegistry.IsWorkspaceMutation(pendingCall.Name))
                    yield return new AssistantWorkspaceMutated();
            }

            messages.Add(new ChatMessage(ChatRole.Tool, resultContents));

            if (iteration == maxIterations - 1)
            {
                yield return new AssistantTurnError(
                    $"Assistant tool-call loop hit cap of {maxIterations} iterations without producing a final response.",
                    Cancelled: false);
                yield break;
            }
        }
    }

    private async Task<List<ChatMessage>> BuildModelHistoryAsync(
        IReadOnlyList<AssistantMessage> history,
        Guid currentUserMessageId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        WorkbenchView? workbench = null;
        foreach (var message in history)
        {
            if (message.Role == AssistantMessageRole.System)
                continue;

            if (message.Id == currentUserMessageId)
            {
                workbench ??= await workflow.GetWorkbenchAsync(projectId, cancellationToken);
                messages.Add(BuildCurrentUserMessage(message.Content, workbench));
                continue;
            }

            var chatMessage = ToChatMessage(message);
            if (chatMessage is not null)
                messages.Add(chatMessage);
        }

        return messages;
    }

    private static ChatMessage BuildCurrentUserMessage(string text, WorkbenchView workbench)
    {
        var contents = new List<AIContent>();
        var contextSummary = BuildVisibleContextSummary(workbench);
        contents.Add(new TextContent(string.IsNullOrWhiteSpace(contextSummary)
            ? text
            : $"{text}\n\nVisible PixelChat chat attachments:\n{contextSummary}"));

        var includedAssetIds = new HashSet<Guid>();

        foreach (var attachment in workbench.Attachments)
        {
            switch (attachment.Type)
            {
                case ChatContextAttachmentType.Asset:
                case ChatContextAttachmentType.Crop:
                    var asset = workbench.Assets.FirstOrDefault(item => item.Id == attachment.RefId);
                    if (asset is not null && includedAssetIds.Add(asset.Id))
                    {
                        contents.Add(new DataContent(asset.PreviewDataUrl, asset.ContentType)
                        {
                            Name = asset.FileName,
                        });
                    }
                    break;

                case ChatContextAttachmentType.Mask:
                    var mask = workbench.Masks.FirstOrDefault(item => item.Id == attachment.RefId);
                    if (mask is not null)
                    {
                        contents.Add(new DataContent(mask.PreviewDataUrl, mask.ContentType)
                        {
                            Name = $"{mask.Label}.png",
                        });
                    }
                    break;
            }
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    private static string BuildVisibleContextSummary(WorkbenchView workbench)
    {
        if (workbench.Attachments.Count == 0)
            return string.Empty;

        var lines = new List<string>();
        foreach (var attachment in workbench.Attachments)
        {
            lines.Add($"- {attachment.Type}: {attachment.Label} ({attachment.RefId})");
        }

        return string.Join('\n', lines);
    }

    private static ChatMessage? ToChatMessage(AssistantMessage message) => message.Role switch
    {
        AssistantMessageRole.User => new ChatMessage(ChatRole.User, message.Content),
        AssistantMessageRole.Assistant => BuildAssistantReplay(message),
        AssistantMessageRole.Tool => new ChatMessage(ChatRole.Tool, [new FunctionResultContent(message.ToolCallId ?? string.Empty, message.Content)]),
        _ => null
    };

    private static ChatMessage BuildAssistantReplay(AssistantMessage message)
    {
        var calls = ReadPersistedToolCalls(message.ToolCallsJson);
        var contents = calls.Count == 0
            ? BuildTextOnlyAssistantContents(message.Content)
            : BuildAssistantContents(message.Content, calls);

        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private static List<AIContent> BuildAssistantContents(string text, IReadOnlyList<PersistedToolCall> calls)
    {
        if (calls.Count == 0)
            return BuildTextOnlyAssistantContents(text);

        if (calls.Any(call => call.TextOffset is null))
        {
            var fallbackContents = BuildTextOnlyAssistantContents(text);
            foreach (var call in calls)
                fallbackContents.Add(ToFunctionCallContent(call));
            return fallbackContents;
        }

        var contents = new List<AIContent>();
        var cursor = 0;
        foreach (var item in calls
            .Select((call, index) => new { Call = call, Index = index })
            .OrderBy(item => item.Call.TextOffset!.Value)
            .ThenBy(item => item.Index))
        {
            var offset = Math.Clamp(item.Call.TextOffset!.Value, 0, text.Length);
            if (offset > cursor)
            {
                contents.Add(new TextContent(text[cursor..offset]));
                cursor = offset;
            }
            contents.Add(ToFunctionCallContent(item.Call));
        }

        if (cursor < text.Length)
            contents.Add(new TextContent(text[cursor..]));

        if (contents.Count == 0)
            contents.Add(new TextContent(string.Empty));
        return contents;
    }

    private static List<AIContent> BuildTextOnlyAssistantContents(string text)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(text))
            contents.Add(new TextContent(text));
        if (contents.Count == 0)
            contents.Add(new TextContent(string.Empty));
        return contents;
    }

    private static FunctionCallContent ToFunctionCallContent(PersistedToolCall call)
    {
        var args = ToolCallArguments.ParseObjectOrNull(call.ArgumentsJson);
        return new FunctionCallContent(call.CallId, call.Name, args);
    }

    private async Task<ToolInvocationOutcome> InvokeToolAsync(
        IList<AITool> aiTools,
        PendingToolCall pendingCall,
        CancellationToken cancellationToken)
    {
        var aiFunction = aiTools.OfType<AIFunction>().FirstOrDefault(function => function.Name == pendingCall.Name)
            ?? throw new InvalidOperationException($"Unknown tool '{pendingCall.Name}'.");
        return await InvokeToolAsync(aiFunction, pendingCall, cancellationToken);
    }

    private async Task<ToolInvocationOutcome> InvokeToolAsync(
        AIFunction aiFunction,
        PendingToolCall pendingCall,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var invokeResult = await aiFunction.InvokeAsync(
                ToolCallArguments.Create(pendingCall.Content.Arguments, pendingCall.ArgumentsJson),
                cancellationToken);
            stopwatch.Stop();
            return new ToolInvocationOutcome(invokeResult?.ToString() ?? string.Empty, Error: null, Cancelled: false, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ToolInvocationOutcome(string.Empty, Error: null, Cancelled: true, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Assistant tool '{Tool}' failed.", pendingCall.Name);
            return new ToolInvocationOutcome($"Error: {ex.Message}", ex.Message, Cancelled: false, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task PersistToolResultAsync(
        Guid conversationId,
        int order,
        string callId,
        string toolName,
        string result,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var toolMessage = new AssistantMessage
        {
            ConversationId = conversationId,
            Order = order,
            Role = AssistantMessageRole.Tool,
            Content = result,
            ToolCallId = callId,
            ToolName = toolName,
            Status = error is null ? AssistantMessageStatus.Completed : AssistantMessageStatus.Failed,
            ErrorMessage = error,
        };
        await conversations.AddMessageAsync(toolMessage, cancellationToken);
        await conversations.SaveChangesAsync(cancellationToken);
    }

    private static List<PersistedToolCall> ReadPersistedToolCalls(string toolCallsJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallsJson) || toolCallsJson == "[]")
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PersistedToolCall>>(toolCallsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryReadFormDraft(string toolName, string result, out AssistantFormDraft draft)
    {
        draft = null!;
        if (toolName is not ("draft_generate_form" or "draft_edit_form" or "draft_prompt_recipe_form")
            || string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AssistantFormDraft>(result, JsonOptions);
            if (parsed is null)
                return false;

            draft = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private int EffectiveMaxToolResultCharsForModel() =>
        Math.Max(1000, agentOptions.Value.MaxToolResultCharsForModel);

    private static string BuildToolResultForModel(string toolName, string result, int maxToolResultCharsForModel)
    {
        if (result.Length <= maxToolResultCharsForModel)
            return result;

        return result[..maxToolResultCharsForModel]
            + $"\n\n[Tool result for {toolName} truncated before returning it to the model.]";
    }

    private async Task<Guid?> PersistFailedAssistantAsync(Guid conversationId, int order, string error)
    {
        try
        {
            var message = new AssistantMessage
            {
                ConversationId = conversationId,
                Order = order,
                Role = AssistantMessageRole.Assistant,
                Status = AssistantMessageStatus.Failed,
                ErrorMessage = error,
            };
            await conversations.AddMessageAsync(message, CancellationToken.None);
            await conversations.SaveChangesAsync(CancellationToken.None);
            return message.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist assistant setup failure.");
            return null;
        }
    }

    private async Task SafePersistAsync(AssistantMessage message)
    {
        try
        {
            conversations.UpdateMessage(message);
            await conversations.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist assistant message {MessageId}.", message.Id);
        }
    }

    private sealed record PendingToolCall(
        FunctionCallContent Content,
        string CallId,
        string Name,
        string ArgumentsJson,
        int TextOffset);

    private sealed record ToolInvocationOutcome(
        string Result,
        string? Error,
        bool Cancelled,
        double DurationMs);
}
