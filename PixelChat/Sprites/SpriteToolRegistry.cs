using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Chat;
using PixelChat.Models;

namespace PixelChat.Sprites;

public sealed class SpriteToolRegistry(ISpriteDocumentService documents, SpriteScriptService scripts, SpriteInspectionService inspections,
    IFrameSetService frameSets, SpriteValidationService? validation = null, SpriteGenerationService? generation = null, SpriteExportService? exports = null)
{
    public async Task<IReadOnlyList<AIContent>> ImageContentsAsync(Guid projectId, string result, CancellationToken cancellationToken)
    {
        // Workflow help is Markdown and has no image artifacts to extract.
        if (!result.AsSpan().TrimStart().StartsWith("{")) return [];
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array) return [];
        var background = json.RootElement.TryGetProperty("backgroundColor", out var bg) ? bg.GetString() : null;
        var images = new List<AIContent>();
        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.TryGetProperty("sendImage", out var send) && !send.GetBoolean()) continue;
            var id = artifact.GetProperty("id").GetGuid();
            var image = await inspections.ReadArtifactAsync(projectId, id, cancellationToken);
            var view = ModelImageInspection.Create(image.Data, $"sprite-inspection-{id}.png", background);
            images.Add(new TextContent(image.Label + "\n" + ((TextContent)view[0]).Text));
            images.Add(view[1]);
        }
        return images;
    }

    public AITool[] Build(Guid projectId, PixelChat.Chat.AssistantTurnGenerationBudget? budget = null) =>
    [
        AIFunctionFactory.Create((Guid documentId, long? revision = null, int offset = 0, int limit = 24, Guid? frameId = null, Guid? layerId = null, SpriteRect? pixelRect = null, CancellationToken cancellationToken = default) =>
            ReadAsync(projectId, documentId, revision, offset, limit, frameId, layerId, pixelRect, cancellationToken),
            "sprite_read", "Read native sprite revision, production rules, layers, clips, selection, and a page of frame metadata. Optional pixelRect (at most 32x32) reads exact RGBA pixels for frameId; omit layerId to read the visible composite. No mutation."),
        AIFunctionFactory.Create((string topic = "overview") => Help(topic), "sprite_help", "Load one compact native sprite reference: overview, commands, scripting, drawing, poses, animation, or export. Load the relevant reference before using an unfamiliar command."),
        AIFunctionFactory.Create(async (Guid documentId, long revision, long? compareRevision = null, SpriteFinding[]? judgments = null, CancellationToken cancellationToken = default) =>
            Json(await validation!.ValidateAsync(projectId, documentId, revision, compareRevision, judgments, cancellationToken)),
            "sprite_validate", "Measure EVERY frame, clips/timing/order, dimensions, alpha, palette, clipping, duplicate frames, pivots, selections, silhouette changes and loop seams. Facts and heuristic warnings are separate; motion warnings never modify pixels. Optional frame-addressed artistic judgments are stored explicitly as judgments."),
        AIFunctionFactory.Create(async (SpriteGenerationRequest request, bool prepareOnly = false, CancellationToken cancellationToken = default) =>
        {
            if (prepareOnly) return Json(await generation!.PrepareAsync(projectId, request, cancellationToken));
            if (budget?.IsExhausted == true) throw new InvalidOperationException("Generation round budget exhausted.");
            budget?.Consume();
            return Json(await generation!.StartAsync(projectId, request, cancellationToken));
        }, "sprite_generate", "Start a native reference/pose/edit candidate job against a captured document revision and target layer. Explicit reference roles, model choice, recipes, and masks are supported. prepareOnly returns source/mask PNGs without generation; padded edits require inspecting that preparation first. Candidates never automatically replace pixels; inspect and apply with sprite_job."),
        AIFunctionFactory.Create(async (Guid jobId, string action = "read", Guid? candidateId = null, int waitSeconds = 20, CancellationToken cancellationToken = default) =>
        {
            if (action is "retry" or "resume") { if (budget?.IsExhausted == true) throw new InvalidOperationException("Generation round budget exhausted."); budget?.Consume(); }
            return Json(await generation!.OperateAsync(projectId, jobId, action, candidateId, waitSeconds, token: cancellationToken));
        },
            "sprite_job", "Read/wait/cancel/resume/retry a persisted sprite job, inspect a candidate with before/after/difference PNGs, or apply a candidate atomically against the job's captured revision. Stale results remain available but cannot overwrite manual work. Strict pixel conversion is explicit; provider masks remain advisory."),
        AIFunctionFactory.Create(async (string name, int width = 64, int height = 64, string artMode = "pixel", Guid[]? assetIds = null, Guid? sourceAssetId = null, Guid[]? regionIds = null, Guid? bundleExportId = null, CancellationToken cancellationToken = default) =>
        {
            if (bundleExportId is { } exportId)
            {
                var imported = await exports!.ImportAsync(projectId, (await exports.ReadAsync(projectId, exportId, cancellationToken)).Data, name, cancellationToken);
                await frameSets.SetActiveFrameSetAsync(projectId, imported.Snapshot.DocumentId, cancellationToken);
                return Json(imported);
            }
            return await CreateAsync(projectId, name, width, height, artMode, assetIds, sourceAssetId, regionIds, cancellationToken);
        },
            "sprite_create", "Create a blank native sprite, import assets/regions, or reimport a native/atlas/frames bundle by bundleExportId. Image imports preserve pixels in painted mode; native bundles retain art mode and editable layers. Returns stable IDs and import mappings."),
        AIFunctionFactory.Create(async (Guid documentId, long revision, SpriteExportSpec specification, CancellationToken cancellationToken = default) =>
        {
            var artifact = await exports!.ExportAsync(projectId, documentId, revision, specification, cancellationToken);
            return Json(new { artifact.Id, artifact.DocumentId, artifact.Revision, artifact.FileName, artifact.ContentType, artifact.Bytes, artifact.Url, artifact.Warnings });
        },
            "sprite_export", "Export a specified revision as a portable native bundle, PNG atlas+JSON zip, PNG frames+JSON zip, versioned metadata, or animated GIF preview. Padding is inside slots, gutter between slots, outerMargin around atlas. Exact timing/pivots/slices/alpha remain in native/PNG/JSON; GIF is a quantized preview. Artifacts are persisted and downloadable."),
        new SpriteApplyFunction(AIFunctionFactory.Create((Guid documentId, long expectedRevision, string label, JsonElement[] operations, string? taskId = null, bool returnPreview = true, CancellationToken cancellationToken = default) =>
            ApplyAsync(projectId, new(documentId, expectedRevision, label, operations, "agent", taskId), returnPreview, cancellationToken),
            "sprite_apply", "Atomically apply a typed command batch to the expected document revision. Conflicts change nothing. Use sprite_help(commands) for operations. Prefer one coherent batch over one call per pixel. Optional preview returns actual PNG evidence.")),
        AIFunctionFactory.Create((Guid documentId, long expectedRevision, string script, string label = "Sprite script", string? taskId = null, bool returnPreview = true, CancellationToken cancellationToken = default) =>
            ScriptAsync(projectId, documentId, expectedRevision, script, label, taskId, returnPreview, cancellationToken),
            "sprite_script", "Run resource-bounded JavaScript in an isolated worker. document is the starting snapshot; sprite.apply(op) and sprite.batch(ops) emit the same typed operations as sprite_apply. No filesystem/network/CLR/modules. Success commits one undoable batch; errors and cancellation commit nothing."),
        AIFunctionFactory.Create((Guid documentId, long revision, string kind = "frame", Guid[]? frameIds = null, SpriteRect? crop = null, int scale = 1, int page = 0, int pageSize = 12, long? compareRevision = null, Guid[]? knownArtifacts = null, string? backgroundColor = null, CancellationToken cancellationToken = default) =>
            RenderAsync(projectId, new(documentId, revision, kind, frameIds, crop, scale, page, pageSize, compareRevision), knownArtifacts, backgroundColor, cancellationToken),
            "sprite_render", "Inspect a specific revision as actual labeled PNGs: frame, contact, onion, difference (requires compareRevision), or playback (GIF plus PNG contact sheet). Integer scale 1-16. Paginate contact sheets; frame IDs and timing accompany the pixels. Measured source alpha accompanies each image. backgroundColor accepts opaque #RRGGBB for a composited inspection (default #808080). Choose a color distinct from the artwork before diagnosing haze. knownArtifacts suppresses unchanged default views; specifying a background always resends the cached artifact on that color. Source pixels are unchanged."),
        AIFunctionFactory.Create((Guid documentId, string action = "list", long expectedRevision = 0, bool wholeTask = false, int offset = 0, CancellationToken cancellationToken = default) =>
            HistoryAsync(projectId, documentId, action, expectedRevision, wholeTask, offset, cancellationToken),
            "sprite_history", "Read persisted command history (independent of chat) or perform revision-checked undo/redo. wholeTask undoes only a contiguous task suffix and stops at intervening manual work."),
    ];

    // JsonElement alone emits an unconstrained schema. Operations are objects with an
    // op discriminator and command-specific fields, validated by the command engine.
    private sealed class SpriteApplyFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        private readonly Lazy<JsonElement> _schema = new(() =>
        {
            var schema = JsonNode.Parse(inner.JsonSchema.GetRawText())!;
            schema["properties"]!["operations"]!["items"] = JsonNode.Parse("""
                {"type":"object","properties":{"op":{"type":"string","description":"Command name from sprite_help(commands)."}},"required":["op"],"additionalProperties":true}
                """);
            return JsonSerializer.SerializeToElement(schema);
        });
        public override JsonElement JsonSchema => _schema.Value;
    }

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
        return Json(new { documentId = id, snapshot.Revision, doc.Name, doc.FormatVersion, doc.Specification, doc.Layers, doc.Clips, doc.Slices,
            selection = doc.Selection is { } selection ? new { selection.FrameId, selection.Polygon, selection.Color, selection.Width, hasPixelMask = selection.PixelMask is not null } : null,
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
            snapshot = await documents.ImportAssetsAsync(projectId, name, assetIds, token);
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
    private async Task<string> RenderAsync(Guid projectId, SpriteRenderRequest request, Guid[]? known, string? backgroundColor, CancellationToken token)
    {
        var background = ModelImageInspection.Background(backgroundColor);
        if (request.Kind == "playback")
        {
            var preview = await exports!.ExportAsync(projectId, request.DocumentId, request.Revision, new("preview"), token);
            var contact = await inspections.RenderAsync(projectId, request with { Kind = "contact" }, token);
            return Json(new { backgroundColor = background, preview, artifacts = contact.Artifacts, contact.NextPage, contact.TotalFrames });
        }
        var render = await inspections.RenderAsync(projectId, request, token);
        return Json(new { backgroundColor = background, render.DocumentId, render.Revision, render.TotalFrames, render.NextPage,
            artifacts = render.Artifacts.Select(a => new { a.Id, a.Label, a.Url, a.Width, a.Height, a.Revision, sendImage = backgroundColor is not null || known?.Contains(a.Id) != true }) });
    }
    private async Task<string> HistoryAsync(Guid projectId, Guid id, string action, long expected, bool task, int offset, CancellationToken token)
    {
        if (action == "list") return Json((await documents.ListHistoryAsync(projectId, id, offset, token)).Select(r => new { r.Id, r.Number, r.Label, r.Source, r.TaskId, r.OperationsJson, r.Script, r.UndoTarget, r.CreatedAt }));
        return await CommitResultAsync(projectId, await documents.HistoryAsync(projectId, id, expected, action, task, token), true, token);
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, SpriteDocument.JsonOptions);
}
