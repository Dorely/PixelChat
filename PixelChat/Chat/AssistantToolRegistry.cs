using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(
    IArtWorkflowService workflow,
    IWorkspaceVisibleStateStore visibleState)
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
    };

    public IList<AITool> Build(Guid projectId) =>
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
            description: "List compact saved prompt recipe summaries for the current project. Use read_recipe for full reusable style and production guidance."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ReadPromptRecipeJsonAsync(projectId, recipeId),
            name: "read_recipe",
            description: "Read a saved prompt recipe's full reusable guide, durable rules, avoid rules, notes, preferred defaults, and passive example ids. This is read-only."),

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
            method: (int? limit = null) => workflow.ListSpriteSheetsJsonAsync(projectId, limit),
            name: "list_sprite_sheets",
            description: "List compact sprite-sheet definitions for the current project. Use read_sprite_sheet for layout and frame boxes."),

        AIFunctionFactory.Create(
            method: (Guid spriteSheetId) => workflow.ReadSpriteSheetJsonAsync(projectId, spriteSheetId),
            name: "read_sprite_sheet",
            description: "Read a sprite sheet's layout and frame boxes without returning preview image bytes. Use attach_sprite_sheet_frames when frame previews need to be visible image context."),

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
                Guid[]? exampleAssetIds = null,
                string? preferredSize = null,
                string? notes = null) => DraftPromptRecipeFormAsync(name, promptTemplate, assetType, styleRules, avoidRules, exampleAssetIds, preferredSize, notes),
            name: "draft_prompt_recipe_form",
            description: "Draft values for the prompt recipe editor. Recipes are reusable style and production guides for repeatable asset classes, not specific one-off asset prompts. Keep promptTemplate and styleRules durable across many future requests, and put only reusable constraints in avoidRules. This does not save a recipe; the user reviews the form and clicks Save manually."),

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
        return Task.FromResult(JsonSerializer.Serialize(draft));
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
        return Task.FromResult(JsonSerializer.Serialize(draft));
    }

    private static Task<string> DraftPromptRecipeFormAsync(
        string name,
        string promptTemplate,
        string? assetType,
        string[]? styleRules,
        string[]? avoidRules,
        Guid[]? exampleAssetIds,
        string? preferredSize,
        string? notes)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Recipe,
            ReferenceAssetIds: exampleAssetIds ?? [],
            RecipeName: name,
            AssetType: assetType ?? string.Empty,
            PromptTemplate: promptTemplate,
            StyleRules: styleRules ?? [],
            AvoidRules: avoidRules ?? [],
            Notes: notes ?? string.Empty,
            PreferredSize: preferredSize ?? "auto");
        return Task.FromResult(JsonSerializer.Serialize(draft));
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
