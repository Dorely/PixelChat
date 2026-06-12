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
            foreach (var pendingCall in pendingCalls)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new AssistantTurnError("Cancelled.", Cancelled: true);
                    yield break;
                }

                var outcome = await InvokeToolAsync(aiTools, pendingCall, projectId, cancellationToken);
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
                messages.Add(BuildCurrentUserMessage(message.Content, workbench));
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

    private async Task<List<ChatMessage>> BuildIdleModelMessagesAsync(
        IReadOnlyList<AssistantMessage> history,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var messages = BuildPersistedModelMessages(history);
        var workbench = await workflow.GetWorkbenchAsync(projectId, cancellationToken);
        if (workbench.Attachments.Count > 0)
            messages.Add(BuildCurrentUserMessage(string.Empty, workbench));

        return messages;
    }

    private static List<ChatMessage> BuildPersistedModelMessages(IReadOnlyList<AssistantMessage> history)
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

    private static ChatMessage BuildCurrentUserMessage(string text, WorkbenchView workbench)
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

                case ChatContextAttachmentType.SpriteFrame:
                    var frame = workbench.SpriteSheets
                        .SelectMany(sheet => sheet.Frames)
                        .FirstOrDefault(item => item.Id == attachment.RefId);
                    if (frame is not null)
                    {
                        contents.Add(new DataContent(frame.PreviewDataUrl, "image/png")
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

            if (string.Equals(pendingCall.Name, "review_sprite_animation", StringComparison.Ordinal))
                return await BuildSpriteAnimationReviewModelOnlyContentsAsync(pendingCall, projectId, toolResult, cancellationToken);

            if (string.Equals(pendingCall.Name, "detect_sprite_sheet_frames", StringComparison.Ordinal))
                return await BuildSpriteSheetDetectionModelOnlyContentsAsync(projectId, toolResult, cancellationToken);
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
            new TextContent($"Model-only images: sprite animation review for sprite sheet {spriteSheetId}. Filenames identify sheet view, ordered frames, pairwise diffs, onion-skin, and filmstrip; JSON manifest fields include kind/frame indexes. These images are not attached to visible chat context."),
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

    private async Task<IReadOnlyList<AIContent>> BuildSpriteSheetDetectionModelOnlyContentsAsync(
        Guid projectId,
        string toolResult,
        CancellationToken cancellationToken)
    {
        var detection = JsonSerializer.Deserialize<SpriteSheetDetectionResult>(toolResult, JsonOptions);
        if (detection is null)
            return Array.Empty<AIContent>();

        var image = await workflow.BuildSpriteSheetDetectionAnnotatedSheetAsync(projectId, detection, cancellationToken);
        return
        [
            new TextContent($"Model-only image: annotated sprite-sheet detection view for source asset {detection.SourceAssetId}. Frame boxes are labeled by index and background mode is {detection.Background.Mode}. This image is not attached to visible chat context."),
            new DataContent(image.DataUrl, image.ContentType)
            {
                Name = image.FileName,
            },
        ];
    }

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
