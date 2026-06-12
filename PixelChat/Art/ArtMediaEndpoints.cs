namespace PixelChat.Art;

public static class ArtMediaEndpoints
{
    public static IEndpointRouteBuilder MapPixelChatArtMedia(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/media/projects/{projectId:guid}");

        group.MapGet("/assets/{assetId:guid}/preview", async (
            Guid projectId,
            Guid assetId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetAssetPreviewImageAsync(projectId, assetId, cancellationToken);
            return ImageResult(image);
        });

        group.MapGet("/assets/{assetId:guid}/full", async (
            Guid projectId,
            Guid assetId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetAssetFullImageAsync(projectId, assetId, cancellationToken);
            return ImageResult(image);
        });

        group.MapGet("/masks/{maskId:guid}", async (
            Guid projectId,
            Guid maskId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetMaskImageAsync(projectId, maskId, cancellationToken);
            return ImageResult(image);
        });

        group.MapGet("/sprite-frames/{frameId:guid}/preview", async (
            Guid projectId,
            Guid frameId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetSpriteFramePreviewImageAsync(projectId, frameId, cancellationToken);
            return ImageResult(image);
        });

        return app;
    }

    private static IResult ImageResult(ImageBinaryView image)
    {
        var lastModified = image.LastModified.Kind == DateTimeKind.Utc
            ? image.LastModified
            : DateTime.SpecifyKind(image.LastModified, DateTimeKind.Utc);
        return Results.File(
            image.Data,
            image.ContentType,
            lastModified: lastModified,
            enableRangeProcessing: false);
    }
}
