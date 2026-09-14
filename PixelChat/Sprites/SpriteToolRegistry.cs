using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Models;

namespace PixelChat.Sprites;

public sealed class SpriteToolRegistry(ISpriteDocumentService documents, SpriteScriptService scripts, SpriteInspectionService inspections,
    IArtWorkflowService workflow, IFrameSetService frameSets)
{
    public async Task<IReadOnlyList<AIContent>> ImageContentsAsync(Guid projectId, string result, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array) return [];
        var images = new List<AIContent>();
        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.TryGetProperty("sendImage", out var send) && !send.GetBoolean()) continue;
            var id = artifact.GetProperty("id").GetGuid();
            var image = await inspections.ReadArtifactAsync(projectId, id, cancellationToken);
            images.Add(new TextContent(image.Label));
            images.Add(new DataContent(DataUrl.ToDataUrl("image/png", image.Data), "image/png") { Name = $"sprite-inspection-{id}.png" });
        }
        return images;
    }

    public AITool[] Build(Guid projectId) =>
    [
        AIFunctionFactory.Create((Guid documentId, long? revision = null, int offset = 0, int limit = 24, Guid? frameId = null, Guid? layerId = null, SpriteRect? pixelRect = null, CancellationToken cancellationToken = default) =>
            ReadAsync(projectId, documentId, revision, offset, limit, frameId, layerId, pixelRect, cancellationToken),
            "sprite_read", "Read native sprite revision, production rules, layers, clips, selection, and a page of frame metadata. Optional pixelRect (at most 32x32) reads exact RGBA pixels for frameId; omit layerId to read the visible composite. No mutation."),
        AIFunctionFactory.Create((string topic = "overview") => Help(topic), "sprite_help", "Load one compact native sprite reference: overview, commands, scripting, drawing, poses, animation, or export. Load the relevant reference before using an unfamiliar command."),
        AIFunctionFactory.Create((string name, int width = 64, int height = 64, string artMode = "pixel", Guid[]? assetIds = null, Guid? sourceAssetId = null, Guid[]? regionIds = null, CancellationToken cancellationToken = default) =>
            CreateAsync(projectId, name, width, height, artMode, assetIds, sourceAssetId, regionIds, cancellationToken),
            "sprite_create", "Create a blank native sprite, import assets as frames, or import selected source regions. Imports preserve pixels and use painted mode; explicit conversion is required for strict pixel art. Returns stable document/layer/frame IDs."),
        AIFunctionFactory.Create((Guid documentId, long expectedRevision, string label, JsonElement[] operations, string? taskId = null, bool returnPreview = true, CancellationToken cancellationToken = default) =>
            ApplyAsync(projectId, new(documentId, expectedRevision, label, operations, "agent", taskId), returnPreview, cancellationToken),
            "sprite_apply", "Atomically apply a typed command batch to the expected document revision. Conflicts change nothing. Use sprite_help(commands) for operations. Prefer one coherent batch over one call per pixel. Optional preview returns actual PNG evidence."),
        AIFunctionFactory.Create((Guid documentId, long expectedRevision, string script, string label = "Sprite script", string? taskId = null, bool returnPreview = true, CancellationToken cancellationToken = default) =>
            ScriptAsync(projectId, documentId, expectedRevision, script, label, taskId, returnPreview, cancellationToken),
            "sprite_script", "Run resource-bounded JavaScript in an isolated worker. document is the starting snapshot; sprite.apply(op) and sprite.batch(ops) emit the same typed operations as sprite_apply. No filesystem/network/CLR/modules. Success commits one undoable batch; errors and cancellation commit nothing."),
        AIFunctionFactory.Create((Guid documentId, long revision, string kind = "frame", Guid[]? frameIds = null, SpriteRect? crop = null, int scale = 1, int page = 0, int pageSize = 12, long? compareRevision = null, Guid[]? knownArtifacts = null, CancellationToken cancellationToken = default) =>
            RenderAsync(projectId, new(documentId, revision, kind, frameIds, crop, scale, page, pageSize, compareRevision), knownArtifacts, cancellationToken),
            "sprite_render", "Inspect a specific revision as actual labeled PNGs: frame, contact, onion, or difference (requires compareRevision). Integer scale 1-16. Paginate contact sheets; frame IDs and timing accompany the pixels. knownArtifacts suppresses resending identical images."),
        AIFunctionFactory.Create((Guid documentId, string action = "list", long expectedRevision = 0, bool wholeTask = false, int offset = 0, CancellationToken cancellationToken = default) =>
            HistoryAsync(projectId, documentId, action, expectedRevision, wholeTask, offset, cancellationToken),
            "sprite_history", "Read persisted command history (independent of chat) or perform revision-checked undo/redo. wholeTask undoes only a contiguous task suffix and stops at intervening manual work."),
    ];

    public static string Help(string topic)
    {
        var topics = new[] { "overview", "commands", "scripting", "drawing", "poses", "animation", "export" };
        if (!topics.Contains(topic)) throw new InvalidOperationException($"Unknown sprite reference. Choose {string.Join(", ", topics)}.");
        using var stream = typeof(SpriteToolRegistry).Assembly.GetManifestResourceStream($"PixelChat.Sprites.Skills.{topic}.md") ?? throw new InvalidOperationException("Sprite reference missing.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    private async Task<string> ReadAsync(Guid projectId, Guid id, long? revision, int offset, int limit, Guid? frameId, Guid? layerId, SpriteRect? rect, CancellationToken token)
    {
        var snapshot = await documents.ReadAsync(projectId, id, revision, token); var doc = snapshot.Document;
        List<List<string>>? pixels = null;
        if (rect is not null)
        {
            if (rect.Width is < 1 or > 32 || rect.Height is < 1 or > 32) throw new InvalidOperationException("Pixel reads are limited to 32x32 regions.");
            var frame = doc.Frames.SingleOrDefault(f => f.Id == frameId) ?? throw new InvalidOperationException("Pixel reads require a frameId.");
            if (layerId is { } requestedLayer && !doc.Layers.Any(l => l.Id == requestedLayer)) throw new InvalidOperationException("Layer not found.");
            var bitmaps = await documents.LoadBitmapsAsync(doc, token);
            var raster = layerId is { } layer
                ? frame.Cels.TryGetValue(layer, out var hash) ? SpriteRaster.Decode(bitmaps[hash].Data) : SpriteRaster.Blank(frame.Width, frame.Height)
                : SpriteRaster.Composite(doc, frame, h => bitmaps[h]);
            pixels = [];
            for (var y = 0; y < rect.Height; y++) { var row = new List<string>(); for (var x = 0; x < rect.Width; x++) row.Add("#" + raster.Get(rect.X + x, rect.Y + y).ToHex()); pixels.Add(row); }
        }
        return Json(new { documentId = id, snapshot.Revision, doc.Name, doc.FormatVersion, doc.Specification, doc.Layers, doc.Clips, doc.Slices, doc.Selection,
            frameCount = doc.Frames.Count, offset = Math.Max(0, offset), frames = doc.Frames.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 64)), pixelRect = rect, pixels });
    }

    private async Task<string> CreateAsync(Guid projectId, string name, int width, int height, string mode, Guid[]? assetIds, Guid? sourceId, Guid[]? regionIds, CancellationToken token)
    {
        SpriteSnapshot snapshot;
        if (sourceId is { } source && regionIds is { Length: > 0 })
        {
            var set = await frameSets.CreateFrameSetFromRegionsAsync(projectId, new(source, regionIds, name), token);
            snapshot = await documents.ReadAsync(projectId, set.Id, cancellationToken: token);
        }
        else if (assetIds is { Length: > 0 })
        {
            var doc = new SpriteDocument { Name = name, Layers = [new() { Name = "Artwork" }] }; var bitmaps = new List<SpriteBitmap>();
            foreach (var id in assetIds)
            {
                var image = await workflow.GetAssetFullImageAsync(projectId, id, token) ?? throw new InvalidOperationException("Import asset not found.");
                var raster = SpriteRaster.Decode(image.Data); var bitmap = raster.Encode(); bitmaps.Add(bitmap);
                var frame = new SpriteFrame { Name = $"Frame {doc.Frames.Count + 1}", Width = raster.Width, Height = raster.Height, Cels = new() { [doc.Layers[0].Id] = bitmap.Hash } };
                doc.Frames.Add(frame); doc.Provenance[$"frame:{frame.Id}:sourceAssetId"] = id.ToString();
            }
            doc.Specification.Width = doc.Frames.Max(f => f.Width); doc.Specification.Height = doc.Frames.Max(f => f.Height);
            snapshot = await documents.ImportAsync(projectId, assetIds.Length == 1 ? assetIds[0] : null, doc, bitmaps, token);
        }
        else snapshot = await documents.CreateAsync(projectId, name, width, height, mode, token);
        await frameSets.SetActiveFrameSetAsync(projectId, snapshot.DocumentId, token);
        return Json(snapshot);
    }

    private async Task<string> ApplyAsync(Guid projectId, SpriteBatch batch, bool preview, CancellationToken token)
    {
        var commit = await documents.ApplyAsync(projectId, batch, token);
        return await CommitResultAsync(projectId, commit, preview, token);
    }
    private async Task<string> ScriptAsync(Guid projectId, Guid id, long expected, string script, string label, string? task, bool preview, CancellationToken token)
    {
        var commit = await scripts.RunAsync(projectId, id, expected, script, label, task, token);
        return await CommitResultAsync(projectId, commit, preview, token);
    }
    private async Task<string> CommitResultAsync(Guid projectId, SpriteCommit commit, bool preview, CancellationToken token)
    {
        SpriteRenderResult? render = null;
        string? previewWarning = null;
        if (preview)
        {
            try { render = await inspections.RenderAsync(projectId, new(commit.DocumentId, commit.Revision, "contact"), token); }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { previewWarning = $"Edit saved. Preview unavailable: {ex.Message}"; }
        }
        return Json(new { commit.DocumentId, commit.Revision, commit.HistoryId, commit.OperationCount, artifacts = render?.Artifacts, nextPreviewPage = render?.NextPage, previewWarning });
    }
    private async Task<string> RenderAsync(Guid projectId, SpriteRenderRequest request, Guid[]? known, CancellationToken token)
    {
        var render = await inspections.RenderAsync(projectId, request, token);
        return Json(new { render.DocumentId, render.Revision, render.TotalFrames, render.NextPage,
            artifacts = render.Artifacts.Select(a => new { a.Id, a.Label, a.Url, a.Width, a.Height, a.Revision, sendImage = known?.Contains(a.Id) != true }) });
    }
    private async Task<string> HistoryAsync(Guid projectId, Guid id, string action, long expected, bool task, int offset, CancellationToken token)
    {
        if (action == "list") return Json((await documents.ListHistoryAsync(projectId, id, offset, token)).Select(r => new { r.Id, r.Number, r.Label, r.Source, r.TaskId, r.OperationsJson, r.Script, r.UndoTarget, r.CreatedAt }));
        return await CommitResultAsync(projectId, await documents.HistoryAsync(projectId, id, expected, action, task, token), true, token);
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, SpriteDocument.JsonOptions);
}
