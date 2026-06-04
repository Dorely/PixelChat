using PixelChat.Models;

namespace PixelChat.Art;

public interface IArtWorkflowService
{
    Task<ProjectView> EnsureDefaultProjectAsync(CancellationToken cancellationToken = default);
    Task<ProjectView> CreateProjectAsync(string name, CancellationToken cancellationToken = default);
    Task<WorkbenchView> GetWorkbenchAsync(Guid? projectId = null, CancellationToken cancellationToken = default);
    Task SetWorkspaceModeAsync(Guid projectId, WorkspaceMode mode, CancellationToken cancellationToken = default);
    Task SelectAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default);
    Task SelectBatchAsync(Guid projectId, Guid batchId, CancellationToken cancellationToken = default);
    Task<GenerationBatchView> GenerateImagesAsync(Guid projectId, GenerateImagesRequest request, CancellationToken cancellationToken = default);
    Task<GenerationBatchView> EditImageAsync(Guid projectId, EditImageRequest request, CancellationToken cancellationToken = default);
    Task<ArtAssetView> ImportAssetAsync(Guid projectId, ImportAssetRequest request, CancellationToken cancellationToken = default);
    Task<ArtAssetView> CreateCropAssetAsync(Guid projectId, CropAssetRequest request, CancellationToken cancellationToken = default);
    Task<ImageMaskView> SaveMaskAsync(Guid projectId, SaveMaskRequest request, CancellationToken cancellationToken = default);
    Task<PromptRecipeView> SavePromptRecipeAsync(Guid projectId, SavePromptRecipeRequest request, CancellationToken cancellationToken = default);
    Task MarkAssetAsync(Guid projectId, Guid assetId, bool? favorite, bool? rejected, string? notes, CancellationToken cancellationToken = default);
    Task UseAssetAsReferenceAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default);
    Task DeleteAssetAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken = default);
    Task<ChatContextAttachmentView> AttachContextAsync(Guid projectId, ChatContextAttachmentType type, Guid refId, string? label = null, CancellationToken cancellationToken = default);
    Task RemoveContextAsync(Guid projectId, Guid attachmentId, CancellationToken cancellationToken = default);
    Task ClearContextAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<string> GetWorkspaceStateJsonAsync(Guid projectId, CancellationToken cancellationToken = default);
}
