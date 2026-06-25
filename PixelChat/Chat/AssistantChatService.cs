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
using PixelChat.Tokens;

namespace PixelChat.Chat;

public sealed class AssistantChatService(
    IAssistantConversationRepository conversations,
    ILlmProviderService providerService,
    IChatClientFactory chatClientFactory,
    AssistantToolRegistry toolRegistry,
    IArtWorkflowService workflow,
    IChatTokenEstimator tokenEstimator,
    IOptions<AgentOptions> agentOptions,
    ILogger<AssistantChatService> logger) : IAssistantChatService
{
    private const string InitialAssistantGreeting =
        "Tell me what kind of 2D game art you are working on. I can analyze the active or attached images, shape style direction, draft generation and edit forms, and help build reusable prompt recipes for you to review.";
    private const string InterruptedToolResult = "Error: PixelChat was interrupted before this tool call produced a saved result.";
    private const string InterruptedToolError = "PixelChat was interrupted before this tool call produced a saved result.";
    private const string CancelledToolResult = "Error: PixelChat was cancelled before this tool call produced a saved result.";
    private const string CancelledToolError = "PixelChat was cancelled before this tool call produced a saved result.";

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

    public async Task RecoverInterruptedToolCallsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetCurrentAsync(projectId, cancellationToken);
        if (conversation is null)
            return;

        await RecoverInterruptedToolCallsAsync(conversation, cancellationToken);
    }

    public async Task<TokenContextEstimate?> EstimateNextRequestTokensAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var providerAvailability = await providerService.GetDefaultChatProviderAvailabilityAsync(cancellationToken);
        if (!providerAvailability.IsAvailable || providerAvailability.Provider is null)
            return null;

        var conversation = await GetOrCreateAsync(projectId, cancellationToken);
        var history = await conversations.LoadMessagesAsync(conversation.Id, cancellationToken);
        return tokenEstimator.Count(await BuildIdleModelMessagesAsync(history, projectId, cancellationToken), providerAvailability.Provider.ModelId);
    }

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
        await RecoverInterruptedToolCallsAsync(conversation, cancellationToken);

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

        var generationBudget = new AssistantTurnGenerationBudget(agentOptions.Value.MaxGenerationRoundsPerTurn);
        var aiTools = toolRegistry.Build(projectId, generationBudget);
        var chatOptions = new ChatOptions
        {
            Tools = aiTools,
            ToolMode = ChatToolMode.Auto,
        };

        var history = await conversations.LoadMessagesAsync(conversation.Id, cancellationToken);
        var messages = new List<ChatMessage> { new(ChatRole.System, AssistantPromptBuilder.Build()) };
        messages.AddRange(await BuildModelHistoryAsync(history, userMessage.Id, projectId, cancellationToken));
        var modelName = providerAvailability.Provider.ModelId;
        yield return BuildTokenCountUpdate(messages, modelName);

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
                    var tokenContextChanged = false;
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
                                tokenContextChanged = true;
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
                                        tokenContextChanged = true;
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

                    if (tokenContextChanged)
                        yield return BuildTokenCountUpdate(BuildAssistantPreviewMessages(messages, textBuilder.ToString(), pendingCalls), modelName);
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
                yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
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
                yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
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
                yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
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
            yield return BuildTokenCountUpdate(messages, modelName);

            var resultContents = new List<AIContent>();
            var modelOnlyContents = new List<AIContent>();
            var completedCallIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pendingCall in pendingCalls)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    nextOrder = await PersistInterruptedToolResultsAsync(
                        conversation.Id,
                        nextOrder,
                        pendingCalls,
                        completedCallIds,
                        CancelledToolResult,
                        CancelledToolError,
                        CancellationToken.None);
                    yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
                    yield return new AssistantTurnError("Cancelled.", Cancelled: true);
                    yield break;
                }

                var outcome = await InvokeToolAsync(aiTools, pendingCall, projectId, cancellationToken);
                if (outcome.Cancelled)
                {
                    nextOrder = await PersistInterruptedToolResultsAsync(
                        conversation.Id,
                        nextOrder,
                        pendingCalls,
                        completedCallIds,
                        CancelledToolResult,
                        CancelledToolError,
                        CancellationToken.None);
                    yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
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
                completedCallIds.Add(pendingCall.CallId);

                resultContents.Add(new FunctionResultContent(
                    pendingCall.CallId,
                    BuildToolResultForModel(pendingCall.Name, outcome.Result, EffectiveMaxToolResultCharsForModel())));
                modelOnlyContents.AddRange(outcome.ModelOnlyContents);
                yield return new AssistantToolCallCompleted(
                    pendingCall.CallId,
                    pendingCall.Name,
                    outcome.Result,
                    outcome.Error,
                    outcome.DurationMs);
                yield return BuildTokenCountUpdate(BuildToolPreviewMessages(messages, resultContents, modelOnlyContents), modelName);

                if (outcome.Error is null && TryReadFormDraft(pendingCall.Name, outcome.Result, out var draft))
                    yield return new AssistantFormDraftProposed(draft);

                if (outcome.Error is null && toolRegistry.IsWorkspaceMutation(pendingCall.Name))
                    yield return new AssistantWorkspaceMutated();
            }

            messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
            if (modelOnlyContents.Count > 0)
                messages.Add(new ChatMessage(ChatRole.User, modelOnlyContents));

            if (iteration == maxIterations - 1)
            {
                yield return await BuildIdleTokenCountUpdateAsync(conversation.Id, projectId, modelName, CancellationToken.None);
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
                messages.Add(await BuildCurrentUserMessageAsync(message.Content, workbench, projectId, cancellationToken));
                continue;
            }

            var chatMessage = ToChatMessage(message);
            if (chatMessage is not null)
                messages.Add(chatMessage);
        }

        return messages;
    }

    private async Task<AssistantTokenCountUpdated> BuildIdleTokenCountUpdateAsync(
        Guid conversationId,
        Guid projectId,
        string modelName,
        CancellationToken cancellationToken)
    {
        var history = await conversations.LoadMessagesAsync(conversationId, cancellationToken);
        return BuildTokenCountUpdate(await BuildIdleModelMessagesAsync(history, projectId, cancellationToken), modelName);
    }

    private AssistantTokenCountUpdated BuildTokenCountUpdate(IReadOnlyList<ChatMessage> messages, string modelName) =>
        new(tokenEstimator.Count(messages, modelName));

    private async Task RecoverInterruptedToolCallsAsync(
        AssistantConversation conversation,
        CancellationToken cancellationToken)
    {
        var history = await conversations.LoadMessagesAsync(conversation.Id, cancellationToken);
        var syntheticMessages = BuildInterruptedToolResultMessages(conversation.Id, history);
        if (syntheticMessages.Count == 0)
            return;

        var syntheticMessageIds = syntheticMessages
            .Select(message => message.Id)
            .ToHashSet();
        var messages = history
            .Concat(syntheticMessages)
            .ToList();
        RebuildConversationOrder(messages);

        foreach (var message in syntheticMessages)
            await conversations.AddMessageAsync(message, cancellationToken);

        foreach (var message in messages.Where(message => !syntheticMessageIds.Contains(message.Id)))
            conversations.UpdateMessage(message);

        conversation.UpdatedAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Recovered {ToolCallCount} interrupted assistant tool call(s) in conversation {ConversationId}.",
            syntheticMessages.Count,
            conversation.Id);
    }

    private static List<AssistantMessage> BuildInterruptedToolResultMessages(
        Guid conversationId,
        IReadOnlyList<AssistantMessage> history)
    {
        var existingToolCallIds = history
            .Where(message => message.Role == AssistantMessageRole.Tool && message.ToolCallId is not null)
            .Select(message => message.ToolCallId!)
            .ToHashSet(StringComparer.Ordinal);

        var syntheticMessages = new List<AssistantMessage>();
        foreach (var assistantMessage in history
            .Where(message => message.Role == AssistantMessageRole.Assistant)
            .OrderBy(message => message.Order)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id))
        {
            foreach (var call in ReadPersistedToolCalls(assistantMessage.ToolCallsJson))
            {
                if (existingToolCallIds.Contains(call.CallId))
                    continue;

                existingToolCallIds.Add(call.CallId);
                syntheticMessages.Add(new AssistantMessage
                {
                    ConversationId = conversationId,
                    Role = AssistantMessageRole.Tool,
                    Content = InterruptedToolResult,
                    ToolCallId = call.CallId,
                    ToolName = call.Name,
                    Status = AssistantMessageStatus.Failed,
                    ErrorMessage = InterruptedToolError,
                });
            }
        }

        return syntheticMessages;
    }

    private static void RebuildConversationOrder(List<AssistantMessage> messages)
    {
        var ordered = messages
            .OrderBy(message => message.Order)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToList();
        var ownedToolCallIds = ordered
            .Where(message => message.Role == AssistantMessageRole.Assistant)
            .SelectMany(message => ReadPersistedToolCalls(message.ToolCallsJson))
            .Select(call => call.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var toolRowsByCallId = ordered
            .Where(message => message.Role == AssistantMessageRole.Tool && message.ToolCallId is not null)
            .GroupBy(message => message.ToolCallId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(message => message.Order)
                    .ThenBy(message => message.CreatedAt)
                    .ThenBy(message => message.Id)
                    .ToList(),
                StringComparer.Ordinal);
        var consumedToolRows = new HashSet<Guid>();
        var reordered = new List<AssistantMessage>(ordered.Count);

        foreach (var message in ordered)
        {
            if (message.Role == AssistantMessageRole.Tool
                && message.ToolCallId is not null
                && ownedToolCallIds.Contains(message.ToolCallId))
            {
                continue;
            }

            reordered.Add(message);
            if (message.Role != AssistantMessageRole.Assistant)
                continue;

            foreach (var call in ReadPersistedToolCalls(message.ToolCallsJson))
            {
                if (!toolRowsByCallId.TryGetValue(call.CallId, out var toolRows))
                    continue;

                foreach (var toolRow in toolRows)
                {
                    if (consumedToolRows.Add(toolRow.Id))
                        reordered.Add(toolRow);
                }
            }
        }

        for (var index = 0; index < reordered.Count; index++)
            reordered[index].Order = index;
    }

    private async Task<List<ChatMessage>> BuildIdleModelMessagesAsync(
        IReadOnlyList<AssistantMessage> history,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var messages = BuildPersistedModelMessages(history);
        var workbench = await workflow.GetWorkbenchAsync(projectId, cancellationToken);
        if (workbench.Attachments.Count > 0)
            messages.Add(await BuildCurrentUserMessageAsync(string.Empty, workbench, projectId, cancellationToken));

        return messages;
    }

    private List<ChatMessage> BuildPersistedModelMessages(IReadOnlyList<AssistantMessage> history)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, AssistantPromptBuilder.Build()) };
        foreach (var message in history)
        {
            if (message.Role == AssistantMessageRole.System)
                continue;

            var chatMessage = ToChatMessage(message);
            if (chatMessage is not null)
                messages.Add(chatMessage);
        }

        return messages;
    }

    private static List<ChatMessage> BuildAssistantPreviewMessages(
        IReadOnlyList<ChatMessage> baseMessages,
        string assistantText,
        IReadOnlyList<PendingToolCall> pendingCalls)
    {
        var messages = new List<ChatMessage>(baseMessages);
        if (!string.IsNullOrEmpty(assistantText) || pendingCalls.Count > 0)
        {
            var calls = pendingCalls
                .Select(ToPersistedToolCall)
                .ToList();
            messages.Add(new ChatMessage(ChatRole.Assistant, calls.Count == 0
                ? BuildTextOnlyAssistantContents(assistantText)
                : BuildAssistantContents(assistantText, calls)));
        }

        return messages;
    }

    private static List<ChatMessage> BuildToolPreviewMessages(
        IReadOnlyList<ChatMessage> baseMessages,
        IReadOnlyList<AIContent> resultContents,
        IReadOnlyList<AIContent> modelOnlyContents)
    {
        var messages = new List<ChatMessage>(baseMessages);
        if (resultContents.Count > 0)
            messages.Add(new ChatMessage(ChatRole.Tool, resultContents.ToList()));
        if (modelOnlyContents.Count > 0)
            messages.Add(new ChatMessage(ChatRole.User, modelOnlyContents.ToList()));

        return messages;
    }

    private static PersistedToolCall ToPersistedToolCall(PendingToolCall pendingCall) =>
        new(
            pendingCall.CallId,
            pendingCall.Name,
            pendingCall.ArgumentsJson,
            pendingCall.TextOffset);

    private async Task<ChatMessage> BuildCurrentUserMessageAsync(
        string text,
        WorkbenchView workbench,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>();
        var contextSummary = BuildVisibleContextSummary(workbench);
        contents.Add(new TextContent(BuildUserTextWithVisibleContext(text, contextSummary)));

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
                        var image = await workflow.GetAssetFullImageAsync(projectId, asset.Id, cancellationToken);
                        contents.Add(new DataContent(DataUrl.ToDataUrl(image.ContentType, image.Data), image.ContentType)
                        {
                            Name = asset.FileName,
                        });
                    }
                    break;

                case ChatContextAttachmentType.Mask:
                    var mask = workbench.Masks.FirstOrDefault(item => item.Id == attachment.RefId);
                    if (mask is not null)
                    {
                        var image = await workflow.GetMaskImageAsync(projectId, mask.Id, cancellationToken);
                        contents.Add(new DataContent(DataUrl.ToDataUrl(image.ContentType, image.Data), image.ContentType)
                        {
                            Name = $"{mask.Label}.png",
                        });
                    }
                    break;

                case ChatContextAttachmentType.SpriteFrame:
                    var frame = workbench.SpriteSheets
                        .SelectMany(sheet => sheet.Frames)
                        .FirstOrDefault(item => item.Id == attachment.RefId);
                    if (frame is not null)
                    {
                        var image = await workflow.GetSpriteFramePreviewImageAsync(projectId, frame.Id, cancellationToken);
                        contents.Add(new DataContent(DataUrl.ToDataUrl(image.ContentType, image.Data), image.ContentType)
                        {
                            Name = $"{frame.Label}.png",
                        });
                    }
                    break;
            }
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    private static string BuildUserTextWithVisibleContext(string text, string contextSummary)
    {
        if (string.IsNullOrWhiteSpace(contextSummary))
            return text;

        return string.IsNullOrWhiteSpace(text)
            ? $"Visible PixelChat chat attachments:\n{contextSummary}"
            : $"{text}\n\nVisible PixelChat chat attachments:\n{contextSummary}";
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

    private ChatMessage? ToChatMessage(AssistantMessage message) => message.Role switch
    {
        AssistantMessageRole.User => new ChatMessage(ChatRole.User, message.Content),
        AssistantMessageRole.Assistant => BuildAssistantReplay(message),
        AssistantMessageRole.Tool => new ChatMessage(ChatRole.Tool, [new FunctionResultContent(
            message.ToolCallId ?? string.Empty,
            BuildToolResultForModel(message.ToolName ?? "tool", message.Content, EffectiveMaxToolResultCharsForModel()))]),
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
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var aiFunction = aiTools.OfType<AIFunction>().FirstOrDefault(function => function.Name == pendingCall.Name)
            ?? throw new InvalidOperationException($"Unknown tool '{pendingCall.Name}'.");
        return await InvokeToolAsync(aiFunction, pendingCall, projectId, cancellationToken);
    }

    private async Task<ToolInvocationOutcome> InvokeToolAsync(
        AIFunction aiFunction,
        PendingToolCall pendingCall,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var invokeResult = await aiFunction.InvokeAsync(
                ToolCallArguments.Create(pendingCall.Content.Arguments, pendingCall.ArgumentsJson),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                // The turn was cancelled mid-tool. Tools handle cancellation cooperatively and may
                // return a partial result instead of throwing, so flag the cancellation explicitly.
                stopwatch.Stop();
                return new ToolInvocationOutcome(string.Empty, Error: null, Cancelled: true, stopwatch.Elapsed.TotalMilliseconds, Array.Empty<AIContent>());
            }
            var result = invokeResult?.ToString() ?? string.Empty;
            var modelOnlyContents = await BuildModelOnlyToolContentsAsync(pendingCall, projectId, result, cancellationToken);
            stopwatch.Stop();
            return new ToolInvocationOutcome(result, Error: null, Cancelled: false, stopwatch.Elapsed.TotalMilliseconds, modelOnlyContents);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ToolInvocationOutcome(string.Empty, Error: null, Cancelled: true, stopwatch.Elapsed.TotalMilliseconds, Array.Empty<AIContent>());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Assistant tool '{Tool}' failed.", pendingCall.Name);
            return new ToolInvocationOutcome($"Error: {ex.Message}", ex.Message, Cancelled: false, stopwatch.Elapsed.TotalMilliseconds, Array.Empty<AIContent>());
        }
    }

    private async Task<IReadOnlyList<AIContent>> BuildModelOnlyToolContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(pendingCall.Name, "read_asset", StringComparison.Ordinal)
                && ReadGuidArgument(pendingCall, "assetId") is Guid assetId)
            {
                var asset = await workflow.GetAssetForExportAsync(projectId, assetId, cancellationToken);
                return
                [
                    new TextContent($"Model-only image returned by read_asset for asset '{asset.Label}' ({asset.Id}). This image is not attached to visible chat context."),
                    new DataContent(asset.DataUrl, asset.ContentType)
                    {
                        Name = asset.FileName,
                    },
                ];
            }

            if (string.Equals(pendingCall.Name, "run_generation_round", StringComparison.Ordinal))
                return await BuildGenerationRoundModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "generate_animation_guide", StringComparison.Ordinal))
                return await BuildAnimationGuideModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (IsSpriteFrameWorkingImageTool(pendingCall.Name))
                return await BuildSpriteFrameWorkingModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "review_sprite_animation", StringComparison.Ordinal))
                return await BuildSpriteAnimationReviewModelOnlyContentsAsync(pendingCall, projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "map_sprite_sheet_frames", StringComparison.Ordinal))
            {
                var mapMode = ReadStringArgument(pendingCall, "mode")?.Trim().ToLowerInvariant();
                return mapMode is "grid-repair" or "repair"
                    ? await BuildSpriteMutationModelOnlyContentsAsync("repair_sprite_sheet_frames", projectId, toolResult, cancellationToken)
                    : await BuildSpriteSheetDetectionModelOnlyContentsAsync(projectId, toolResult, cancellationToken);
            }

            if (string.Equals(pendingCall.Name, "detect_sprite_frame_boxes", StringComparison.Ordinal))
                return await BuildSpriteSheetDetectionModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "stabilize_sprite_sheet_frames", StringComparison.Ordinal))
                return await BuildSpriteStabilizationModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (IsSpriteMutationFeedbackTool(pendingCall.Name))
                return await BuildSpriteMutationModelOnlyContentsAsync(pendingCall.Name, projectId, toolResult, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not build model-only contents for tool '{Tool}'.", pendingCall.Name);
        }

        return Array.Empty<AIContent>();
    }

    private async Task<IReadOnlyList<AIContent>> BuildGenerationRoundModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (!root.TryGetProperty("batch", out var batch)
            || !batch.TryGetProperty("outputAssetIds", out var outputAssetIds)
            || outputAssetIds.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AIContent>();
        }

        var maxImages = Math.Clamp(agentOptions.Value.MaxImagesPerGenerationRound <= 0 ? 2 : agentOptions.Value.MaxImagesPerGenerationRound, 1, 2);
        var batchId = batch.TryGetProperty("id", out var batchIdElement) ? batchIdElement.GetString() : null;
        var round = root.TryGetProperty("round", out var roundElement) && roundElement.TryGetInt32(out var roundValue)
            ? roundValue
            : 0;
        var contents = new List<AIContent>
        {
            new TextContent($"Model-only images: outputs of generation round {round}, batch {batchId ?? "unknown"}. These images are not attached to visible chat context."),
        };
        foreach (var item in outputAssetIds.EnumerateArray().Take(maxImages))
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var assetId))
                continue;

            var asset = await workflow.GetAssetForExportAsync(projectId, assetId, cancellationToken);
            contents.Add(new DataContent(asset.DataUrl, asset.ContentType)
            {
                Name = asset.FileName,
            });
        }

        return contents.Count > 1 ? contents : Array.Empty<AIContent>();
    }

    private async Task<IReadOnlyList<AIContent>> BuildSpriteFrameWorkingModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (root.TryGetProperty("frame", out var nestedFrame))
            root = nestedFrame;

        if (!TryReadGuidProperty(root, "spriteSheetId", out var spriteSheetId)
            || !TryReadIntProperty(root, "frameNumber", out var frameNumber)
            || frameNumber <= 0)
        {
            return Array.Empty<AIContent>();
        }

        var frame = await workflow.GetSpriteFrameWorkingImageAsync(projectId, spriteSheetId, frameNumber - 1, cancellationToken);
        if (string.IsNullOrWhiteSpace(frame.WorkingPngDataUrl))
        {
            return
            [
                new TextContent($"Model-only sprite frame read for sprite sheet {spriteSheetId}, frame {frameNumber}: no hidden working image is stored."),
            ];
        }

        var contents = new List<AIContent>
        {
            new TextContent($"Model-only images: hidden working sprite frame {frameNumber} for sprite sheet {spriteSheetId}. The clean copy comes first; the coordinate-grid companion overlays gridlines with labeled pixel coordinates (origin top-left of this {frame.WorkingWidth}x{frame.WorkingHeight} working image, margin {frame.WorkingMargin}px) — use it to compute rect/polygon coordinates, and judge pixels on the clean copy. These images are not attached to visible chat context."),
            new DataContent(frame.WorkingPngDataUrl, "image/png")
            {
                Name = $"sprite-frame-{frameNumber}-working.png",
            },
        };

        try
        {
            var grid = await workflow.BuildSpriteFrameGridImageAsync(projectId, spriteSheetId, frameNumber - 1, cancellationToken);
            if (grid is not null)
            {
                contents.Add(new DataContent(grid.DataUrl, grid.ContentType)
                {
                    Name = grid.FileName,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not build coordinate grid image for sprite sheet {SpriteSheetId} frame {FrameNumber}.", spriteSheetId, frameNumber);
        }

        return contents;
    }

    private async Task<IReadOnlyList<AIContent>> BuildSpriteAnimationReviewModelOnlyContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        if (!document.RootElement.TryGetProperty("spriteSheetId", out var spriteSheetElement)
            || spriteSheetElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(spriteSheetElement.GetString(), out var spriteSheetId))
        {
            return Array.Empty<AIContent>();
        }

        var review = await workflow.BuildSpriteAnimationReviewAsync(
            projectId,
            spriteSheetId,
            ReadIntArgument(pendingCall, "maxFrames") ?? 12,
            cancellationToken);
        var contents = new List<AIContent>
        {
            new TextContent($"Model-only images: sprite animation review for sprite sheet {spriteSheetId}. Filenames identify sheet view, ordered frames, pairwise diffs, onion-skin, filmstrip, and removed-vs-source overlays for frames with hidden working images (red marks pixels erased from the source foreground — check them for clipped owned silhouette before declaring frame cleanup done); JSON manifest fields include kind/frame indexes. These images are not attached to visible chat context."),
        };
        foreach (var image in review.Images)
        {
            contents.Add(new DataContent(image.DataUrl, image.ContentType)
            {
                Name = image.FileName,
            });
        }

        return contents;
    }

    private async Task<IReadOnlyList<AIContent>> BuildAnimationGuideModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (!TryReadGuidProperty(root, "guideAssetId", out var guideAssetId))
            return Array.Empty<AIContent>();

        var contents = new List<AIContent>();
        var guide = await workflow.GetAssetForExportAsync(projectId, guideAssetId, cancellationToken);
        contents.Add(new TextContent($"Model-only images returned by generate_animation_guide for SpriteGuide asset '{guide.Label}' ({guide.Id}). Use the guide asset first in sprite-sheet generation references; the diagnostic guide is for inspection only."));
        contents.Add(new DataContent(guide.DataUrl, guide.ContentType)
        {
            Name = guide.FileName,
        });

        if (TryReadGuidProperty(root, "diagnosticGuideAssetId", out var diagnosticGuideAssetId))
        {
            var diagnostic = await workflow.GetAssetForExportAsync(projectId, diagnosticGuideAssetId, cancellationToken);
            contents.Add(new DataContent(diagnostic.DataUrl, diagnostic.ContentType)
            {
                Name = diagnostic.FileName,
            });
        }

        return contents;
    }

    private async Task<IReadOnlyList<AIContent>> BuildSpriteSheetDetectionModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (TryReadGuidProperty(root, "spriteSheetId", out var spriteSheetId))
        {
            var image = await workflow.BuildSpriteSheetAnnotatedSheetAsync(
                projectId,
                spriteSheetId,
                ReadSpriteSheetRejectedSegments(root),
                cancellationToken);
            var frameCount = TryReadIntProperty(root, "frameCount", out var parsedFrameCount)
                ? parsedFrameCount
                : 0;
            var contents = new List<AIContent>
            {
                new TextContent($"Model-only images: compact sprite-sheet detection feedback for sprite sheet {spriteSheetId}. The annotated sheet labels frames with 1-based numbers matching the tool result frameNumber values; compact filmstrip/contact imagery is included when frame previews exist. These images are not attached to visible chat context. Detected frames: {frameCount}."),
                new DataContent(image.DataUrl, image.ContentType)
                {
                    Name = image.FileName,
                },
            };
            try
            {
                var review = await workflow.BuildSpriteAnimationReviewAsync(projectId, spriteSheetId, 12, cancellationToken);
                foreach (var reviewImage in SelectCompactMutationReviewImages(review, includeSheetView: false))
                {
                    contents.Add(new DataContent(reviewImage.DataUrl, reviewImage.ContentType)
                    {
                        Name = reviewImage.FileName,
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not build detection review images for sprite sheet {SpriteSheetId}.", spriteSheetId);
            }

            return contents;
        }

        var detection = JsonSerializer.Deserialize<SpriteSheetDetectionResult>(toolResult, JsonOptions);
        if (detection is null)
            return Array.Empty<AIContent>();

        var legacyImage = await workflow.BuildSpriteSheetDetectionAnnotatedSheetAsync(projectId, detection, cancellationToken);
        return
        [
            new TextContent($"Model-only image: annotated sprite-sheet detection view for source asset {detection.SourceAssetId}. Frame boxes are labeled by index and background mode is {detection.Background.Mode}. This image is not attached to visible chat context."),
            new DataContent(legacyImage.DataUrl, legacyImage.ContentType)
            {
                Name = legacyImage.FileName,
            },
        ];
    }

    private async Task<IReadOnlyList<AIContent>> BuildSpriteStabilizationModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (!TryReadGuidProperty(root, "spriteSheetId", out var spriteSheetId))
            return Array.Empty<AIContent>();

        SpriteAnimationReviewImageView image;
        if (root.TryGetProperty("stabilization", out var stabilizationElement)
            && stabilizationElement.ValueKind == JsonValueKind.Object)
        {
            var stabilization = JsonSerializer.Deserialize<SpriteSheetStabilizationView>(stabilizationElement.GetRawText(), JsonOptions);
            image = stabilization is null
                ? await workflow.BuildSpriteSheetStabilizationAnnotatedSheetAsync(projectId, spriteSheetId, cancellationToken)
                : await workflow.BuildSpriteSheetStabilizationAnnotatedSheetAsync(projectId, spriteSheetId, stabilization, cancellationToken);
        }
        else
        {
            image = await workflow.BuildSpriteSheetStabilizationAnnotatedSheetAsync(projectId, spriteSheetId, cancellationToken);
        }

        return
        [
            new TextContent($"Model-only image: sprite stabilization diagnostic for sprite sheet {spriteSheetId}. Green boxes show each full working-frame placement on the normalized canvas, yellow anchors are accepted matches, red anchors are low-confidence matches, and magenta is the reference anchor. Apply writes stabilized working frames; run Reassemble afterward. These images are not attached to visible chat context."),
            new DataContent(image.DataUrl, image.ContentType)
            {
                Name = image.FileName,
            },
        ];
    }

    private async Task<IReadOnlyList<AIContent>> BuildSpriteMutationModelOnlyContentsAsync(
        string toolName,
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;
        if (!TryReadGuidProperty(root, "spriteSheetId", out var spriteSheetId)
            && !TryReadGuidProperty(root, "id", out spriteSheetId))
        {
            return Array.Empty<AIContent>();
        }

        var contents = new List<AIContent>
        {
            new TextContent($"Model-only images: compact result of {toolName} for sprite sheet {spriteSheetId}. Inspect the annotated sheet and filmstrip/contact image before deciding whether frame boxes/polygons are acceptable. These images are not attached to visible chat context."),
        };

        var hasAnnotatedSheet = false;
        if (string.Equals(toolName, "repair_sprite_sheet_frames", StringComparison.Ordinal)
            && TryBuildRepairResultFromToolJson(root, out var repair))
        {
            try
            {
                var repairImage = repair.Applied
                    ? await workflow.BuildSpriteSheetAnnotatedSheetAsync(projectId, spriteSheetId, repair.RejectedSegments, cancellationToken)
                    : await workflow.BuildSpriteSheetRepairAnnotatedSheetAsync(projectId, repair, cancellationToken);
                contents.Add(new DataContent(repairImage.DataUrl, repairImage.ContentType)
                {
                    Name = repairImage.FileName,
                });
                hasAnnotatedSheet = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not build repair annotation image for sprite sheet {SpriteSheetId}.", spriteSheetId);
            }
        }

        try
        {
            var review = await workflow.BuildSpriteAnimationReviewAsync(projectId, spriteSheetId, 12, cancellationToken);
            foreach (var image in SelectCompactMutationReviewImages(review, includeSheetView: !hasAnnotatedSheet))
            {
                contents.Add(new DataContent(image.DataUrl, image.ContentType)
                {
                    Name = image.FileName,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not build sprite mutation review images for sprite sheet {SpriteSheetId}.", spriteSheetId);
            if (TryReadGuidProperty(root, "workingAssetId", out var workingAssetId))
            {
                try
                {
                    var asset = await workflow.GetAssetForExportAsync(projectId, workingAssetId, cancellationToken);
                    contents.Add(new DataContent(asset.DataUrl, asset.ContentType)
                    {
                        Name = asset.FileName,
                    });
                }
                catch (Exception assetEx) when (assetEx is not OperationCanceledException)
                {
                    logger.LogDebug(assetEx, "Could not build fallback sprite mutation image for working asset {AssetId}.", workingAssetId);
                }
            }
        }

        return contents.Count > 1 ? contents : Array.Empty<AIContent>();
    }

    private static IEnumerable<SpriteAnimationReviewImageView> SelectCompactMutationReviewImages(
        SpriteAnimationReviewView review,
        bool includeSheetView)
    {
        foreach (var image in review.Images)
        {
            if (string.Equals(image.Kind, "filmstrip", StringComparison.Ordinal)
                || (includeSheetView && string.Equals(image.Kind, "sheet-view", StringComparison.Ordinal)))
            {
                yield return image;
            }
        }
    }

    private static bool IsSpriteMutationFeedbackTool(string toolName) =>
        toolName is "update_sprite_sheet_frames"
            or "adjust_sprite_frame_box"
            or "normalize_sprite_sheet"
            or "reset_sprite_sheet_to_original"
            or "reassemble_sprite_sheet";

    private static bool IsSpriteFrameWorkingImageTool(string toolName) =>
        toolName is "isolate_sprite_frame"
            or "read_sprite_frame_image"
            or "erase_sprite_frame_regions"
            or "edit_sprite_frame";

    private static bool TryBuildRepairResultFromToolJson(JsonElement root, out RepairSpriteSheetFramesResult repair)
    {
        repair = null!;
        if (!TryReadGuidProperty(root, "spriteSheetId", out var spriteSheetId)
            || !TryReadGuidProperty(root, "sourceAssetId", out var sourceAssetId))
        {
            return false;
        }

        TryReadGuidProperty(root, "workingAssetId", out var workingAssetId);
        var hasWorkingAsset = workingAssetId != default;
        var background = root.TryGetProperty("background", out var backgroundElement)
            ? JsonSerializer.Deserialize<SpriteSheetBackground>(backgroundElement.GetRawText(), JsonOptions) ?? new SpriteSheetBackground("alpha", 0, 0, 0, 0)
            : new SpriteSheetBackground("alpha", 0, 0, 0, 0);
        var frames = ReadSpriteSheetFrameUpdates(root);
        var warnings = root.TryGetProperty("warnings", out var warningsElement)
            ? JsonSerializer.Deserialize<List<string>>(warningsElement.GetRawText(), JsonOptions) ?? []
            : [];
        var rejectedSegments = ReadSpriteSheetRejectedSegments(root);
        var frameQuality = root.TryGetProperty("frameQuality", out var qualityElement)
            ? JsonSerializer.Deserialize<List<SpriteSheetFrameQualityView>>(qualityElement.GetRawText(), JsonOptions) ?? []
            : [];

        repair = new RepairSpriteSheetFramesResult(
            spriteSheetId,
            sourceAssetId,
            hasWorkingAsset ? workingAssetId : null,
            TryReadBoolProperty(root, "applied", out var applied) && applied,
            TryReadIntProperty(root, "imageWidth", out var imageWidth) ? imageWidth : 1,
            TryReadIntProperty(root, "imageHeight", out var imageHeight) ? imageHeight : 1,
            TryReadIntProperty(root, "rows", out var rows) ? rows : 1,
            TryReadIntProperty(root, "columns", out var columns) ? columns : Math.Max(1, frames.Count),
            TryReadIntProperty(root, "cellWidth", out var cellWidth) ? cellWidth : Math.Max(1, frames.Select(frame => frame.SourceRect.Width).DefaultIfEmpty(1).Max()),
            TryReadIntProperty(root, "cellHeight", out var cellHeight) ? cellHeight : Math.Max(1, frames.Select(frame => frame.SourceRect.Height).DefaultIfEmpty(1).Max()),
            TryReadIntProperty(root, "padding", out var padding) ? padding : 0,
            TryReadIntProperty(root, "gutter", out var gutter) ? gutter : 0,
            TryReadIntProperty(root, "fps", out var fps) ? fps : 8,
            TryReadBoolProperty(root, "loop", out var loop) ? loop : true,
            TryReadStringProperty(root, "horizontalAnchor") ?? "center",
            TryReadStringProperty(root, "verticalAnchor") ?? "bottom",
            background,
            frames,
            warnings,
            rejectedSegments,
            frameQuality,
            SavedSheet: null);
        return true;
    }

    private static IReadOnlyList<SpriteSheetRejectedSegmentView> ReadSpriteSheetRejectedSegments(JsonElement root) =>
        root.TryGetProperty("rejectedSegments", out var rejectedElement)
            && rejectedElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<SpriteSheetRejectedSegmentView>>(rejectedElement.GetRawText(), JsonOptions) ?? []
            : [];

    private static IReadOnlyList<SpriteSheetFrameUpdateView> ReadSpriteSheetFrameUpdates(JsonElement root)
    {
        if (!root.TryGetProperty("frames", out var framesElement)
            || framesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var frames = new List<SpriteSheetFrameUpdateView>();
        foreach (var frameElement in framesElement.EnumerateArray())
        {
            if (frameElement.ValueKind != JsonValueKind.Object)
                continue;

            var index = TryReadIntProperty(frameElement, "index", out var indexValue)
                ? indexValue
                : TryReadIntProperty(frameElement, "frameNumber", out var frameNumber)
                    ? Math.Max(0, frameNumber - 1)
                    : frames.Count;
            var label = TryReadStringProperty(frameElement, "label");
            if (string.IsNullOrWhiteSpace(label))
                label = $"Frame {index + 1}";
            var sourceRect = frameElement.TryGetProperty("sourceRect", out var rectElement)
                ? JsonSerializer.Deserialize<SpriteSheetRect>(rectElement.GetRawText(), JsonOptions) ?? new SpriteSheetRect(0, 0, 1, 1)
                : new SpriteSheetRect(0, 0, 1, 1);
            var shapePaths = frameElement.TryGetProperty("shapePaths", out var shapeElement)
                && shapeElement.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<SpriteSheetShapePath>>(shapeElement.GetRawText(), JsonOptions) ?? []
                : [];
            var sourceImageAssetId = TryReadGuidProperty(frameElement, "sourceImageAssetId", out var sourceImageId)
                ? sourceImageId
                : (Guid?)null;
            var sourceImageRect = frameElement.TryGetProperty("sourceImageRect", out var sourceImageRectElement)
                && sourceImageRectElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<SpriteSheetRect>(sourceImageRectElement.GetRawText(), JsonOptions)
                : null;

            frames.Add(new SpriteSheetFrameUpdateView(index, label, sourceRect, shapePaths, sourceImageAssetId, sourceImageRect));
        }

        return frames;
    }

    private static bool TryReadGuidProperty(JsonElement root, string name, out Guid value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out value);
    }

    private static bool TryReadIntProperty(JsonElement root, string name, out int value)
    {
        value = default;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryReadBoolProperty(JsonElement root, string name, out bool value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var element)
            || (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static string? TryReadStringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static Guid? ReadGuidArgument(PendingToolCall pendingCall, string name)
    {
        var arguments = ToolCallArguments.ParseObjectOrNull(pendingCall.ArgumentsJson) ?? pendingCall.Content.Arguments;
        if (arguments is null || !arguments.TryGetValue(name, out var value))
            return null;

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var guid) => guid,
            JsonElement { ValueKind: JsonValueKind.String } element when Guid.TryParse(element.GetString(), out var guid) => guid,
            _ => null,
        };
    }

    private static string? ReadStringArgument(PendingToolCall pendingCall, string name)
    {
        var arguments = ToolCallArguments.ParseObjectOrNull(pendingCall.ArgumentsJson) ?? pendingCall.Content.Arguments;
        if (arguments is null || !arguments.TryGetValue(name, out var value))
            return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
    }

    private static int? ReadIntArgument(PendingToolCall pendingCall, string name)
    {
        var arguments = ToolCallArguments.ParseObjectOrNull(pendingCall.ArgumentsJson) ?? pendingCall.Content.Arguments;
        if (arguments is null || !arguments.TryGetValue(name, out var value))
            return null;

        return value switch
        {
            int intValue => intValue,
            long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
            string text when int.TryParse(text, out var intValue) => intValue,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var intValue) => intValue,
            JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var intValue) => intValue,
            _ => null,
        };
    }

    private async Task<int> PersistInterruptedToolResultsAsync(
        Guid conversationId,
        int nextOrder,
        IReadOnlyList<PendingToolCall> pendingCalls,
        ISet<string> completedCallIds,
        string result,
        string error,
        CancellationToken cancellationToken)
    {
        foreach (var pendingCall in pendingCalls)
        {
            if (completedCallIds.Contains(pendingCall.CallId))
                continue;

            await PersistToolResultAsync(
                conversationId,
                nextOrder++,
                pendingCall.CallId,
                pendingCall.Name,
                result,
                error,
                cancellationToken);
            completedCallIds.Add(pendingCall.CallId);
        }

        return nextOrder;
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
        double DurationMs,
        IReadOnlyList<AIContent> ModelOnlyContents);
}
