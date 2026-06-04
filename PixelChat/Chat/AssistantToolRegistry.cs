using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(IArtWorkflowService workflow)
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "list_workspace_state"
    };

    public IList<AITool> Build(Guid projectId) =>
    [
        AIFunctionFactory.Create(
            method: () => workflow.GetWorkspaceStateJsonAsync(projectId),
            name: "list_workspace_state",
            description: "Read the current PixelChat workspace state, including active asset, visible chat context chips, recent batches, recipes, and provider status. This is read-only."),

        AIFunctionFactory.Create(
            method: (string type, Guid refId, string? label = null) => AttachContextAsync(projectId, type, refId, label),
            name: "attach_context",
            description: "Attach an existing asset, mask, crop, prompt recipe, or generation batch to the visible chat context chips."),

        AIFunctionFactory.Create(
            method: () => ClearContextAsync(projectId),
            name: "clear_context",
            description: "Clear all visible chat context chips for the current project."),

        AIFunctionFactory.Create(
            method: (string mode) => SwitchWorkspaceModeAsync(projectId, mode),
            name: "switch_workspace_mode",
            description: "Switch the visible workspace mode. Allowed values: generate, compare, edit."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => SelectAssetAsync(projectId, assetId),
            name: "select_asset",
            description: "Select an existing asset as the active asset in the visible workspace."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                int count = 1,
                string? negativePrompt = null,
                string? size = null,
                Guid? recipeId = null,
                Guid[]? referenceAssetIds = null,
                Guid? parentBatchId = null) => GenerateImagesAsync(projectId, prompt, count, negativePrompt, size, recipeId, referenceAssetIds, parentBatchId),
            name: "generate_images",
            description: "Generate a batch of game-art images from the prompt. This is a paid/mutating action and requires user confirmation in PixelChat."),

        AIFunctionFactory.Create(
            method: (
                Guid assetId,
                string prompt,
                int count = 1,
                Guid? maskId = null,
                string? size = null,
                Guid[]? referenceAssetIds = null) => EditImageAsync(projectId, assetId, prompt, count, maskId, size, referenceAssetIds),
            name: "edit_image",
            description: "Run a masked edit on an existing asset using an existing saved mask. This is a paid/mutating action and requires user confirmation in PixelChat."),

        AIFunctionFactory.Create(
            method: (
                string name,
                string promptTemplate,
                string? assetType = null,
                string[]? styleRules = null,
                string[]? avoidRules = null,
                Guid[]? exampleAssetIds = null,
                string? preferredSize = null,
                string? notes = null) => SavePromptRecipeAsync(projectId, name, promptTemplate, assetType, styleRules, avoidRules, exampleAssetIds, preferredSize, notes),
            name: "save_prompt_recipe",
            description: "Save a visible prompt recipe for later reuse."),

        AIFunctionFactory.Create(
            method: (Guid assetId, bool? favorite = null, bool? rejected = null, string? notes = null) =>
                MarkAssetAsync(projectId, assetId, favorite, rejected, notes),
            name: "mark_asset",
            description: "Mark an asset as favorite/rejected and optionally update notes."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => UseAsReferenceAsync(projectId, assetId),
            name: "use_as_reference",
            description: "Mark an asset as a reusable reference and attach it to the visible chat context."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => ExportAssetAsync(projectId, assetId),
            name: "export_asset",
            description: "Prepare an existing asset for export by returning its export file metadata. The user still controls the actual browser download."),
    ];

    public bool IsReadOnly(string toolName) => ReadOnlyTools.Contains(toolName);

    private async Task<string> AttachContextAsync(Guid projectId, string type, Guid refId, string? label)
    {
        var attachment = await workflow.AttachContextAsync(projectId, ParseAttachmentType(type), refId, label);
        return JsonSerializer.Serialize(attachment);
    }

    private async Task<string> ClearContextAsync(Guid projectId)
    {
        await workflow.ClearContextAsync(projectId);
        return "Context cleared.";
    }

    private async Task<string> SwitchWorkspaceModeAsync(Guid projectId, string mode)
    {
        await workflow.SetWorkspaceModeAsync(projectId, ParseWorkspaceMode(mode));
        return $"Workspace mode switched to {mode}.";
    }

    private async Task<string> SelectAssetAsync(Guid projectId, Guid assetId)
    {
        await workflow.SelectAssetAsync(projectId, assetId);
        return $"Selected asset {assetId}.";
    }

    private async Task<string> GenerateImagesAsync(
        Guid projectId,
        string prompt,
        int count,
        string? negativePrompt,
        string? size,
        Guid? recipeId,
        Guid[]? referenceAssetIds,
        Guid? parentBatchId)
    {
        var batch = await workflow.GenerateImagesAsync(projectId, new GenerateImagesRequest(
            prompt,
            negativePrompt ?? string.Empty,
            size ?? "auto",
            count,
            recipeId,
            referenceAssetIds ?? [],
            parentBatchId));
        return JsonSerializer.Serialize(batch);
    }

    private async Task<string> EditImageAsync(
        Guid projectId,
        Guid assetId,
        string prompt,
        int count,
        Guid? maskId,
        string? size,
        Guid[]? referenceAssetIds)
    {
        var batch = await workflow.EditImageAsync(projectId, new EditImageRequest(
            assetId,
            prompt,
            size ?? "auto",
            count,
            maskId,
            SourcePngDataUrl: null,
            MaskPngDataUrl: null,
            referenceAssetIds ?? []));
        return JsonSerializer.Serialize(batch);
    }

    private async Task<string> SavePromptRecipeAsync(
        Guid projectId,
        string name,
        string promptTemplate,
        string? assetType,
        string[]? styleRules,
        string[]? avoidRules,
        Guid[]? exampleAssetIds,
        string? preferredSize,
        string? notes)
    {
        var recipe = await workflow.SavePromptRecipeAsync(projectId, new SavePromptRecipeRequest(
            name,
            assetType ?? string.Empty,
            promptTemplate,
            styleRules ?? [],
            avoidRules ?? [],
            exampleAssetIds ?? [],
            PreferredProvider: "openai-account",
            PreferredModel: string.Empty,
            PreferredSize: preferredSize ?? string.Empty,
            Notes: notes ?? string.Empty));
        return JsonSerializer.Serialize(recipe);
    }

    private async Task<string> MarkAssetAsync(Guid projectId, Guid assetId, bool? favorite, bool? rejected, string? notes)
    {
        await workflow.MarkAssetAsync(projectId, assetId, favorite, rejected, notes);
        return $"Asset {assetId} updated.";
    }

    private async Task<string> UseAsReferenceAsync(Guid projectId, Guid assetId)
    {
        await workflow.UseAssetAsReferenceAsync(projectId, assetId);
        return $"Asset {assetId} marked as reference and attached to chat context.";
    }

    private static Task<string> ExportAssetAsync(Guid projectId, Guid assetId) =>
        Task.FromResult(JsonSerializer.Serialize(new
        {
            projectId,
            assetId,
            message = "Use the visible Export link for this asset in the UI.",
        }));

    private static ChatContextAttachmentType ParseAttachmentType(string type) =>
        NormalizeToken(type) switch
        {
            "asset" => ChatContextAttachmentType.Asset,
            "mask" => ChatContextAttachmentType.Mask,
            "crop" => ChatContextAttachmentType.Crop,
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
            _ => throw new InvalidOperationException($"Unknown workspace mode '{mode}'.")
        };

    private static string NormalizeToken(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
