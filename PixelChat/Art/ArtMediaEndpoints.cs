using Microsoft.AspNetCore.Hosting;

namespace PixelChat.Art;

public static class ArtMediaEndpoints
{
    public static IEndpointRouteBuilder MapPixelChatArtMedia(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/motion-clips/{clipId}/asset", IResult (
            string clipId,
            IWebHostEnvironment environment) =>
        {
            var catalog = MotionClipCatalog.Load(environment.ContentRootPath);
            var clip = catalog.Find(clipId);
            if (clip is null)
                return Results.NotFound();

            var assetPath = catalog.ResolveAssetPath(clip);
            if (!File.Exists(assetPath))
                return Results.NotFound();

            return Results.File(
                File.OpenRead(assetPath),
                "model/gltf-binary",
                fileDownloadName: Path.GetFileName(assetPath),
                enableRangeProcessing: false);
        });

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

        group.MapGet("/chat-visuals/{visualId:guid}/preview", async (
            Guid projectId,
            Guid visualId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetChatVisualImageAsync(projectId, visualId, preview: true, cancellationToken);
            return ImageResult(image);
        });

        group.MapGet("/chat-visuals/{visualId:guid}/full", async (
            Guid projectId,
            Guid visualId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var image = await workflow.GetChatVisualImageAsync(projectId, visualId, preview: false, cancellationToken);
            return ImageResult(image);
        });

        group.MapGet("/frame-set-frames/{frameId:guid}/preview", async (
            Guid projectId,
            Guid frameId,
            IFrameSetService frameSets,
            CancellationToken cancellationToken) =>
        {
            var image = await frameSets.GetFramePreviewImageAsync(projectId, frameId, cancellationToken);
            return image is null
                ? Results.NotFound()
                : Results.File(image.Value.Data, image.Value.ContentType, enableRangeProcessing: false);
        });

        group.MapGet("/frame-set-frames/{frameId:guid}/mask", async (
            Guid projectId,
            Guid frameId,
            IFrameSetService frameSets,
            CancellationToken cancellationToken) =>
        {
            var image = await frameSets.GetFrameMaskImageAsync(projectId, frameId, cancellationToken);
            return image is null
                ? Results.NotFound()
                : Results.File(image.Value.Data, image.Value.ContentType, enableRangeProcessing: false);
        });

        group.MapGet("/frame-set-frames/{frameId:guid}/content", async (
            Guid projectId,
            Guid frameId,
            IFrameSetService frameSets,
            CancellationToken cancellationToken) =>
        {
            var image = await frameSets.GetFrameContentImageAsync(projectId, frameId, cancellationToken);
            return image is null
                ? Results.NotFound()
                : Results.File(image.Value.Data, image.Value.ContentType, enableRangeProcessing: false);
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
