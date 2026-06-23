namespace PixelChat.Art;

public interface IAssetAnimationWorkflowService
{
    Task<AssetProfileView> CreateAssetProfileAsync(Guid projectId, CreateAssetProfileRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> PlanAssetAnimationAsync(Guid projectId, PlanAssetAnimationRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> RenderAnimationGuideAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> RunAnimationCandidatesAsync(Guid projectId, RunAnimationCandidatesRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> MarkAnimationFramesAsync(Guid projectId, MarkAnimationFramesRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> RegenerateAnimationFramesAsync(Guid projectId, RegenerateAnimationFramesRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> ExtractAnimationFixedSlotsAsync(Guid projectId, ExtractAnimationFixedSlotsRequest request, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> ReviewAnimationJobAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken = default);
    Task<AssetAnimationJobView> PackageAnimationJobAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken = default);
    Task<string> ReadAnimationJobJsonAsync(Guid projectId, Guid assetAnimationJobId, CancellationToken cancellationToken = default);
}
