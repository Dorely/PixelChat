using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.ComponentModel;
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
    IOptions<AgentOptions> agentOptions,
    IOptions<PixelChat.Art.ImageGenerationOptions> imageOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    {
        "set_compare_review_set",
        "add_compare_review_items",
        "remove_compare_review_item",
        "clear_compare_review_set",
        "mark_asset",
        "run_generation_round",
        "save_prompt_recipe",
        "set_prompt_recipe_attachments",
        "save_animation_recipe",
        "set_animation_recipe_attachments",
        "revert_recipe_version",
        "revert_animation_recipe_version",
        "generate_animation_guide",
        "generate_sprite_sheet_candidates",
        "extract_region_as_asset",
        "detect_source_regions",
        "save_source_regions",
        "create_frame_set",
        "create_frame_set_from_regions",
        "compose_frame_set_from_assets",
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
        "auto_anchor_align_frames",
        "normalize_frame_scale",
        "upsert_frame_mask",
        "clear_frame_mask",
        "erase_frame_regions",
        "edit_frame",
        "build_sheet",
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
            description: "List compact saved art recipe summaries for the current project, including current version and attachment counts. Check this before generation/editing when style or production guidance may be reusable. Use read_recipe for the full reusable prompt, notes, and attachments."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ReadPromptRecipeJsonAsync(projectId, recipeId),
            name: "read_recipe",
            description: "Read a saved art recipe's reusable prompt, private notes, current version, and example/guide attachments. Notes are never sent to image generation. This is read-only."),

        AIFunctionFactory.Create(
            method: (string? query = null, int? limit = null) =>
                workflow.ListAnimationRecipesJsonAsync(projectId, query, limit),
            name: "list_animation_recipes",
            description: "List compact saved animation recipes for reusable motion prompt guidance, notes, and attachments. Check this before sprite-sheet or motion work. Animation recipes are independent of art style unless their prompt explicitly says otherwise."),

        AIFunctionFactory.Create(
            method: (Guid recipeId) => workflow.ReadAnimationRecipeJsonAsync(projectId, recipeId),
            name: "read_animation_recipe",
            description: "Read one animation recipe's reusable motion prompt, private notes, current version, and example/guide attachments. Notes are never sent to image generation. This is read-only."),

        AIFunctionFactory.Create(
            method: (string? query = null, string? animationKind = null, bool? loop = null, int? limit = null, CancellationToken cancellationToken = default) =>
                workflow.ListMotionClipsJsonAsync(query, animationKind, loop, limit, cancellationToken),
            name: "list_motion_clips",
            description: "List available GLTF-backed humanoid motion guide clips. Use this before generate_animation_guide when the user asks for a 3D/mannequin-guided humanoid animation, then pass the selected motionClipId. Omit motionClipId for a layout-only labeled box guide."),

        AIFunctionFactory.Create(
            method: (Guid? recipeId,
                string name,
                string prompt,
                string changeSummary,
                string? notes = null,
                CancellationToken cancellationToken = default) =>
                SavePromptRecipeToolAsync(projectId, recipeId, name, prompt, changeSummary, notes, cancellationToken),
            name: "save_prompt_recipe",
            description: "Create or update an art recipe. A recipe is a name, a reusable prompt for visual style and production guidance, private notes, and optional asset attachments. Keep the prompt broad, minimal, and composable for the repeatable use case, not a one-off subject. Always provide changeSummary. Every save is versioned and revertible."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, RecipeAttachmentToolItem[]? attachments = null, CancellationToken cancellationToken = default) =>
                SetPromptRecipeAttachmentsToolAsync(projectId, recipeId, attachments, cancellationToken),
            name: "set_prompt_recipe_attachments",
            description: "Replace an art recipe's ordered asset attachments. Attachments can point to any project asset and use role 'example' or 'guide'. Attached assets are automatically used as references when the recipe is selected."),

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
                string prompt,
                string changeSummary,
                string? notes = null,
                CancellationToken cancellationToken = default) =>
                SaveAnimationRecipeToolAsync(projectId, recipeId, name, prompt, changeSummary, notes, cancellationToken),
            name: "save_animation_recipe",
            description: "Create or update an animation recipe: a name, a reusable prompt for motion and layout guidance, private notes, and optional asset attachments. It is independent of art style unless intentionally style-specific. Keep it broad, minimal, and composable for reusable motion/layout behavior. Always provide changeSummary."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, RecipeAttachmentToolItem[]? attachments = null, CancellationToken cancellationToken = default) =>
                SetAnimationRecipeAttachmentsToolAsync(projectId, recipeId, attachments, cancellationToken),
            name: "set_animation_recipe_attachments",
            description: "Replace an animation recipe's ordered asset attachments. Use role 'guide' for motion/layout guide assets and 'example' for successful outputs or visual references. Attachments can point to any project asset and are automatically used as references when the recipe is selected."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, int version, CancellationToken cancellationToken = default) =>
                RevertPromptRecipeToolAsync(projectId, recipeId, version, cancellationToken),
            name: "revert_recipe_version",
            description: "Restore an older prompt recipe snapshot as a new assistant-authored version. This is non-destructive and appends a new version entry."),

        AIFunctionFactory.Create(
            method: (Guid recipeId, int version, CancellationToken cancellationToken = default) =>
                RevertAnimationRecipeToolAsync(projectId, recipeId, version, cancellationToken),
            name: "revert_animation_recipe_version",
            description: "Restore an older animation recipe name/prompt/notes snapshot as a new assistant-authored version. Attachments are current recipe state and are not restored."),

        AIFunctionFactory.Create(
            method: (
                string? motionClipId = null,
                int? frameCount = null,
                int? fps = null,
                int? rows = null,
                int? columns = null,
                string? guideCanvasSize = null,
                string? guideCellSize = null,
                string? label = null,
                double? guideCameraYawDegrees = null,
                double? guideCameraPitchDegrees = null,
                bool? loop = null,
                double? safeMarginPercent = null,
                CancellationToken cancellationToken = default) =>
                GenerateAnimationGuideToolAsync(projectId, motionClipId, frameCount, fps, rows, columns, guideCanvasSize, guideCellSize, label, guideCameraYawDegrees, guideCameraPitchDegrees, loop, safeMarginPercent, cancellationToken),
            name: "generate_animation_guide",
            description: "Render and save a reusable guide as SpriteGuide assets. Omit motionClipId to create a layout-only labeled box guide; use mannequin vs layout-only as an iteration lever when one over-constrains or underspecifies motion. For GLTF-backed humanoid motion, first call list_motion_clips, then pass the chosen motionClipId plus explicit grid/layout controls. UI-equivalent defaults are frameCount 8, rows 2, columns 4, guideCanvasSize 1024x1024, guideCellSize 256x512, fps 8, loop true, and safeMarginPercent 12. guideCameraYawDegrees controls horizontal angle and guideCameraPitchDegrees controls camera elevation only when motionClipId is supplied. Use this before generate_sprite_sheet_candidates for guide-driven sprite-sheet work. The returned guideAssetId must be first in referenceAssetIds; do not use old SpriteSheet or Generated assets as guides."),

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
                [Description("Required readable name for the asset(s) this round will save. Use a short production name, not Image A or a generic batch label.")] string assetName,
                [Description("Hard prohibitions only, phrased as no X; phrase wanted states positively in the main prompt.")] string? negativePrompt = null,
                string? size = null,
                string? background = null,
                int count = 2,
                Guid[]? referenceAssetIds = null,
                Guid? editSourceAssetId = null,
                Guid? recipeId = null,
                Guid? animationRecipeId = null,
                CancellationToken cancellationToken = default) =>
                RunGenerationRoundAsync(projectId, budget, specificRequest, assetName, negativePrompt, size, background, count, referenceAssetIds, editSourceAssetId, recipeId, animationRecipeId, cancellationToken),
            name: "run_generation_round",
            description: "Run one generic autonomous generation or edit round and wait for completion. Requires assetName, a readable saved asset base name. Use for starter assets, broad recipe tests, focused variations, and non-sheet image edits. When reusable guidance applies, pass recipeId and/or animationRecipeId instead of pasting recipe text into specificRequest. For recipe tests, save the recipe revision first and pass recipeId. Outputs are returned as model-only images. Counts against the fixed per-turn generation-round budget."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                [Description("Required readable base name for the saved sprite-sheet candidate asset(s). Use a short production name, not Image A or a generic batch label.")] string assetName,
                Guid[]? referenceAssetIds = null,
                Guid? artRecipeId = null,
                Guid? animationRecipeId = null,
                [Description("Hard prohibitions only, phrased as no X; phrase wanted states positively in the main prompt.")] string? negativePrompt = null,
                string? size = null,
                string? background = null,
                int count = 2,
                CancellationToken cancellationToken = default) =>
                RunGenerationRoundAsync(projectId, budget, prompt, assetName, negativePrompt, size, background, count, referenceAssetIds, editSourceAssetId: null, recipeId: artRecipeId, animationRecipeId: animationRecipeId, cancellationToken: cancellationToken),
            name: "generate_sprite_sheet_candidates",
            description: "Generate sprite-sheet candidates from a concise labeled-slot prompt plus ordered references whose roles are indexed in the prompt. Requires assetName, a readable saved asset base name. Pass artRecipeId and/or animationRecipeId when saved reusable guidance applies; do not paste recipe prompts into the one-off prompt. For new guide-driven sprite sheets, first call generate_animation_guide, then attach the returned guide asset to an animation recipe or put it first manually in referenceAssetIds. Put hard prohibitions in negativePrompt. Returns model-only candidate images; add promising batches/assets to Review for the user."),

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
            description: "Greenfield Frames pipeline: manually nudge one frame by setting its artwork offset inside the logical cell, then update the visible Sprites workspace. Use this after auto-anchor review when one or a few frames still drift; it does not change source bounds."),

        AIFunctionFactory.Create(
            method: (
                Guid frameId,
                SpriteSheetRect? rect = null,
                int scale = 4,
                CancellationToken cancellationToken = default) =>
                InspectFrameAsync(projectId, frameId, rect, scale, cancellationToken),
            name: "inspect_frame",
            description: "Zoom into one frame or a sub-region at N times scale for close visual QC of hands, feet, face, or weapon grip. Coordinates are in the logical cell; out-of-range rect values are clamped. Returns a model-only image. Use before passing anatomy/facing checks, especially on back-facing sprites. Read-only and does not consume generation budget."),

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
                string verticalAnchor = "middle",
                string? name = null,
                CancellationToken cancellationToken = default) =>
                BuildSheetAsync(projectId, frameSetId, rows, columns, padding, gutter, outerMargin, ordering, horizontalAnchor, verticalAnchor, name, cancellationToken),
            name: "build_sheet",
            description: "Greenfield Sheet pipeline: reassemble a FrameSet into a deterministic, opaque sprite-sheet project asset with equal cells, persist a linked per-frame placement manifest (BuiltSheet), and update the visible Sprites workspace to Sheet. Pass columns 0 to auto-fit. Default placement is center/middle; use bottom only when the animation clearly needs grounded or base alignment. The sheet stays opaque; transparency is Export-only. Returns the output asset id, grid, cell size, and manifest."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid referenceFrameId,
                SpriteSheetRect anchorRect,
                int searchPadding = 24,
                double minScore = 0.68d,
                bool axisX = true,
                bool axisY = true,
                bool apply = true,
                CancellationToken cancellationToken = default) =>
                AutoAnchorAlignFramesAsync(projectId, frameSetId, referenceFrameId, anchorRect, searchPadding, minScore, axisX, axisY, apply, cancellationToken),
            name: "auto_anchor_align_frames",
            description: "Greenfield Frames pipeline: align frames by template-matching a small, distinctive anchor detail chosen from a reference frame. anchorRect is required and is in the reference frame's local content-pixel coordinates, not source-sheet or logical-cell coordinates. Pick a detail repeated across every frame, favor stable center-mass details when possible, avoid broad bounding boxes/generic content centers, and inspect the returned scores/deltas before trusting the result. Use axisX/axisY to preserve intentional motion on one axis. If only a few frames still drift after review, use translate_frame_content for manual nudges. Run build_sheet afterward."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                int targetHeight = 0,
                double tolerancePercent = 2.0d,
                string anchor = "bottom",
                CancellationToken cancellationToken = default) =>
                NormalizeFrameScaleAsync(projectId, frameSetId, targetHeight, tolerancePercent, anchor, cancellationToken),
            name: "normalize_frame_scale",
            description: "Greenfield Frames pipeline: deterministic cross-frame character scale normalization. Run before alignment when review reports scale deviation. targetHeight 0 uses the median foreground height; anchor is bottom or center. Does not consume generation budget. Follow with review_frame_set_animation."),

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
                string? name = null,
                CancellationToken cancellationToken = default) =>
                ComposeFrameSetFromAssetsAsync(projectId, assetIds, name, cancellationToken),
            name: "compose_frame_set_from_assets",
            description: "Greenfield Frames pipeline: compose a FrameSet from ordered individual PNG assets by laying them into one equal-cell row, then update the visible Sprites workspace. Use when frames already exist as separate PNG assets. The result stays opaque."),

        AIFunctionFactory.Create(
            method: (Guid? frameSetId = null, int maxFrames = 12, CancellationToken cancellationToken = default) =>
                ReviewFrameSetAnimationAsync(projectId, frameSetId, maxFrames, cancellationToken),
            name: "review_frame_set_animation",
            description: "Greenfield animation-quality review for a FrameSet. Renders the frames into a one-row strip and returns motion metrics, scaleStability, and visualChecklist in JSON plus labeled frame images, an annotated sheet view, pairwise diffs, onion-skin, and filmstrip images as model-only content. Answer every visualChecklist item individually before declaring the animation clean. For frames with edited/erased working images it also returns removed-vs-source overlays where red marks pixels erased from the source foreground; inspect these for clipped owned silhouette before declaring an animation clean. Omit frameSetId to use the active FrameSet. This is read-only."),

        AIFunctionFactory.Create(
            method: (string? title = null, string? summary = null, CompareReviewToolItem[]? items = null, bool switchToReview = true, CancellationToken cancellationToken = default) =>
                SetCompareReviewSetAsync(projectId, title, summary, items, switchToReview, cancellationToken),
            name: "set_compare_review_set",
            description: "Replace the Review tab set with ordered items to show the user. Item kind values: asset, artRecipe, animationRecipe, frame (a greenfield Frame id), animation (a greenfield FrameSet id, played as a looping preview). Use this to present assets, recipes, individual frames, and animation previews. This does not attach images to chat or send them back as model context."),

        AIFunctionFactory.Create(
            method: (CompareReviewToolItem[]? items = null, string? title = null, string? summary = null, bool switchToReview = true, CancellationToken cancellationToken = default) =>
                AddCompareReviewItemsAsync(projectId, items, title, summary, switchToReview, cancellationToken),
            name: "add_compare_review_items",
            description: "Append or update items in the Review tab set. Item kind values: asset, artRecipe, animationRecipe, frame (a greenfield Frame id), animation (a greenfield FrameSet id, played as a looping preview). This is for grouping things the user should review visually, not for model image context."),

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
                Guid? animationRecipeId = null,
                int? count = null,
                Guid[]? referenceAssetIds = null) => DraftGenerateFormAsync(prompt, negativePrompt, size, background, recipeId, animationRecipeId, count, referenceAssetIds),
            name: "draft_generate_form",
            description: "Draft values for the Generate form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. Use recipeId to select a saved reusable art recipe and animationRecipeId to select a saved motion/layout recipe; do not paste recipe prompts into the one-off prompt. Keep prompt focused on the new asset request and omit fields that should stay unchanged. This does not run image generation; the user reviews the form and clicks Generate manually."),

        AIFunctionFactory.Create(
            method: (
                string prompt,
                string? size = null,
                string? background = null,
                Guid? recipeId = null,
                int count = 1) => DraftEditFormAsync(prompt, size, background, recipeId, count),
            name: "draft_edit_form",
            description: "Draft values for the current Edit form. Use background as removable, auto, or opaque instead of adding background instructions to the prompt. Use removable for isolated sprites, icons, props, reusable foreground assets, and transparent-background requests; PixelChat will add the flat magenta export-prep instruction and Export background removal creates the final real-alpha PNG. Use recipeId to select saved reusable guidance; keep prompt focused on the requested edit, not the reusable recipe text. This does not choose an asset or run an image edit; the user selects an asset, may paint/review a mask for targeted edits, and clicks Send Edit manually."),

        AIFunctionFactory.Create(
            method: (
                string name,
                string prompt,
                string? notes = null) => DraftPromptRecipeFormAsync(name, prompt, notes),
            name: "draft_prompt_recipe_form",
            description: "Draft values for the recipe editor. A recipe is a reusable named prompt with private notes and optional asset attachments. This does not save a recipe; the user reviews the form and clicks Save manually."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                SpriteSheetRect[] rects,
                SpriteSheetShapePath[]? polygons = null,
                string? mode = null,
                CancellationToken cancellationToken = default) =>
                EraseFrameRegionsAsync(projectId, frameSetId, frameId, rects, polygons, mode, cancellationToken),
            name: "erase_frame_regions",
            description: "Greenfield Frames pipeline: deterministically clean one frame's logical cell using rects or polygons, store the result as the frame's working image, and update the visible Sprites workspace. mode 'erase' (default) fills the selected regions with the sheet background; mode 'keep' inverts the selection, keeping only the selected regions and filling everything else with background. Use keep to isolate the owned sprite and discard neighbor bleed in one call. Coordinates are in the logical cell; out-of-bounds coordinates are clamped. Does not consume generation budget."),

        AIFunctionFactory.Create(
            method: (
                Guid frameSetId,
                Guid frameId,
                string prompt,
                string? background = null,
                Guid[]? referenceAssetIds = null,
                bool includeAdjacentFrames = true,
                bool useFrameMask = true,
                CancellationToken cancellationToken = default) =>
                EditFrameAsync(projectId, budget, frameSetId, frameId, prompt, background, referenceAssetIds, includeAdjacentFrames, useFrameMask, cancellationToken),
            name: "edit_frame",
            description: "Greenfield Frames pipeline: AI-edit one frame's logical cell with a Change/Preserve/Constraints prompt, store the result as the frame's working image, and update the visible Sprites workspace. Consumes one autonomous generation round budget. Use only when deterministic crop/cell/offset/align/erase cannot fix the frame. Pass the identity anchor asset in referenceAssetIds when fixing anatomy or identity; previous/next frame references are included by default for continuity. Use upsert_frame_mask first for surgical edits and keep useFrameMask true. background defaults to opaque so the frame stays opaque."),

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

    private async Task<string> SetCompareReviewSetAsync(
        Guid projectId,
        string? title,
        string? summary,
        CompareReviewToolItem[]? items,
        bool switchToReview,
        CancellationToken cancellationToken)
    {
        var reviewSet = await workflow.SetCompareReviewSetAsync(
            projectId,
            new SetCompareReviewSetRequest(title, summary, ToCompareReviewRequests(items), switchToReview),
            cancellationToken);
        return JsonSerializer.Serialize(reviewSet, JsonOptions);
    }

    private async Task<string> AddCompareReviewItemsAsync(
        Guid projectId,
        CompareReviewToolItem[]? items,
        string? title,
        string? summary,
        bool switchToReview,
        CancellationToken cancellationToken)
    {
        var reviewSet = await workflow.AddCompareReviewItemsAsync(
            projectId,
            new AddCompareReviewItemsRequest(title, summary, ToCompareReviewRequests(items), switchToReview),
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
        string prompt,
        string changeSummary,
        string? notes,
        CancellationToken cancellationToken)
    {
        PromptRecipeView saved;
        if (recipeId is Guid existingRecipeId)
        {
            saved = await workflow.UpdatePromptRecipeAsync(projectId, existingRecipeId, new UpdatePromptRecipeRequest(
                name,
                prompt,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }
        else
        {
            saved = await workflow.SavePromptRecipeAsync(projectId, new SavePromptRecipeRequest(
                name,
                prompt,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            recipeId = saved.Id,
            recipeName = saved.Name,
            version = saved.CurrentVersion,
            attachmentCount = saved.Attachments.Count,
            message = "Prompt recipe saved and versioned.",
        }, JsonOptions);
    }

    private async Task<string> SetPromptRecipeAttachmentsToolAsync(
        Guid projectId,
        Guid recipeId,
        RecipeAttachmentToolItem[]? attachments,
        CancellationToken cancellationToken)
    {
        var saved = await workflow.ReplacePromptRecipeAttachmentsAsync(
            projectId,
            recipeId,
            ToAttachmentRequests(attachments),
            cancellationToken);
        return JsonSerializer.Serialize(new
        {
            recipeId = saved.Id,
            recipeName = saved.Name,
            attachmentCount = saved.Attachments.Count,
            attachments = saved.Attachments,
            message = "Prompt recipe attachments replaced.",
        }, JsonOptions);
    }

    private async Task<string> SaveAnimationRecipeToolAsync(
        Guid projectId,
        Guid? recipeId,
        string name,
        string prompt,
        string changeSummary,
        string? notes,
        CancellationToken cancellationToken)
    {
        AnimationRecipeView saved;
        if (recipeId is Guid existingRecipeId)
        {
            saved = await workflow.UpdateAnimationRecipeAsync(projectId, existingRecipeId, new UpdateAnimationRecipeRequest(
                name,
                prompt,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }
        else
        {
            saved = await workflow.SaveAnimationRecipeAsync(projectId, new SaveAnimationRecipeRequest(
                name,
                prompt,
                notes ?? string.Empty,
                "assistant",
                changeSummary), cancellationToken);
        }

        return JsonSerializer.Serialize(new
        {
            animationRecipeId = saved.Id,
            saved.Name,
            saved.CurrentVersion,
            attachmentCount = saved.Attachments.Count,
            message = "Animation recipe saved and versioned.",
        }, JsonOptions);
    }

    private async Task<string> SetAnimationRecipeAttachmentsToolAsync(
        Guid projectId,
        Guid recipeId,
        RecipeAttachmentToolItem[]? attachments,
        CancellationToken cancellationToken)
    {
        var saved = await workflow.ReplaceAnimationRecipeAttachmentsAsync(
            projectId,
            recipeId,
            ToAttachmentRequests(attachments),
            cancellationToken);
        return JsonSerializer.Serialize(new
        {
            animationRecipeId = saved.Id,
            saved.Name,
            attachmentCount = saved.Attachments.Count,
            attachments = saved.Attachments,
            message = "Animation recipe attachments replaced.",
        }, JsonOptions);
    }

    private async Task<string> GenerateAnimationGuideToolAsync(
        Guid projectId,
        string? motionClipId,
        int? frameCount,
        int? fps,
        int? rows,
        int? columns,
        string? guideCanvasSize,
        string? guideCellSize,
        string? label,
        double? guideCameraYawDegrees,
        double? guideCameraPitchDegrees,
        bool? loop,
        double? safeMarginPercent,
        CancellationToken cancellationToken)
    {
        var layoutOnly = string.IsNullOrWhiteSpace(motionClipId);
        var effectiveFrameCount = Math.Clamp(frameCount ?? 8, 1, 16);
        var effectiveRows = Math.Clamp(rows ?? 2, 1, 8);
        var effectiveColumns = Math.Clamp(columns ?? 4, 1, 8);
        if (effectiveRows * effectiveColumns < effectiveFrameCount)
            effectiveRows = Math.Clamp((int)Math.Ceiling(effectiveFrameCount / (double)effectiveColumns), 1, 8);
        if (effectiveRows * effectiveColumns < effectiveFrameCount)
            effectiveColumns = Math.Clamp((int)Math.Ceiling(effectiveFrameCount / (double)effectiveRows), 1, 8);

        var effectiveCanvasSize = string.IsNullOrWhiteSpace(guideCanvasSize) ? "1024x1024" : guideCanvasSize.Trim();
        var effectiveCellSize = string.IsNullOrWhiteSpace(guideCellSize)
            ? DerivedGuideCellSize(effectiveCanvasSize, effectiveColumns, effectiveRows)
            : guideCellSize.Trim();
        MotionClipView? clip = null;
        if (!layoutOnly)
        {
            var clips = await workflow.ListMotionClipsAsync(limit: 100, cancellationToken: cancellationToken);
            clip = FindMotionClip(clips, motionClipId!);
        }

        var animationKind = layoutOnly ? "layout" : clip?.SupportedAnimationKinds.FirstOrDefault() ?? "walk";
        var rootMotion = layoutOnly || string.IsNullOrWhiteSpace(clip?.RecommendedRootMotion) ? "in_place" : clip!.RecommendedRootMotion;
        var effectiveFps = fps ?? (clip?.DefaultFps > 0 ? clip.DefaultFps : 8);
        var effectiveLoop = loop ?? clip?.LoopRecommended ?? true;

        var guide = await workflow.GenerateAnimationGuideAsync(projectId, new GenerateAnimationGuideRequest(
            ReferenceAssetId: null,
            AnimationKind: animationKind,
            AssetType: "unit",
            StructureType: "humanoid",
            Facing: SpriteFacing.SideRight,
            FrameCount: effectiveFrameCount,
            Fps: effectiveFps,
            RootMotion: rootMotion,
            TargetCellSize: effectiveCellSize,
            MotionClipId: layoutOnly ? null : motionClipId!.Trim(),
            Label: label,
            Rows: effectiveRows,
            Columns: effectiveColumns,
            GuideCellSize: effectiveCellSize,
            GuideCameraYawDegrees: layoutOnly ? null : guideCameraYawDegrees,
            GuideCameraPitchDegrees: layoutOnly ? null : guideCameraPitchDegrees,
            Loop: effectiveLoop,
            SafeMarginPercent: safeMarginPercent ?? 12d,
            GuideCanvasSize: effectiveCanvasSize,
            LayoutOnly: layoutOnly), cancellationToken);

        return JsonSerializer.Serialize(new
        {
            layoutOnly,
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
            guide.GuideCameraYawDegrees,
            guide.GuideCameraPitchDegrees,
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
            attachmentCount = saved.Attachments.Count,
            revertedTo = version,
            message = $"Prompt recipe reverted to version {version} as a new version.",
        }, JsonOptions);
    }

    private async Task<string> RevertAnimationRecipeToolAsync(
        Guid projectId,
        Guid recipeId,
        int version,
        CancellationToken cancellationToken)
    {
        var saved = await workflow.RevertAnimationRecipeAsync(projectId, recipeId, version, "assistant", cancellationToken);
        return JsonSerializer.Serialize(new
        {
            animationRecipeId = saved.Id,
            saved.Name,
            version = saved.CurrentVersion,
            attachmentCount = saved.Attachments.Count,
            revertedTo = version,
            message = $"Animation recipe reverted to version {version} as a new version.",
        }, JsonOptions);
    }

    private async Task<string> RunGenerationRoundAsync(
        Guid projectId,
        AssistantTurnGenerationBudget budget,
        string specificRequest,
        string assetName,
        string? negativePrompt,
        string? size,
        string? background,
        int count,
        Guid[]? referenceAssetIds,
        Guid? editSourceAssetId,
        Guid? recipeId,
        Guid? animationRecipeId,
        CancellationToken cancellationToken)
    {
        var outputLabel = CleanAssetName(assetName);
        if (outputLabel is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = "assetName is required for assistant-created assets. Provide a short readable saved asset name before starting generation.",
                budget.RoundsUsed,
                budget.MaxRounds,
                roundsRemaining = Math.Max(0, budget.MaxRounds - budget.RoundsUsed),
            }, JsonOptions);
        }

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
                references,
                OutputLabel: outputLabel), cancellationToken);
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
                animationRecipeId,
                references,
                ParentBatchId: null,
                OutputLabel: outputLabel), cancellationToken);
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

    private async Task<string> InspectFrameAsync(
        Guid projectId,
        Guid frameId,
        SpriteSheetRect? rect,
        int scale,
        CancellationToken cancellationToken)
    {
        var image = await frameSets.InspectFrameAsync(projectId, frameId, rect, scale, cancellationToken);
        if (image is null)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Frame was not found or could not be rendered for inspection.",
                frameId,
            }, JsonOptions);
        }

        var (width, height) = ImageMetadataReader.TryReadSize(image.Value.Data, image.Value.ContentType);
        return JsonSerializer.Serialize(new
        {
            frameId,
            rect,
            requestedScale = scale,
            width,
            height,
            modelOnlyImage = true,
            message = "Frame inspection image returned as model-only content.",
        }, JsonOptions);
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

    private async Task<string> AutoAnchorAlignFramesAsync(
        Guid projectId,
        Guid frameSetId,
        Guid referenceFrameId,
        SpriteSheetRect anchorRect,
        int searchPadding,
        double minScore,
        bool axisX,
        bool axisY,
        bool apply,
        CancellationToken cancellationToken)
    {
        var result = await spriteActions.AlignFramesByAnchorRectAsync(projectId, new AlignFramesByAnchorRectRequest(
            frameSetId,
            referenceFrameId,
            anchorRect,
            searchPadding,
            minScore,
            axisX,
            axisY,
            apply), cancellationToken);

        using var frameSetDocument = JsonDocument.Parse(SerializeFrameSet(result.FrameSet, null));
        var frameSet = frameSetDocument.RootElement.Clone();
        var lowConfidenceCount = result.Matches.Count(match => match.LowConfidence);
        return JsonSerializer.Serialize(new
        {
            frameSet,
            result.ReferenceFrameId,
            result.AnchorRect,
            result.SearchPadding,
            result.MinScore,
            AxisX = axisX,
            AxisY = axisY,
            result.Applied,
            MatchCount = result.Matches.Count,
            LowConfidenceCount = lowConfidenceCount,
            result.Matches,
            result.Warnings,
            message = result.Applied
                ? "Auto-anchor alignment applied. Review the animation and nudge any drifting frames."
                : "Auto-anchor alignment previewed. Review diagnostics before applying or choosing a better anchor.",
        }, JsonOptions);
    }

    private async Task<string> NormalizeFrameScaleAsync(
        Guid projectId,
        Guid frameSetId,
        int targetHeight,
        double tolerancePercent,
        string anchor,
        CancellationToken cancellationToken)
    {
        var result = await spriteActions.NormalizeFrameScaleAsync(projectId, new NormalizeFrameScaleRequest(
            frameSetId,
            targetHeight,
            tolerancePercent,
            anchor), cancellationToken);
        using var frameSetDocument = JsonDocument.Parse(SerializeFrameSet(result.FrameSet, "Frame scale normalization applied."));
        return JsonSerializer.Serialize(new
        {
            frameSet = frameSetDocument.RootElement.Clone(),
            result.TargetHeight,
            result.TolerancePercent,
            result.Anchor,
            result.Frames,
            result.Warnings,
            message = "Run review_frame_set_animation next to verify scale stability before final alignment/build.",
        }, JsonOptions);
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

    private async Task<string> ComposeFrameSetFromAssetsAsync(
        Guid projectId,
        Guid[]? assetIds,
        string? name,
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
                error = "compose_frame_set_from_assets requires at least one image asset id.",
            }, JsonOptions);
        }

        var view = await spriteActions.ComposeFrameSetFromAssetsAsync(
            projectId,
            new ComposeFrameSetFromAssetsRequest(cleanedAssetIds, string.IsNullOrWhiteSpace(name) ? null : name.Trim()),
            cancellationToken);
        return SerializeFrameSet(view, "Frame set composed from individual image assets.");
    }

    private async Task<Guid?> ResolveFrameSetIdAsync(Guid projectId, Guid? frameSetId, CancellationToken cancellationToken)
    {
        if (frameSetId is Guid id && id != Guid.Empty)
            return id;
        var active = await frameSets.GetActiveFrameSetAsync(projectId, cancellationToken);
        return active?.Id;
    }

    private async Task<string> ReviewFrameSetAnimationAsync(
        Guid projectId,
        Guid? frameSetId,
        int maxFrames,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveFrameSetIdAsync(projectId, frameSetId, cancellationToken);
        if (resolved is null)
            return JsonSerializer.Serialize(new { error = "No FrameSet was found to review. Create or select a frame set first." }, JsonOptions);

        var review = await frameSets.BuildAnimationReviewAsync(projectId, resolved.Value, maxFrames, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            frameSetId = review.FrameSetId,
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

    private async Task<string> EraseFrameRegionsAsync(
        Guid projectId,
        Guid frameSetId,
        Guid frameId,
        SpriteSheetRect[]? rects,
        SpriteSheetShapePath[]? polygons,
        string? mode,
        CancellationToken cancellationToken)
    {
        var keepSelection = string.Equals(mode?.Trim(), "keep", StringComparison.OrdinalIgnoreCase);
        var view = await spriteActions.EraseFrameRegionsAsync(projectId, new EraseFrameRegionsRequest(
            frameSetId,
            frameId,
            rects ?? [],
            polygons,
            keepSelection), cancellationToken);
        return SerializeFrameSet(view, keepSelection
            ? "Frame selection kept; everything outside the regions was filled with background."
            : "Frame regions erased.");
    }

    private async Task<string> EditFrameAsync(
        Guid projectId,
        AssistantTurnGenerationBudget budget,
        Guid frameSetId,
        Guid frameId,
        string prompt,
        string? background,
        Guid[]? referenceAssetIds,
        bool includeAdjacentFrames,
        bool useFrameMask,
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
                message = "Stop editing frames with AI for this turn; deterministic erase and alignment are still available.",
            }, JsonOptions);
        }

        if (imageRuntime.HasRunningBatch(projectId))
        {
            return JsonSerializer.Serialize(new
            {
                error = "An image generation batch is already running for this project. Wait for it to finish before editing a frame.",
                budget.RoundsUsed,
                budget.MaxRounds,
            }, JsonOptions);
        }

        var round = budget.Consume();
        var view = await spriteActions.EditFrameAsync(projectId, new EditFrameRequest(
            frameSetId,
            frameId,
            prompt,
            background,
            referenceAssetIds?.Where(id => id != Guid.Empty).Distinct().ToList(),
            includeAdjacentFrames,
            useFrameMask), cancellationToken);
        using var document = JsonDocument.Parse(SerializeFrameSet(view, "Frame AI edit completed."));
        return JsonSerializer.Serialize(new
        {
            round,
            budget.RoundsUsed,
            budget.MaxRounds,
            roundsRemaining = Math.Max(0, budget.MaxRounds - budget.RoundsUsed),
            frameSet = document.RootElement.Clone(),
        }, JsonOptions);
    }

    private static Task<string> DraftGenerateFormAsync(
        string? prompt,
        string? negativePrompt,
        string? size,
        string? background,
        Guid? recipeId,
        Guid? animationRecipeId,
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
            AnimationRecipeId: animationRecipeId,
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
        string prompt,
        string? notes)
    {
        var draft = new AssistantFormDraft(
            AssistantFormDraftTarget.Recipe,
            RecipeName: name,
            Prompt: prompt,
            Notes: notes ?? string.Empty);
        return Task.FromResult(JsonSerializer.Serialize(draft, JsonOptions));
    }

    private static object BuildAnimationQualityGate(SpriteAnimationMetricsView metrics)
    {
        var pairs = metrics.FramePairs.ToList();
        var frameMetrics = metrics.Frames.ToList();
        var foregroundHeights = frameMetrics
            .Select(frame => new
            {
                frame.FrameIndex,
                frame.ForegroundHeight,
                frame.ForegroundWidth,
                frame.HeightDeviationFromMedianPercent,
                frame.ForegroundBounds,
            })
            .ToList();
        var medianForegroundHeight = Median(frameMetrics
            .Where(frame => frame.ForegroundHeight > 0)
            .Select(frame => frame.ForegroundHeight)
            .ToList());
        var scalableFrames = frameMetrics.Where(frame => frame.ForegroundHeight > 0).ToList();
        var maxHeightDeviationPercent = scalableFrames.Count == 0
            ? 0
            : scalableFrames.Max(frame => frame.HeightDeviationFromMedianPercent);
        var needsScaleNormalization = maxHeightDeviationPercent > 6d;
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
        if (needsScaleNormalization)
            warnings.Add("Cross-frame foreground height deviation is high; normalize frame scale before alignment or rebuild.");
        if (metrics.AreaVariancePercent > 30d)
            warnings.Add("Frame silhouette area variance is high; check for distortions, crop errors, or wrong boxes.");
        warnings.Add("Wrong pose order and action semantics require visual review against the requested animation/guide.");

        var status = needsScaleNormalization
            ? "needs_scale_normalization"
            : majorOutliers.Count > 0 || repeatedPosePairs.Count > 0 || metrics.AreaVariancePercent > 30d
            ? "needs_motion_or_pose_review"
            : "pass_with_visual_review";
        var recommendedAction = needsScaleNormalization
            ? "normalize_frame_scale"
            : majorOutliers.Count > 0 || repeatedPosePairs.Count > 0
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
            scaleStability = new
            {
                foregroundHeights,
                medianForegroundHeight,
                maxDeviationPercent = Math.Round(maxHeightDeviationPercent, 3, MidpointRounding.AwayFromZero),
                needsScaleNormalization,
                recommendedAction = needsScaleNormalization ? "normalize_frame_scale" : "none",
            },
            visualChecklist = BuildVisualChecklist(),
            cleanupRule = "Use deterministic erase/keep only for guide marks, edge bleed, or background artifacts after boxes and poses are acceptable.",
        };
    }

    private static object[] BuildVisualChecklist() =>
    [
        new { name = "facingConsistent", question = "Does every frame face the requested direction without accidental flips?", lookAt = "frame images; call inspect_frame for ambiguous head/torso direction" },
        new { name = "limbCountCorrect", question = "Does each frame have the correct number of arms, legs, hands, feet, weapons, or held objects?", lookAt = "frame images and inspect_frame zooms on hands/feet" },
        new { name = "anatomyOrientation", question = "Are hands and feet oriented correctly for the facing, especially back-facing sprites?", lookAt = "inspect_frame before passing this check" },
        new { name = "characterScaleStable", question = "Does the character maintain stable height and proportions across frames?", lookAt = "scaleStability plus frame images and onion skin" },
        new { name = "feetGrounded", question = "Do grounded frames share the intended baseline or contact point?", lookAt = "frame images and onion skin" },
        new { name = "guideMarksGone", question = "Are guide lines, labels, boxes, numbers, mannequins, and construction marks absent?", lookAt = "frame images and removed-vs-source overlays" },
        new { name = "silhouetteClean", question = "Is the owned silhouette clean without clipped limbs, neighbor bleed, or accidental erasures?", lookAt = "frame images and removed-vs-source overlays" },
        new { name = "motionArcMatchesRequest", question = "Does the pose sequence match the requested action and timing?", lookAt = "filmstrip, onion skin, and pairwise diffs" },
        new { name = "identityConsistent", question = "Does every frame preserve the same palette, outfit, proportions, and head-to-body ratio?", lookAt = "frame images; call inspect_frame for small-scale identity details" },
    ];

    private static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static bool HasShapePaths(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        ShapePathCount(shapePaths) > 0;

    private static int ShapePathCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths.Count(path => path.Points.Count >= 3);

    private static int ShapePointCount(IReadOnlyList<SpriteSheetShapePath> shapePaths) =>
        shapePaths
            .Where(path => path.Points.Count >= 3)
            .Sum(path => path.Points.Count);


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

    private static IReadOnlyList<RecipeAssetAttachmentRequest> ToAttachmentRequests(RecipeAttachmentToolItem[]? attachments) =>
        attachments?
            .Where(item => item.AssetId != Guid.Empty)
            .Select(item => new RecipeAssetAttachmentRequest(item.AssetId, item.Role, item.Notes))
            .ToList() ?? [];

    private static CompareReviewItemKind ParseCompareReviewItemKind(string kind) =>
        NormalizeToken(kind) switch
        {
            "asset" => CompareReviewItemKind.Asset,
            "artrecipe" or "recipe" or "promptrecipe" => CompareReviewItemKind.ArtRecipe,
            "animationrecipe" or "motionrecipe" => CompareReviewItemKind.AnimationRecipe,
            "frame" => CompareReviewItemKind.Frame,
            "animation" or "frameset" or "animationpreview" => CompareReviewItemKind.Animation,
            _ => throw new InvalidOperationException($"Unknown compare review item kind '{kind}'.")
        };

    private static string NormalizeToken(string value) =>
        value.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static MotionClipView? FindMotionClip(IReadOnlyList<MotionClipView> clips, string motionClipId)
    {
        var normalized = MotionClipCatalog.Normalize(motionClipId);
        return clips.FirstOrDefault(clip =>
            string.Equals(MotionClipCatalog.Normalize(clip.MotionClipId), normalized, StringComparison.Ordinal)
            || clip.Aliases.Any(alias => string.Equals(MotionClipCatalog.Normalize(alias), normalized, StringComparison.Ordinal)));
    }

    private static string DerivedGuideCellSize(string guideCanvasSize, int columns, int rows)
    {
        var (width, height) = TryParseGuideSize(guideCanvasSize) ?? (1024, 1024);
        return $"{Math.Max(1, width / Math.Max(1, columns))}x{Math.Max(1, height / Math.Max(1, rows))}";
    }

    private static (int Width, int Height)? TryParseGuideSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Trim().Split('x', 'X');
        if (parts.Length != 2)
            return null;

        return int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            && width > 0
            && height > 0
            ? (width, height)
            : null;
    }

    private int ClampGenerationRoundCount(int count)
    {
        var configuredMax = agentOptions.Value.MaxImagesPerGenerationRound <= 0
            ? imageOptions.Value.MaxOutputs
            : agentOptions.Value.MaxImagesPerGenerationRound;
        var max = Math.Clamp(configuredMax, 1, Math.Max(1, imageOptions.Value.MaxOutputs));
        return Math.Clamp(count <= 0 ? max : count, 1, max);
    }

    private static string? CleanAssetName(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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
            "bottom" => "bottom",
            _ => "middle",
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
