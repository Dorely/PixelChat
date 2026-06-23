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
    IAssetAnimationWorkflowService assetAnimation,
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
        "switch_workspace_mode",
        "set_compare_review_set",
        "add_compare_review_items",
        "remove_compare_review_item",
        "clear_compare_review_set",
        "mark_asset",
        "update_sprite_sheet_frames",
        "normalize_sprite_sheet",
        "reset_sprite_sheet_to_original",
        "run_generation_round",
        "save_prompt_recipe",
        "revert_recipe_version",
        "create_sprite_sheet",
        "compose_sprite_sheet_from_images",
        "map_sprite_sheet_frames",
        "review_sprite_animation",
        "isolate_sprite_frame",
        "erase_sprite_frame_regions",
        "edit_sprite_frame",
        "clear_sprite_frame_working_image",
        "reassemble_sprite_sheet",
        "create_asset_profile",
        "plan_asset_animation",
        "render_animation_guide",
        "run_animation_candidates",
        "mark_animation_frames",
        "regenerate_animation_frames",
        "extract_animation_fixed_slots",
        "review_animation_job",
        "package_animation_job",
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
            description: "List compact asset metadata for the current project. Optional kind values include generated, imported, edited, cropped, spriteGuide, and spriteSheet. This omits image bytes; use read_asset to inspect an image."),

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
            description: "Run one generic autonomous generation or edit round and wait for completion. Do not use this as the first path for new animation requests such as walk cycles, tower rotations, projectiles, or explosions; use the asset-animation tools instead. For recipe tests, first save the recipe revision and pass recipeId. Outputs are returned as model-only images. Counts against the fixed per-turn generation-round budget."),

        AIFunctionFactory.Create(
            method: (
                Guid canonicalAssetId,
                Guid? styleAssetId = null,
                string? label = null,
                string? assetType = null,
                string? structureType = null,
                string[]? requiredFeatures = null,
                string[]? forbiddenChanges = null,
                bool frozen = true,
                CancellationToken cancellationToken = default) =>
                CreateAssetProfileAsync(projectId, canonicalAssetId, styleAssetId, label, assetType, structureType, requiredFeatures, forbiddenChanges, frozen, cancellationToken),
            name: "create_asset_profile",
            description: "Create a frozen asset profile from an existing canonical image before starting a new animation job. Use this for units, towers, projectiles, VFX, and prop-state animations. Returns concise profile metadata including selected chroma color; no image bytes."),

        AIFunctionFactory.Create(
            method: (
                Guid assetProfileId,
                string animationKind,
                string? facing = null,
                string? strategy = null,
                int? frameCount = null,
                int? fps = null,
                string? rootMotion = null,
                string? promptSummary = null,
                string? targetCellSize = null,
                CancellationToken cancellationToken = default) =>
                PlanAssetAnimationAsync(projectId, assetProfileId, animationKind, facing, strategy, frameCount, fps, rootMotion, promptSummary, targetCellSize, cancellationToken),
            name: "plan_asset_animation",
            description: "Compile the internal animation contract for a profile: motion/structure plan, frame specs, grid, slots, pivots, safe regions, chroma, and job budget. Use this before any new guided animation generation."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, CancellationToken cancellationToken = default) =>
                RenderAnimationGuideAsync(projectId, assetAnimationJobId, cancellationToken),
            name: "render_animation_guide",
            description: "Render the model-facing structure/layout guide for an animation job. Guides contain no text labels and are returned as model-only images after the tool result. Use before candidate generation."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, int? candidateCount = null, CancellationToken cancellationToken = default) =>
                RunAnimationCandidatesAsync(projectId, assetAnimationJobId, candidateCount, cancellationToken),
            name: "run_animation_candidates",
            description: "Generate guided strip/grid candidates for an animation job using ordered references: guide, canonical profile image, optional style reference. Uses the animation job budget, not the normal chat-turn round budget. Returns concise candidate/frame status JSON and model-only candidate images."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, CancellationToken cancellationToken = default) =>
                assetAnimation.ReadAnimationJobJsonAsync(projectId, assetAnimationJobId, cancellationToken),
            name: "read_animation_job",
            description: "Read concise state for one asset-animation job: status, IDs, budget, frame statuses, candidates, and next recommended action. This is read-only and intentionally omits full specs, prompts, and image bytes."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, AnimationFrameMark[] frames, CancellationToken cancellationToken = default) =>
                MarkAnimationFramesAsync(projectId, assetAnimationJobId, frames, cancellationToken),
            name: "mark_animation_frames",
            description: "Mark exact animation frames as accepted, rejected, warning, or repair_requested with short typed reasons. Use this after inspecting candidates; keep reasons concise because they feed repair routing."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, int[] frameNumbers, string? prompt = null, CancellationToken cancellationToken = default) =>
                RegenerateAnimationFramesAsync(projectId, assetAnimationJobId, frameNumbers, prompt, cancellationToken),
            name: "regenerate_animation_frames",
            description: "Regenerate exact failed frame numbers using single-frame guides plus profile references. Prefer this over full-strip regeneration when one or two frames fail. Returns concise status and model-only repair images."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, Guid? candidateId = null, CancellationToken cancellationToken = default) =>
                ExtractAnimationFixedSlotsAsync(projectId, assetAnimationJobId, candidateId, cancellationToken),
            name: "extract_animation_fixed_slots",
            description: "Extract accepted scaffolded animation frames by known layout slots into a normal sprite sheet. This preserves slot geometry and avoids per-frame alpha-bounds scaling. Returns concise frame QA and model-only sheet/preview feedback."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, CancellationToken cancellationToken = default) =>
                ReviewAnimationJobAsync(projectId, assetAnimationJobId, cancellationToken),
            name: "review_animation_job",
            description: "Run/record motion review for a fixed-slot animation job after extraction. Returns concise next action and model-only animation review images when a sprite sheet exists."),

        AIFunctionFactory.Create(
            method: (Guid assetAnimationJobId, CancellationToken cancellationToken = default) =>
                PackageAnimationJobAsync(projectId, assetAnimationJobId, cancellationToken),
            name: "package_animation_job",
            description: "Finalize an extracted animation job, put the animation and sheet in Compare, and leave the packaged sheet in the Sprites workspace for export."),

        AIFunctionFactory.Create(
            method: (int? limit = null) => workflow.ListSpriteSheetsJsonAsync(projectId, limit),
            name: "list_sprite_sheets",
            description: "List compact sprite-sheet definitions for the current project. For new animation jobs use read_animation_job; use sprite-sheet reads for packaged results or salvage/manual sheet work."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                ReadSpriteSheetToolAsync(projectId, spriteSheetId, cancellationToken),
            name: "read_sprite_sheet",
            description: "Read a sprite sheet's layout and frame boxes without returning preview image bytes. Use compare review-set tools when the user should review sprite-sheet results visually."),

        AIFunctionFactory.Create(
            method: (Guid sourceAssetId, CancellationToken cancellationToken = default) =>
                CreateSpriteSheetAsync(projectId, sourceAssetId, cancellationToken),
            name: "create_sprite_sheet",
            description: "Salvage/import tool: create or select a sprite-sheet definition from an existing generated or imported asset and switch to Sprites. For new animation requests, use the asset-animation tools instead."),

        AIFunctionFactory.Create(
            method: (
                Guid[] assetIds,
                Guid? spriteSheetId = null,
                int? insertAt = null,
                string? label = null,
                int? rows = null,
                int? columns = null,
                int? padding = null,
                int? gutter = null,
                int? fps = null,
                bool? loop = null,
                string? horizontalAnchor = null,
                string? verticalAnchor = null,
                CancellationToken cancellationToken = default) =>
                ComposeSpriteSheetFromImagesAsync(projectId, assetIds, spriteSheetId, insertAt, label, rows, columns, padding, gutter, fps, loop, horizontalAnchor, verticalAnchor, cancellationToken),
            name: "compose_sprite_sheet_from_images",
            description: "Salvage/manual tool: create or extend a sprite sheet from ordered individual PNG assets. For new generated animation jobs, prefer package_animation_job after fixed-slot extraction. Use this when frames already exist as separate PNG assets or the user explicitly wants manual composition."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, int maxFrames = 12, CancellationToken cancellationToken = default) =>
                ReviewSpriteAnimationAsync(projectId, spriteSheetId, maxFrames, cancellationToken),
            name: "review_sprite_animation",
            description: "Review a sprite animation from the current PNG working sheet. Returns motion metrics in JSON and supplies labeled frame images, an annotated sheet view, pairwise diffs, onion-skin, and filmstrip images as model-only content with manifest fields. For frames with hidden working images it also returns removed-vs-source overlays where red marks pixels erased from the source foreground; inspect these for clipped owned silhouette before declaring cleanup done."),

        AIFunctionFactory.Create(
            method: (string mode) => SwitchWorkspaceModeAsync(projectId, mode),
            name: "switch_workspace_mode",
            description: "Switch the visible workspace mode. Allowed values: generate, compare, edit, sprites, recipes, assets."),

        AIFunctionFactory.Create(
            method: (string? title = null, string? summary = null, CompareReviewToolItem[]? items = null, bool switchToCompare = true, CancellationToken cancellationToken = default) =>
                SetCompareReviewSetAsync(projectId, title, summary, items, switchToCompare, cancellationToken),
            name: "set_compare_review_set",
            description: "Replace the current Compare tab review set with an ordered set of user-visible review items. Item kind values: asset, generationBatch, spriteSheet, spriteAnimation, spriteFrame. This does not attach images to chat or send them back as model context."),

        AIFunctionFactory.Create(
            method: (CompareReviewToolItem[]? items = null, string? title = null, string? summary = null, bool switchToCompare = true, CancellationToken cancellationToken = default) =>
                AddCompareReviewItemsAsync(projectId, items, title, summary, switchToCompare, cancellationToken),
            name: "add_compare_review_items",
            description: "Append or update items in the current Compare tab review set. Item kind values: asset, generationBatch, spriteSheet, spriteAnimation, spriteFrame. This is for grouping things the user should review visually, not for model image context."),

        AIFunctionFactory.Create(
            method: (Guid itemId, CancellationToken cancellationToken = default) =>
                RemoveCompareReviewItemAsync(projectId, itemId, cancellationToken),
            name: "remove_compare_review_item",
            description: "Remove one item from the current Compare tab review set by review item id."),

        AIFunctionFactory.Create(
            method: (CancellationToken cancellationToken = default) =>
                ClearCompareReviewSetAsync(projectId, cancellationToken),
            name: "clear_compare_review_set",
            description: "Clear the current Compare tab review set."),

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
                string mode,
                Guid? sourceAssetId = null,
                Guid? spriteSheetId = null,
                int? expectedFrames = null,
                string? layoutHint = null,
                string? backgroundMode = null,
                int[]? targetFrameNumbers = null,
                bool apply = true,
                CancellationToken cancellationToken = default) =>
                MapSpriteSheetFramesAsync(projectId, mode, sourceAssetId, spriteSheetId, expectedFrames, layoutHint, backgroundMode, targetFrameNumbers, apply, cancellationToken),
            name: "map_sprite_sheet_frames",
            description: "Salvage/import tool: heuristically map frame boxes on an unscaffolded or imported PNG sprite sheet. Do not use this as the first path for new generated animations; use the asset-animation guide and fixed-slot extraction tools. Give this heuristic one attempt, inspect model-only feedback, then move to manual repair if needed."),

        AIFunctionFactory.Create(
            method: (
                Guid? spriteSheetId,
                int rows,
                int columns,
                int cellWidth,
                int cellHeight,
                SpriteSheetFrameUpdateView[] frames,
                int? padding = null,
                int? gutter = null,
                int? fps = null,
                bool? loop = null,
                string? horizontalAnchor = null,
                string? verticalAnchor = null,
                CancellationToken cancellationToken = default) => UpdateSpriteSheetFramesAsync(projectId, spriteSheetId, rows, columns, cellWidth, cellHeight, frames, padding, gutter, fps, loop, horizontalAnchor, verticalAnchor, cancellationToken),
            name: "update_sprite_sheet_frames",
            description: "Manual salvage tool: replace the visible Sprites workspace frame set and layout without changing working image bytes. Do not use for new scaffolded animation jobs unless fixed-slot extraction failed and the user/agent is deliberately repairing a malformed sheet."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId, int frameNumber, int? margin = null, CancellationToken cancellationToken = default) =>
                IsolateSpriteFrameAsync(projectId, spriteSheetId, frameNumber, margin, cancellationToken),
            name: "isolate_sprite_frame",
            description: "Extract one saved sprite frame source region into a hidden working PNG with optional margin. frameNumber is 1-based. The source sheet can be irregular; this does not require equal source cells. Returns compact state JSON plus model-only images of the isolated working frame: a clean copy for judging pixels and a coordinate-grid companion for computing region coordinates. The reported workingMargin pads all sides, so sprite content starts at (margin, margin)."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId, int frameNumber, CancellationToken cancellationToken = default) =>
                ReadSpriteFrameImageAsync(projectId, spriteSheetId, frameNumber, cancellationToken),
            name: "read_sprite_frame_image",
            description: "Read one hidden isolated/edited sprite frame working PNG. frameNumber is 1-based. Returns compact state JSON and, when present, the working image as model-only content: a clean copy plus a coordinate-grid companion for computing region coordinates. This is read-only."),

        AIFunctionFactory.Create(
            method: (
                Guid? spriteSheetId,
                int frameNumber,
                SpriteSheetRect[] rects,
                SpriteSheetShapePath[]? polygons = null,
                string? mode = null,
                CancellationToken cancellationToken = default) =>
                EraseSpriteFrameRegionsAsync(projectId, spriteSheetId, frameNumber, rects, polygons, mode, cancellationToken),
            name: "erase_sprite_frame_regions",
            description: "Deterministically clean one hidden isolated sprite frame using rects or polygons. mode 'erase' (default) fills the selected regions with the sheet background; mode 'keep' inverts the selection, keeping only the selected regions and filling everything else. Use keep to select the owned sprite and discard all neighbor bleed in one call. frameNumber is 1-based and coordinates are in the isolated working image; out-of-bounds coordinates are clamped, so generous edge-overshooting regions are safe. Auto-isolates if needed and does not consume generation budget; inspect the returned model-only images (clean copy plus coordinate-grid companion)."),

        AIFunctionFactory.Create(
            method: (
                Guid? spriteSheetId,
                int frameNumber,
                string prompt,
                string? background = null,
                CancellationToken cancellationToken = default) =>
                EditSpriteFrameAsync(projectId, budget, spriteSheetId, frameNumber, prompt, background, cancellationToken),
            name: "edit_sprite_frame",
            description: "AI-edit one hidden isolated sprite frame without creating a visible asset or generation batch. frameNumber is 1-based. Auto-isolates if needed, consumes one autonomous generation round budget, and returns the edited hidden frame as model-only image content."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId, int frameNumber, CancellationToken cancellationToken = default) =>
                ClearSpriteFrameWorkingImageAsync(projectId, spriteSheetId, frameNumber, cancellationToken),
            name: "clear_sprite_frame_working_image",
            description: "Clear the hidden isolated/edited working image for one frame. frameNumber is 1-based. Use this to revert a frame's isolated work before re-isolating or reassembling."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                ReassembleSpriteSheetAsync(projectId, spriteSheetId, cancellationToken),
            name: "reassemble_sprite_sheet",
            description: "Salvage/manual tool: normalize irregular source regions or hidden working frame images into equal animation frames, then stitch a new working sprite sheet. For new scaffolded animation jobs, use extract_animation_fixed_slots instead because it preserves planned slot geometry."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                NormalizeSpriteSheetAsync(projectId, spriteSheetId, cancellationToken),
            name: "normalize_sprite_sheet",
            description: "Salvage/manual tool: normalize an existing selected PNG sprite sheet by copying saved source rects and rebasing frame boxes/shapes. Do not use this as the first path for new guided animation jobs."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                ResetSpriteSheetToOriginalAsync(projectId, spriteSheetId, cancellationToken),
            name: "reset_sprite_sheet_to_original",
            description: "Reset the selected working sprite sheet to its immutable original image and clear all frame records. Returns the reset working image as model-only feedback because no frame review exists after records are cleared."),

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
                note = "The live UI snapshot includes compact state for all workspace tabs. Use focused list/read tools for broader project data or full records.",
                state = snapshot,
            }, JsonOptions);
        }

        return await workflow.GetWorkspaceStateJsonAsync(projectId);
    }

    private async Task<string> SwitchWorkspaceModeAsync(Guid projectId, string mode)
    {
        await workflow.SetWorkspaceModeAsync(projectId, ParseWorkspaceMode(mode));
        return $"Workspace mode switched to {mode}.";
    }

    private async Task<string> SetCompareReviewSetAsync(
        Guid projectId,
        string? title,
        string? summary,
        CompareReviewToolItem[]? items,
        bool switchToCompare,
        CancellationToken cancellationToken)
    {
        var reviewSet = await workflow.SetCompareReviewSetAsync(
            projectId,
            new SetCompareReviewSetRequest(title, summary, ToCompareReviewRequests(items), switchToCompare),
            cancellationToken);
        return JsonSerializer.Serialize(reviewSet, JsonOptions);
    }

    private async Task<string> AddCompareReviewItemsAsync(
        Guid projectId,
        CompareReviewToolItem[]? items,
        string? title,
        string? summary,
        bool switchToCompare,
        CancellationToken cancellationToken)
    {
        var reviewSet = await workflow.AddCompareReviewItemsAsync(
            projectId,
            new AddCompareReviewItemsRequest(title, summary, ToCompareReviewRequests(items), switchToCompare),
            cancellationToken);
        return JsonSerializer.Serialize(reviewSet, JsonOptions);
    }

    private async Task<string> RemoveCompareReviewItemAsync(Guid projectId, Guid itemId, CancellationToken cancellationToken)
    {
        await workflow.RemoveCompareReviewItemAsync(projectId, itemId, cancellationToken);
        return $"Compare review item {itemId} removed.";
    }

    private async Task<string> ClearCompareReviewSetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await workflow.ClearCompareReviewSetAsync(projectId, cancellationToken);
        return "Compare review set cleared.";
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

        var normalizedSize = string.IsNullOrWhiteSpace(size) ? "auto" : size.Trim();
        if (!ImageSizeValidator.TryValidate(normalizedSize, out var sizeError, out var suggestedSize))
        {
            return JsonSerializer.Serialize(new
            {
                error = sizeError,
                suggestedSize,
                budget.RoundsUsed,
                budget.MaxRounds,
                roundsRemaining = Math.Max(0, budget.MaxRounds - budget.RoundsUsed),
                message = "No generation round was consumed. Fix the size and run the round again.",
            }, JsonOptions);
        }

        var round = budget.Consume();
        var outputCount = ClampGenerationRoundCount(count);
        var normalizedBackground = NormalizeBackground(background) ?? "auto";
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

    private async Task<string> CreateAssetProfileAsync(
        Guid projectId,
        Guid canonicalAssetId,
        Guid? styleAssetId,
        string? label,
        string? assetType,
        string? structureType,
        string[]? requiredFeatures,
        string[]? forbiddenChanges,
        bool frozen,
        CancellationToken cancellationToken)
    {
        var profile = await assetAnimation.CreateAssetProfileAsync(projectId, new CreateAssetProfileRequest(
            canonicalAssetId,
            styleAssetId,
            label,
            assetType,
            structureType,
            requiredFeatures ?? [],
            forbiddenChanges ?? [],
            frozen), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            profileId = profile.Id,
            profile.CanonicalAssetId,
            profile.StyleAssetId,
            profile.Label,
            profile.AssetType,
            profile.StructureType,
            profile.ChromaColor,
            profile.Frozen,
            message = "Asset profile created.",
        }, JsonOptions);
    }

    private async Task<string> PlanAssetAnimationAsync(
        Guid projectId,
        Guid assetProfileId,
        string animationKind,
        string? facing,
        string? strategy,
        int? frameCount,
        int? fps,
        string? rootMotion,
        string? promptSummary,
        string? targetCellSize,
        CancellationToken cancellationToken)
    {
        var job = await assetAnimation.PlanAssetAnimationAsync(projectId, new PlanAssetAnimationRequest(
            assetProfileId,
            animationKind,
            facing,
            strategy,
            frameCount,
            fps,
            rootMotion,
            promptSummary,
            targetCellSize), cancellationToken);
        return CompactAnimationJobJson(job, "Animation plan compiled.");
    }

    private async Task<string> RenderAnimationGuideAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken)
    {
        var job = await assetAnimation.RenderAnimationGuideAsync(projectId, assetAnimationJobId, cancellationToken);
        return CompactAnimationJobJson(job, "Animation guide rendered. Model-only guide images are supplied separately.");
    }

    private async Task<string> RunAnimationCandidatesAsync(Guid projectId, Guid assetAnimationJobId, int? candidateCount, CancellationToken cancellationToken)
    {
        var job = await assetAnimation.RunAnimationCandidatesAsync(projectId, new RunAnimationCandidatesRequest(assetAnimationJobId, candidateCount), cancellationToken);
        return CompactAnimationJobJson(job, "Animation candidates generated.");
    }

    private async Task<string> MarkAnimationFramesAsync(Guid projectId, Guid assetAnimationJobId, AnimationFrameMark[]? frames, CancellationToken cancellationToken)
    {
        var marks = (frames ?? [])
            .Where(frame => frame.FrameNumber > 0)
            .Select(frame => new MarkAnimationFrameRequest(frame.FrameNumber, frame.Status ?? "warning", frame.Reason))
            .ToList();
        if (marks.Count == 0)
            return JsonSerializer.Serialize(new { error = "mark_animation_frames requires at least one frame mark." }, JsonOptions);

        var job = await assetAnimation.MarkAnimationFramesAsync(projectId, new MarkAnimationFramesRequest(assetAnimationJobId, marks), cancellationToken);
        return CompactAnimationJobJson(job, "Animation frames marked.");
    }

    private async Task<string> RegenerateAnimationFramesAsync(Guid projectId, Guid assetAnimationJobId, int[]? frameNumbers, string? prompt, CancellationToken cancellationToken)
    {
        var numbers = (frameNumbers ?? []).Where(number => number > 0).Distinct().ToList();
        if (numbers.Count == 0)
            return JsonSerializer.Serialize(new { error = "regenerate_animation_frames requires at least one 1-based frame number." }, JsonOptions);

        var job = await assetAnimation.RegenerateAnimationFramesAsync(projectId, new RegenerateAnimationFramesRequest(assetAnimationJobId, numbers, prompt), cancellationToken);
        return CompactAnimationJobJson(job, "Requested animation frames regenerated.");
    }

    private async Task<string> ExtractAnimationFixedSlotsAsync(Guid projectId, Guid assetAnimationJobId, Guid? candidateId, CancellationToken cancellationToken)
    {
        var job = await assetAnimation.ExtractAnimationFixedSlotsAsync(projectId, new ExtractAnimationFixedSlotsRequest(assetAnimationJobId, candidateId), cancellationToken);
        return CompactAnimationJobJson(job, "Animation extracted by fixed slots.");
    }

    private async Task<string> ReviewAnimationJobAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken)
    {
        var job = await assetAnimation.ReviewAnimationJobAsync(projectId, assetAnimationJobId, cancellationToken);
        return CompactAnimationJobJson(job, "Animation job reviewed.");
    }

    private async Task<string> PackageAnimationJobAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken)
    {
        var job = await assetAnimation.PackageAnimationJobAsync(projectId, assetAnimationJobId, cancellationToken);
        return CompactAnimationJobJson(job, "Animation job packaged.");
    }

    private static string CompactAnimationJobJson(AssetAnimationJobView job, string message) =>
        JsonSerializer.Serialize(new
        {
            jobId = job.Id,
            profileId = job.AssetProfileId,
            job.Status,
            job.AnimationKind,
            job.Strategy,
            job.RecommendedAction,
            budget = new
            {
                used = job.GenerationRoundsUsed,
                max = job.MaxGenerationRounds,
                remaining = Math.Max(0, job.MaxGenerationRounds - job.GenerationRoundsUsed),
                repairAttemptsPerFrame = job.MaxRepairAttemptsPerFrame,
            },
            guideAssetId = job.GuideAssetId,
            diagnosticGuideAssetId = job.DiagnosticGuideAssetId,
            outputSpriteSheetId = job.OutputSpriteSheetId,
            selectedCandidateId = job.SelectedCandidateId,
            animation = new
            {
                job.AnimationSpec.AssetType,
                job.AnimationSpec.StructureType,
                job.AnimationSpec.AnimationKind,
                job.AnimationSpec.Facing,
                job.AnimationSpec.FrameCount,
                job.AnimationSpec.Fps,
                targetCell = $"{job.AnimationSpec.TargetCellWidth}x{job.AnimationSpec.TargetCellHeight}",
            },
            layout = new
            {
                job.LayoutSpec.Rows,
                job.LayoutSpec.Columns,
                canvas = $"{job.LayoutSpec.CanvasWidth}x{job.LayoutSpec.CanvasHeight}",
                chroma = job.LayoutSpec.BackgroundColor,
            },
            candidates = job.Candidates.Select(candidate => new
            {
                candidate.Id,
                candidate.OutputAssetId,
                candidate.CandidateIndex,
                candidate.State,
                candidate.RawQaStatus,
            }),
            frames = job.FrameStatuses,
            modelOnlyImages = "Relevant guide/candidate/sheet/review images are supplied separately when this tool produces visual artifacts.",
            message,
        }, JsonOptions);

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

    private async Task<string> ComposeSpriteSheetFromImagesAsync(
        Guid projectId,
        Guid[]? assetIds,
        Guid? spriteSheetId,
        int? insertAt,
        string? label,
        int? rows,
        int? columns,
        int? padding,
        int? gutter,
        int? fps,
        bool? loop,
        string? horizontalAnchor,
        string? verticalAnchor,
        CancellationToken cancellationToken)
    {
        var cleanedAssetIds = (assetIds ?? [])
            .Where(id => id != Guid.Empty)
            .Take(2048)
            .ToList();
        if (cleanedAssetIds.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                error = "compose_sprite_sheet_from_images requires at least one PNG asset id.",
            }, JsonOptions);
        }

        var saved = await workflow.ComposeSpriteSheetFromImagesAsync(projectId, new ComposeSpriteSheetFromImagesRequest(
            cleanedAssetIds,
            spriteSheetId,
            insertAt,
            label,
            rows,
            columns,
            padding,
            gutter,
            fps,
            loop,
            horizontalAnchor,
            verticalAnchor), cancellationToken);
        return JsonSerializer.Serialize(CompactSpriteSheetResult(saved, spriteSheetId is Guid id && id != Guid.Empty
            ? "Images added to sprite sheet."
            : "Sprite sheet composed from individual images."), JsonOptions);
    }

    private async Task<string> ReadSpriteSheetToolAsync(
        Guid projectId,
        Guid? spriteSheetId,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        return await workflow.ReadSpriteSheetJsonAsync(projectId, resolution.SpriteSheetId!.Value, cancellationToken);
    }

    private async Task<string> ReviewSpriteAnimationAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int maxFrames,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var review = await workflow.BuildSpriteAnimationReviewAsync(projectId, resolution.SpriteSheetId!.Value, maxFrames, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            review.SpriteSheetId,
            review.FrameCount,
            review.Rows,
            review.Columns,
            review.Fps,
            review.Loop,
            review.Metrics,
            modelOnlyImages = review.Images.Select(image => new { image.Label, image.FileName, image.ContentType, image.Kind, image.FrameIndex, image.FromFrame, image.ToFrame }).ToList(),
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

    private async Task<string> MapSpriteSheetFramesAsync(
        Guid projectId,
        string mode,
        Guid? sourceAssetId,
        Guid? spriteSheetId,
        int? expectedFrames,
        string? layoutHint,
        string? backgroundMode,
        int[]? targetFrameNumbers,
        bool apply,
        CancellationToken cancellationToken)
    {
        var normalizedMode = mode?.Trim().ToLowerInvariant();
        if (normalizedMode is "grid-repair" or "repair")
        {
            if (expectedFrames is not int expected || expected <= 0)
                return JsonSerializer.Serialize(new { error = "mode 'grid-repair' requires expectedFrames." }, JsonOptions);

            return await RepairSpriteSheetFramesAsync(projectId, spriteSheetId, expected, layoutHint, targetFrameNumbers, apply, cancellationToken);
        }

        if (normalizedMode is not "auto")
            return JsonSerializer.Serialize(new { error = "mode must be 'auto' or 'grid-repair'." }, JsonOptions);
        if (sourceAssetId is not Guid asset)
            return JsonSerializer.Serialize(new { error = "mode 'auto' requires sourceAssetId." }, JsonOptions);

        return await DetectSpriteSheetFramesAsync(projectId, asset, expectedFrames, layoutHint, backgroundMode);
    }

    private async Task<string> DetectSpriteSheetFramesAsync(
        Guid projectId,
        Guid sourceAssetId,
        int? expectedFrames,
        string? layoutHint,
        string? backgroundMode)
    {
        var detection = await workflow.DetectSpriteSheetFramesAsync(
            projectId,
            new SpriteSheetDetectionRequest(
                sourceAssetId,
                expectedFrames,
                layoutHint,
                backgroundMode));
        var sheet = await workflow.StartSpriteSheetEditAsync(projectId, sourceAssetId);
        var frames = detection.Frames
            .Select(frame => new SpriteSheetFrameUpdateView(
                frame.Index,
                $"Frame {frame.Index + 1}",
                frame.SourceRect,
                frame.ShapePaths,
                sourceAssetId,
                frame.SourceRect))
            .ToList();
        var saved = await workflow.UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
            sheet.Id,
            Math.Clamp(detection.Rows, 1, 32),
            Math.Clamp(detection.Columns, 1, 64),
            Math.Clamp(MaxFrameWidth(detection.Frames, CeilDiv(detection.ImageWidth, Math.Max(1, detection.Columns))), 1, 8192),
            Math.Clamp(MaxFrameHeight(detection.Frames, CeilDiv(detection.ImageHeight, Math.Max(1, detection.Rows))), 1, 8192),
            Padding: 0,
            Gutter: 0,
            sheet.Fps,
            sheet.Loop,
            sheet.HorizontalAnchor,
            sheet.VerticalAnchor,
            frames));

        return JsonSerializer.Serialize(CompactDetectionResult(detection, saved), JsonOptions);
    }

    private async Task<string> RepairSpriteSheetFramesAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int expectedFrames,
        string? layoutHint,
        int[]? targetFrameNumbers,
        bool apply,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var repair = await workflow.RepairSpriteSheetFramesAsync(projectId, new RepairSpriteSheetFramesRequest(
            resolution.SpriteSheetId!.Value,
            Math.Clamp(expectedFrames, 1, 256),
            layoutHint,
            targetFrameNumbers,
            apply), cancellationToken);
        return JsonSerializer.Serialize(CompactRepairResult(repair), JsonOptions);
    }

    private async Task<string> UpdateSpriteSheetFramesAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int rows,
        int columns,
        int cellWidth,
        int cellHeight,
        SpriteSheetFrameUpdateView[] frames,
        int? padding,
        int? gutter,
        int? fps,
        bool? loop,
        string? horizontalAnchor,
        string? verticalAnchor,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var saved = await workflow.UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
            resolution.SpriteSheetId!.Value,
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
            frames), cancellationToken);
        return JsonSerializer.Serialize(CompactSpriteSheetResult(saved, "Sprite sheet frames updated."), JsonOptions);
    }

    private static object CompactDetectionResult(SpriteSheetDetectionResult detection, SpriteSheetDefinitionView saved) => new
    {
        sourceAssetId = detection.SourceAssetId,
        spriteSheetId = saved.Id,
        saved.WorkingAssetId,
        detection.ImageWidth,
        detection.ImageHeight,
        saved.Rows,
        saved.Columns,
        saved.CellWidth,
        saved.CellHeight,
        saved.Padding,
        saved.Gutter,
        saved.Fps,
        saved.Loop,
        saved.HorizontalAnchor,
        saved.VerticalAnchor,
        background = detection.Background,
        warnings = detection.Warnings,
        rejectedSegments = detection.RejectedSegments,
        frameQuality = detection.FrameQuality,
        frameCount = saved.Frames.Count,
        frames = saved.Frames.Select(CompactFrame),
        modelOnlyImage = "Annotated sheet image supplied separately. Number labels in the image match frameNumber values here.",
        modelOnlyImages = "This mutation returns model-only annotated sheet and compact filmstrip/contact imagery after the tool result.",
        omitted = "Preview image data is stored server-side and intentionally omitted from JSON.",
        message = "Sprite-sheet frames detected and applied.",
    };

    private static object CompactRepairResult(RepairSpriteSheetFramesResult repair) => new
    {
        spriteSheetId = repair.SpriteSheetId,
        repair.SourceAssetId,
        repair.WorkingAssetId,
        repair.Applied,
        repair.ImageWidth,
        repair.ImageHeight,
        repair.Rows,
        repair.Columns,
        repair.CellWidth,
        repair.CellHeight,
        repair.Padding,
        repair.Gutter,
        repair.Fps,
        repair.Loop,
        repair.HorizontalAnchor,
        repair.VerticalAnchor,
        background = repair.Background,
        frameCount = repair.Frames.Count,
        frames = repair.Frames.Select((frame, index) => new
        {
            frameNumber = index + 1,
            index,
            label = string.IsNullOrWhiteSpace(frame.Label) ? $"Frame {index + 1}" : frame.Label,
            frame.SourceRect,
            frame.SourceImageAssetId,
            frame.SourceImageRect,
            hasShapePaths = HasShapePaths(frame.ShapePaths),
            shapePathCount = ShapePathCount(frame.ShapePaths),
            shapePointCount = ShapePointCount(frame.ShapePaths),
        }),
        warnings = repair.Warnings,
        rejectedSegments = repair.RejectedSegments,
        frameQuality = repair.FrameQuality,
        savedFrameIds = repair.SavedSheet?.Frames.Select(frame => frame.Id).ToList() ?? [],
        modelOnlyImages = "Repair returns a model-only annotated repair sheet plus compact filmstrip/contact imagery when frames are available. Inspect them before claiming success.",
        message = repair.Applied
            ? "Sprite-sheet frame repair applied."
            : "Sprite-sheet frame repair proposed without applying.",
    };

    private static object CompactSpriteSheetResult(SpriteSheetDefinitionView saved, string message) => new
    {
        spriteSheetId = saved.Id,
        saved.SourceAssetId,
        saved.WorkingAssetId,
        saved.Rows,
        saved.Columns,
        saved.CellWidth,
        saved.CellHeight,
        saved.Padding,
        saved.Gutter,
        saved.Fps,
        saved.Loop,
        saved.HorizontalAnchor,
        saved.VerticalAnchor,
        background = saved.Background,
        frameCount = saved.Frames.Count,
        frames = saved.Frames.Select(CompactFrame),
        warnings = CompactSpriteSheetWarnings(saved),
        rejectedSegments = Array.Empty<SpriteSheetRejectedSegmentView>(),
        frameQuality = CompactFrameQuality(saved),
        modelOnlyImages = "This mutation returns model-only annotated sheet and compact filmstrip/contact imagery after the tool result.",
        omitted = "Preview image data is stored server-side and intentionally omitted from JSON.",
        message,
    };

    private static object CompactFrame(SpriteSheetFrameRecordView frame) => new
    {
        frameNumber = frame.Index + 1,
        frame.Index,
        frame.Label,
        frame.SourceRect,
        frame.SourceImageAssetId,
        frame.SourceImageRect,
        frame.CellRect,
        frame.SpriteRect,
        hasShapePaths = HasShapePaths(frame.ShapePaths),
        shapePathCount = ShapePathCount(frame.ShapePaths),
        shapePointCount = ShapePointCount(frame.ShapePaths),
        frame.PreviewWidth,
        frame.PreviewHeight,
        frame.WorkingState,
        frame.WorkingWidth,
        frame.WorkingHeight,
        frame.WorkingMargin,
        frame.WorkingUpdatedAt,
    };

    private static object CompactFrameWorkingResult(SpriteFrameWorkingView frame, string message) => new
    {
        spriteSheetId = frame.SpriteSheetId,
        frameId = frame.FrameId,
        frameNumber = frame.Index + 1,
        frame.Index,
        frame.Label,
        workingState = frame.State,
        frame.WorkingWidth,
        frame.WorkingHeight,
        frame.WorkingMargin,
        frame.WorkingUpdatedAt,
        hasWorkingImage = !string.IsNullOrWhiteSpace(frame.WorkingPngDataUrl),
        modelOnlyImages = string.IsNullOrWhiteSpace(frame.WorkingPngDataUrl)
            ? "No working image is available for this frame."
            : "The hidden working frame image is returned as model-only image content after the tool result.",
        omitted = "Working image data is stored server-side and intentionally omitted from JSON.",
        message,
    };

    private static int FrameIndexFromNumber(int frameNumber)
    {
        if (frameNumber <= 0)
            throw new InvalidOperationException("frameNumber must be 1 or greater.");
        return frameNumber - 1;
    }

    private static IReadOnlyList<string> CompactSpriteSheetWarnings(SpriteSheetDefinitionView saved)
    {
        var warnings = new List<string>();
        if (saved.Frames.Count == 0)
            warnings.Add("No frame records are currently saved.");
        if (saved.Frames.Any(frame => frame.SourceRect.Width > saved.CellWidth || frame.SourceRect.Height > saved.CellHeight))
            warnings.Add("One or more source rectangles exceed the configured cell size and may clip when normalized.");
        if (saved.Frames.Any(frame => !frame.ShapePaths.Any(path => path.Points.Count >= 3)))
            warnings.Add("One or more frames do not have polygon shapePaths; overlapping sprites may bleed into neighboring frames.");
        warnings.AddRange(CompactFrameQuality(saved)
            .SelectMany(item => item.Warnings.Select(warning => $"Frame {item.FrameNumber}: {warning}")));
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<CompactFrameQualityItem> CompactFrameQuality(SpriteSheetDefinitionView saved)
    {
        var areas = saved.Frames
            .Select(frame => frame.SourceRect.Width * (double)frame.SourceRect.Height)
            .Where(area => area > 0)
            .Order()
            .ToList();
        var median = areas.Count == 0 ? 0 : areas[areas.Count / 2];
        return saved.Frames
            .OrderBy(frame => frame.Index)
            .Select(frame =>
            {
                var warnings = new List<string>();
                var area = frame.SourceRect.Width * (double)frame.SourceRect.Height;
                if (median > 0 && area < median * 0.25d)
                    warnings.Add("source rectangle is a small outlier");
                if (median > 0 && area > median * 2.75d)
                    warnings.Add("source rectangle is a large/merged outlier");
                if (frame.SourceRect.Width > saved.CellWidth)
                    warnings.Add("source rectangle is wider than the cell");
                if (frame.SourceRect.Height > saved.CellHeight)
                    warnings.Add("source rectangle is taller than the cell");
                if (!frame.ShapePaths.Any(path => path.Points.Count >= 3))
                    warnings.Add("missing polygon shapePaths for overlap separation");
                return new CompactFrameQualityItem(
                    frame.Index + 1,
                    frame.Index,
                    frame.SourceRect,
                    HasShapePaths(frame.ShapePaths),
                    ShapePathCount(frame.ShapePaths),
                    ShapePointCount(frame.ShapePaths),
                    warnings);
            })
            .ToList();
    }

    private sealed record CompactFrameQualityItem(
        int FrameNumber,
        int Index,
        SpriteSheetRect SourceRect,
        bool HasShapePaths,
        int ShapePathCount,
        int ShapePointCount,
        IReadOnlyList<string> Warnings);

    private sealed record SpriteSheetToolResolution(Guid? SpriteSheetId, string? ErrorJson);

    private async Task<SpriteSheetToolResolution> ResolveSpriteSheetIdForToolAsync(
        Guid projectId,
        Guid? requestedSpriteSheetId,
        CancellationToken cancellationToken)
    {
        var workbench = await workflow.GetWorkbenchAsync(projectId, cancellationToken);
        var orderedSheets = workbench.SpriteSheets
            .OrderByDescending(sheet => sheet.Id == workbench.Project.ActiveSpriteSheetId)
            .ThenByDescending(sheet => sheet.UpdatedAt)
            .ToList();

        if (orderedSheets.Count == 0)
        {
            return new(null, SpriteSheetResolutionErrorJson(
                requestedSpriteSheetId,
                workbench,
                "No sprite sheets exist in this project. Create or detect a sprite sheet before using sprite-frame tools."));
        }

        if (requestedSpriteSheetId is null || requestedSpriteSheetId == Guid.Empty)
        {
            var active = workbench.ActiveSpriteSheet ?? orderedSheets.FirstOrDefault();
            return active is not null
                ? new(active.Id, null)
                : new(null, SpriteSheetResolutionErrorJson(requestedSpriteSheetId, workbench, "No active sprite sheet is available."));
        }

        var requested = requestedSpriteSheetId.Value;
        var exact = orderedSheets.FirstOrDefault(sheet => sheet.Id == requested);
        if (exact is not null)
            return new(exact.Id, null);

        var assetMatches = orderedSheets
            .Where(sheet => sheet.SourceAssetId == requested || sheet.WorkingAssetId == requested)
            .ToList();
        if (assetMatches.Count == 1)
            return new(assetMatches[0].Id, null);

        if (assetMatches.Count > 1)
        {
            var activeMatch = assetMatches.FirstOrDefault(sheet => sheet.Id == workbench.Project.ActiveSpriteSheetId);
            if (activeMatch is not null)
                return new(activeMatch.Id, null);

            return new(null, SpriteSheetResolutionErrorJson(
                requestedSpriteSheetId,
                workbench,
                "The provided id matches multiple sprite-sheet source or working assets. Use one of the listed spriteSheetId values."));
        }

        if (orderedSheets.Count == 1)
            return new(orderedSheets[0].Id, null);

        return new(null, SpriteSheetResolutionErrorJson(
            requestedSpriteSheetId,
            workbench,
            "Sprite sheet was not found for the provided id. The id may be stale or may be an unrelated asset id."));
    }

    private static string SpriteSheetResolutionErrorJson(Guid? requestedSpriteSheetId, WorkbenchView workbench, string message)
    {
        var activeSpriteSheetId = workbench.Project.ActiveSpriteSheetId;
        return JsonSerializer.Serialize(new
        {
            error = message,
            requestedSpriteSheetId,
            activeSpriteSheetId,
            spriteSheets = workbench.SpriteSheets
                .OrderByDescending(sheet => sheet.Id == activeSpriteSheetId)
                .ThenByDescending(sheet => sheet.UpdatedAt)
                .Select(sheet => new
                {
                    spriteSheetId = sheet.Id,
                    isActive = sheet.Id == activeSpriteSheetId,
                    sheet.SourceAssetId,
                    sheet.WorkingAssetId,
                    sheet.Label,
                    sheet.Rows,
                    sheet.Columns,
                    frameCount = sheet.Frames.Count,
                    sheet.UpdatedAt,
                }),
            guidance = "Use a spriteSheetId from this list, or omit spriteSheetId when there is an active sprite sheet.",
        }, JsonOptions);
    }

    private static bool HasShapePaths(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        ShapePathCount(shapePaths) > 0;

    private static int ShapePathCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths.Count(path => path.Points.Count >= 3);

    private static int ShapePointCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths
            .Where(path => path.Points.Count >= 3)
            .Sum(path => path.Points.Count);

    private async Task<string> IsolateSpriteFrameAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int frameNumber,
        int? margin,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var frame = await workflow.IsolateSpriteFrameAsync(projectId, resolution.SpriteSheetId!.Value, FrameIndexFromNumber(frameNumber), margin, cancellationToken);
        return JsonSerializer.Serialize(CompactFrameWorkingResult(frame, "Sprite frame isolated."), JsonOptions);
    }

    private async Task<string> ReadSpriteFrameImageAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int frameNumber,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var frame = await workflow.GetSpriteFrameWorkingImageAsync(projectId, resolution.SpriteSheetId!.Value, FrameIndexFromNumber(frameNumber), cancellationToken);
        return JsonSerializer.Serialize(CompactFrameWorkingResult(frame, string.IsNullOrWhiteSpace(frame.WorkingPngDataUrl)
            ? "Sprite frame has no hidden working image."
            : "Sprite frame working image read."), JsonOptions);
    }

    private async Task<string> EraseSpriteFrameRegionsAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int frameNumber,
        SpriteSheetRect[] rects,
        SpriteSheetShapePath[]? polygons,
        string? mode,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var keepSelection = string.Equals(mode?.Trim(), "keep", StringComparison.OrdinalIgnoreCase);
        var frame = await workflow.EraseSpriteFrameRegionsAsync(projectId, new EraseSpriteFrameRegionsRequest(
            resolution.SpriteSheetId!.Value,
            FrameIndexFromNumber(frameNumber),
            rects ?? [],
            polygons,
            keepSelection ? "keep" : "erase"), cancellationToken);
        return JsonSerializer.Serialize(CompactFrameWorkingResult(frame, keepSelection
            ? "Sprite frame selection kept; everything outside the regions was filled with background."
            : "Sprite frame regions erased."), JsonOptions);
    }

    private async Task<string> EditSpriteFrameAsync(
        Guid projectId,
        AssistantTurnGenerationBudget budget,
        Guid? spriteSheetId,
        int frameNumber,
        string prompt,
        string? background,
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
                message = "Stop editing frames with AI for this turn; deterministic erase and reassembly are still available.",
            }, JsonOptions);
        }

        if (imageRuntime.HasRunningBatch(projectId))
        {
            return JsonSerializer.Serialize(new
            {
                error = "An image generation batch is already running for this project. Wait for it to finish before editing a sprite frame.",
                budget.RoundsUsed,
                budget.MaxRounds,
            }, JsonOptions);
        }

        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var round = budget.Consume();
        var frame = await workflow.EditSpriteFrameAsync(projectId, new EditSpriteFrameRequest(
            resolution.SpriteSheetId!.Value,
            FrameIndexFromNumber(frameNumber),
            prompt,
            background), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            round,
            budget.RoundsUsed,
            budget.MaxRounds,
            roundsRemaining = Math.Max(0, budget.MaxRounds - budget.RoundsUsed),
            frame = CompactFrameWorkingResult(frame, "Sprite frame AI edit completed."),
        }, JsonOptions);
    }

    private async Task<string> ClearSpriteFrameWorkingImageAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int frameNumber,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var frame = await workflow.ClearSpriteFrameWorkingImageAsync(projectId, resolution.SpriteSheetId!.Value, FrameIndexFromNumber(frameNumber), cancellationToken);
        return JsonSerializer.Serialize(CompactFrameWorkingResult(frame, "Sprite frame working image cleared."), JsonOptions);
    }

    private async Task<string> ReassembleSpriteSheetAsync(
        Guid projectId,
        Guid? spriteSheetId,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var result = await workflow.ReassembleSpriteSheetAsync(projectId, resolution.SpriteSheetId!.Value, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            spriteSheetId = result.Sheet.Id,
            result.Sheet.SourceAssetId,
            result.Sheet.WorkingAssetId,
            result.Sheet.Rows,
            result.Sheet.Columns,
            result.Sheet.CellWidth,
            result.Sheet.CellHeight,
            result.Sheet.Padding,
            result.Sheet.Gutter,
            result.Sheet.Fps,
            result.Sheet.Loop,
            result.Sheet.HorizontalAnchor,
            result.Sheet.VerticalAnchor,
            background = result.Sheet.Background,
            frameCount = result.Sheet.Frames.Count,
            frames = result.Frames.Select(frame => new
            {
                frameNumber = frame.Index + 1,
                frame.Index,
                frame.Label,
                frame.UsedWorkingImage,
                frame.DetectedRect,
                frame.PlacedRect,
                frame.Warnings,
            }),
            warnings = result.Warnings,
            modelOnlyImages = "This mutation returns model-only annotated sheet and compact filmstrip/contact imagery after the tool result.",
            message = "Sprite sheet reassembled from irregular frame regions.",
        }, JsonOptions);
    }

    private async Task<string> ResetSpriteSheetToOriginalAsync(Guid projectId, Guid? spriteSheetId, CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var saved = await workflow.ResetSpriteSheetToOriginalAsync(projectId, resolution.SpriteSheetId!.Value, cancellationToken);
        return JsonSerializer.Serialize(CompactSpriteSheetResult(saved, "Sprite sheet reset to original image and frame records cleared."), JsonOptions);
    }

    private async Task<string> NormalizeSpriteSheetAsync(Guid projectId, Guid? spriteSheetId, CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var saved = await workflow.NormalizeSpriteSheetAsync(projectId, resolution.SpriteSheetId!.Value, cancellationToken);
        return JsonSerializer.Serialize(CompactSpriteSheetResult(saved, "Sprite sheet normalized."), JsonOptions);
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

    private static IReadOnlyList<CompareReviewSetItemRequest> ToCompareReviewRequests(CompareReviewToolItem[]? items) =>
        items?
            .Where(item => item.RefId != Guid.Empty)
            .Select(item => new CompareReviewSetItemRequest(
                ParseCompareReviewItemKind(item.Kind),
                item.RefId,
                item.Label,
                item.Notes))
            .ToList() ?? [];

    private static CompareReviewItemKind ParseCompareReviewItemKind(string kind) =>
        NormalizeToken(kind) switch
        {
            "asset" => CompareReviewItemKind.Asset,
            "generationbatch" or "batch" => CompareReviewItemKind.GenerationBatch,
            "spritesheet" or "sheet" => CompareReviewItemKind.SpriteSheet,
            "spriteanimation" or "animation" => CompareReviewItemKind.SpriteAnimation,
            "spriteframe" or "frame" => CompareReviewItemKind.SpriteFrame,
            _ => throw new InvalidOperationException($"Unknown compare review item kind '{kind}'.")
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

    private static int CeilDiv(int value, int divisor) =>
        (Math.Max(0, value) + Math.Max(1, divisor) - 1) / Math.Max(1, divisor);

    private static int MaxFrameWidth(IReadOnlyList<SpriteSheetFrameDetectionView> frames, int fallback) =>
        frames.Count == 0 ? fallback : Math.Max(1, frames.Max(frame => frame.SourceRect.Width));

    private static int MaxFrameHeight(IReadOnlyList<SpriteSheetFrameDetectionView> frames, int fallback) =>
        frames.Count == 0 ? fallback : Math.Max(1, frames.Max(frame => frame.SourceRect.Height));

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
