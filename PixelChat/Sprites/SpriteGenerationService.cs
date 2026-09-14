using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Art;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Sprites;

public sealed record SpriteReference(Guid AssetId, string Role, string Preserve);
public sealed record SpriteGenerationRequest(Guid DocumentId, long ExpectedRevision, Guid FrameId, Guid LayerId, string Prompt,
    string Kind = "edit", int Count = 1, IReadOnlyList<SpriteReference>? References = null, Guid? CanvasPreparationId = null,
    EditCanvasOptions? CanvasOptions = null, string? MaskPngDataUrl = null, string? ImageModel = null, string? Quality = null,
    string Background = "auto", Guid? RecipeId = null, Guid? AnimationRecipeId = null, string? TaskId = null);
public sealed record SpriteGenerationTarget(Guid DocumentId, long Revision, Guid FrameId, Guid LayerId, string Kind,
    IReadOnlyList<SpriteReference> References, string? TaskId, string SourceHash, int PaddingLeft, int PaddingTop,
    IReadOnlyDictionary<Guid, string>? ReferenceMetadata = null);
public sealed record SpriteCandidate(Guid AssetId, string Label, string Url, int Width, int Height, bool HasRawProviderData);
public sealed record SpriteJobView(Guid JobId, SpriteGenerationTarget Target, string Status, bool Running, long CurrentRevision,
    bool Stale, IReadOnlyList<GenerationOutputStateView> Outputs, IReadOnlyList<SpriteCandidate> Candidates);

public sealed class SpriteGenerationService(AppDbContext db, ISpriteDocumentService documents, IImageGenerationRuntime runtime,
    IImageEditCanvasService canvas, IEditCanvasPreparationStore preparations, IImageProvider provider, SpriteInspectionService inspections)
{
    public async Task<object> PrepareAsync(Guid projectId, SpriteGenerationRequest request, CancellationToken token = default)
    {
        var snapshot = await documents.ReadAsync(projectId, request.DocumentId, cancellationToken: token);
        if (snapshot.Revision != request.ExpectedRevision) throw new SpriteConflictException(request.ExpectedRevision, snapshot.Revision);
        var frame = snapshot.Document.Frames.SingleOrDefault(f => f.Id == request.FrameId) ?? throw new InvalidOperationException("Target frame not found.");
        var layer = snapshot.Document.Layers.SingleOrDefault(l => l.Id == request.LayerId) ?? throw new InvalidOperationException("Target layer not found.");
        if (layer.Locked) throw new InvalidOperationException("Target layer is locked.");
        var bits = await documents.LoadBitmapsAsync(snapshot.Document, token);
        var source = frame.Cels.TryGetValue(layer.Id, out var hash) ? bits[hash].Data : SpriteRaster.Blank(frame.Width, frame.Height).Encode().Data;
        byte[]? mask = null;
        if (request.MaskPngDataUrl is not null)
        {
            mask = DataUrl.Parse(request.MaskPngDataUrl).Data; var decoded = SpriteRaster.Decode(mask);
            if (decoded.Width != frame.Width || decoded.Height != frame.Height) throw new InvalidOperationException("Mask must match target logical dimensions.");
        }
        else if (snapshot.Document.Selection is { } selection && selection.FrameId == frame.Id)
        {
            var raster = SpriteRaster.Blank(frame.Width, frame.Height);
            for (var y = 0; y < frame.Height; y++) for (var x = 0; x < frame.Width; x++)
            {
                var i = y * selection.Width + x;
                var selected = selection.PixelMask is { } pixels ? i / 8 < pixels.Length && (pixels[i / 8] & (1 << (i % 8))) != 0 : SpriteRaster.InsidePolygon(x, y, selection.Polygon);
                raster.Put(x, y, selected ? default : new(255,255,255,255));
            }
            mask = raster.Encode().Data;
        }
        var options = request.CanvasOptions ?? new EditCanvasOptions(ResampleMode: snapshot.Document.Specification.ArtMode == "pixel" ? EditCanvasResampleMode.NearestNeighbor : EditCanvasResampleMode.Smooth);
        var background = request.Background == "auto" ? (SpriteRaster.Decode(source).Pixels.Where((_, i) => i % 4 == 3).Any(a => a < 255) ? "transparent" : "opaque") : request.Background;
        var prepared = canvas.Prepare(source, mask, background, options, provider.DescribeCapabilities());
        var stored = preparations.Add(projectId, TargetKind(layer.Id), frame.Id, snapshot.DocumentId, snapshot.Revision, background, options, null, mask, prepared);
        var images = new[]
        {
            await inspections.StoreAsync(projectId, snapshot.DocumentId, snapshot.Revision, $"Preparation {stored.Id}: target {frame.Id}, layer {layer.Id}, logical source", prepared.LogicalSourcePng, token),
            await inspections.StoreAsync(projectId, snapshot.DocumentId, snapshot.Revision, $"Preparation {stored.Id}: editable mask overlay; provider mask is advisory", prepared.PreviewPng, token)
        };
        return new { canvasPreparationId = stored.Id, snapshot.DocumentId, snapshot.Revision, frameId = frame.Id, layerId = layer.Id, prepared.Transform, artifacts = images };
    }

    public async Task<SpriteJobView> StartAsync(Guid projectId, SpriteGenerationRequest request, CancellationToken token = default)
    {
        if (request.Kind is not ("reference" or "pose" or "edit")) throw new InvalidOperationException("Kind must be reference, pose, or edit.");
        if (request.Kind != "reference" && request.CanvasPreparationId is null)
        {
            if (request.CanvasOptions?.HasPadding == true) throw new InvalidOperationException("Preview padded preparation first using prepareOnly, then start with its canvasPreparationId.");
            var result = JsonSerializer.SerializeToElement(await PrepareAsync(projectId, request, token), SpriteDocument.JsonOptions);
            request = request with { CanvasPreparationId = result.GetProperty("canvasPreparationId").GetGuid(), CanvasOptions = null, MaskPngDataUrl = null };
        }
        var batch = await runtime.StartSpriteGenerationAsync(projectId, request, token);
        return await ReadAsync(projectId, batch.Id, token);
    }

    public async Task<SpriteJobView> ReadAsync(Guid projectId, Guid jobId, CancellationToken token = default)
    {
        var batch = await BatchAsync(projectId, jobId, token); var target = Target(batch);
        var current = await documents.ReadAsync(projectId, target.DocumentId, cancellationToken: token);
        var candidates = await db.ArtAssets.AsNoTracking().Where(a => a.ProjectId == projectId && a.SourceBatchId == jobId)
            .Select(a => new SpriteCandidate(a.Id, a.Label, $"/media/projects/{projectId}/assets/{a.Id}/full", a.Width ?? 0, a.Height ?? 0, a.RawProviderData != null)).ToListAsync(token);
        return new(jobId, target, batch.Status.ToString(), runtime.GetSnapshot().Batches.Any(b => b.BatchId == jobId && b.IsRunning), current.Revision,
            current.Revision != target.Revision, batch.Outputs.OrderBy(o => o.OutputIndex).Select(GenerationQueueState.Read).ToList(), candidates);
    }

    public async Task<IReadOnlyList<SpriteJobView>> ListAsync(Guid projectId, Guid documentId, CancellationToken token = default)
    {
        var batches = await db.GenerationBatches.AsNoTracking().IgnoreAutoIncludes().Where(b => b.ProjectId == projectId && b.SpriteTargetJson != "").OrderByDescending(b => b.CreatedAt).ToListAsync(token);
        var jobs = new List<SpriteJobView>();
        foreach (var batch in batches.Where(b => Target(b).DocumentId == documentId).Take(20)) jobs.Add(await ReadAsync(projectId, batch.Id, token));
        return jobs;
    }

    public async Task<object> OperateAsync(Guid projectId, Guid jobId, string action, Guid? candidateId = null, int waitSeconds = 20, string source = "agent", CancellationToken token = default)
    {
        var batch = await BatchAsync(projectId, jobId, token);
        switch (action)
        {
            case "read": break;
            case "wait": await runtime.WaitForBatchCompletionAsync(jobId, TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0, 30)), token); break;
            case "cancel": await runtime.StopAsync(projectId, jobId, token); break;
            case "resume": await runtime.ResumeAsync(projectId, jobId, cancellationToken: token); break;
            case "retry": await runtime.ResumeAsync(projectId, jobId, retryFailed: true, cancellationToken: token); break;
            case "inspect": return await InspectAsync(projectId, batch, candidateId ?? throw new InvalidOperationException("Choose a candidate."), token);
            case "apply":
            {
                var commit = await ApplyAsync(projectId, jobId, candidateId ?? throw new InvalidOperationException("Choose a candidate."), source, token);
                return new { commit.DocumentId, commit.Revision, commit.HistoryId, artifacts = (await inspections.RenderAsync(projectId, new(commit.DocumentId, commit.Revision, "frame", [Target(batch).FrameId]), token)).Artifacts };
            }
            default: throw new InvalidOperationException("Action must be read, wait, cancel, resume, retry, inspect, or apply.");
        }
        return await ReadAsync(projectId, jobId, token);
    }

    public async Task<SpriteCommit> ApplyAsync(Guid projectId, Guid jobId, Guid candidateId, string source = "user", CancellationToken token = default)
    {
        var batch = await BatchAsync(projectId, jobId, token); var target = Target(batch);
        var snapshot = await documents.ReadAsync(projectId, target.DocumentId, cancellationToken: token);
        if (snapshot.Revision != target.Revision) throw new SpriteConflictException(target.Revision, snapshot.Revision);
        var asset = await CandidateAsync(projectId, jobId, candidateId, token);
        var raster = SpriteRaster.Decode(asset.Data); var bitmap = raster.Encode();
        if (!await db.SpriteBitmaps.AnyAsync(b => b.Hash == bitmap.Hash, token)) { db.SpriteBitmaps.Add(bitmap); await db.SaveChangesAsync(token); }
        var frame = snapshot.Document.Frames.Single(f => f.Id == target.FrameId);
        var operations = new List<JsonElement> { JsonSerializer.SerializeToElement(new { op = "clearSelection" }) };
        if (frame.Width != raster.Width || frame.Height != raster.Height)
            operations.Add(JsonSerializer.SerializeToElement(new { op = "crop", frameId = target.FrameId, layerId = target.LayerId, x = -target.PaddingLeft, y = -target.PaddingTop, width = raster.Width, height = raster.Height }));
        operations.Add(JsonSerializer.SerializeToElement(new { op = "replaceCel", frameId = target.FrameId, layerId = target.LayerId, bitmapHash = bitmap.Hash }));
        operations.Add(JsonSerializer.SerializeToElement(new { op = "setProvenance", key = $"ai:{jobId}:{candidateId}", value = JsonSerializer.Serialize(new { target, candidateId, asset.SourceMetadataJson }, SpriteDocument.JsonOptions) }));
        // Full provider output is authoritative. The command engine may reject strict palette/alpha violations;
        // it never silently quantizes a candidate or pastes source pixels over it.
        return await documents.ApplyAsync(projectId, new(target.DocumentId, target.Revision, $"Apply AI candidate {candidateId}", operations, source, target.TaskId), token);
    }

    private async Task<object> InspectAsync(Guid projectId, GenerationBatch batch, Guid candidateId, CancellationToken token)
    {
        var target = Target(batch); var candidate = await CandidateAsync(projectId, batch.Id, candidateId, token);
        var before = batch.SpriteLogicalSourceData ?? await documents.RenderAsync(projectId, target.DocumentId, target.FrameId, target.Revision, token);
        var a = SpriteRaster.Decode(before); var b = SpriteRaster.Decode(candidate.Data); var diff = SpriteRaster.Blank(b.Width, b.Height); var changed = 0;
        for (var y = 0; y < b.Height; y++) for (var x = 0; x < b.Width; x++) if (!a.Get(x, y).Equals(b.Get(x, y))) { diff.Put(x, y, new(255,40,80,255)); changed++; }
        var artifacts = new List<SpriteArtifactView>
        {
            await inspections.StoreAsync(projectId, target.DocumentId, target.Revision, $"AI source r{target.Revision}, frame {target.FrameId}, layer {target.LayerId}", before, token),
            await inspections.StoreAsync(projectId, target.DocumentId, target.Revision, $"Candidate {candidateId}; {b.Width}x{b.Height}; not applied", candidate.Data, token),
            await inspections.StoreAsync(projectId, target.DocumentId, target.Revision, $"Candidate {candidateId} difference: {changed} pixels differ on candidate canvas; dimensions {a.Width}x{a.Height} → {b.Width}x{b.Height}", diff.Encode().Data, token)
        };
        return new { jobId = batch.Id, candidateId, target, changedPixels = changed, artifacts };
    }

    private async Task<GenerationBatch> BatchAsync(Guid projectId, Guid id, CancellationToken token) =>
        await db.GenerationBatches.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(b => b.ProjectId == projectId && b.Id == id && b.SpriteTargetJson != "", token) ?? throw new InvalidOperationException("Sprite job not found.");
    private async Task<ArtAsset> CandidateAsync(Guid projectId, Guid jobId, Guid id, CancellationToken token) =>
        await db.ArtAssets.AsNoTracking().SingleOrDefaultAsync(a => a.ProjectId == projectId && a.SourceBatchId == jobId && a.Id == id, token) ?? throw new InvalidOperationException("Candidate does not belong to this job.");
    public static SpriteGenerationTarget Target(GenerationBatch batch) => JsonSerializer.Deserialize<SpriteGenerationTarget>(batch.SpriteTargetJson, SpriteDocument.JsonOptions) ?? throw new InvalidOperationException("Invalid sprite target.");
    public static string TargetKind(Guid layerId) => $"sprite-layer:{layerId}";
}
