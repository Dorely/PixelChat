using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(IArtWorkflowService workflow)
{
    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    {
        "attach_context",
        "clear_context",
        "switch_workspace_mode",
        "select_asset",
        "mark_asset",
        "use_as_reference"
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
            description: "Switch the visible workspace mode. Allowed values: generate, compare, edit, recipes."),

        AIFunctionFactory.Create(
            method: (Guid assetId) => SelectAssetAsync(projectId, assetId),
            name: "select_asset",
            description: "Select an existing asset as the active asset in the visible workspace."),

        AIFunctionFactory.Create(
            method: (
                string? prompt = null,
                string? negativePrompt = null,
                string? size = null,
                Guid? recipeId = null,
                int? count = null,
                Guid[]? referenceAssetIds = null) => DraftGenerateFormAsync(prompt, negativePrompt, size, recipeId, count, referenceAssetIds),
            name: "draft_generate_form",
            description: "Draft values for the Generate form. Use recipeId to select a saved style recipe; keep prompt focused on the asset-specific request and omit fields that should stay unchanged. This does not run image generation; the user reviews the form and clicks Generate manually."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                string? size = null,
                int count = 1) => DraftEditFormAsync(prompt, size, count),
            name: "draft_edit_form",
            description: "Draft values for the active asset Edit form. This does not run an image edit; the user paints/reviews the mask and clicks Send Masked Edit manually."),

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

    public bool IsWorkspaceMutation(string toolName) => WorkspaceMutationTools.Contains(toolName);

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

    private static Task<string> DraftGenerateFormAsync(
        string? prompt,
        string? negativePrompt,
        string? size,
        Guid? recipeId,
        int? count,
        Guid[]? referenceAssetIds)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Generate,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt,
            NegativePrompt: negativePrompt,
            Size: size,
            Count: count is int countValue ? ClampCount(countValue) : null,
            PromptRecipeId: recipeId,
            ReferenceAssetIds: referenceAssetIds);
        return Task.FromResult(JsonSerializer.Serialize(draft));
    }

    private static Task<string> DraftEditFormAsync(
        string prompt,
        string? size,
        int count)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Edit,
            Prompt: prompt,
            Size: size ?? "auto",
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
            "recipes" or "recipe" => WorkspaceMode.Recipes,
            _ => throw new InvalidOperationException($"Unknown workspace mode '{mode}'.")
        };

    private static string NormalizeToken(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static int ClampCount(int count) => Math.Clamp(count <= 0 ? 1 : count, 1, 4);
}
