using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PixelChat.Art;
using PixelChat.Llm;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(
    IArtWorkflowService workflow,
    IWorkspaceVisibleStateStore visibleState,
    IImageGenerationRuntime imageRuntime,
    IOptions<AgentOptions> agentOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    {
        "attach_chat_attachment",
        "clear_chat_attachments",
        "switch_workspace_mode",
        "mark_asset",
        "update_sprite_sheet_frames",
        "normalize_sprite_sheet",
        "reset_sprite_sheet_to_original",
        "attach_sprite_sheet_frames",
        "run_generation_round",
        "save_prompt_recipe",
        "revert_recipe_version",
        "create_sprite_sheet",
        "review_sprite_animation",
    };

    public IList<AITool> Build(Guid projectId, AssistantTurnGenerationBudget budget) =>
    [
        AIFunctionFactory.Create(
            method: () => ListWorkspaceStateAsync(projectId),
            name: "list_workspace_state",
            description: "Read only the visible PixelChat UI state: project, active tab, provider status, visible chat attachments, and the active page's selected/draft form context. This intentionally omits broad libraries; use list/read asset, recipe, batch, or sprite-sheet tools for broader data. This is read-only."),

        AIFunctionFactory.Create(
            method: (string? kind = null, string? query = null, bool? favorite = null, int? limit = null) =>
                workflow.ListAssetsJsonAsync(projectId, kind, query, favorite, limit),
            name: "list_assets",
            description: "List compact asset metadata for the current project. Optional kind values include generated, imported, edited, cropped, and spriteSheet. This omits image bytes; use read_asset to inspect an image."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => workflow.ReadAssetJsonAsync(projectId, assetId),
            name: "read_asset",
            description: "Read an asset image for inspection. The JSON result returns metadata only, while the full image is delivered to the model as model-only image content for this tool call. This is read-only and does not attach the asset to visible chat context."),

        AIFunctionFactory.Create(
            method: (string? query = null, int? limit = null) =>
                workflow.ListPromptRecipesJsonAsync(projectId, query, limit),
            name: "list_recipes",
            description: "List compact saved prompt recipe summaries for the current project, including current version and single example image id. Use read_recipe for full reusable style and production guidance."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ReadPromptRecipeJsonAsync(projectId, recipeId),
            name: "read_recipe",
            description: "Read a saved prompt recipe's full reusable guide, durable rules, avoid rules, notes, preferred defaults, current version, and single example image id. This is read-only."),

        AIFunctionFactory.Create(
            method: (Guid? recipeId,
                string name,
                string promptTemplate,
                string changeSummary,
                string? assetType = null,
                string[]? styleRules = null,
                string[]? avoidRules = null,
                Guid? exampleAssetId = null,
                string? preferredSize = null,
                string? notes = null,
                CancellationToken cancellationToken = default) =>
                SavePromptRecipeToolAsync(projectId, recipeId, name, promptTemplate, changeSummary, assetType, styleRules, avoidRules, exampleAssetId, preferredSize, notes, cancellationToken),
            name: "save_prompt_recipe",
            description: "Create or update a durable prompt recipe directly during autonomous iteration. Recipes are reusable style guides, not one-off task prompts. The optional exampleAssetId is automatically sent as a style reference whenever the recipe is used; when an iteration produces an accepted result, set the best output as the example. Always provide a meaningful changeSummary. Every save is versioned and revertible."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ListPromptRecipeVersionsJsonAsync(projectId, recipeId),
            name: "list_recipe_versions",
            description: "List the append-only version history for a saved prompt recipe. This is read-only."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, int version, CancellationToken cancellationToken = default) =>
                RevertPromptRecipeToolAsync(projectId, recipeId, version, cancellationToken),
            name: "revert_recipe_version",
            description: "Restore an older prompt recipe snapshot as a new assistant-authored version. This is non-destructive and appends a new version entry."),

        AIFunctionFactory.Create(
            method: (string? status = null, int? limit = null) =>
                workflow.ListGenerationBatchesJsonAsync(projectId, status, limit),
            name: "list_batches",
            description: "List compact generation/edit batch history for the current project. Use read_batch for full prompt, state, input, and output details."),

        AIFunctionFactory.Create(
            method: (Guid batchId) => workflow.ReadGenerationBatchJsonAsync(projectId, batchId),
            name: "read_batch",
            description: "Read full metadata for one generation or edit batch. This is read-only and does not include image bytes."),

        AIFunctionFactory.Create(
            method: (
                string specificRequest,
                string? negativePrompt = null,
                string? size = null,
                string? background = null,
                int count = 2,
                Guid[]? referenceAssetIds = null,
                Guid? editSourceAssetId = null,
                Guid? recipeId = null,
                CancellationToken cancellationToken = default) =>
                RunGenerationRoundAsync(projectId, budget, specificRequest, negativePrompt, size, background, count, referenceAssetIds, editSourceAssetId, recipeId, cancellationToken),
            name: "run_generation_round",
            description: "Run one autonomous generation or edit round and wait for completion. To test recipe changes, first save the recipe revision and pass recipeId. The recipe's example image is auto-included as a reference; do not also pass it in referenceAssetIds. editSourceAssetId switches to image edit mode. Outputs are returned as model-only images. Counts against the fixed per-turn generation-round budget."),

        AIFunctionFactory.Create(
            method: (int? limit = null) => workflow.ListSpriteSheetsJsonAsync(projectId, limit),
            name: "list_sprite_sheets",
            description: "List compact sprite-sheet definitions for the current project. Use read_sprite_sheet for layout and frame boxes."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId) => workflow.ReadSpriteSheetJsonAsync(projectId, spriteSheetId),
            name: "read_sprite_sheet",
            description: "Read a sprite sheet's layout and frame boxes without returning preview image bytes. Use attach_sprite_sheet_frames when frame previews need to be visible image context."),

        AIFunctionFactory.Create(
            method: (Guid sourceAssetId, CancellationToken cancellationToken = default) =>
                CreateSpriteSheetAsync(projectId, sourceAssetId, cancellationToken),
            name: "create_sprite_sheet",
            description: "Create or select a sprite-sheet definition from an existing generated or imported asset and switch the visible workspace to Sprites."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId, int maxFrames = 12, CancellationToken cancellationToken = default) =>
                ReviewSpriteAnimationAsync(projectId, spriteSheetId, maxFrames, cancellationToken),
            name: "review_sprite_animation",
            description: "Review a sprite animation from saved frame records. Returns motion metrics in JSON and supplies ordered frame, onion-skin, and filmstrip images as model-only content."),

        AIFunctionFactory.Create(
            method: (string type, Guid refId, string? label = null) => AttachContextAsync(projectId, type, refId, label),
            name: "attach_chat_attachment",
            description: "Attach an existing asset, mask, crop, sprite frame, prompt recipe, or generation batch to the visible chat attachments."),

        AIFunctionFactory.Create(
            method: () => ClearContextAsync(projectId),
            name: "clear_chat_attachments",
            description: "Clear all visible chat attachments for the current project."),

        AIFunctionFactory.Create(
            method: (string mode) => SwitchWorkspaceModeAsync(projectId, mode),
            name: "switch_workspace_mode",
            description: "Switch the visible workspace mode. Allowed values: generate, compare, edit, sprites, recipes, assets."),

        AIFunctionFactory.Create(
            method: (
                string? prompt = null,
                string? negativePrompt = null,
                string? size = null,
                string? background = null,
                Guid? recipeId = null,
                int? count = null,
                Guid[]? referenceAssetIds = null) => DraftGenerateFormAsync(prompt, negativePrompt, size, background, recipeId, count, referenceAssetIds),
            name: "draft_generate_form",
            description: "Draft values for the Generate form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. Use recipeId to select a saved reusable recipe guide for the asset class; keep prompt focused on the new one-off asset request and omit fields that should stay unchanged. This does not run image generation; the user reviews the form and clicks Generate manually."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                string? size = null,
                string? background = null,
                Guid? recipeId = null,
                int count = 1) => DraftEditFormAsync(prompt, size, background, recipeId, count),
            name: "draft_edit_form",
            description: "Draft values for the current Edit form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. Use recipeId to select a saved reusable recipe guide; keep prompt focused on the requested edit or animation, not the reusable recipe text. This does not choose an asset or run an image edit; the user selects an asset, may paint/review a mask for targeted edits, and clicks Send Edit manually."),

        AIFunctionFactory.Create(
            method: (
                string name,
                string promptTemplate,
                string? assetType = null,
                string[]? styleRules = null,
                string[]? avoidRules = null,
                Guid? exampleAssetId = null,
                string? preferredSize = null,
                string? notes = null) => DraftPromptRecipeFormAsync(name, promptTemplate, assetType, styleRules, avoidRules, exampleAssetId, preferredSize, notes),
            name: "draft_prompt_recipe_form",
            description: "Draft values for the prompt recipe editor. Recipes are reusable style and production guides for repeatable asset classes, not specific one-off asset prompts. Use exampleAssetId for the single image that should anchor future recipe generations. Keep promptTemplate and styleRules durable across many future requests, and put only reusable constraints in avoidRules. This does not save a recipe; the user reviews the form and clicks Save manually."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                int? expectedFrames = null,
                string? layoutHint = null,
                string? backgroundMode = null) => DetectSpriteSheetFramesAsync(projectId, sourceAssetId, expectedFrames, layoutHint, backgroundMode),
            name: "detect_sprite_sheet_frames",
            description: "Read-only sprite-sheet image analysis for an existing source asset. Detects connected foreground objects using alpha or magenta key-color background and returns row-major boxes plus editable shape paths."),

        AIFunctionFactory.Create(
            method: (
                Guid spriteSheetId,
                int rows,
                int columns,
                int cellWidth,
                int cellHeight,
                int? padding = null,
                int? gutter = null,
                int? fps = null,
                bool? loop = null,
                string? horizontalAnchor = null,
                string? verticalAnchor = null,
                SpriteSheetFrameUpdateView[]? frames = null) => UpdateSpriteSheetFramesAsync(projectId, spriteSheetId, rows, columns, cellWidth, cellHeight, padding, gutter, fps, loop, horizontalAnchor, verticalAnchor, frames),
            name: "update_sprite_sheet_frames",
            description: "Update the visible Sprites workspace frame boxes, optional shape paths, sheet-wide alignment anchors, and layout settings without changing the working image bytes. Use detected boxes/shapes or user-requested adjustments."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId) => NormalizeSpriteSheetAsync(projectId, spriteSheetId),
            name: "normalize_sprite_sheet",
            description: "Normalize the selected sprite sheet by cropping saved boxes/shapes, applying padding/gutter/grid/alignment settings, stitching a new working PNG, and rebasing frame boxes/shapes."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId) => ResetSpriteSheetToOriginalAsync(projectId, spriteSheetId),
            name: "reset_sprite_sheet_to_original",
            description: "Reset the selected working sprite sheet to its immutable original image and clear all frame records."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId, Guid[]? frameIds = null) => AttachSpriteSheetFramesAsync(projectId, spriteSheetId, frameIds),
            name: "attach_sprite_sheet_frames",
            description: "Attach one or more saved sprite frame previews to visible chat context. Omit frameIds to attach every frame in the sheet."),

        AIFunctionFactory.Create(
            method: (Guid assetId, bool? favorite = null, string? notes = null) =>
                MarkAssetAsync(projectId, assetId, favorite, notes),
            name: "mark_asset",
            description: "Mark an asset as favorite/unfavorite and optionally update notes."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => ExportAssetAsync(projectId, assetId),
            name: "export_asset",
            description: "Prepare an existing asset for export by identifying it for the visible export modal. Export uses a persisted applied-step stack with key-color cleanup as the default next step; the user can apply fast cleanup, key-color cleanup, and Local AI in sequence, choose None to download the current preview without adding a processing step, reset to the original image, then download the current PNG."),
    ];

    public bool IsWorkspaceMutation(string toolName) => WorkspaceMutationTools.Contains(toolName);

    private async Task<string> ListWorkspaceStateAsync(Guid projectId)
    {
        var snapshot = visibleState.Get(projectId);
        if (snapshot is not null)
        {
            return JsonSerializer.Serialize(new
            {
                snapshotMissing = false,
                note = "Only the active visible page section is populated. Use focused list/read tools for broader project data.",
                state = snapshot,
            }, JsonOptions);
        }

        return await workflow.GetWorkspaceStateJsonAsync(projectId);
    }

    private async Task<string> AttachContextAsync(Guid projectId, string type, Guid refId, string? label)
    {
        var attachment = await workflow.AttachContextAsync(projectId, ParseAttachmentType(type), refId, label);
        return JsonSerializer.Serialize(attachment, JsonOptions);
    }

    private async Task<string> ClearContextAsync(Guid projectId)
    {
        await workflow.ClearContextAsync(projectId);
        return "Chat attachments cleared.";
    }

    private async Task<string> SwitchWorkspaceModeAsync(Guid projectId, string mode)
    {
        await workflow.SetWorkspaceModeAsync(projectId, ParseWorkspaceMode(mode));
        return $"Workspace mode switched to {mode}.";
    }

    private async Task<string> SavePromptRecipeToolAsync(
        Guid projectId,
        Guid? recipeId,
        string name,
        string promptTemplate,
        string changeSummary,
        string? assetType,
        string[]? styleRules,
        string[]? avoidRules,
        Guid? exampleAssetId,
        string? preferredSize,
        string? notes,
        CancellationToken cancellationToken)
    {
        PromptRecipeView saved;
        if (recipeId is Guid existingRecipeId)
        {
            saved = await workflow.UpdatePromptRecipeAsync(projectId, existingRecipeId, new UpdatePromptRecipeRequest(
                name,
                assetType ?? string.Empty,
                promptTemplate,
                styleRules ?? [],
                avoidRules ?? [],
                exampleAssetId,
                "openai-account",
                string.Empty,
                string.IsNullOrWhiteSpace(preferredSize) ? "auto" : preferredSize,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }
        else
        {
            saved = await workflow.SavePromptRecipeAsync(projectId, new SavePromptRecipeRequest(
                name,
                assetType ?? string.Empty,
                promptTemplate,
                styleRules ?? [],
                avoidRules ?? [],
                exampleAssetId,
                "openai-account",
                string.Empty,
                string.IsNullOrWhiteSpace(preferredSize) ? "auto" : preferredSize,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            recipeId = saved.Id,
            recipeName = saved.Name,
            version = saved.CurrentVersion,
            saved.ExampleAssetId,
            message = "Prompt recipe saved and versioned.",
        }, JsonOptions);
    }

    private async Task<string> RevertPromptRecipeToolAsync(
        Guid projectId,
        Guid recipeId,
        int version,
        CancellationToken cancellationToken)
    {
        var saved = await workflow.RevertPromptRecipeAsync(projectId, recipeId, version, "assistant", cancellationToken);
        return JsonSerializer.Serialize(new
        {
            recipeId = saved.Id,
            recipeName = saved.Name,
            version = saved.CurrentVersion,
            saved.ExampleAssetId,
            revertedTo = version,
            message = $"Prompt recipe reverted to version {version} as a new version.",
        }, JsonOptions);
    }

    private async Task<string> RunGenerationRoundAsync(
        Guid projectId,
        AssistantTurnGenerationBudget budget,
        string specificRequest,
        string? negativePrompt,
        string? size,
        string? background,
        int count,
        Guid[]? referenceAssetIds,
        Guid? editSourceAssetId,
        Guid? recipeId,
        CancellationToken cancellationToken)
    {
        if (budget.IsExhausted)
        {
            return JsonSerializer.Serialize(new
            {
                budgetExhausted = true,
                budget.RoundsUsed,
                budget.MaxRounds,
                roundsRemaining = 0,
                message = "Stop iterating, present the best result so far, and summarize recipe changes.",
            }, JsonOptions);
        }

        if (imageRuntime.HasRunningBatch(projectId))
        {
            return JsonSerializer.Serialize(new
            {
                error = "An image generation batch is already running for this project. Wait for it to finish before starting another generation round.",
                budget.RoundsUsed,
                budget.MaxRounds,
            }, JsonOptions);
        }

        var round = budget.Consume();
        var outputCount = ClampGenerationRoundCount(count);
        var normalizedBackground = NormalizeBackground(background) ?? "auto";
        var normalizedSize = string.IsNullOrWhiteSpace(size) ? "auto" : size.Trim();
        var references = referenceAssetIds ?? [];
        GenerationBatchView batch;
        if (editSourceAssetId is Guid sourceAssetId)
        {
            batch = await imageRuntime.StartEditImageAsync(projectId, new EditImageRequest(
                sourceAssetId,
                specificRequest,
                normalizedSize,
                outputCount,
                normalizedBackground,
                recipeId,
                null,
                null,
                references), cancellationToken);
        }
        else
        {
            batch = await imageRuntime.StartGenerateImagesAsync(projectId, new GenerateImagesRequest(
                specificRequest,
                negativePrompt ?? string.Empty,
                normalizedSize,
                outputCount,
                normalizedBackground,
                recipeId,
                references,
                ParentBatchId: null), cancellationToken);
        }

        var timeoutSeconds = agentOptions.Value.GenerationRoundWaitTimeoutSeconds <= 0
            ? 600
            : agentOptions.Value.GenerationRoundWaitTimeoutSeconds;
        var completed = await imageRuntime.WaitForBatchCompletionAsync(batch.Id, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        var batchJson = await workflow.ReadGenerationBatchJsonAsync(projectId, batch.Id, cancellationToken);
        using var document = JsonDocument.Parse(batchJson);
        return JsonSerializer.Serialize(new
        {
            round,
            budget.RoundsUsed,
            budget.MaxRounds,
            roundsRemaining = Math.Max(0, budget.MaxRounds - budget.RoundsUsed),
            timedOut = !completed,
            batch = document.RootElement.Clone(),
        }, JsonOptions);
    }

    private async Task<string> CreateSpriteSheetAsync(
        Guid projectId,
        Guid sourceAssetId,
        CancellationToken cancellationToken)
    {
        var sheet = await workflow.StartSpriteSheetEditAsync(projectId, sourceAssetId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            spriteSheetId = sheet.Id,
            sheet.SourceAssetId,
            workingAssetId = sheet.WorkingAssetId,
            sheet.Rows,
            sheet.Columns,
            frameCount = sheet.Frames.Count,
            message = "Sprite sheet is active in the Sprites workspace.",
        }, JsonOptions);
    }

    private async Task<string> ReviewSpriteAnimationAsync(
        Guid projectId,
        Guid spriteSheetId,
        int maxFrames,
        CancellationToken cancellationToken)
    {
        var review = await workflow.BuildSpriteAnimationReviewAsync(projectId, spriteSheetId, maxFrames, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            review.SpriteSheetId,
            review.FrameCount,
            review.Rows,
            review.Columns,
            review.Fps,
            review.Loop,
            review.Metrics,
            modelOnlyImages = review.Images.Select(image => new { image.Label, image.FileName, image.ContentType }).ToList(),
        }, JsonOptions);
    }

    private static Task<string> DraftGenerateFormAsync(
        string? prompt,
        string? negativePrompt,
        string? size,
        string? background,
        Guid? recipeId,
        int? count,
        Guid[]? referenceAssetIds)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Generate,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt,
            NegativePrompt: negativePrompt,
            Size: size,
            Background: NormalizeBackground(background),
            Count: count is int countValue ? ClampCount(countValue) : null,
            PromptRecipeId: recipeId,
            ReferenceAssetIds: referenceAssetIds);
        return Task.FromResult(JsonSerializer.Serialize(draft, JsonOptions));
    }

    private static Task<string> DraftEditFormAsync(
        string prompt,
        string? size,
        string? background,
        Guid? recipeId,
        int count)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Edit,
            Prompt: prompt,
            Size: size ?? "auto",
            Background: NormalizeBackground(background),
            Count: ClampCount(count),
            PromptRecipeId: recipeId);
        return Task.FromResult(JsonSerializer.Serialize(draft, JsonOptions));
    }

    private static Task<string> DraftPromptRecipeFormAsync(
        string name,
        string promptTemplate,
        string? assetType,
        string[]? styleRules,
        string[]? avoidRules,
        Guid? exampleAssetId,
        string? preferredSize,
        string? notes)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Recipe,
            RecipeExampleAssetId: exampleAssetId,
            RecipeName: name,
            AssetType: assetType ?? string.Empty,
            PromptTemplate: promptTemplate,
            StyleRules: styleRules ?? [],
            AvoidRules: avoidRules ?? [],
            Notes: notes ?? string.Empty,
            PreferredSize: preferredSize ?? "auto");
        return Task.FromResult(JsonSerializer.Serialize(draft, JsonOptions));
    }

    private async Task<string> DetectSpriteSheetFramesAsync(
        Guid projectId,
        Guid sourceAssetId,
        int? expectedFrames,
        string? layoutHint,
        string? backgroundMode)
    {
        var result = await workflow.DetectSpriteSheetFramesAsync(
            projectId,
            new SpriteSheetDetectionRequest(
                sourceAssetId,
                expectedFrames,
                layoutHint,
                backgroundMode));
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private async Task<string> UpdateSpriteSheetFramesAsync(
        Guid projectId,
        Guid spriteSheetId,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        int? padding,
        int? gutter,
        int? fps,
        bool? loop,
        string? horizontalAnchor,
        string? verticalAnchor,
        SpriteSheetFrameUpdateView[]? frames)
    {
        var saved = await workflow.UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
            spriteSheetId,
            Math.Clamp(rows, 1, 32),
            Math.Clamp(columns, 1, 64),
            Math.Clamp(cellWidth, 1, 8192),
            Math.Clamp(cellHeight, 1, 8192),
            padding is int paddingValue ? Math.Clamp(paddingValue, 0, 4096) : 8,
            gutter is int gutterValue ? Math.Clamp(gutterValue, 0, 4096) : 16,
            fps is int fpsValue ? Math.Clamp(fpsValue, 1, 60) : 8,
            loop ?? true,
            NormalizeHorizontalAnchor(horizontalAnchor),
            NormalizeVerticalAnchor(verticalAnchor),
            frames ?? []));
        return JsonSerializer.Serialize(saved, JsonOptions);
    }

    private async Task<string> ResetSpriteSheetToOriginalAsync(Guid projectId, Guid spriteSheetId)
    {
        var saved = await workflow.ResetSpriteSheetToOriginalAsync(projectId, spriteSheetId);
        return JsonSerializer.Serialize(new
        {
            saved.Id,
            saved.WorkingAssetId,
            saved.SourceAssetId,
            message = "Sprite sheet reset to original image and frame records cleared.",
        }, JsonOptions);
    }

    private async Task<string> NormalizeSpriteSheetAsync(Guid projectId, Guid spriteSheetId)
    {
        var saved = await workflow.NormalizeSpriteSheetAsync(projectId, spriteSheetId);
        return JsonSerializer.Serialize(new
        {
            saved.Id,
            saved.WorkingAssetId,
            saved.SourceAssetId,
            frameCount = saved.Frames.Count,
            message = "Sprite sheet normalized.",
        }, JsonOptions);
    }

    private async Task<string> AttachSpriteSheetFramesAsync(Guid projectId, Guid spriteSheetId, Guid[]? frameIds)
    {
        var attachments = await workflow.AttachSpriteSheetFramesAsync(projectId, spriteSheetId, frameIds);
        return JsonSerializer.Serialize(attachments, JsonOptions);
    }

    private async Task<string> MarkAssetAsync(Guid projectId, Guid assetId, bool? favorite, string? notes)
    {
        await workflow.MarkAssetAsync(projectId, assetId, favorite, notes);
        return $"Asset {assetId} updated.";
    }

    private static Task<string> ExportAssetAsync(Guid projectId, Guid assetId) =>
        Task.FromResult(JsonSerializer.Serialize(new
        {
            projectId,
            assetId,
            message = "Use the visible Export button for this asset to open export processing options.",
        }, JsonOptions));

    private static ChatContextAttachmentType ParseAttachmentType(string type) =>
        NormalizeToken(type) switch
        {
            "asset" => ChatContextAttachmentType.Asset,
            "mask" => ChatContextAttachmentType.Mask,
            "crop" => ChatContextAttachmentType.Crop,
            "spriteframe" or "sprite" or "frame" => ChatContextAttachmentType.SpriteFrame,
            "promptrecipe" or "recipe" => ChatContextAttachmentType.PromptRecipe,
            "generationbatch" or "batch" => ChatContextAttachmentType.GenerationBatch,
            _ => throw new InvalidOperationException($"Unknown context attachment type '{type}'.")
        };

    private static WorkspaceMode ParseWorkspaceMode(string mode) =>
        NormalizeToken(mode) switch
        {
            "generate" => WorkspaceMode.Generate,
            "compare" => WorkspaceMode.Compare,
            "edit" => WorkspaceMode.Edit,
            "sprites" or "sprite" or "spritesheet" or "spritesheets" => WorkspaceMode.Sprites,
            "recipes" or "recipe" => WorkspaceMode.Recipes,
            "assets" or "asset" => WorkspaceMode.Assets,
            _ => throw new InvalidOperationException($"Unknown workspace mode '{mode}'.")
        };

    private static string NormalizeToken(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private int ClampGenerationRoundCount(int count)
    {
        var configuredMax = agentOptions.Value.MaxImagesPerGenerationRound <= 0
            ? 2
            : agentOptions.Value.MaxImagesPerGenerationRound;
        var max = Math.Clamp(configuredMax, 1, 2);
        return Math.Clamp(count <= 0 ? max : count, 1, max);
    }

    private static int ClampCount(int count) => Math.Clamp(count <= 0 ? 1 : count, 1, 4);

    private static string NormalizeHorizontalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center",
        };

    private static string NormalizeVerticalAnchor(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "top" => "top",
            "middle" or "center" => "middle",
            _ => "bottom",
        };

    private static string? NormalizeBackground(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "removable" or "removablecolor" or "removable-color" or "transparent" or "chroma" or "chromakey" or "chroma-key" => "removable",
            "opaque" => "opaque",
            "auto" => "auto",
            null or "" => null,
            _ => "auto",
        };
}
