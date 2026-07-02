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
    IFrameSetService frameSets,
    IChatTokenEstimator tokenEstimator,
    IOptions<AgentOptions> agentOptions,
    IOptions<PixelChat.Art.ImageGenerationOptions> imageOptions,
    ILogger<AssistantChatService> logger) : IAssistantChatService
{
    private const string InitialAssistantGreeting =
        "Tell me what kind of 2D game art you are working on. I can analyze the active or attached images, shape style direction, draft generation and edit forms, and help build reusable prompt recipes for you to review.";
    private const string InterruptedToolResult = "Error: PixelChat was interrupted before this tool call produced a saved result.";
    private const string InterruptedToolError = "PixelChat was interrupted before this tool call produced a saved result.";
    private const string CancelledToolResult = "Error: PixelChat was cancelled before this tool call produced a saved result.";
    private const string CancelledToolError = "PixelChat was cancelled before this tool call produced a saved result.";
    private const string DisplayTitleArgumentName = "displayTitle";

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

        var workbenchForUserMessage = await workflow.GetWorkbenchAsync(projectId, cancellationToken);
        var userVisuals = BuildUserMessageVisuals(userMessage.Id, workbenchForUserMessage);
        if (userVisuals.Count > 0)
        {
            await conversations.AddMessageVisualsAsync(userVisuals, cancellationToken);
            await conversations.SaveChangesAsync(cancellationToken);
        }

        yield return new AssistantUserMessagePersisted(
            userMessage.Id,
            userVisuals.Select(visual => ToVisualUpdate(projectId, visual)).ToList());

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
                                        var startedArguments = ExtractToolDisplayMetadata(started.ArgumentsJson);
                                        updatesToYield.Add(new AssistantToolCallStarted(
                                            started.CallId,
                                            started.ToolName,
                                            startedArguments.ArgumentsJson,
                                            started.ArgumentsComplete,
                                            startedArguments.ExplicitDisplayTitle));
                                        break;

                                    case StreamingToolCallArgumentsDeltaUpdate delta:
                                        if (!delta.ArgumentsComplete)
                                        {
                                            updatesToYield.Add(new AssistantToolCallArgumentsDelta(
                                                delta.CallId,
                                                delta.ArgumentsDelta,
                                                delta.ArgumentsComplete));
                                        }
                                        break;

                                    case StreamingToolCallReadyUpdate ready:
                                        var readyArguments = ExtractToolDisplayMetadata(ready.ArgumentsJson);
                                        updatesToYield.Add(new AssistantToolCallStarted(
                                            ready.CallId,
                                            ready.ToolName,
                                            readyArguments.ArgumentsJson,
                                            ArgumentsComplete: true,
                                            readyArguments.ExplicitDisplayTitle));
                                        pendingCalls.Add(new PendingToolCall(
                                            ready.Content,
                                            ready.CallId,
                                            ready.ToolName,
                                            readyArguments.ArgumentsJson,
                                            ready.TextOffset,
                                            readyArguments.ExplicitDisplayTitle));
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
                .Select(ToPersistedToolCall)
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

                var toolMessage = await PersistToolResultAsync(
                    conversation.Id,
                    nextOrder++,
                    pendingCall.CallId,
                    pendingCall.Name,
                    outcome.Result,
                    outcome.Error);
                completedCallIds.Add(pendingCall.CallId);

                IReadOnlyList<AssistantMessageVisual> toolVisuals = outcome.Error is null
                    ? await PersistToolVisualsAsync(toolMessage, pendingCall, projectId, outcome.Result, outcome.ModelOnlyContents, cancellationToken)
                    : Array.Empty<AssistantMessageVisual>();

                resultContents.Add(new FunctionResultContent(
                    pendingCall.CallId,
                    BuildToolResultForModel(pendingCall.Name, outcome.Result, EffectiveMaxToolResultCharsForModel())));
                modelOnlyContents.AddRange(outcome.ModelOnlyContents);
                yield return new AssistantToolCallCompleted(
                    pendingCall.CallId,
                    pendingCall.Name,
                    outcome.Result,
                    outcome.Error,
                    outcome.DurationMs,
                    toolVisuals.Select(visual => ToVisualUpdate(projectId, visual)).ToList());
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
            pendingCall.TextOffset,
            pendingCall.ExplicitDisplayTitle);

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

            if (string.Equals(pendingCall.Name, "review_frame_set_animation", StringComparison.Ordinal))
                return await BuildFrameSetAnimationReviewModelOnlyContentsAsync(pendingCall, projectId, cancellationToken);

            if (string.Equals(pendingCall.Name, "build_sheet", StringComparison.Ordinal))
                return await BuildBuiltSheetModelOnlyContentsAsync(projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "translate_frame_content", StringComparison.Ordinal))
                return await BuildFrameCellModelOnlyContentsAsync(pendingCall, projectId, "rendered logical cell after translation", cancellationToken);

            if (string.Equals(pendingCall.Name, "inspect_frame", StringComparison.Ordinal))
                return await BuildInspectFrameModelOnlyContentsAsync(pendingCall, projectId, cancellationToken);

            if (string.Equals(pendingCall.Name, "edit_frame", StringComparison.Ordinal)
                || string.Equals(pendingCall.Name, "erase_frame_regions", StringComparison.Ordinal))
                return await BuildEditedFrameModelOnlyContentsAsync(pendingCall, projectId, cancellationToken);
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

        var maxImages = MaxGenerationRoundImages();
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

    private async Task<IReadOnlyList<AIContent>> BuildFrameSetAnimationReviewModelOnlyContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        Guid resolved;
        if (ReadGuidArgument(pendingCall, "frameSetId") is Guid frameSetId && frameSetId != Guid.Empty)
        {
            resolved = frameSetId;
        }
        else
        {
            var active = await frameSets.GetActiveFrameSetAsync(projectId, cancellationToken);
            if (active is null)
                return Array.Empty<AIContent>();
            resolved = active.Id;
        }

        var review = await frameSets.BuildAnimationReviewAsync(projectId, resolved, ReadIntArgument(pendingCall, "maxFrames") ?? 12, cancellationToken);
        var contents = new List<AIContent>
        {
            new TextContent($"Model-only images: animation-quality review for FrameSet {resolved}. Filenames identify the sheet view, ordered frames, pairwise diffs, onion-skin, filmstrip, and removed-vs-source overlays for edited/erased frames (red marks pixels erased from the source foreground - check them for clipped owned silhouette before declaring the animation clean). These images are not attached to visible chat context."),
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

    private async Task<IReadOnlyList<AIContent>> BuildBuiltSheetModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(toolResult);
        if (!TryReadGuidProperty(document.RootElement, "outputAssetId", out var outputAssetId))
            return Array.Empty<AIContent>();

        var asset = await workflow.GetAssetForExportAsync(projectId, outputAssetId, cancellationToken);
        return
        [
            new TextContent($"Model-only image: rebuilt sheet '{asset.Label}' ({asset.Id}) - verify one row, equal cells, and no guide marks. This image is not attached to visible chat context."),
            new DataContent(asset.DataUrl, asset.ContentType)
            {
                Name = asset.FileName,
            },
        ];
    }

    private async Task<IReadOnlyList<AIContent>> BuildEditedFrameModelOnlyContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return await BuildFrameCellModelOnlyContentsAsync(pendingCall, projectId, "working pixels after the edit", cancellationToken);
    }

    private async Task<IReadOnlyList<AIContent>> BuildFrameCellModelOnlyContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        string label,
        CancellationToken cancellationToken)
    {
        if (ReadGuidArgument(pendingCall, "frameId") is not Guid frameId || frameId == Guid.Empty)
            return Array.Empty<AIContent>();

        var image = await frameSets.GetFramePreviewImageAsync(projectId, frameId, cancellationToken);
        if (image is null)
            return Array.Empty<AIContent>();

        return
        [
            new TextContent($"Model-only image: {label} for frame {frameId}. This image is not attached to visible chat context."),
            new DataContent(DataUrl.ToDataUrl(image.Value.ContentType, image.Value.Data), image.Value.ContentType)
            {
                Name = $"frame-{frameId:N}-cell.png",
            },
        ];
    }

    private async Task<IReadOnlyList<AIContent>> BuildInspectFrameModelOnlyContentsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (ReadGuidArgument(pendingCall, "frameId") is not Guid frameId || frameId == Guid.Empty)
            return Array.Empty<AIContent>();

        var image = await frameSets.InspectFrameAsync(
            projectId,
            frameId,
            ReadRectArgument(pendingCall, "rect"),
            ReadIntArgument(pendingCall, "scale") ?? 4,
            cancellationToken);
        if (image is null)
            return Array.Empty<AIContent>();

        return
        [
            new TextContent($"Model-only image: zoomed inspection for frame {frameId}. This image is not attached to visible chat context."),
            new DataContent(DataUrl.ToDataUrl(image.Value.ContentType, image.Value.Data), image.Value.ContentType)
            {
                Name = $"frame-{frameId:N}-inspect.png",
            },
        ];
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
        var hasSeparateDiagnostic = TryReadGuidProperty(root, "diagnosticGuideAssetId", out var diagnosticGuideAssetId)
            && diagnosticGuideAssetId != guideAssetId;
        contents.Add(new TextContent(hasSeparateDiagnostic
            ? $"Model-only images returned by generate_animation_guide for SpriteGuide asset '{guide.Label}' ({guide.Id}). Use the guide asset first in sprite-sheet generation references; the diagnostic guide is for inspection only."
            : $"Model-only image returned by generate_animation_guide for SpriteGuide asset '{guide.Label}' ({guide.Id}). Use the guide asset first in sprite-sheet generation references."));
        contents.Add(new DataContent(guide.DataUrl, guide.ContentType)
        {
            Name = guide.FileName,
        });

        if (hasSeparateDiagnostic)
        {
            var diagnostic = await workflow.GetAssetForExportAsync(projectId, diagnosticGuideAssetId, cancellationToken);
            contents.Add(new DataContent(diagnostic.DataUrl, diagnostic.ContentType)
            {
                Name = diagnostic.FileName,
            });
        }

        return contents;
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

    private static List<AssistantMessageVisual> BuildUserMessageVisuals(
        Guid messageId,
        WorkbenchView workbench)
    {
        var visuals = new List<AssistantMessageVisual>();
        var includedAssetIds = new HashSet<Guid>();
        var sortOrder = 0;

        foreach (var attachment in workbench.Attachments.OrderBy(attachment => attachment.SortOrder))
        {
            switch (attachment.Type)
            {
                case ChatContextAttachmentType.Asset:
                case ChatContextAttachmentType.Crop:
                    var asset = workbench.Assets.FirstOrDefault(item => item.Id == attachment.RefId);
                    if (asset is not null && includedAssetIds.Add(asset.Id))
                    {
                        visuals.Add(new AssistantMessageVisual
                        {
                            AssistantMessageId = messageId,
                            SortOrder = sortOrder++,
                            Title = VisualTitle(attachment.Label, asset.Label),
                            Caption = "Visible chat image context.",
                            SourceKind = "asset",
                            SourceRefId = asset.Id,
                            ContentType = asset.ContentType,
                            FileName = asset.FileName,
                            Width = asset.Width,
                            Height = asset.Height,
                        });
                    }
                    break;

                case ChatContextAttachmentType.Mask:
                    var mask = workbench.Masks.FirstOrDefault(item => item.Id == attachment.RefId);
                    if (mask is not null)
                    {
                        visuals.Add(new AssistantMessageVisual
                        {
                            AssistantMessageId = messageId,
                            SortOrder = sortOrder++,
                            Title = VisualTitle(attachment.Label, mask.Label),
                            Caption = "Visible chat mask context.",
                            SourceKind = "mask",
                            SourceRefId = mask.Id,
                            ContentType = mask.ContentType,
                            FileName = $"{CleanVisualFileName(mask.Label, "mask")}.png",
                            Width = mask.Width,
                            Height = mask.Height,
                        });
                    }
                    break;

            }
        }

        return visuals;
    }

    private async Task<IReadOnlyList<AssistantMessageVisual>> PersistToolVisualsAsync(
        AssistantMessage toolMessage,
        PendingToolCall pendingCall,
        Guid projectId,
        string toolResult,
        IReadOnlyList<AIContent> modelOnlyContents,
        CancellationToken cancellationToken)
    {
        var drafts = await BuildToolVisualDraftsAsync(pendingCall, projectId, toolResult, modelOnlyContents, cancellationToken);
        if (drafts.Count == 0)
            return Array.Empty<AssistantMessageVisual>();

        var visuals = drafts
            .Select((draft, index) => draft.ToVisual(toolMessage.Id, pendingCall.CallId, index))
            .ToList();
        await conversations.AddMessageVisualsAsync(visuals, cancellationToken);
        await conversations.SaveChangesAsync(cancellationToken);
        return visuals;
    }

    private async Task<List<ToolVisualDraft>> BuildToolVisualDraftsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        string toolResult,
        IReadOnlyList<AIContent> modelOnlyContents,
        CancellationToken cancellationToken)
    {
        var sourceBacked = await BuildSourceBackedToolVisualDraftsAsync(pendingCall, projectId, toolResult, cancellationToken);
        return sourceBacked.Count > 0
            ? sourceBacked
            : BuildDataContentToolVisualDrafts(pendingCall, modelOnlyContents);
    }

    private async Task<List<ToolVisualDraft>> BuildSourceBackedToolVisualDraftsAsync(
        PendingToolCall pendingCall,
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        var drafts = new List<ToolVisualDraft>();
        var seenAssetIds = new HashSet<Guid>();

        if (string.Equals(pendingCall.Name, "read_asset", StringComparison.Ordinal)
            && ReadGuidArgument(pendingCall, "assetId") is Guid assetId)
        {
            if (await TryCreateAssetVisualDraftAsync(
                    projectId,
                    assetId,
                    pendingCall.ExplicitDisplayTitle ?? "Inspect asset",
                    "Model-only image returned by read_asset.",
                    cancellationToken) is { } draft)
            {
                drafts.Add(draft);
            }

            return drafts;
        }

        try
        {
            using var document = JsonDocument.Parse(toolResult);
            var root = document.RootElement;
            if (string.Equals(pendingCall.Name, "run_generation_round", StringComparison.Ordinal)
                && root.TryGetProperty("batch", out var batch)
                && batch.TryGetProperty("outputAssetIds", out var outputAssetIds)
                && outputAssetIds.ValueKind == JsonValueKind.Array)
            {
                var maxImages = MaxGenerationRoundImages();
                var outputIndex = 1;
                foreach (var item in outputAssetIds.EnumerateArray().Take(maxImages))
                {
                    if (item.ValueKind != JsonValueKind.String
                        || !Guid.TryParse(item.GetString(), out var generatedOutputAssetId)
                        || !seenAssetIds.Add(generatedOutputAssetId))
                    {
                        continue;
                    }

                    if (await TryCreateAssetVisualDraftAsync(
                            projectId,
                            generatedOutputAssetId,
                            pendingCall.ExplicitDisplayTitle ?? "Generate candidates",
                            $"Generated output {outputIndex}.",
                            cancellationToken) is { } draft)
                    {
                        drafts.Add(draft);
                        outputIndex++;
                    }
                }

                return drafts;
            }

            if (string.Equals(pendingCall.Name, "generate_animation_guide", StringComparison.Ordinal))
            {
                if (TryReadGuidProperty(root, "guideAssetId", out var guideAssetId)
                    && await TryCreateAssetVisualDraftAsync(
                        projectId,
                        guideAssetId,
                        pendingCall.ExplicitDisplayTitle ?? "Generate animation guide",
                        "Animation guide asset.",
                        cancellationToken) is { } guideDraft)
                {
                    drafts.Add(guideDraft);
                }

                if (TryReadGuidProperty(root, "diagnosticGuideAssetId", out var diagnosticGuideAssetId)
                    && diagnosticGuideAssetId != guideAssetId
                    && await TryCreateAssetVisualDraftAsync(
                        projectId,
                        diagnosticGuideAssetId,
                        "Review animation guide diagnostic",
                        "Diagnostic animation guide asset.",
                        cancellationToken) is { } diagnosticDraft)
                {
                    drafts.Add(diagnosticDraft);
                }

                return drafts;
            }

            if (string.Equals(pendingCall.Name, "build_sheet", StringComparison.Ordinal)
                && TryReadGuidProperty(root, "outputAssetId", out var outputAssetId)
                && await TryCreateAssetVisualDraftAsync(
                    projectId,
                    outputAssetId,
                    pendingCall.ExplicitDisplayTitle ?? "Build sprite sheet",
                    "Rebuilt sprite sheet asset.",
                    cancellationToken) is { } builtSheetDraft)
            {
                drafts.Add(builtSheetDraft);
                return drafts;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not JsonException)
        {
            logger.LogDebug(ex, "Could not build source-backed visuals for tool '{Tool}'.", pendingCall.Name);
        }
        catch (JsonException)
        {
        }

        return drafts;
    }

    private async Task<ToolVisualDraft?> TryCreateAssetVisualDraftAsync(
        Guid projectId,
        Guid assetId,
        string title,
        string caption,
        CancellationToken cancellationToken)
    {
        try
        {
            var asset = await workflow.GetAssetForExportAsync(projectId, assetId, cancellationToken);
            return new ToolVisualDraft(
                VisualTitle(title, asset.Label),
                caption,
                "asset",
                asset.Id,
                asset.ContentType,
                asset.FileName,
                asset.Width,
                asset.Height,
                Data: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not build chat visual reference for asset {AssetId}.", assetId);
            return null;
        }
    }

    private static List<ToolVisualDraft> BuildDataContentToolVisualDrafts(
        PendingToolCall pendingCall,
        IReadOnlyList<AIContent> modelOnlyContents)
    {
        var drafts = new List<ToolVisualDraft>();
        var caption = string.Empty;
        var imageIndex = 1;
        foreach (var content in modelOnlyContents)
        {
            if (content is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                caption = CompactText(textContent.Text, 220);
                continue;
            }

            if (content is not DataContent dataContent
                || !dataContent.HasTopLevelMediaType("image"))
            {
                continue;
            }

            var contentType = string.IsNullOrWhiteSpace(dataContent.MediaType)
                ? "image/png"
                : dataContent.MediaType;
            var data = dataContent.Data.ToArray();
            if (data.Length == 0)
                continue;

            var (width, height) = ImageMetadataReader.TryReadSize(data, contentType);
            var fileName = string.IsNullOrWhiteSpace(dataContent.Name)
                ? $"{CleanVisualFileName(pendingCall.Name, "tool-image")}-{imageIndex}.{ExtensionForVisualContentType(contentType)}"
                : dataContent.Name!;
            var title = pendingCall.ExplicitDisplayTitle ?? CleanVisualTitle(pendingCall.Name.Replace('_', ' '));
            if (drafts.Count > 0 && !string.IsNullOrWhiteSpace(dataContent.Name))
                title = CleanVisualTitle(dataContent.Name!);

            drafts.Add(new ToolVisualDraft(
                title,
                string.IsNullOrWhiteSpace(caption)
                    ? $"Model-only image returned by {pendingCall.Name}."
                    : caption,
                "modelOnly",
                null,
                contentType,
                fileName,
                width,
                height,
                data));
            imageIndex++;
        }

        return drafts;
    }

    private static AssistantMessageVisualUpdate ToVisualUpdate(Guid projectId, AssistantMessageVisual visual) =>
        new(
            visual.Id,
            visual.ToolCallId,
            visual.Title,
            visual.Caption,
            ChatVisualPreviewImageUrl(projectId, visual),
            ChatVisualFullImageUrl(projectId, visual),
            visual.Width,
            visual.Height);

    private static string ChatVisualPreviewImageUrl(Guid projectId, AssistantMessageVisual visual) =>
        $"/media/projects/{projectId:D}/chat-visuals/{visual.Id:D}/preview{VisualVersionQuery(visual)}";

    private static string ChatVisualFullImageUrl(Guid projectId, AssistantMessageVisual visual) =>
        $"/media/projects/{projectId:D}/chat-visuals/{visual.Id:D}/full{VisualVersionQuery(visual)}";

    private static string VisualVersionQuery(AssistantMessageVisual visual)
    {
        var createdAt = visual.CreatedAt.Kind == DateTimeKind.Utc
            ? visual.CreatedAt
            : DateTime.SpecifyKind(visual.CreatedAt, DateTimeKind.Utc);
        return $"?v={createdAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static ToolDisplayMetadata ExtractToolDisplayMetadata(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return new("{}", null);

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new(argumentsJson, null);

            var sanitized = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var foundDisplayTitle = false;
            string? explicitDisplayTitle = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, DisplayTitleArgumentName, StringComparison.Ordinal))
                {
                    foundDisplayTitle = true;
                    if (property.Value.ValueKind == JsonValueKind.String)
                        explicitDisplayTitle = NormalizeExplicitDisplayTitle(property.Value.GetString());
                    continue;
                }

                sanitized[property.Name] = property.Value.Clone();
            }

            if (!foundDisplayTitle)
                return new(argumentsJson, explicitDisplayTitle);

            return new(JsonSerializer.Serialize(sanitized, JsonOptions), explicitDisplayTitle);
        }
        catch (JsonException)
        {
            return new(argumentsJson, null);
        }
    }

    private static string? NormalizeExplicitDisplayTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return CompactText(value.Trim(), 96);
    }

    private static string VisualTitle(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return CleanVisualTitle(candidate);
        }

        return "Image";
    }

    private static string CleanVisualTitle(string value)
    {
        var title = value.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(title)
            ? "Image"
            : CompactText(title, 96);
    }

    private static string CompactText(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength
            ? compact
            : compact[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string CleanVisualFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((string.IsNullOrWhiteSpace(value) ? fallback : value.Trim())
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string ExtensionForVisualContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";

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

    private static SpriteSheetRect? ReadRectArgument(PendingToolCall pendingCall, string name)
    {
        var arguments = ToolCallArguments.ParseObjectOrNull(pendingCall.ArgumentsJson) ?? pendingCall.Content.Arguments;
        if (arguments is null || !arguments.TryGetValue(name, out var value))
            return null;

        if (value is SpriteSheetRect rect)
            return rect;

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            try
            {
                return JsonSerializer.Deserialize<SpriteSheetRect>(element.GetRawText(), JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private int MaxGenerationRoundImages()
    {
        var configuredMax = agentOptions.Value.MaxImagesPerGenerationRound <= 0
            ? imageOptions.Value.MaxOutputs
            : agentOptions.Value.MaxImagesPerGenerationRound;
        var imageMax = Math.Max(1, imageOptions.Value.MaxOutputs);
        return Math.Clamp(configuredMax, 1, imageMax);
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

    private async Task<AssistantMessage> PersistToolResultAsync(
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
        return toolMessage;
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
        int TextOffset,
        string? ExplicitDisplayTitle);

    private sealed record ToolDisplayMetadata(
        string ArgumentsJson,
        string? ExplicitDisplayTitle);

    private sealed record ToolVisualDraft(
        string Title,
        string Caption,
        string SourceKind,
        Guid? SourceRefId,
        string ContentType,
        string FileName,
        int? Width,
        int? Height,
        byte[]? Data)
    {
        public AssistantMessageVisual ToVisual(Guid messageId, string? toolCallId, int sortOrder) =>
            new()
            {
                AssistantMessageId = messageId,
                ToolCallId = toolCallId,
                SortOrder = sortOrder,
                Title = Title,
                Caption = Caption,
                SourceKind = SourceKind,
                SourceRefId = SourceRefId,
                ContentType = ContentType,
                FileName = FileName,
                Width = Width,
                Height = Height,
                Data = Data,
            };
    }

    private sealed record ToolInvocationOutcome(
        string Result,
        string? Error,
        bool Cancelled,
        double DurationMs,
        IReadOnlyList<AIContent> ModelOnlyContents);
}
