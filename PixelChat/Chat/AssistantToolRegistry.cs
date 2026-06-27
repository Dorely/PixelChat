using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PixelChat.Art;
using PixelChat.Llm;
using PixelChat.Models;

namespace PixelChat.Chat;

public sealed class AssistantToolRegistry(
    IArtWorkflowService workflow,
    IFrameSetService frameSets,
    ISpriteWorkspaceActionService spriteActions,
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
        "save_animation_recipe",
        "revert_recipe_version",
        "generate_animation_guide",
        "generate_sprite_sheet_candidates",
        "create_sprite_sheet",
        "extract_region_as_asset",
        "detect_source_regions",
        "save_source_regions",
        "create_frame_set",
        "create_frame_set_from_regions",
        "set_active_frame_set",
        "set_common_cell_size",
        "add_frame_from_region",
        "duplicate_frame",
        "set_frame_logical_cell",
        "update_frame_source_bounds",
        "translate_frame_content",
        "reorder_frame",
        "delete_frame",
        "set_frame_duration",
        "align_frames",
        "upsert_frame_mask",
        "clear_frame_mask",
        "build_sheet",
        "export_asset",
        "compose_sprite_sheet_from_images",
        "map_sprite_sheet_frames",
        "detect_sprite_frame_boxes",
        "adjust_sprite_frame_box",
        "stabilize_sprite_sheet_frames",
        "clear_sprite_sheet_stabilization",
        "split_sprite_sheet_frames",
        "isolate_sprite_frame",
        "erase_sprite_frame_regions",
        "edit_sprite_frame",
        "clear_sprite_frame_working_image",
        "reassemble_sprite_sheet",
    };

    public IList<AITool> Build(Guid projectId, AssistantTurnGenerationBudget budget) =>
        WithDisplayTitleParameters(
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
            method: (string? query = null, int? limit = null) =>
                workflow.ListAnimationRecipesJsonAsync(projectId, query, limit),
            name: "list_animation_recipes",
            description: "List compact saved animation recipes for reusable motion, guide, frame layout, anchor, prompt scaffold, timing, and export defaults. Animation recipes are independent of art style."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ReadAnimationRecipeJsonAsync(projectId, recipeId),
            name: "read_animation_recipe",
            description: "Read one animation recipe's guide asset id, animation kind, facing, frame order, expected boxes, anchor strategy, prompt scaffold, timing, export defaults, notes, and primary successful example. This is read-only."),

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
            method: (Guid recipeId) => workflow.ListAnimationRecipeVersionsJsonAsync(projectId, recipeId),
            name: "list_animation_recipe_versions",
            description: "List the append-only version history for a saved animation recipe. This is read-only."),

        AIFunctionFactory.Create(
            method: (
                Guid? recipeId,
                string name,
                string animationKind,
                string promptScaffold,
                string changeSummary,
                string? facing = null,
                int frameCount = 0,
                int[]? frameOrder = null,
                int fps = 8,
                bool loop = true,
                Guid? guideAssetId = null,
                SpriteSheetRect[]? expectedFrameBoxes = null,
                string? anchorStrategy = null,
                string? exportDefaultsJson = null,
                string? notes = null,
                Guid? primaryExampleSpriteSheetId = null,
                CancellationToken cancellationToken = default) =>
                SaveAnimationRecipeToolAsync(projectId, recipeId, name, animationKind, promptScaffold, changeSummary, facing, frameCount, frameOrder, fps, loop, guideAssetId, expectedFrameBoxes, anchorStrategy, exportDefaultsJson, notes, primaryExampleSpriteSheetId, cancellationToken),
            name: "save_animation_recipe",
            description: "Create or update a durable animation recipe for reusable motion/layout work. If guideAssetId is supplied it must be an existing SpriteGuide asset in this project; generated images and sprite sheets are rejected. Include generic guide/layout guidance, expected frame boxes if known, frame order, fps/loop, anchor strategy, prompt scaffold, export defaults, notes, and a primary successful sprite-sheet example when available. Do not put art style rules here unless the recipe is intentionally style-specific. Always provide changeSummary."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, int version, CancellationToken cancellationToken = default) =>
                RevertPromptRecipeToolAsync(projectId, recipeId, version, cancellationToken),
            name: "revert_recipe_version",
            description: "Restore an older prompt recipe snapshot as a new assistant-authored version. This is non-destructive and appends a new version entry."),

        AIFunctionFactory.Create(
            method: (
                string animationKind,
                Guid? referenceAssetId = null,
                string? assetType = null,
                string? structureType = null,
                string? facing = null,
                int? frameCount = null,
                int? fps = null,
                string? rootMotion = null,
                string? targetCellSize = null,
                string? motionClipId = null,
                string? label = null,
                CancellationToken cancellationToken = default) =>
                GenerateAnimationGuideToolAsync(projectId, referenceAssetId, animationKind, assetType, structureType, facing, frameCount, fps, rootMotion, targetCellSize, motionClipId, label, cancellationToken),
            name: "generate_animation_guide",
            description: "Render and save a reusable animation guide as SpriteGuide assets. Use this before generate_sprite_sheet_candidates for animation work. The returned guideAssetId must be first in referenceAssetIds; do not use old SpriteSheet or Generated assets as guides."),

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
            description: "Run one generic autonomous generation or edit round and wait for completion. Use for starter assets, art recipe tests, focused variations, and non-sheet image edits. For recipe tests, first save the art recipe revision and pass recipeId. Outputs are returned as model-only images. Counts against the fixed per-turn generation-round budget."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                Guid[]? referenceAssetIds = null,
                Guid? artRecipeId = null,
                string? negativePrompt = null,
                string? size = null,
                string? background = null,
                int count = 2,
                CancellationToken cancellationToken = default) =>
                RunGenerationRoundAsync(projectId, budget, prompt, negativePrompt, size, background, count, referenceAssetIds, editSourceAssetId: null, recipeId: artRecipeId, cancellationToken),
            name: "generate_sprite_sheet_candidates",
            description: "Generate sprite-sheet candidates from a concise prompt plus ordered references. For guide-driven animation, first call generate_animation_guide and put the returned SpriteGuide asset first, starter/reference sprite second, and optional style reference/art recipe after that. Do not use old SpriteSheet, Generated, Edited, Imported, or Cropped assets as motion guides. The prompt should define frame count/order, boundaries, no overlap, preservation, and guide-mark cleanup. Returns model-only candidate images; add promising batches/assets to Review for the user."),

        AIFunctionFactory.Create(
            method: (int? limit = null) => workflow.ListSpriteSheetsJsonAsync(projectId, limit),
            name: "list_sprite_sheets",
            description: "List compact sprite-sheet definitions for the current project. Use this for generated candidates that have been selected for frame detection, splitting, repair, review, or export."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                ReadSpriteSheetToolAsync(projectId, spriteSheetId, cancellationToken),
            name: "read_sprite_sheet",
            description: "Read a sprite sheet's layout and frame boxes without returning preview image bytes. Use compare review-set tools when the user should review sprite-sheet results visually."),

        AIFunctionFactory.Create(
            method: (Guid sourceAssetId, CancellationToken cancellationToken = default) =>
                CreateSpriteSheetAsync(projectId, sourceAssetId, cancellationToken),
            name: "create_sprite_sheet",
            description: "Create or select a sprite-sheet definition from an existing generated or imported asset and switch to Sprites. Use after choosing the best candidate sheet or when importing a sheet."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                int x,
                int y,
                int width,
                int height,
                string? name = null,
                int padding = 0,
                int? fixedCanvasWidth = null,
                int? fixedCanvasHeight = null,
                bool centerInCanvas = true,
                bool linkToSource = true,
                CancellationToken cancellationToken = default) =>
                ExtractRegionAsAssetAsync(projectId, sourceAssetId, x, y, width, height, name, padding, fixedCanvasWidth, fixedCanvasHeight, centerInCanvas, linkToSource, cancellationToken),
            name: "extract_region_as_asset",
            description: "Extract a rectangular region of a source image into a standalone, opaque project asset (weapon, prop, portrait, tile, UI element, VFX) and update the visible Sprites Source workspace. Coordinates are in source-image pixel space. Optional padding or a fixed centered canvas. The result stays opaque - transparency and background removal are Export-only. Returns the new asset id and its logical size/content offset."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                int? expectedFrames = null,
                string? layoutHint = null,
                bool replaceExisting = true,
                CancellationToken cancellationToken = default) =>
                DetectSourceRegionsAsync(projectId, sourceAssetId, expectedFrames, layoutHint, replaceExisting, cancellationToken),
            name: "detect_source_regions",
            description: "Greenfield Source pipeline: detect visual or grid-like frame regions on an image asset, persist them as editable SpriteRegions, update the visible Sprites workspace to Source, and return source-image pixel bounds. Use this before creating a FrameSet when regions can be detected automatically."),

        AIFunctionFactory.Create(
            method: (Guid sourceAssetId, CancellationToken cancellationToken = default) =>
                ListSourceRegionsAsync(projectId, sourceAssetId, cancellationToken),
            name: "list_source_regions",
            description: "Greenfield Source pipeline: list saved editable regions for a source image. Regions are in source-image pixel space and can be used to extract assets or create frames."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                SourceRegionEditRequest[] regions,
                CancellationToken cancellationToken = default) =>
                SaveSourceRegionsAsync(projectId, sourceAssetId, regions, cancellationToken),
            name: "save_source_regions",
            description: "Greenfield Source pipeline: replace a source image's editable SpriteRegions with explicit rectangles or polygon paths and update the visible Sprites workspace to Source. Use for agent-driven draw, move, resize, rename, delete, reorder, and polygon-region commits. Coordinates are source-image pixels."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                string? name = null,
                int? expectedFrames = null,
                string? layoutHint = null,
                CancellationToken cancellationToken = default) =>
                CreateFrameSetFromAssetAsync(projectId, sourceAssetId, name, expectedFrames, layoutHint, cancellationToken),
            name: "create_frame_set",
            description: "Greenfield Frames pipeline: detect frames in a source sheet asset, create a FrameSet, and update the visible Sprites workspace to Frames. Returns the frame set id and per-frame geometry. Deterministic; no image generation."),

        AIFunctionFactory.Create(
            method: (
                Guid sourceAssetId,
                Guid[] regionIds,
                string? name = null,
                CancellationToken cancellationToken = default) =>
                CreateFrameSetFromRegionsAsync(projectId, sourceAssetId, regionIds, name, cancellationToken),
            name: "create_frame_set_from_regions",
            description: "Greenfield Source -> Frames pipeline: create a FrameSet from selected saved SpriteRegions without re-detecting the source image, then update the visible Sprites workspace to Frames. Use after manual region placement or correction."),

        AIFunctionFactory.Create(
            method: (CancellationToken cancellationToken = default) =>
                ListFrameSetsAsync(projectId, cancellationToken),
            name: "list_frame_sets",
            description: "Greenfield Frames pipeline: list saved FrameSets for the current project so the active editing target can be selected deliberately."),

        AIFunctionFactory.Create(
            method: (Guid frameSetId, CancellationToken cancellationToken = default) =>
                SetActiveFrameSetAsync(projectId, frameSetId, cancellationToken),
            name: "set_active_frame_set",
            description: "Greenfield Frames pipeline: set the active FrameSet used by the visible Sprites workspace, assistant state, and subsequent frame/sheet operations."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default) =>
                SetCommonCellSizeAsync(projectId, frameSetId, width, height, cancellationToken),
            name: "set_common_cell_size",
            description: "Greenfield Frames pipeline: set every frame in a FrameSet to a common logical cell size (without resampling artwork), re-center content, and update the visible Sprites workspace. Pass width/height 0 to auto-pick the tightest common cell. Returns updated frame geometry."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid sourceRegionId,
                int? insertAt = null,
                string? name = null,
                CancellationToken cancellationToken = default) =>
                AddFrameFromRegionAsync(projectId, frameSetId, sourceRegionId, insertAt, name, cancellationToken),
            name: "add_frame_from_region",
            description: "Greenfield Frames pipeline: add one saved Source region as a new editable frame in an existing FrameSet and update the visible Sprites workspace. Use when a user draws an additional region after the frame set exists."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int? insertAt = null,
                string? name = null,
                CancellationToken cancellationToken = default) =>
                DuplicateFrameAsync(projectId, frameSetId, frameId, insertAt, name, cancellationToken),
            name: "duplicate_frame",
            description: "Greenfield Frames pipeline: duplicate a frame's source bounds, logical cell, content offset, timing, working image, preview, and frame mask into the same FrameSet and update the visible Sprites workspace."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int width,
                int height,
                CancellationToken cancellationToken = default) =>
                SetFrameLogicalCellAsync(projectId, frameSetId, frameId, width, height, cancellationToken),
            name: "set_frame_logical_cell",
            description: "Greenfield Frames pipeline: set one frame's logical cell dimensions without moving its source crop or resampling artwork and update the visible Sprites workspace. Use for targeted cell fixes; use set_common_cell_size for whole-set normalization."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int x,
                int y,
                int width,
                int height,
                SpriteSheetShapePath[]? shapePaths = null,
                CancellationToken cancellationToken = default) =>
                UpdateFrameSourceBoundsAsync(projectId, frameSetId, frameId, x, y, width, height, shapePaths, cancellationToken),
            name: "update_frame_source_bounds",
            description: "Greenfield Frames pipeline: move or resize one frame's source crop in source-image pixel coordinates and update the visible Sprites workspace. This changes which source pixels the frame uses without changing content offset inside the logical cell."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int contentOffsetX,
                int contentOffsetY,
                CancellationToken cancellationToken = default) =>
                TranslateFrameContentAsync(projectId, frameSetId, frameId, contentOffsetX, contentOffsetY, cancellationToken),
            name: "translate_frame_content",
            description: "Greenfield Frames pipeline: set one frame's artwork offset inside its logical cell and update the visible Sprites workspace. Use this for alignment nudges; it does not change source bounds."),

        AIFunctionFactory.Create(
            method: (Guid frameSetId, CancellationToken cancellationToken = default) =>
                ReadFrameSetAsync(projectId, frameSetId, cancellationToken),
            name: "read_frame_set",
            description: "Greenfield Frames pipeline: read a FrameSet's frames, source bounds, logical cell sizes, and content offsets without image bytes."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int targetIndex,
                CancellationToken cancellationToken = default) =>
                ReorderFrameAsync(projectId, frameSetId, frameId, targetIndex, cancellationToken),
            name: "reorder_frame",
            description: "Greenfield Frames pipeline: move one frame to a new zero-based playback/sheet order index and update the visible Sprites workspace."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                CancellationToken cancellationToken = default) =>
                DeleteFrameAsync(projectId, frameSetId, frameId, cancellationToken),
            name: "delete_frame",
            description: "Greenfield Frames pipeline: delete one frame from a FrameSet, remove its frame-owned masks, and update the visible Sprites workspace."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                int durationMs,
                CancellationToken cancellationToken = default) =>
                SetFrameDurationAsync(projectId, frameSetId, frameId, durationMs, cancellationToken),
            name: "set_frame_duration",
            description: "Greenfield Frames pipeline: set one frame's animation duration in milliseconds without changing image geometry and update the visible Sprites workspace."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                int rows = 1,
                int columns = 0,
                int padding = 0,
                int gutter = 0,
                int outerMargin = 0,
                string ordering = "rowMajor",
                string horizontalAnchor = "center",
                string verticalAnchor = "bottom",
                string? name = null,
                CancellationToken cancellationToken = default) =>
                BuildSheetAsync(projectId, frameSetId, rows, columns, padding, gutter, outerMargin, ordering, horizontalAnchor, verticalAnchor, name, cancellationToken),
            name: "build_sheet",
            description: "Greenfield Sheet pipeline: reassemble a FrameSet into a deterministic, opaque sprite-sheet project asset with equal cells, persist a linked per-frame placement manifest (BuiltSheet), and update the visible Sprites workspace to Sheet. Pass columns 0 to auto-fit. The sheet stays opaque; transparency is Export-only. Returns the output asset id, grid, cell size, and manifest."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                string anchor = "feet",
                bool axisX = true,
                bool axisY = true,
                CancellationToken cancellationToken = default) =>
                AlignFramesAsync(projectId, frameSetId, anchor, axisX, axisY, cancellationToken),
            name: "align_frames",
            description: "Greenfield Frames pipeline: deterministically align every frame inside its equal logical cell by a detected content anchor (feet | bottom | center | top | left | right), then update the visible Sprites workspace. Detects each frame's visible-content bounds, sets the anchor, and renders aligned opaque cell images. Use axisX/axisY to preserve intentional motion on one axis. Run build_sheet afterward. Prefer this over generation for jitter/alignment."),

        AIFunctionFactory.Create(
            method: (
                Guid frameId,
                string maskDataUrl,
                string? label = null,
                string coordinateSpace = "logicalFrame",
                CancellationToken cancellationToken = default) =>
                UpsertFrameMaskAsync(projectId, frameId, maskDataUrl, label, coordinateSpace, cancellationToken),
            name: "upsert_frame_mask",
            description: "Greenfield Frames pipeline: create or replace a frame-owned mask and update the visible Sprites workspace. The mask payload is a PNG data URL in the requested coordinate space, usually logicalFrame from the frame canvas."),

        AIFunctionFactory.Create(
            method: (
                Guid frameId,
                CancellationToken cancellationToken = default) =>
                ClearFrameMaskAsync(projectId, frameId, cancellationToken),
            name: "clear_frame_mask",
            description: "Greenfield Frames pipeline: remove the mask owned by one frame and update the visible Sprites workspace. History/candidate retention remains deferred."),

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
            description: "Create or extend a sprite sheet from ordered individual PNG assets. Use this when frames already exist as separate PNG assets or the user explicitly wants manual composition."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, int maxFrames = 12, CancellationToken cancellationToken = default) =>
                ReviewSpriteAnimationAsync(projectId, spriteSheetId, maxFrames, cancellationToken),
            name: "review_sprite_animation",
            description: "Review a sprite animation from the current PNG working sheet. Returns motion metrics in JSON and supplies labeled frame images, an annotated sheet view, pairwise diffs, onion-skin, and filmstrip images as model-only content with manifest fields. For frames with hidden working images it also returns removed-vs-source overlays where red marks pixels erased from the source foreground; inspect these for clipped owned silhouette before declaring cleanup done."),

        AIFunctionFactory.Create(
            method: (
                SpriteSheetRect anchorRect,
                Guid? spriteSheetId = null,
                int referenceFrameNumber = 1,
                int? searchPadding = null,
                double? minScore = null,
                int[]? targetFrameNumbers = null,
                bool apply = false,
                CancellationToken cancellationToken = default) =>
                StabilizeSpriteSheetFramesAsync(projectId, spriteSheetId, referenceFrameNumber, anchorRect, searchPadding, minScore, targetFrameNumbers, apply, cancellationToken),
            name: "stabilize_sprite_sheet_frames",
            description: "Preview or apply translation-only frame stabilization using one manual anchor box on a reference frame. anchorRect is in source-sheet pixels and must be fully inside referenceFrameNumber. Defaults: searchPadding 24px, minScore 0.68, apply false. Apply writes stabilized working-frame PNGs without changing source boxes; run reassemble_sprite_sheet afterward."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                ClearSpriteSheetStabilizationAsync(projectId, spriteSheetId, cancellationToken),
            name: "clear_sprite_sheet_stabilization",
            description: "Clear saved sprite-sheet stabilization metadata without changing frame boxes, order, previews, or working image bytes."),

        AIFunctionFactory.Create(
            method: (string mode) => SwitchWorkspaceModeAsync(projectId, mode),
            name: "switch_workspace_mode",
            description: "Switch the visible workspace mode. Allowed values: generate, review, edit, sprites, recipes, assets. Legacy alias compare is accepted."),

        AIFunctionFactory.Create(
            method: (string? title = null, string? summary = null, CompareReviewToolItem[]? items = null, bool switchToCompare = true, CancellationToken cancellationToken = default) =>
                SetCompareReviewSetAsync(projectId, title, summary, items, switchToCompare, cancellationToken),
            name: "set_compare_review_set",
            description: "Replace the current Review workspace set with ordered user-visible review items. Item kind values: asset, generationBatch, spriteSheet, spriteAnimation, spriteFrame. This does not attach images to chat or send them back as model context."),

        AIFunctionFactory.Create(
            method: (CompareReviewToolItem[]? items = null, string? title = null, string? summary = null, bool switchToCompare = true, CancellationToken cancellationToken = default) =>
                AddCompareReviewItemsAsync(projectId, items, title, summary, switchToCompare, cancellationToken),
            name: "add_compare_review_items",
            description: "Append or update items in the current Review workspace set. Item kind values: asset, generationBatch, spriteSheet, spriteAnimation, spriteFrame. This is for grouping things the user should review visually, not for model image context."),

        AIFunctionFactory.Create(
            method: (Guid itemId, CancellationToken cancellationToken = default) =>
                RemoveCompareReviewItemAsync(projectId, itemId, cancellationToken),
            name: "remove_compare_review_item",
            description: "Remove one item from the current Review workspace set by review item id."),

        AIFunctionFactory.Create(
            method: (CancellationToken cancellationToken = default) =>
                ClearCompareReviewSetAsync(projectId, cancellationToken),
            name: "clear_compare_review_set",
            description: "Clear the current Review workspace set."),

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
            description: "Heuristically map frame boxes on a generated or imported PNG sprite sheet. Give this heuristic one attempt with expectedFrames/layoutHint, inspect model-only feedback, then move to manual box repair or isolated frames."),

        AIFunctionFactory.Create(
            method: (
                Guid? sourceAssetId = null,
                Guid? spriteSheetId = null,
                int? expectedFrames = null,
                string? layoutHint = "rows",
                string? backgroundMode = "auto",
                bool apply = false,
                CancellationToken cancellationToken = default) =>
                MapSpriteSheetFramesAsync(projectId, "auto", sourceAssetId, spriteSheetId, expectedFrames, layoutHint, backgroundMode, targetFrameNumbers: null, apply, cancellationToken),
            name: "detect_sprite_frame_boxes",
            description: "Auto-detect candidate frame boxes for the current working sheet without applying by default. Defaults match the UI Auto Detect button: active/current working sheet, layoutHint rows, backgroundMode auto, and fit-cells behavior when apply=true. Use after choosing a candidate sheet and before splitting frames. If detection is close but imperfect, apply then adjust individual frame boxes instead of repeatedly detecting."),

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
            description: "Replace the visible Sprites workspace frame set and layout without changing working image bytes. Use after detection when boxes need manual correction, ordering changes, or precise geometry updates."),

        AIFunctionFactory.Create(
            method: (
                Guid? spriteSheetId,
                int frameNumber,
                SpriteSheetRect sourceRect,
                SpriteSheetRect? sourceImageRect = null,
                string? label = null,
                bool fitCells = true,
                CancellationToken cancellationToken = default) =>
                AdjustSpriteFrameBoxAsync(projectId, spriteSheetId, frameNumber, sourceRect, sourceImageRect, label, fitCells, cancellationToken),
            name: "adjust_sprite_frame_box",
            description: "Adjust one saved frame source rectangle without replacing every frame. Use after detect_sprite_frame_boxes when a specific frame box is shifted, too small, or too large. frameNumber is 1-based. fitCells matches the UI Auto Detect behavior by resizing cells to the largest current frame plus padding."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId, int frameNumber, int? margin = null, CancellationToken cancellationToken = default) =>
                IsolateSpriteFrameAsync(projectId, spriteSheetId, frameNumber, margin, cancellationToken),
            name: "isolate_sprite_frame",
            description: "Extract one saved sprite frame source region into a hidden working PNG with optional margin. frameNumber is 1-based. The source sheet can be irregular; this does not require equal source cells. Returns compact state JSON plus model-only images of the isolated working frame: a clean copy for judging pixels and a coordinate-grid companion for computing region coordinates. The reported workingMargin pads all sides, so sprite content starts at (margin, margin)."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, int? margin = null, CancellationToken cancellationToken = default) =>
                SplitSpriteSheetFramesAsync(projectId, spriteSheetId, margin, cancellationToken),
            name: "split_sprite_sheet_frames",
            description: "Split every saved frame region in the selected sprite sheet into hidden isolated working PNGs for alignment and repair. Use this after boxes are applied and before detailed frame cleanup. Returns compact frame state; use read_sprite_frame_image or review_sprite_animation to inspect pixels."),

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
            description: "Normalize irregular source regions or hidden working frame images into equal animation frames, then stitch a new one-row working sprite sheet for export."),

        AIFunctionFactory.Create(
            method: (Guid? spriteSheetId = null, CancellationToken cancellationToken = default) =>
                NormalizeSpriteSheetAsync(projectId, spriteSheetId, cancellationToken),
            name: "normalize_sprite_sheet",
            description: "Normalize an existing selected PNG sprite sheet by copying saved source rects and rebasing frame boxes/shapes. Use when the current working sheet needs a clean normalized intermediate before split/review/export."),

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
    ]);

    private static IList<AITool> WithDisplayTitleParameters(AITool[] tools) =>
        tools.Select(tool => tool is AIFunction function
            ? new DisplayTitleAIFunction(function)
            : tool).ToList();

    public bool IsWorkspaceMutation(string toolName) => WorkspaceMutationTools.Contains(toolName);

    private sealed class DisplayTitleAIFunction(AIFunction innerFunction) : DelegatingAIFunction(innerFunction)
    {
        private const string DisplayTitleDescription =
            "Short user-visible title for this tool chip. Set this for every nontrivial call. Omit only for trivial reads where the formatted tool name is enough.";

        private readonly Lazy<JsonElement> _jsonSchema = new(() => AddDisplayTitleParameter(innerFunction.JsonSchema));

        public override JsonElement JsonSchema => _jsonSchema.Value;

        private static JsonElement AddDisplayTitleParameter(JsonElement schema)
        {
            var node = JsonNode.Parse(schema.GetRawText()) as JsonObject ?? new JsonObject();
            var properties = node["properties"] as JsonObject;
            if (properties is null)
            {
                properties = new JsonObject();
                node["properties"] = properties;
            }

            if (!properties.ContainsKey("displayTitle"))
            {
                properties["displayTitle"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = DisplayTitleDescription,
                };
            }

            return JsonSerializer.SerializeToElement(node, JsonOptions);
        }
    }

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
        return $"Review item {itemId} removed.";
    }

    private async Task<string> ClearCompareReviewSetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await workflow.ClearCompareReviewSetAsync(projectId, cancellationToken);
        return "Review set cleared.";
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

    private async Task<string> SaveAnimationRecipeToolAsync(
        Guid projectId,
        Guid? recipeId,
        string name,
        string animationKind,
        string promptScaffold,
        string changeSummary,
        string? facing,
        int frameCount,
        int[]? frameOrder,
        int fps,
        bool loop,
        Guid? guideAssetId,
        SpriteSheetRect[]? expectedFrameBoxes,
        string? anchorStrategy,
        string? exportDefaultsJson,
        string? notes,
        Guid? primaryExampleSpriteSheetId,
        CancellationToken cancellationToken)
    {
        AnimationRecipeView saved;
        if (recipeId is Guid existingRecipeId)
        {
            saved = await workflow.UpdateAnimationRecipeAsync(projectId, existingRecipeId, new UpdateAnimationRecipeRequest(
                name,
                animationKind,
                facing ?? string.Empty,
                frameCount,
                frameOrder ?? [],
                Math.Clamp(fps <= 0 ? 8 : fps, 1, 60),
                loop,
                guideAssetId,
                expectedFrameBoxes ?? [],
                anchorStrategy ?? "recipe-defined",
                promptScaffold,
                exportDefaultsJson ?? "{}",
                notes ?? string.Empty,
                primaryExampleSpriteSheetId,
                "assistant",
                changeSummary), cancellationToken);
        }
        else
        {
            saved = await workflow.SaveAnimationRecipeAsync(projectId, new SaveAnimationRecipeRequest(
                name,
                animationKind,
                facing ?? string.Empty,
                frameCount,
                frameOrder ?? [],
                Math.Clamp(fps <= 0 ? 8 : fps, 1, 60),
                loop,
                guideAssetId,
                expectedFrameBoxes ?? [],
                anchorStrategy ?? "recipe-defined",
                promptScaffold,
                exportDefaultsJson ?? "{}",
                notes ?? string.Empty,
                primaryExampleSpriteSheetId,
                "assistant",
                changeSummary), cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            animationRecipeId = saved.Id,
            saved.Name,
            saved.AnimationKind,
            saved.Facing,
            saved.FrameCount,
            saved.Fps,
            saved.Loop,
            saved.CurrentVersion,
            saved.GuideAssetId,
            saved.PrimaryExampleSpriteSheetId,
            message = "Animation recipe saved and versioned.",
        }, JsonOptions);
    }

    private async Task<string> GenerateAnimationGuideToolAsync(
        Guid projectId,
        Guid? referenceAssetId,
        string animationKind,
        string? assetType,
        string? structureType,
        string? facing,
        int? frameCount,
        int? fps,
        string? rootMotion,
        string? targetCellSize,
        string? motionClipId,
        string? label,
        CancellationToken cancellationToken)
    {
        var guide = await workflow.GenerateAnimationGuideAsync(projectId, new GenerateAnimationGuideRequest(
            referenceAssetId,
            animationKind,
            assetType,
            structureType,
            facing,
            frameCount,
            fps,
            rootMotion,
            targetCellSize,
            motionClipId,
            label), cancellationToken);

        return JsonSerializer.Serialize(new
        {
            guide.GuideAssetId,
            guide.DiagnosticGuideAssetId,
            guide.Label,
            guide.AnimationKind,
            guide.AssetType,
            guide.StructureType,
            guide.Facing,
            guide.RootMotion,
            guide.FrameCount,
            guide.FrameOrder,
            guide.Fps,
            guide.Loop,
            guide.Rows,
            guide.Columns,
            guide.CanvasWidth,
            guide.CanvasHeight,
            guide.GuideCellWidth,
            guide.GuideCellHeight,
            guide.TargetCellWidth,
            guide.TargetCellHeight,
            guide.ExpectedFrameBoxes,
            guide.AnchorStrategy,
            guide.PromptScaffold,
            guide.ExportDefaultsJson,
            guide.Renderer,
            guide.RenderStyle,
            guide.MotionClipId,
            guide.MotionSourcePackage,
            guide.MotionSourceLicense,
            guide.MotionSourceUrl,
            guide.Message,
            nextStep = "Call generate_sprite_sheet_candidates with referenceAssetIds ordered as [guideAssetId, subjectAssetId, optionalStyleAssetId].",
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
        if (cancellationToken.IsCancellationRequested)
            return JsonSerializer.Serialize(new { round, budget.RoundsUsed, budget.MaxRounds, cancelled = true }, JsonOptions);
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

    private async Task<string> ExtractRegionAsAssetAsync(
        Guid projectId,
        Guid sourceAssetId,
        int x,
        int y,
        int width,
        int height,
        string? name,
        int padding,
        int? fixedCanvasWidth,
        int? fixedCanvasHeight,
        bool centerInCanvas,
        bool linkToSource,
        CancellationToken cancellationToken)
    {
        var result = await spriteActions.ExtractRegionAsAssetAsync(projectId, new ExtractRegionAsAssetRequest(
            sourceAssetId,
            x,
            y,
            width,
            height,
            string.IsNullOrWhiteSpace(name) ? null : name,
            padding,
            fixedCanvasWidth,
            fixedCanvasHeight,
            centerInCanvas,
            linkToSource), cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(new
        {
            assetId = result.Asset.Id,
            label = result.Asset.Label,
            kind = result.Asset.Kind.ToString(),
            regionId = result.RegionId,
            standaloneAssetId = result.StandaloneAssetId,
            logicalWidth = result.LogicalWidth,
            logicalHeight = result.LogicalHeight,
            contentOffsetX = result.ContentOffsetX,
            contentOffsetY = result.ContentOffsetY,
            message = "Region extracted as a standalone opaque project asset and shown in the visible Sprites Source workspace.",
        }, JsonOptions);
    }

    private async Task<string> CreateFrameSetFromAssetAsync(
        Guid projectId,
        Guid sourceAssetId,
        string? name,
        int? expectedFrames,
        string? layoutHint,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.CreateFrameSetFromAssetAsync(projectId, new CreateFrameSetFromAssetRequest(
            sourceAssetId,
            string.IsNullOrWhiteSpace(name) ? null : name,
            expectedFrames,
            layoutHint), cancellationToken);
        return SerializeFrameSet(view, "Frame set created from detected frames.");
    }

    private async Task<string> DetectSourceRegionsAsync(
        Guid projectId,
        Guid sourceAssetId,
        int? expectedFrames,
        string? layoutHint,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var regions = await spriteActions.DetectSourceRegionsAsync(projectId, new DetectSourceRegionsRequest(
            sourceAssetId,
            expectedFrames,
            layoutHint,
            replaceExisting), cancellationToken);
        return SerializeSourceRegions(sourceAssetId, regions, "Detected and saved editable source regions.");
    }

    private async Task<string> ListSourceRegionsAsync(Guid projectId, Guid sourceAssetId, CancellationToken cancellationToken)
    {
        var regions = await frameSets.ListSourceRegionsAsync(projectId, sourceAssetId, cancellationToken);
        return SerializeSourceRegions(sourceAssetId, regions, null);
    }

    private async Task<string> SaveSourceRegionsAsync(
        Guid projectId,
        Guid sourceAssetId,
        SourceRegionEditRequest[]? regions,
        CancellationToken cancellationToken)
    {
        var saved = await spriteActions.SaveSourceRegionsAsync(projectId, new SaveSourceRegionsRequest(
            sourceAssetId,
            regions?.ToList() ?? []), cancellationToken: cancellationToken);
        return SerializeSourceRegions(sourceAssetId, saved, "Saved editable source regions.");
    }

    private async Task<string> CreateFrameSetFromRegionsAsync(
        Guid projectId,
        Guid sourceAssetId,
        Guid[]? regionIds,
        string? name,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.CreateFrameSetFromRegionsAsync(projectId, new CreateFrameSetFromRegionsRequest(
            sourceAssetId,
            regionIds?.ToList() ?? [],
            string.IsNullOrWhiteSpace(name) ? null : name), cancellationToken);
        return SerializeFrameSet(view, "Frame set created from selected source regions.");
    }

    private async Task<string> ListFrameSetsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var sets = await frameSets.ListFrameSetsAsync(projectId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            frameSets = sets.Select(set => new
            {
                set.Id,
                set.Name,
                set.SourceAssetId,
                set.DefaultCellWidth,
                set.DefaultCellHeight,
                set.FrameCount,
                set.UpdatedAt,
            }),
        }, JsonOptions);
    }

    private async Task<string> SetActiveFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken)
    {
        var view = await spriteActions.SetActiveFrameSetAsync(projectId, frameSetId, cancellationToken);
        return SerializeFrameSet(view, "Active frame set selected.");
    }

    private async Task<string> SetCommonCellSizeAsync(
        Guid projectId,
        Guid frameSetId,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.SetCommonCellSizeAsync(projectId, new SetCommonCellSizeRequest(frameSetId, width, height), cancellationToken);
        return SerializeFrameSet(view, "Common logical cell size applied.");
    }

    private async Task<string> AddFrameFromRegionAsync(
        Guid projectId,
        Guid frameSetId,
        Guid sourceRegionId,
        int? insertAt,
        string? name,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.AddFrameFromRegionAsync(projectId, new AddFrameFromRegionRequest(
            frameSetId,
            sourceRegionId,
            insertAt,
            string.IsNullOrWhiteSpace(name) ? null : name), cancellationToken);
        return SerializeFrameSet(view, "Source region added as a frame.");
    }

    private async Task<string> DuplicateFrameAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int? insertAt,
        string? name,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.DuplicateFrameAsync(projectId, new DuplicateFrameRequest(
            frameSetId,
            frameId,
            insertAt,
            string.IsNullOrWhiteSpace(name) ? null : name), cancellationToken);
        return SerializeFrameSet(view, "Frame duplicated.");
    }

    private async Task<string> SetFrameLogicalCellAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.SetFrameLogicalCellAsync(projectId, new SetFrameLogicalCellRequest(frameSetId, frameId, width, height), cancellationToken);
        return SerializeFrameSet(view, "Frame logical cell updated.");
    }

    private async Task<string> UpdateFrameSourceBoundsAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int x,
        int y,
        int width,
        int height,
        SpriteSheetShapePath[]? shapePaths,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.UpdateFrameSourceBoundsAsync(projectId, new UpdateFrameSourceBoundsRequest(
            frameSetId,
            frameId,
            x,
            y,
            width,
            height,
            shapePaths?.ToList()), cancellationToken);
        return SerializeFrameSet(view, "Frame source bounds updated.");
    }

    private async Task<string> TranslateFrameContentAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int contentOffsetX,
        int contentOffsetY,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.TranslateFrameContentAsync(projectId, new TranslateFrameContentRequest(
            frameSetId,
            frameId,
            contentOffsetX,
            contentOffsetY), cancellationToken);
        return SerializeFrameSet(view, "Frame content offset updated.");
    }

    private async Task<string> ReadFrameSetAsync(Guid projectId, Guid frameSetId, CancellationToken cancellationToken)
    {
        var view = await frameSets.GetFrameSetAsync(projectId, frameSetId, cancellationToken);
        return SerializeFrameSet(view, null);
    }

    private async Task<string> ReorderFrameAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int targetIndex,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.ReorderFrameAsync(projectId, frameSetId, frameId, targetIndex, cancellationToken);
        return SerializeFrameSet(view, "Frame reordered.");
    }

    private async Task<string> DeleteFrameAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.DeleteFrameAsync(projectId, frameSetId, frameId, cancellationToken);
        return SerializeFrameSet(view, "Frame deleted.");
    }

    private async Task<string> SetFrameDurationAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        int durationMs,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.SetFrameDurationAsync(projectId, frameSetId, frameId, durationMs, cancellationToken);
        return SerializeFrameSet(view, "Frame duration updated.");
    }

    private async Task<string> AlignFramesAsync(
        Guid projectId,
        Guid frameSetId,
        string anchor,
        bool axisX,
        bool axisY,
        CancellationToken cancellationToken)
    {
        var view = await spriteActions.AlignFramesAsync(projectId, new AlignFramesRequest(frameSetId, anchor, axisX, axisY), cancellationToken);
        return SerializeFrameSet(view, $"Frames aligned by {anchor}.");
    }

    private async Task<string> UpsertFrameMaskAsync(
        Guid projectId,
        Guid frameId,
        string maskDataUrl,
        string? label,
        string coordinateSpace,
        CancellationToken cancellationToken)
    {
        var mask = await spriteActions.UpsertFrameMaskAsync(projectId, new UpsertFrameMaskRequest(
            frameId,
            maskDataUrl,
            string.IsNullOrWhiteSpace(label) ? null : label,
            string.IsNullOrWhiteSpace(coordinateSpace) ? "logicalFrame" : coordinateSpace), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            mask.Id,
            mask.AssetId,
            mask.Label,
            mask.Width,
            mask.Height,
            message = "Frame mask saved.",
        }, JsonOptions);
    }

    private async Task<string> ClearFrameMaskAsync(Guid projectId, Guid frameId, CancellationToken cancellationToken)
    {
        await spriteActions.ClearFrameMaskAsync(projectId, frameId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            frameId,
            message = "Frame mask cleared.",
        }, JsonOptions);
    }

    private async Task<string> BuildSheetAsync(
        Guid projectId,
        Guid frameSetId,
        int rows,
        int columns,
        int padding,
        int gutter,
        int outerMargin,
        string ordering,
        string horizontalAnchor,
        string verticalAnchor,
        string? name,
        CancellationToken cancellationToken)
    {
        var result = await spriteActions.BuildSheetAsync(projectId, new BuildSheetRequest(
            frameSetId,
            rows,
            columns,
            padding,
            gutter,
            outerMargin,
            ordering,
            horizontalAnchor,
            verticalAnchor,
            string.IsNullOrWhiteSpace(name) ? null : name), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            builtSheetId = result.BuiltSheetId,
            sheetLayoutId = result.SheetLayoutId,
            outputAssetId = result.OutputAssetId,
            rows = result.Rows,
            columns = result.Columns,
            cellWidth = result.CellWidth,
            cellHeight = result.CellHeight,
            width = result.Width,
            height = result.Height,
            warnings = result.Warnings,
            manifest = result.ManifestJson,
            message = "Built an opaque sprite sheet with a linked frame manifest.",
        }, JsonOptions);
    }

    private static string SerializeSourceRegions(Guid sourceAssetId, IReadOnlyList<SourceRegionView> regions, string? message) =>
        JsonSerializer.Serialize(new
        {
            sourceAssetId,
            regions = regions.Select(region => new
            {
                region.Id,
                region.SourceAssetId,
                region.Name,
                bounds = new { region.X, region.Y, region.Width, region.Height },
                region.ShapePaths,
                region.RegionType,
                region.Order,
            }),
            message,
        }, JsonOptions);

    private static string SerializeFrameSet(FrameSetView view, string? message) =>
        JsonSerializer.Serialize(new
        {
            frameSetId = view.Id,
            name = view.Name,
            sourceAssetId = view.SourceAssetId,
            defaultCellWidth = view.DefaultCellWidth,
            defaultCellHeight = view.DefaultCellHeight,
            frameCount = view.FrameCount,
            latestBuiltSheetAssetId = view.LatestBuiltSheetAssetId,
            latestBuiltSheetManifest = view.LatestBuiltSheetManifest,
            frames = view.Frames.Select(f => new
            {
                f.Id,
                f.SourceRegionId,
                f.Index,
                f.Name,
                source = new { f.SourceX, f.SourceY, f.SourceWidth, f.SourceHeight },
                logical = new { f.LogicalWidth, f.LogicalHeight },
                content = new { f.ContentOffsetX, f.ContentOffsetY },
                f.DurationMs,
                f.WorkingState,
                f.HideFromOnionSkin,
                f.HasMask,
                f.MaskId,
            }),
            message,
        }, JsonOptions);

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
            qualityGate = BuildAnimationQualityGate(review.Metrics),
            modelOnlyImages = review.Images.Select(image => new { image.Label, image.FileName, image.ContentType, image.Kind, image.FrameIndex, image.FromFrame, image.ToFrame }).ToList(),
        }, JsonOptions);
    }

    private async Task<string> StabilizeSpriteSheetFramesAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int referenceFrameNumber,
        SpriteSheetRect? anchorRect,
        int? searchPadding,
        double? minScore,
        int[]? targetFrameNumbers,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (anchorRect is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = "anchorRect is required and must be fully inside the selected reference frame.",
            }, JsonOptions);
        }

        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var result = await workflow.StabilizeSpriteSheetFramesAsync(projectId, new StabilizeSpriteSheetFramesRequest(
            resolution.SpriteSheetId!.Value,
            referenceFrameNumber,
            anchorRect,
            searchPadding,
            minScore,
            targetFrameNumbers,
            apply), cancellationToken);
        return JsonSerializer.Serialize(CompactStabilizationResult(result), JsonOptions);
    }

    private async Task<string> ClearSpriteSheetStabilizationAsync(
        Guid projectId,
        Guid? spriteSheetId,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var saved = await workflow.ClearSpriteSheetStabilizationAsync(projectId, resolution.SpriteSheetId!.Value, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            spriteSheetId = saved.Id,
            stabilization = (object?)null,
            frameCount = saved.Frames.Count,
            frames = saved.Frames.Select(CompactFrame),
            message = "Sprite-sheet stabilization metadata cleared. Frame boxes were not changed.",
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

        return await DetectSpriteSheetFramesAsync(projectId, sourceAssetId, spriteSheetId, expectedFrames, layoutHint, backgroundMode, apply, cancellationToken);
    }

    private async Task<string> DetectSpriteSheetFramesAsync(
        Guid projectId,
        Guid? sourceAssetId,
        Guid? spriteSheetId,
        int? expectedFrames,
        string? layoutHint,
        string? backgroundMode,
        bool apply,
        CancellationToken cancellationToken)
    {
        var workbench = await workflow.GetWorkbenchAsync(projectId, cancellationToken);
        var sheet = ResolveDetectionSheet(workbench, sourceAssetId, spriteSheetId);
        if (sheet is null && sourceAssetId is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Auto detection requires sourceAssetId, spriteSheetId, or an active sprite sheet.",
                activeSpriteSheetId = workbench.Project.ActiveSpriteSheetId,
            }, JsonOptions);
        }

        var sourceForDetection = sheet?.WorkingAssetId ?? sheet?.SourceAssetId ?? sourceAssetId!.Value;
        var detection = await workflow.DetectSpriteSheetFramesAsync(
            projectId,
            new SpriteSheetDetectionRequest(
                sourceForDetection,
                expectedFrames,
                string.IsNullOrWhiteSpace(layoutHint) ? "rows" : layoutHint,
                string.IsNullOrWhiteSpace(backgroundMode) ? "auto" : backgroundMode));
        if (!apply)
            return JsonSerializer.Serialize(detection, JsonOptions);

        var savedSheet = sheet is null
            ? await workflow.StartSpriteSheetEditAsync(projectId, sourceForDetection, cancellationToken)
            : sheet;
        var frames = detection.Frames
            .Select(frame => new SpriteSheetFrameUpdateView(
                frame.Index,
                $"Frame {frame.Index + 1}",
                frame.SourceRect,
                frame.ShapePaths,
                sourceForDetection,
                frame.SourceRect))
            .ToList();
        var padding = Math.Max(0, savedSheet.Padding);
        var saved = await workflow.UpdateSpriteSheetFramesAsync(projectId, new UpdateSpriteSheetFramesRequest(
            savedSheet.Id,
            Math.Clamp(detection.Rows, 1, 32),
            Math.Clamp(detection.Columns, 1, 64),
            Math.Clamp(MaxFrameWidth(detection.Frames, CeilDiv(detection.ImageWidth, Math.Max(1, detection.Columns))) + (padding * 2), 1, 8192),
            Math.Clamp(MaxFrameHeight(detection.Frames, CeilDiv(detection.ImageHeight, Math.Max(1, detection.Rows))) + (padding * 2), 1, 8192),
            padding,
            savedSheet.Gutter,
            savedSheet.Fps,
            savedSheet.Loop,
            savedSheet.HorizontalAnchor,
            savedSheet.VerticalAnchor,
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

    private async Task<string> AdjustSpriteFrameBoxAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int frameNumber,
        SpriteSheetRect sourceRect,
        SpriteSheetRect? sourceImageRect,
        string? label,
        bool fitCells,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var saved = await workflow.AdjustSpriteSheetFrameBoxAsync(projectId, new AdjustSpriteSheetFrameBoxRequest(
            resolution.SpriteSheetId!.Value,
            FrameIndexFromNumber(frameNumber),
            sourceRect,
            sourceImageRect,
            label,
            fitCells), cancellationToken);

        return JsonSerializer.Serialize(CompactSpriteSheetResult(saved, $"Frame {frameNumber} box adjusted."), JsonOptions);
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

    private static object CompactStabilizationResult(StabilizeSpriteSheetFramesResult result) => new
    {
        spriteSheetId = result.SpriteSheetId,
        result.SourceAssetId,
        result.WorkingAssetId,
        result.Applied,
        result.ImageWidth,
        result.ImageHeight,
        stabilization = CompactStabilization(result.Stabilization),
        frameCount = result.Frames.Count,
        frames = result.Frames,
        warnings = result.Warnings,
        requiresReassembly = result.Stabilization.RequiresReassembly,
        diagnosticImage = new
        {
            result.DiagnosticImage.Label,
            result.DiagnosticImage.FileName,
            result.DiagnosticImage.ContentType,
            result.DiagnosticImage.Kind,
            result.DiagnosticImage.FrameIndex,
            result.DiagnosticImage.FromFrame,
            result.DiagnosticImage.ToFrame,
        },
        savedFrameIds = result.SavedSheet?.Frames.Select(frame => frame.Id).ToList() ?? [],
        modelOnlyImages = "Stabilization returns a model-only annotated diagnostic sheet after the tool result. Green is normalized placement, yellow/red are matched anchors, and magenta is the reference anchor.",
        message = result.Applied
            ? "Sprite-sheet frames stabilized as working images. Run reassemble_sprite_sheet next."
            : "Sprite-sheet stabilization preview completed without changing frame boxes or working images.",
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
        stabilization = CompactStabilization(saved.Stabilization),
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
        translation = new { x = frame.TranslationX, y = frame.TranslationY },
    };

    private static object? CompactStabilization(SpriteSheetStabilizationView? stabilization)
    {
        if (stabilization is null)
            return null;

        return new
        {
            stabilization.ReferenceFrameNumber,
            stabilization.ReferenceFrameIndex,
            stabilization.AnchorRect,
            stabilization.ReferenceWorkingAnchorRect,
            stabilization.ReferenceAnchorCenter,
            stabilization.NormalizedWidth,
            stabilization.NormalizedHeight,
            stabilization.SearchPadding,
            stabilization.MinScore,
            stabilization.Applied,
            stabilization.RequiresReassembly,
            stabilization.UpdatedAt,
            matchCount = stabilization.Matches.Count,
            lowConfidenceCount = stabilization.Matches.Count(match => match.LowConfidence),
            clippedCount = stabilization.Matches.Count(match => match.Clipped),
            warnings = stabilization.Warnings,
            matches = stabilization.Matches.Select(match => new
            {
                match.FrameNumber,
                match.Index,
                match.SourceRect,
                match.InputFrameRect,
                match.MatchedAnchorRect,
                match.PlacementRect,
                match.DeltaX,
                match.DeltaY,
                match.Score,
                match.LowConfidence,
                match.Clipped,
                match.Warnings,
            }),
        };
    }

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

    private static object BuildAnimationQualityGate(SpriteAnimationMetricsView metrics)
    {
        var pairs = metrics.FramePairs.ToList();
        var majorOutliers = pairs
            .Where(pair =>
                pair.CentroidDistance > Math.Max(32d, metrics.MeanCentroidDrift * 2.25d)
                || Math.Abs(pair.BoundingBoxWidthDelta) > 64
                || Math.Abs(pair.BoundingBoxHeightDelta) > 64
                || Math.Abs(pair.SilhouetteAreaChangePercent) > 45d
                || pair.ForegroundPixelDiffPercent > 42d)
            .Select(pair => new
            {
                fromFrame = pair.FromFrame,
                toFrame = pair.ToFrame,
                pair.LoopSeam,
                pair.CentroidDistance,
                pair.ForegroundPixelDiffPercent,
                pair.SilhouetteAreaChangePercent,
                pair.BoundingBoxWidthDelta,
                pair.BoundingBoxHeightDelta,
            })
            .ToList();
        var repeatedPosePairs = pairs
            .Where(pair => !pair.LoopSeam && pair.CentroidDistance < 4d && pair.ForegroundPixelDiffPercent < 8d)
            .Select(pair => new { fromFrame = pair.FromFrame, toFrame = pair.ToFrame })
            .ToList();
        var warnings = new List<string>();
        if (majorOutliers.Count > 0)
            warnings.Add("Major motion, scale, silhouette, or foreground-diff outliers detected.");
        if (repeatedPosePairs.Count > 0)
            warnings.Add("Adjacent frames may repeat the same pose.");
        if (metrics.AreaVariancePercent > 30d)
            warnings.Add("Frame silhouette area variance is high; check for distortions, crop errors, or wrong boxes.");
        warnings.Add("Wrong pose order and action semantics require visual review against the requested animation/guide.");

        var status = majorOutliers.Count > 0 || repeatedPosePairs.Count > 0 || metrics.AreaVariancePercent > 30d
            ? "needs_motion_or_pose_review"
            : "pass_with_visual_review";
        var recommendedAction = majorOutliers.Count > 0 || repeatedPosePairs.Count > 0
            ? "regenerate_or_full_strip_edit_bad_pose_sequence_before_cleanup"
            : metrics.AreaVariancePercent > 30d
                ? "check_boxes_then_regenerate_or_edit_distorted_frames"
                : "continue_only_if_visual_pose_review_passes";

        return new
        {
            status,
            recommendedAction,
            warnings,
            majorOutliers,
            repeatedPosePairs,
            cleanupRule = "Use deterministic erase/keep only for guide marks, edge bleed, or background artifacts after boxes and poses are acceptable.",
        };
    }

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

    private static SpriteSheetDefinitionView? ResolveDetectionSheet(
        WorkbenchView workbench,
        Guid? sourceAssetId,
        Guid? requestedSpriteSheetId)
    {
        if (requestedSpriteSheetId is Guid requested && requested != Guid.Empty)
        {
            var exact = workbench.SpriteSheets.FirstOrDefault(sheet => sheet.Id == requested);
            if (exact is not null)
                return exact;

            var assetMatches = workbench.SpriteSheets
                .Where(sheet => sheet.SourceAssetId == requested || sheet.WorkingAssetId == requested)
                .ToList();
            if (assetMatches.Count == 1)
                return assetMatches[0];

            var activeMatch = assetMatches.FirstOrDefault(sheet => sheet.Id == workbench.Project.ActiveSpriteSheetId);
            if (activeMatch is not null)
                return activeMatch;
        }

        if (sourceAssetId is Guid source && source != Guid.Empty)
        {
            var assetMatches = workbench.SpriteSheets
                .Where(sheet => sheet.SourceAssetId == source || sheet.WorkingAssetId == source)
                .ToList();
            if (assetMatches.Count == 1)
                return assetMatches[0];

            var activeMatch = assetMatches.FirstOrDefault(sheet => sheet.Id == workbench.Project.ActiveSpriteSheetId);
            if (activeMatch is not null)
                return activeMatch;
        }

        return workbench.ActiveSpriteSheet;
    }

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

    private async Task<string> SplitSpriteSheetFramesAsync(
        Guid projectId,
        Guid? spriteSheetId,
        int? margin,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSpriteSheetIdForToolAsync(projectId, spriteSheetId, cancellationToken);
        if (resolution.ErrorJson is not null)
            return resolution.ErrorJson;

        var frames = await workflow.SplitSpriteSheetFramesAsync(projectId, resolution.SpriteSheetId!.Value, margin, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            spriteSheetId = resolution.SpriteSheetId.Value,
            frameCount = frames.Count,
            frames = frames.Select(frame => new
            {
                frameNumber = frame.Index + 1,
                frame.Index,
                frame.Label,
                frame.State,
                frame.WorkingWidth,
                frame.WorkingHeight,
                frame.WorkingMargin,
                frame.WorkingUpdatedAt,
            }),
            message = "Sprite sheet frames split into isolated working images. Use read_sprite_frame_image for exact frames or review_sprite_animation for motion review.",
        }, JsonOptions);
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
            message = "Sprite sheet reassembled as one horizontal equal-cell row.",
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
            "review" or "compare" => WorkspaceMode.Compare,
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
