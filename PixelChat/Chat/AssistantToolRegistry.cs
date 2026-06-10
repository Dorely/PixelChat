using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(IArtWorkflowService workflow)
{
    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    {
        "attach_chat_attachment",
        "clear_chat_attachments",
        "switch_workspace_mode",
        "mark_asset",
        "update_sprite_sheet_frames",
        "reset_sprite_sheet_to_original",
        "attach_sprite_sheet_frames",
    };

    public IList<AITool> Build(Guid projectId) =>
    [
        AIFunctionFactory.Create(
            method: () => workflow.GetWorkspaceStateJsonAsync(projectId),
            name: "list_workspace_state",
            description: "Read the current PixelChat workspace state, including visible chat attachments, recent assets, batches, recipes, and provider status. This is read-only."),

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
            description: "Draft values for the Generate form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. Use recipeId to select a saved style recipe; keep prompt focused on the asset-specific request and omit fields that should stay unchanged. This does not run image generation; the user reviews the form and clicks Generate manually."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                string? size = null,
                string? background = null,
                int count = 1) => DraftEditFormAsync(prompt, size, background, count),
            name: "draft_edit_form",
            description: "Draft values for the current Edit form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. This does not choose an asset or run an image edit; the user selects an asset, may paint/review a mask for targeted edits, and clicks Send Edit manually."),

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
            description: "Draft values for the prompt recipe editor. This does not save a recipe; the user reviews the form and clicks Save manually."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                int? expectedFrames = null,
                string? layoutHint = null,
                string? backgroundMode = null) => DetectSpriteSheetFramesAsync(projectId, sourceAssetId, expectedFrames, layoutHint, backgroundMode),
            name: "detect_sprite_sheet_frames",
            description: "Read-only sprite-sheet image analysis for an existing source asset. Detects foreground frame bounding boxes using alpha or magenta key-color background and returns row-major pixel boxes."),

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
                SpriteSheetFrameUpdateView[]? frames = null) => UpdateSpriteSheetFramesAsync(projectId, spriteSheetId, rows, columns, cellWidth, cellHeight, padding, gutter, fps, loop, frames),
            name: "update_sprite_sheet_frames",
            description: "Update the visible Sprites workspace working sheet. Sets frame boxes/layout, renders the normalized working PNG in place, rebases frame boxes, and autosaves frame records. Use detected boxes or user-requested adjustments."),

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

    private async Task<string> AttachContextAsync(Guid projectId, string type, Guid refId, string? label)
    {
        var attachment = await workflow.AttachContextAsync(projectId, ParseAttachmentType(type), refId, label);
        return JsonSerializer.Serialize(attachment);
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
        int count)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Edit,
            Prompt: prompt,
            Size: size ?? "auto",
            Background: NormalizeBackground(background),
            Count: ClampCount(count));
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
        return JsonSerializer.Serialize(result);
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
            frames ?? []));
        return JsonSerializer.Serialize(saved);
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
        });
    }

    private async Task<string> AttachSpriteSheetFramesAsync(Guid projectId, Guid spriteSheetId, Guid[]? frameIds)
    {
        var attachments = await workflow.AttachSpriteSheetFramesAsync(projectId, spriteSheetId, frameIds);
        return JsonSerializer.Serialize(attachments);
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
        }));

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
