using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PixelChat.Llm;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Art;

public sealed class ArtWorkflowService(
    AppDbContext db,
    IImageProvider imageProvider,
    ILlmProviderService providerService,
    IOptions<ImageGenerationOptions> imageOptions) : IArtWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<ProjectView> EnsureDefaultProjectAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.Projects.OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return ProjectView(existing);

        var project = new Project { Name = "Untitled Game Art Project" };
        await db.Projects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectView(project);
    }

    public async Task<ProjectView> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Untitled Game Art Project" : name.Trim();
        var project = new Project { Name = trimmed };
        await db.Projects.AddAsync(project, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ProjectView(project);
    }

    public async Task<WorkbenchView> GetWorkbenchAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var selected = projectId is Guid id
            ? await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            : null;
        if (selected is null)
        {
            var defaultProject = await EnsureDefaultProjectAsync(cancellationToken);
            selected = await db.Projects.FirstAsync(p => p.Id == defaultProject.Id, cancellationToken);
        }

        var projects = await db.Projects.OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var assets = await db.ArtAssets
            .Where(a => a.ProjectId == selected.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        var batches = await db.GenerationBatches
            .Where(b => b.ProjectId == selected.Id)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        var recipes = await db.PromptRecipes
            .Where(r => r.ProjectId == selected.Id)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var masks = await db.ImageMasks
            .Where(m => m.ProjectId == selected.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == selected.Id)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var assetViews = assets.Select(AssetView).ToList();
        var batchViews = batches.Select(batch => BatchView(batch, assets)).ToList();
        var recipeViews = recipes.Select(RecipeView).ToList();
        var maskViews = masks.Select(MaskView).ToList();
        var attachmentViews = attachments.Select(AttachmentView).ToList();

        var providerStatus = await BuildProviderStatusAsync(cancellationToken);
        return new WorkbenchView(
            ProjectView(selected),
            projects.Select(ProjectView).ToList(),
            assetViews,
            batchViews,
            recipeViews,
            maskViews,
            attachmentViews,
            assetViews.FirstOrDefault(a => a.Id == selected.ActiveAssetId),
            batchViews.FirstOrDefault(b => b.Id == selected.ActiveBatchId),
            providerStatus);
    }

    public async Task SetWorkspaceModeAsync(Guid projectId, WorkspaceMode mode, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveWorkspaceMode = mode;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SelectAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (!await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken))
            throw new InvalidOperationException("Asset was not found.");

        project.ActiveAssetId = assetId;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SelectBatchAsync(Guid projectId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (!await db.GenerationBatches.AnyAsync(b => b.ProjectId == projectId && b.Id == batchId, cancellationToken))
            throw new InvalidOperationException("Generation batch was not found.");

        project.ActiveBatchId = batchId;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GenerationBatchView> GenerateImagesAsync(
        Guid projectId,
        GenerateImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var prompt = CleanRequired(request.Prompt, "Prompt is required.");
        var count = ClampCount(request.Count);
        var references = await ResolveAssetsAsync(projectId, request.ReferenceAssetIds, cancellationToken);
        if (references.Count > imageOptions.Value.MaxReferenceImages)
            throw new InvalidOperationException($"Select no more than {imageOptions.Value.MaxReferenceImages} reference images.");

        PromptRecipe? recipe = null;
        if (request.PromptRecipeId is Guid recipeId)
            recipe = await db.PromptRecipes.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Id == recipeId, cancellationToken)
                ?? throw new InvalidOperationException("Prompt recipe was not found.");

        var batch = new GenerationBatch
        {
            ProjectId = projectId,
            Label = $"Batch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel,
            ImageModel = imageOptions.Value.DefaultImageModel,
            Prompt = prompt,
            NegativePrompt = Clean(request.NegativePrompt),
            Size = NormalizeSize(request.Size),
            Count = count,
            InputAssetIdsJson = SerializeIds(references.Select(a => a.Id)),
            ParentBatchId = request.ParentBatchId,
            PromptRecipeId = recipe?.Id,
            Status = GenerationBatchStatus.Running,
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var providerResult = await imageProvider.GenerateAsync(new ImageProviderGenerateRequest(
                BuildPrompt(prompt, request.NegativePrompt, recipe),
                Clean(request.NegativePrompt),
                batch.Size,
                count,
                batch.MainlineModel,
                batch.ImageModel,
                references.Select(ToProviderReference).ToList(),
                imageOptions.Value.DefaultOutputFormat,
                imageOptions.Value.DefaultQuality,
                imageOptions.Value.DefaultBackground), cancellationToken);

            batch.Provider = providerResult.Provider;
            batch.MainlineModel = providerResult.MainlineModel;
            batch.ImageModel = providerResult.ImageModel;
            batch.RawProviderResponseJson = providerResult.RawMetadataJson;
            batch.Status = GenerationBatchStatus.Succeeded;
            batch.UpdatedAt = DateTime.UtcNow;

            var ordinal = 0;
            ArtAsset? firstAsset = null;
            foreach (var image in providerResult.Images)
            {
                var label = $"Image {LabelForIndex(ordinal++)}";
                var asset = CreateAsset(
                    projectId,
                    label,
                    $"generated-{DateTime.UtcNow:yyyyMMddHHmmss}-{ordinal}.{ExtensionForContentType(image.ContentType)}",
                    ArtAssetKind.Generated,
                    image.ContentType,
                    image.Data,
                    parentAssetId: null,
                    sourceBatchId: batch.Id,
                    promptRecipeId: recipe?.Id,
                    prompt: prompt,
                    metadata: new
                    {
                        image.RevisedPrompt,
                        image.ResponseId,
                        image.CallId,
                        image.OutputFormat,
                        References = references.Select(a => new { a.Id, a.Label, a.ContentType }),
                    });
                await db.ArtAssets.AddAsync(asset, cancellationToken);
                firstAsset ??= asset;
            }

            if (firstAsset is not null)
                project.ActiveAssetId = firstAsset.Id;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            batch.Status = GenerationBatchStatus.Failed;
            batch.Error = ex.Message;
            batch.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var batchAssets = await db.ArtAssets.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        return BatchView(batch, batchAssets);
    }

    public async Task<GenerationBatchView> EditImageAsync(
        Guid projectId,
        EditImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        var prompt = CleanRequired(request.Prompt, "Edit prompt is required.");
        var sourceAsset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.SourceAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Source asset was not found.");
        var references = await ResolveAssetsAsync(projectId, request.ReferenceAssetIds, cancellationToken);
        var count = ClampCount(request.Count);

        var (sourceContentType, sourceData) = string.IsNullOrWhiteSpace(request.SourcePngDataUrl)
            ? (sourceAsset.ContentType, sourceAsset.Data)
            : DataUrl.Parse(request.SourcePngDataUrl);

        ImageMask? storedMask = null;
        string maskContentType;
        byte[] maskData;
        if (!string.IsNullOrWhiteSpace(request.MaskPngDataUrl))
        {
            (maskContentType, maskData) = DataUrl.Parse(request.MaskPngDataUrl);
            storedMask = await AddMaskAsync(projectId, sourceAsset.Id, maskContentType, maskData, "Mask", cancellationToken);
        }
        else if (request.MaskId is Guid maskId)
        {
            storedMask = await db.ImageMasks.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.Id == maskId, cancellationToken)
                ?? throw new InvalidOperationException("Mask was not found.");
            maskContentType = storedMask.ContentType;
            maskData = storedMask.Data;
        }
        else
        {
            throw new InvalidOperationException("A painted mask is required for masked edit.");
        }

        var batch = new GenerationBatch
        {
            ProjectId = projectId,
            Label = $"Edit {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            Provider = OpenAIAccountProvider.Name,
            MainlineModel = imageOptions.Value.DefaultMainlineModel,
            ImageModel = imageOptions.Value.DefaultImageModel,
            Prompt = prompt,
            Size = NormalizeSize(request.Size),
            Count = count,
            InputAssetIdsJson = SerializeIds(new[] { sourceAsset.Id }.Concat(references.Select(a => a.Id))),
            InputMaskIdsJson = storedMask is null ? "[]" : SerializeIds([storedMask.Id]),
            ParentBatchId = sourceAsset.SourceBatchId,
            Status = GenerationBatchStatus.Running,
        };
        await db.GenerationBatches.AddAsync(batch, cancellationToken);
        project.ActiveBatchId = batch.Id;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var providerResult = await imageProvider.EditAsync(new ImageProviderEditRequest(
                prompt,
                batch.Size,
                count,
                batch.MainlineModel,
                batch.ImageModel,
                new ImageProviderReference(sourceAsset.FileName, sourceContentType, sourceData),
                new ImageProviderReference(storedMask?.Label ?? "mask.png", maskContentType, maskData),
                references.Select(ToProviderReference).ToList(),
                imageOptions.Value.DefaultOutputFormat,
                imageOptions.Value.DefaultQuality,
                imageOptions.Value.DefaultBackground), cancellationToken);

            batch.Provider = providerResult.Provider;
            batch.MainlineModel = providerResult.MainlineModel;
            batch.ImageModel = providerResult.ImageModel;
            batch.RawProviderResponseJson = providerResult.RawMetadataJson;
            batch.Status = GenerationBatchStatus.Succeeded;
            batch.UpdatedAt = DateTime.UtcNow;

            var ordinal = 0;
            ArtAsset? firstAsset = null;
            foreach (var image in providerResult.Images)
            {
                var asset = CreateAsset(
                    projectId,
                    $"{sourceAsset.Label} edit {LabelForIndex(ordinal++)}",
                    $"edited-{DateTime.UtcNow:yyyyMMddHHmmss}-{ordinal}.{ExtensionForContentType(image.ContentType)}",
                    ArtAssetKind.Edited,
                    image.ContentType,
                    image.Data,
                    sourceAsset.Id,
                    batch.Id,
                    sourceAsset.SourcePromptRecipeId,
                    prompt,
                    new
                    {
                        image.RevisedPrompt,
                        image.ResponseId,
                        image.CallId,
                        image.OutputFormat,
                        SourceAssetId = sourceAsset.Id,
                        MaskId = storedMask?.Id,
                    });
                await db.ArtAssets.AddAsync(asset, cancellationToken);
                firstAsset ??= asset;
            }

            if (firstAsset is not null)
                project.ActiveAssetId = firstAsset.Id;
            project.ActiveWorkspaceMode = WorkspaceMode.Compare;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            batch.Status = GenerationBatchStatus.Failed;
            batch.Error = ex.Message;
            batch.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var batchAssets = await db.ArtAssets.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        return BatchView(batch, batchAssets);
    }

    public async Task<ArtAssetView> ImportAssetAsync(Guid projectId, ImportAssetRequest request, CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var contentType = NormalizeImageContentType(request.ContentType)
            ?? throw new InvalidOperationException("Only PNG and JPEG images can be imported.");
        if (request.Data.Length == 0)
            throw new InvalidOperationException("Image file is empty.");

        var asset = CreateAsset(
            projectId,
            string.IsNullOrWhiteSpace(request.Label) ? Path.GetFileNameWithoutExtension(request.FileName) : request.Label.Trim(),
            string.IsNullOrWhiteSpace(request.FileName) ? $"imported-{DateTime.UtcNow:yyyyMMddHHmmss}.{ExtensionForContentType(contentType)}" : request.FileName.Trim(),
            ArtAssetKind.Imported,
            contentType,
            request.Data,
            parentAssetId: null,
            sourceBatchId: null,
            promptRecipeId: null,
            prompt: string.Empty,
            metadata: new { Source = "import" });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await SelectAfterAssetMutationAsync(projectId, asset.Id, WorkspaceMode.Edit, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
    }

    public async Task<ArtAssetView> CreateCropAssetAsync(Guid projectId, CropAssetRequest request, CancellationToken cancellationToken = default)
    {
        var parent = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == request.ParentAssetId, cancellationToken)
            ?? throw new InvalidOperationException("Parent asset was not found.");
        var (contentType, data) = DataUrl.Parse(request.CropDataUrl);
        contentType = NormalizeImageContentType(contentType) ?? "image/png";
        var asset = CreateAsset(
            projectId,
            string.IsNullOrWhiteSpace(request.Label) ? $"{parent.Label} crop" : request.Label.Trim(),
            $"crop-{DateTime.UtcNow:yyyyMMddHHmmss}.{ExtensionForContentType(contentType)}",
            ArtAssetKind.Cropped,
            contentType,
            data,
            parent.Id,
            parent.SourceBatchId,
            parent.SourcePromptRecipeId,
            parent.Prompt,
            new { ParentAssetId = parent.Id });
        await db.ArtAssets.AddAsync(asset, cancellationToken);
        await SelectAfterAssetMutationAsync(projectId, asset.Id, WorkspaceMode.Edit, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AssetView(asset);
    }

    public async Task<ImageMaskView> SaveMaskAsync(Guid projectId, SaveMaskRequest request, CancellationToken cancellationToken = default)
    {
        if (!await db.ArtAssets.AnyAsync(a => a.ProjectId == projectId && a.Id == request.AssetId, cancellationToken))
            throw new InvalidOperationException("Asset was not found.");
        var (contentType, data) = DataUrl.Parse(request.MaskDataUrl);
        var mask = await AddMaskAsync(projectId, request.AssetId, contentType, data, request.Label, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return MaskView(mask);
    }

    public async Task<PromptRecipeView> SavePromptRecipeAsync(Guid projectId, SavePromptRecipeRequest request, CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var name = CleanRequired(request.Name, "Recipe name is required.");
        var recipe = new PromptRecipe
        {
            ProjectId = projectId,
            Name = name,
            AssetType = Clean(request.AssetType),
            PromptTemplate = CleanRequired(request.PromptTemplate, "Prompt template is required."),
            StyleRulesJson = SerializeStrings(request.StyleRules),
            AvoidRulesJson = SerializeStrings(request.AvoidRules),
            ExampleAssetIdsJson = SerializeIds(request.ExampleAssetIds),
            PreferredProvider = Clean(request.PreferredProvider),
            PreferredModel = Clean(request.PreferredModel),
            PreferredSize = Clean(request.PreferredSize),
            Notes = Clean(request.Notes),
        };
        await db.PromptRecipes.AddAsync(recipe, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return RecipeView(recipe);
    }

    public async Task MarkAssetAsync(Guid projectId, Guid assetId, bool? favorite, bool? rejected, string? notes, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        if (favorite is not null)
            asset.IsFavorite = favorite.Value;
        if (rejected is not null)
            asset.IsRejected = rejected.Value;
        if (notes is not null)
            asset.Notes = notes.Trim();
        asset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UseAssetAsReferenceAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException("Asset was not found.");
        asset.IsReference = true;
        asset.UpdatedAt = DateTime.UtcNow;
        await AttachContextAsync(projectId, ChatContextAttachmentType.Asset, assetId, asset.Label, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.ArtAssets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == assetId, cancellationToken);
        if (asset is null)
            return;

        var attachments = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId && a.RefId == assetId)
            .ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);
        db.ArtAssets.Remove(asset);
        var project = await GetProjectAsync(projectId, cancellationToken);
        if (project.ActiveAssetId == assetId)
            project.ActiveAssetId = null;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatContextAttachmentView> AttachContextAsync(
        Guid projectId,
        ChatContextAttachmentType type,
        Guid refId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        _ = await GetProjectAsync(projectId, cancellationToken);
        var existing = await db.ChatContextAttachments.FirstOrDefaultAsync(
            a => a.ProjectId == projectId && a.Type == type && a.RefId == refId,
            cancellationToken);
        if (existing is not null)
            return AttachmentView(existing);

        var nextOrder = await db.ChatContextAttachments
            .Where(a => a.ProjectId == projectId)
            .Select(a => (int?)a.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;
        var attachment = new ChatContextAttachment
        {
            ProjectId = projectId,
            Type = type,
            RefId = refId,
            Label = string.IsNullOrWhiteSpace(label)
                ? await ResolveAttachmentLabelAsync(projectId, type, refId, cancellationToken)
                : label.Trim(),
            SortOrder = nextOrder + 1,
        };
        await db.ChatContextAttachments.AddAsync(attachment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AttachmentView(attachment);
    }

    public async Task RemoveContextAsync(Guid projectId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.ChatContextAttachments.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.Id == attachmentId, cancellationToken);
        if (attachment is null)
            return;
        db.ChatContextAttachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var attachments = await db.ChatContextAttachments.Where(a => a.ProjectId == projectId).ToListAsync(cancellationToken);
        db.ChatContextAttachments.RemoveRange(attachments);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetWorkspaceStateJsonAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var view = await GetWorkbenchAsync(projectId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            project = view.Project,
            activeAsset = view.ActiveAsset is null ? null : CompactAsset(view.ActiveAsset),
            activeBatch = view.ActiveBatch,
            attachedContext = view.Attachments,
            recentAssets = view.Assets.Take(12).Select(CompactAsset),
            recentBatches = view.Batches.Take(6),
            recipes = view.Recipes.Take(12),
            provider = view.ImageProviderStatus,
        }, JsonOptions);
    }

    private async Task<ImageMask> AddMaskAsync(
        Guid projectId,
        Guid assetId,
        string contentType,
        byte[] data,
        string label,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeImageContentType(contentType) ?? "image/png";
        var (width, height) = ImageMetadataReader.TryReadSize(data, normalizedType);
        var mask = new ImageMask
        {
            ProjectId = projectId,
            AssetId = assetId,
            Label = string.IsNullOrWhiteSpace(label) ? "Mask" : label.Trim(),
            ContentType = normalizedType,
            Data = data,
            Width = width ?? 0,
            Height = height ?? 0,
        };
        await db.ImageMasks.AddAsync(mask, cancellationToken);
        return mask;
    }

    private async Task SelectAfterAssetMutationAsync(Guid projectId, Guid assetId, WorkspaceMode mode, CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        project.ActiveAssetId = assetId;
        project.ActiveWorkspaceMode = mode;
        project.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
        ?? throw new InvalidOperationException("Project was not found.");

    private async Task<List<ArtAsset>> ResolveAssetsAsync(Guid projectId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken)
    {
        var ids = assetIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var assets = await db.ArtAssets.Where(a => a.ProjectId == projectId && ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (assets.Count != ids.Count)
            throw new InvalidOperationException("One or more reference assets could not be found.");
        var byId = assets.ToDictionary(a => a.Id);
        return ids.Select(id => byId[id]).ToList();
    }

    private async Task<string> ResolveAttachmentLabelAsync(Guid projectId, ChatContextAttachmentType type, Guid refId, CancellationToken cancellationToken)
    {
        return type switch
        {
            ChatContextAttachmentType.Asset or ChatContextAttachmentType.Crop =>
                await db.ArtAssets.Where(a => a.ProjectId == projectId && a.Id == refId).Select(a => a.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Asset",
            ChatContextAttachmentType.Mask =>
                await db.ImageMasks.Where(m => m.ProjectId == projectId && m.Id == refId).Select(m => m.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Mask",
            ChatContextAttachmentType.PromptRecipe =>
                await db.PromptRecipes.Where(r => r.ProjectId == projectId && r.Id == refId).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken)
                ?? "Recipe",
            ChatContextAttachmentType.GenerationBatch =>
                await db.GenerationBatches.Where(b => b.ProjectId == projectId && b.Id == refId).Select(b => b.Label).FirstOrDefaultAsync(cancellationToken)
                ?? "Batch",
            _ => "Context"
        };
    }

    private async Task<ProviderStatusView> BuildProviderStatusAsync(CancellationToken cancellationToken)
    {
        var connected = await providerService.IsOpenAIAccountConnectedAsync(cancellationToken);
        return connected
            ? new ProviderStatusView(true, "OpenAI account connected")
            : new ProviderStatusView(false, "Connect OpenAI account in Providers before generating or editing images.");
    }

    private ArtAsset CreateAsset(
        Guid projectId,
        string label,
        string fileName,
        ArtAssetKind kind,
        string contentType,
        byte[] data,
        Guid? parentAssetId,
        Guid? sourceBatchId,
        Guid? promptRecipeId,
        string prompt,
        object metadata)
    {
        var (width, height) = ImageMetadataReader.TryReadSize(data, contentType);
        return new ArtAsset
        {
            ProjectId = projectId,
            Label = label.Trim(),
            FileName = fileName,
            Kind = kind,
            ContentType = contentType,
            Data = data,
            Width = width,
            Height = height,
            ParentAssetId = parentAssetId,
            SourceBatchId = sourceBatchId,
            SourcePromptRecipeId = promptRecipeId,
            Prompt = prompt,
            SourceMetadataJson = JsonSerializer.Serialize(metadata, JsonOptions),
        };
    }

    private static string BuildPrompt(string prompt, string negativePrompt, PromptRecipe? recipe)
    {
        var parts = new List<string> { prompt.Trim() };
        if (recipe is not null)
        {
            if (!string.IsNullOrWhiteSpace(recipe.StyleRulesJson))
            {
                var styleRules = DeserializeStrings(recipe.StyleRulesJson);
                if (styleRules.Count > 0)
                    parts.Add("Style rules: " + string.Join("; ", styleRules));
            }
        }

        var avoidRules = new List<string>();
        if (!string.IsNullOrWhiteSpace(negativePrompt))
            avoidRules.Add(negativePrompt.Trim());
        if (recipe is not null)
            avoidRules.AddRange(DeserializeStrings(recipe.AvoidRulesJson));
        if (avoidRules.Count > 0)
            parts.Add("Avoid: " + string.Join("; ", avoidRules));
        return string.Join("\n\n", parts);
    }

    private static ImageProviderReference ToProviderReference(ArtAsset asset) =>
        new(asset.FileName, asset.ContentType, asset.Data);

    private static object CompactAsset(ArtAssetView asset) => new
    {
        asset.Id,
        asset.Label,
        kind = asset.Kind.ToString(),
        asset.Width,
        asset.Height,
        asset.ParentAssetId,
        asset.SourceBatchId,
        asset.IsFavorite,
        asset.IsRejected,
        asset.IsReference,
        asset.Notes,
    };

    private static ProjectView ProjectView(Project project) =>
        new(project.Id, project.Name, project.ActiveWorkspaceMode, project.ActiveAssetId, project.ActiveBatchId);

    private static ArtAssetView AssetView(ArtAsset asset) =>
        new(
            asset.Id,
            asset.Label,
            string.IsNullOrWhiteSpace(asset.FileName) ? $"asset-{asset.Id:N}.{ExtensionForContentType(asset.ContentType)}" : asset.FileName,
            asset.Kind,
            asset.ContentType,
            DataUrl.ToDataUrl(asset.ContentType, asset.ThumbnailData is { Length: > 0 } thumb ? thumb : asset.Data),
            asset.Width,
            asset.Height,
            asset.ParentAssetId,
            asset.SourceBatchId,
            asset.SourcePromptRecipeId,
            asset.IsFavorite,
            asset.IsRejected,
            asset.IsReference,
            asset.Notes,
            asset.Prompt,
            asset.CreatedAt);

    private static GenerationBatchView BatchView(GenerationBatch batch, IReadOnlyList<ArtAsset> assets) =>
        new(
            batch.Id,
            batch.Label,
            batch.Provider,
            batch.MainlineModel,
            batch.ImageModel,
            batch.Prompt,
            batch.NegativePrompt,
            batch.Size,
            batch.Count,
            DeserializeIds(batch.InputAssetIdsJson),
            DeserializeIds(batch.InputMaskIdsJson),
            assets.Where(a => a.SourceBatchId == batch.Id).OrderBy(a => a.CreatedAt).Select(a => a.Id).ToList(),
            batch.ParentBatchId,
            batch.PromptRecipeId,
            batch.Status,
            batch.Error,
            batch.CreatedAt);

    private static PromptRecipeView RecipeView(PromptRecipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.AssetType,
            recipe.PromptTemplate,
            DeserializeStrings(recipe.StyleRulesJson),
            DeserializeStrings(recipe.AvoidRulesJson),
            DeserializeIds(recipe.ExampleAssetIdsJson),
            recipe.PreferredProvider,
            recipe.PreferredModel,
            recipe.PreferredSize,
            recipe.Notes,
            recipe.CreatedAt);

    private static ImageMaskView MaskView(ImageMask mask) =>
        new(
            mask.Id,
            mask.AssetId,
            mask.Label,
            mask.ContentType,
            DataUrl.ToDataUrl(mask.ContentType, mask.Data),
            mask.Width,
            mask.Height,
            mask.CreatedAt);

    private static ChatContextAttachmentView AttachmentView(ChatContextAttachment attachment) =>
        new(attachment.Id, attachment.Type, attachment.RefId, attachment.Label, attachment.SortOrder);

    private int ClampCount(int count) =>
        Math.Clamp(count <= 0 ? 1 : count, 1, Math.Max(1, imageOptions.Value.MaxOutputs));

    private static string NormalizeSize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim();

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string CleanRequired(string value, string error)
    {
        var trimmed = Clean(value);
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException(error);
        return trimmed;
    }

    private static string? NormalizeImageContentType(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "image/jpg" => "image/jpeg",
            _ => null,
        };

    private static string ExtensionForContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";

    private static string LabelForIndex(int index)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return index >= 0 && index < alphabet.Length
            ? alphabet[index].ToString()
            : (index + 1).ToString();
    }

    private static string SerializeIds(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.ToList(), JsonOptions);

    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList(), JsonOptions);

    private static List<Guid> DeserializeIds(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> DeserializeStrings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
