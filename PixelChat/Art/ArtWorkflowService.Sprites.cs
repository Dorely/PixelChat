using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Llm;
using PixelChat.Sprites;

namespace PixelChat.Art;

public sealed partial class ArtWorkflowService
{
    public async Task<GenerationBatchView> StartSpriteGenerationAsync(Guid projectId, SpriteGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = await spriteDocuments.ReadAsync(projectId, request.DocumentId, cancellationToken: cancellationToken);
        if (snapshot.Revision != request.ExpectedRevision) throw new SpriteConflictException(request.ExpectedRevision, snapshot.Revision);
        var frame = snapshot.Document.Frames.SingleOrDefault(f => f.Id == request.FrameId) ?? throw new InvalidOperationException("Target frame not found.");
        var layer = snapshot.Document.Layers.SingleOrDefault(l => l.Id == request.LayerId) ?? throw new InvalidOperationException("Target layer not found.");
        if (layer.Locked) throw new InvalidOperationException("Target layer is locked.");
        var project = await GetProjectAsync(projectId, cancellationToken);
        var selection = await imageSelection.GetAsync(cancellationToken);
        var model = request.ImageModel ?? selection.Model; var quality = request.Quality ?? selection.Quality;
        var roles = request.References ?? [];
        if (roles.Any(r => string.IsNullOrWhiteSpace(r.Role) || r.Role.Length > 100 || r.Preserve.Length > 2000)) throw new InvalidOperationException("Each reference needs a concise role and preserve scope.");
        var references = await ResolveAssetsAsync(projectId, roles.Select(r => r.AssetId).Distinct().ToList(), cancellationToken);
        PromptRecipe? recipe = null; AnimationRecipe? animation = null;
        if (request.RecipeId is { } recipeId) recipe = await db.PromptRecipes.Include(r => r.Attachments).ThenInclude(a => a.Asset).SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken) ?? throw new InvalidOperationException("Art recipe not found.");
        if (recipe is not null) foreach (var attachment in recipe.Attachments.OrderBy(a => a.SortOrder)) if (references.All(r => r.Id != attachment.AssetId)) references.Add(attachment.Asset);
        if (request.AnimationRecipeId is { } animationId)
        {
            animation = await db.AnimationRecipes.Include(r => r.Attachments).ThenInclude(a => a.Asset).SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == animationId, cancellationToken) ?? throw new InvalidOperationException("Animation recipe not found.");
            foreach (var attachment in animation.Attachments.OrderBy(a => a.SortOrder)) if (references.All(r => r.Id != attachment.AssetId)) references.Add(attachment.Asset);
        }
        if (references.Count > imageOptions.Value.MaxReferenceImages) throw new InvalidOperationException($"Use at most {imageOptions.Value.MaxReferenceImages} reference images including recipe attachments.");
        // Freeze provider ordering and make every reference role explicit in the one-off prompt.
        var prompt = CleanRequired(request.Prompt, "Sprite prompt is required.");
        prompt += "\nReferences:\n" + string.Join("\n", references.Select((a, i) =>
        {
            var role = roles.FirstOrDefault(r => r.AssetId == a.Id);
            return $"Image {i + 1}: {role?.Role ?? "recipe example"}; {role?.Preserve ?? "use the attached recipe's relevant visual guidance"}.";
        }));
        if (animation is not null && request.Kind != "reference") prompt = $"Animation guidance: {animation.Prompt}\n{prompt}";
        EditCanvasPreparation? preparation = null;
        if (request.Kind != "reference")
        {
            if (request.CanvasPreparationId is not { } preparationId) throw new InvalidOperationException("Native edits require captured canvas preparation.");
            if (request.CanvasOptions is not null || request.MaskPngDataUrl is not null) throw new InvalidOperationException("Preparation locks the source/mask/canvas. Do not repeat canvas or mask arguments.");
            if (!canvasPreparations.TryGet(projectId, preparationId, SpriteGenerationService.TargetKind(layer.Id), frame.Id, snapshot.DocumentId, snapshot.Revision, out preparation, out var error)) throw new InvalidOperationException(error);
        }
        var background = preparation?.Background ?? ImageBackgroundModes.NormalizeGeneration(request.Background);
        ImageModelCatalog.Validate(model, quality, background, "png");
        var target = new SpriteGenerationTarget(snapshot.DocumentId, snapshot.Revision, frame.Id, layer.Id, request.Kind,
            references.Select(a => roles.FirstOrDefault(r => r.AssetId == a.Id) ?? new SpriteReference(a.Id, "recipe attachment", "relevant recipe guidance")).ToList(), request.TaskId,
            frame.Cels.GetValueOrDefault(layer.Id) ?? "", preparation?.Options.PaddingLeft ?? 0, preparation?.Options.PaddingTop ?? 0,
            references.Where(a => !string.IsNullOrEmpty(a.SourceMetadataJson)).ToDictionary(a => a.Id, a => a.SourceMetadataJson));
        var batch = new GenerationBatch
        {
            ProjectId = projectId, Label = $"{snapshot.Document.Name}: {request.Kind} {frame.Name}", Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel, ImageModel = model, Quality = quality, OutputFormat = "png", Background = background,
            Count = ClampCount(request.Count), Size = preparation?.Canvas.OutputSize ?? NormalizeSize("auto"),
            RecipePromptSnapshot = recipe?.Prompt ?? "", AnimationPromptSnapshot = animation?.Prompt ?? "", AnimationNameSnapshot = animation?.Name ?? "",
            PromptRecipeId = recipe?.Id, PromptRecipeVersion = recipe is null ? null : await GetCurrentRecipeVersionAsync(recipe.Id, cancellationToken),
            AnimationRecipeId = animation?.Id, AnimationRecipeVersion = animation is null ? null : await GetCurrentAnimationRecipeVersionAsync(animation.Id, cancellationToken),
            InputAssetIdsJson = SerializeIds(references.Select(r => r.Id)),
            EditSourceContentType = preparation is null ? null : "image/png", EditSourceData = preparation?.Canvas.ProviderSourcePng,
            EditSourceWidth = preparation?.Canvas.Transform.ProviderWidth, EditSourceHeight = preparation?.Canvas.Transform.ProviderHeight,
            EditLogicalMaskData = preparation?.Canvas.LogicalMaskPng, EditCanvasTransformJson = preparation is null ? "" : JsonSerializer.Serialize(preparation.Canvas.Transform, JsonOptions),
            SpriteLogicalSourceData = preparation?.Canvas.LogicalSourcePng, SpriteProviderMaskData = preparation?.Canvas.ProviderMaskPng,
            SpriteTargetJson = JsonSerializer.Serialize(target, SpriteDocument.JsonOptions), Status = GenerationBatchStatus.Running
        };
        GenerationQueueState.Initialize(batch, [new(prompt, batch.Count)], references);
        db.GenerationBatches.Add(batch); project.ActiveBatchId = batch.Id; project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (preparation is not null) canvasPreparations.Remove(preparation.Id);
        return BatchView(batch, []);
    }
}
