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
            return await ImageResultOrNotFoundAsync(
                () => workflow.GetAssetPreviewImageAsync(projectId, assetId, cancellationToken));
        });

        group.MapGet("/assets/{assetId:guid}/full", async (
            Guid projectId,
            Guid assetId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            return await ImageResultOrNotFoundAsync(
                () => workflow.GetAssetFullImageAsync(projectId, assetId, cancellationToken));
        });

        group.MapGet("/masks/{maskId:guid}", async (
            Guid projectId,
            Guid maskId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            return await ImageResultOrNotFoundAsync(
                () => workflow.GetMaskImageAsync(projectId, maskId, cancellationToken));
        });

        group.MapGet("/chat-visuals/{visualId:guid}/preview", async (
            Guid projectId,
            Guid visualId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            return await ImageResultOrNotFoundAsync(
                () => workflow.GetChatVisualImageAsync(projectId, visualId, preview: true, cancellationToken));
        });

        group.MapGet("/chat-visuals/{visualId:guid}/full", async (
            Guid projectId,
            Guid visualId,
            IArtWorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            return await ImageResultOrNotFoundAsync(
                () => workflow.GetChatVisualImageAsync(projectId, visualId, preview: false, cancellationToken));
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

    private static async Task<IResult> ImageResultOrNotFoundAsync(Func<Task<ImageBinaryView>> loadImage)
    {
        try
        {
            return ImageResult(await loadImage());
        }
        catch (InvalidOperationException ex) when (IsMissingMedia(ex))
        {
            return Results.NotFound();
        }
    }

    private static bool IsMissingMedia(InvalidOperationException exception) =>
        exception.Message is "Asset was not found."
            or "Mask was not found."
            or "Chat visual was not found."
            or "Chat visual image data was not found."
            or "Chat visual source was not found.";

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
