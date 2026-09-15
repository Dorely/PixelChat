using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Sprites;

public interface ISpriteDocumentService
{
    Task<SpriteSnapshot> ImportAssetsAsync(Guid projectId, string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<SpriteSnapshot> ImportAsync(Guid projectId, Guid? sourceAssetId, SpriteDocument document, IReadOnlyCollection<SpriteBitmap> bitmaps, CancellationToken cancellationToken = default);
    Task<SpriteSnapshot> CreateAsync(Guid projectId, string name, int width, int height, string artMode, CancellationToken cancellationToken = default);
    Task<SpriteSnapshot> ReadAsync(Guid projectId, Guid documentId, long? revision = null, CancellationToken cancellationToken = default);
    Task<SpriteCommit> ApplyAsync(Guid projectId, SpriteBatch batch, CancellationToken cancellationToken = default);
    Task<SpriteCommit> ReplaceAsync(Guid projectId, Guid documentId, long expectedRevision, SpriteDocument document,
        IReadOnlyCollection<SpriteBitmap> bitmaps, string label, string source = "user", CancellationToken cancellationToken = default);
    Task<SpriteCommit> HistoryAsync(Guid projectId, Guid documentId, long expectedRevision, string action, bool wholeTask = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpriteRevision>> ListHistoryAsync(Guid projectId, Guid documentId, int offset = 0, CancellationToken cancellationToken = default);
    Task<Dictionary<string, SpriteBitmap>> LoadBitmapsAsync(SpriteDocument document, CancellationToken cancellationToken = default);
    Task<byte[]> RenderAsync(Guid projectId, Guid documentId, Guid frameId, long? revision = null, CancellationToken cancellationToken = default);
}

/// <summary>Single writer for sprite state. SQLite compare-and-swap and history share one transaction.</summary>
public sealed class SpriteDocumentService(AppDbContext db, SpriteDocumentEvents? events = null) : ISpriteDocumentService
{
    public async Task<SpriteSnapshot> ImportAssetsAsync(Guid projectId, string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0) throw new InvalidOperationException("Select at least one asset to import.");
        var document = new SpriteDocument { Name = name.Trim(), Layers = [new() { Name = "Artwork" }] };
        var bitmaps = new List<SpriteBitmap>();
        foreach (var id in assetIds)
        {
            var source = await db.ArtAssets.AsNoTracking().Where(asset => asset.ProjectId == projectId && asset.Id == id)
                .Select(asset => new { asset.Data }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Import asset not found.");
            var raster = SpriteRaster.Decode(source.Data);
            var bitmap = raster.Encode();
            bitmaps.Add(bitmap);
            var frame = new SpriteFrame
            {
                Name = $"Frame {document.Frames.Count + 1}", Width = raster.Width, Height = raster.Height,
                Cels = new() { [document.Layers[0].Id] = bitmap.Hash },
            };
            document.Frames.Add(frame);
            document.Provenance[$"frame:{frame.Id}:sourceAssetId"] = id.ToString();
        }
        document.Specification.Width = document.Frames.Max(frame => frame.Width);
        document.Specification.Height = document.Frames.Max(frame => frame.Height);
        return await ImportAsync(projectId, assetIds.Count == 1 ? assetIds[0] : null, document, bitmaps, cancellationToken);
    }

    public async Task<SpriteSnapshot> CreateAsync(Guid projectId, string name, int width, int height, string artMode, CancellationToken cancellationToken = default)
    {
        SpriteRaster.CheckSize(width, height);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken)) throw new InvalidOperationException("Project not found.");
        var document = new SpriteDocument
        {
            Name = name.Trim(), Specification = new() { Width = width, Height = height, ArtMode = artMode, BinaryAlpha = artMode == "pixel" },
            Layers = [new() { Name = "Artwork" }], Frames = [new() { Width = width, Height = height, Name = "Frame 1" }],
        };
        return await ImportAsync(projectId, null, document, [], cancellationToken);
    }

    public async Task<SpriteSnapshot> ImportAsync(Guid projectId, Guid? sourceAssetId, SpriteDocument document, IReadOnlyCollection<SpriteBitmap> bitmaps, CancellationToken cancellationToken = default)
    {
        SpriteCommandEngine.ValidateStructure(document);
        var frameIds = document.Frames.Select(f => f.Id).ToList();
        if (await db.Frames.IgnoreQueryFilters().AnyAsync(f => frameIds.Contains(f.Id), cancellationToken)) throw new InvalidOperationException("Imported frame IDs already belong to another document.");
        var supplied = bitmaps.DistinctBy(b => b.Hash).ToDictionary(b => b.Hash);
        foreach (var bitmap in supplied.Values)
        {
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bitmap.Data)) != bitmap.Hash) throw new InvalidDataException("Bitmap content hash mismatch.");
            var decoded = SpriteRaster.Decode(bitmap.Data);
            if (decoded.Width != bitmap.Width || decoded.Height != bitmap.Height) throw new InvalidDataException("Bitmap metadata dimensions mismatch.");
        }
        foreach (var frame in document.Frames) foreach (var hash in frame.Cels.Values)
        {
            var bitmap = supplied.GetValueOrDefault(hash) ?? await db.SpriteBitmaps.AsNoTracking().SingleOrDefaultAsync(b => b.Hash == hash, cancellationToken) ?? throw new InvalidDataException("Missing imported cel bitmap.");
            if (bitmap.Width != frame.Width || bitmap.Height != frame.Height) throw new InvalidDataException("Imported cel dimensions differ from its frame.");
        }
        var set = new FrameSet { ProjectId = projectId, SourceAssetId = sourceAssetId, Name = document.Name, DefaultCellWidth = document.Specification.Width, DefaultCellHeight = document.Specification.Height, DocumentJson = document.Serialize() };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var bitmap in bitmaps.DistinctBy(b => b.Hash))
                if (!db.SpriteBitmaps.Local.Any(b => b.Hash == bitmap.Hash) && !await db.SpriteBitmaps.AnyAsync(b => b.Hash == bitmap.Hash, cancellationToken)) db.SpriteBitmaps.Add(bitmap);
            db.FrameSets.Add(set);
            db.SpriteRevisions.Add(new() { FrameSetId = set.Id, Number = 0, DocumentJson = set.DocumentJson, Label = "Create sprite" });
            await ProjectFramesAsync(set, document, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            return new(set.Id, 0, document);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<SpriteSnapshot> ReadAsync(Guid projectId, Guid documentId, long? revision = null, CancellationToken cancellationToken = default)
    {
        var set = await db.FrameSets.AsNoTracking().SingleOrDefaultAsync(f => f.ProjectId == projectId && f.Id == documentId, cancellationToken)
            ?? throw new InvalidOperationException("Sprite document not found.");
        var json = set.DocumentJson;
        if (revision.HasValue && revision != set.Revision)
            json = await db.SpriteRevisions.Where(r => r.FrameSetId == documentId && r.Number == revision).Select(r => r.DocumentJson).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Sprite revision not found.");
        if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("Sprite document has not been materialized.");
        return new(documentId, revision ?? set.Revision, SpriteDocument.Deserialize(json));
    }

    public async Task<Dictionary<string, SpriteBitmap>> LoadBitmapsAsync(SpriteDocument document, CancellationToken cancellationToken = default)
    {
        var hashes = document.Frames.SelectMany(f => f.Cels.Values).Distinct().ToList();
        if (document.Clipboard is { } clipboard) hashes.Add(clipboard.BitmapHash);
        hashes = hashes.Distinct().ToList();
        var result = new Dictionary<string, SpriteBitmap>();
        foreach (var chunk in hashes.Chunk(400))
            foreach (var bitmap in await db.SpriteBitmaps.AsNoTracking().Where(b => chunk.Contains(b.Hash)).ToListAsync(cancellationToken)) result.Add(bitmap.Hash, bitmap);
        if (hashes.Any(h => !result.ContainsKey(h))) throw new InvalidDataException("Sprite references missing bitmap content.");
        return result;
    }

    public async Task<SpriteCommit> ApplyAsync(Guid projectId, SpriteBatch batch, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadAsync(projectId, batch.DocumentId, cancellationToken: cancellationToken);
        if (snapshot.Revision != batch.ExpectedRevision) throw new SpriteConflictException(batch.ExpectedRevision, snapshot.Revision);
        var bitmaps = await LoadBitmapsAsync(snapshot.Document, cancellationToken);
        // replaceCel may reference an imported candidate which is not yet used by the document.
        foreach (var operation in batch.Operations)
            if (operation.TryGetProperty("bitmapHash", out var hashValue))
            {
                var hash = hashValue.GetString()!;
                if (!bitmaps.ContainsKey(hash)) bitmaps[hash] = await db.SpriteBitmaps.AsNoTracking().SingleOrDefaultAsync(b => b.Hash == hash, cancellationToken)
                    ?? throw new InvalidOperationException("Bitmap not found.");
            }
        var engine = new SpriteCommandEngine(snapshot.Document, hash => bitmaps[hash], cancellationToken);
        engine.Apply(batch.Operations);
        return await CommitAsync(projectId, batch, snapshot.Document, engine.Created.Values.ToList(), null, null, cancellationToken);
    }

    public Task<SpriteCommit> ReplaceAsync(Guid projectId, Guid documentId, long expectedRevision, SpriteDocument document,
        IReadOnlyCollection<SpriteBitmap> bitmaps, string label, string source = "user", CancellationToken cancellationToken = default) =>
        CommitAsync(projectId, new(documentId, expectedRevision, label, [], source), document, bitmaps, null, null, cancellationToken);

    private async Task<SpriteCommit> CommitAsync(Guid projectId, SpriteBatch batch, SpriteDocument document, IReadOnlyCollection<SpriteBitmap> bitmaps,
        List<long>? undo, List<long>? redo, CancellationToken cancellationToken)
    {
        SpriteCommandEngine.ValidateStructure(document);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var set = await db.FrameSets.SingleAsync(f => f.ProjectId == projectId && f.Id == batch.DocumentId, cancellationToken);
            await db.Entry(set).ReloadAsync(cancellationToken);
            if (set.Revision != batch.ExpectedRevision) throw new SpriteConflictException(batch.ExpectedRevision, set.Revision);
            var next = checked(set.Revision + 1);
            var json = document.Serialize();
            var updated = await db.FrameSets.Where(f => f.ProjectId == projectId && f.Id == set.Id && f.Revision == batch.ExpectedRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Revision, next), cancellationToken);
            if (updated != 1) throw new SpriteConflictException(batch.ExpectedRevision, next);
            set.Revision = next;
            db.Entry(set).Property(f => f.Revision).OriginalValue = next;
            set.DocumentJson = json; set.Name = document.Name; set.UpdatedAt = DateTime.UtcNow;
            set.DefaultCellWidth = document.Specification.Width; set.DefaultCellHeight = document.Specification.Height;
            var history = new SpriteRevision { FrameSetId = set.Id, Number = next, DocumentJson = json, Label = batch.Label,
                Source = batch.Source, TaskId = batch.TaskId, Script = batch.Script, OperationsJson = JsonSerializer.Serialize(batch.Operations, SpriteDocument.JsonOptions) };
            if (undo is null)
            {
                undo = JsonSerializer.Deserialize<List<long>>(set.UndoStackJson)!;
                undo.Add(next); redo = [];
            }
            else history.UndoTarget = undo[^1];
            set.UndoStackJson = JsonSerializer.Serialize(undo); set.RedoStackJson = JsonSerializer.Serialize(redo);
            foreach (var bitmap in bitmaps.DistinctBy(b => b.Hash))
                if (!db.SpriteBitmaps.Local.Any(b => b.Hash == bitmap.Hash) && !await db.SpriteBitmaps.AnyAsync(b => b.Hash == bitmap.Hash, cancellationToken)) db.SpriteBitmaps.Add(bitmap);
            var allHashes = document.Frames.SelectMany(f => f.Cels.Values).Distinct().ToList();
            var addedHashes = bitmaps.Select(b => b.Hash).ToHashSet();
            foreach (var hash in allHashes.Where(h => !addedHashes.Contains(h)))
                if (!await db.SpriteBitmaps.AnyAsync(b => b.Hash == hash, cancellationToken)) throw new InvalidDataException($"Missing bitmap {hash}.");
            db.SpriteRevisions.Add(history);
            await ProjectFramesAsync(set, document, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            events?.Publish(set.Id, next);
            return new(set.Id, next, history.Id, batch.Operations.Count);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // Relational frame rows retain identities and relationships used by masks, references and media routes.
    private async Task ProjectFramesAsync(FrameSet set, SpriteDocument document, CancellationToken cancellationToken)
    {
        var existing = await db.Frames.IgnoreQueryFilters().Where(f => f.FrameSetId == set.Id).ToListAsync(cancellationToken);
        var ids = document.Frames.Select(f => f.Id).ToHashSet();
        var removed = existing.Where(f => !ids.Contains(f.Id)).ToList();
        // Keep relational identities and frame-owned masks so undo restores references after deletion.
        foreach (var frame in removed) frame.IsDeleted = true;
        foreach (var (item, index) in document.Frames.Select((f, i) => (f, i)))
        {
            var frame = existing.SingleOrDefault(f => f.Id == item.Id);
            if (frame is null) { frame = new() { Id = item.Id, ProjectId = set.ProjectId, FrameSetId = set.Id }; db.Frames.Add(frame); }
            frame.IsDeleted = false;
            frame.Name = item.Name; frame.Index = index; frame.LogicalWidth = item.Width; frame.LogicalHeight = item.Height;
            frame.DurationMs = item.DurationMs; frame.HideFromOnionSkin = item.HideFromOnionSkin;
            frame.SourceRegionId = item.SourceRegionId is { } regionId && await db.SpriteRegions.AnyAsync(r => r.Id == regionId && r.ProjectId == set.ProjectId, cancellationToken) ? regionId : null;
            if (item.SourceRect is { } rect) { frame.SourceX = rect.X; frame.SourceY = rect.Y; frame.SourceWidth = rect.Width; frame.SourceHeight = rect.Height; }
            frame.ContentOffsetX = item.ImportOffset.X; frame.ContentOffsetY = item.ImportOffset.Y;
            frame.UpdatedAt = DateTime.UtcNow;
            var anchors = await db.Anchors.IgnoreQueryFilters().Where(a => a.FrameId == item.Id).ToListAsync(cancellationToken);
            db.Anchors.RemoveRange(anchors.Where(a => !item.Pivots.ContainsKey(a.Name)));
            foreach (var (name, point) in item.Pivots)
            {
                var anchor = anchors.FirstOrDefault(a => a.Name == name);
                if (anchor is null) { anchor = new() { ProjectId = set.ProjectId, FrameId = item.Id, Name = name }; db.Anchors.Add(anchor); }
                anchor.X = point.X; anchor.Y = point.Y; anchor.UpdatedAt = DateTime.UtcNow;
            }
        }
        set.OrderedFrameIdsJson = JsonSerializer.Serialize(document.Frames.Select(f => f.Id));
    }

    public async Task<SpriteCommit> HistoryAsync(Guid projectId, Guid documentId, long expectedRevision, string action, bool wholeTask = false, CancellationToken cancellationToken = default)
    {
        var set = await db.FrameSets.AsNoTracking().SingleAsync(f => f.ProjectId == projectId && f.Id == documentId, cancellationToken);
        if (set.Revision != expectedRevision) throw new SpriteConflictException(expectedRevision, set.Revision);
        var undo = JsonSerializer.Deserialize<List<long>>(set.UndoStackJson)!;
        var redo = JsonSerializer.Deserialize<List<long>>(set.RedoStackJson)!;
        if (action == "undo")
        {
            if (undo.Count <= 1) throw new InvalidOperationException("Nothing to undo.");
            var tip = await db.SpriteRevisions.SingleAsync(r => r.FrameSetId == documentId && r.Number == undo[undo.Count - 1], cancellationToken);
            do
            {
                redo.Add(undo[^1]); undo.RemoveAt(undo.Count - 1);
                if (!wholeTask || tip.TaskId is null || undo.Count <= 1) break;
                var previous = await db.SpriteRevisions.SingleAsync(r => r.FrameSetId == documentId && r.Number == undo[undo.Count - 1], cancellationToken);
                if (previous.Source != tip.Source || previous.TaskId != tip.TaskId || previous.Number != redo[^1] - 1) break;
            } while (true);
        }
        else if (action == "redo")
        {
            if (redo.Count == 0) throw new InvalidOperationException("Nothing to redo.");
            undo.Add(redo[^1]); redo.RemoveAt(redo.Count - 1);
        }
        else throw new InvalidOperationException("History action must be undo or redo.");
        var target = await db.SpriteRevisions.SingleAsync(r => r.FrameSetId == documentId && r.Number == undo[undo.Count - 1], cancellationToken);
        return await CommitAsync(projectId, new(documentId, expectedRevision, action == "undo" ? "Undo" : "Redo", []),
            SpriteDocument.Deserialize(target.DocumentJson), [], undo, redo, cancellationToken);
    }

    public async Task<IReadOnlyList<SpriteRevision>> ListHistoryAsync(Guid projectId, Guid documentId, int offset = 0, CancellationToken cancellationToken = default)
    {
        _ = await ReadAsync(projectId, documentId, cancellationToken: cancellationToken);
        return await db.SpriteRevisions.AsNoTracking().Where(r => r.FrameSetId == documentId).OrderByDescending(r => r.Number).Skip(Math.Max(0, offset)).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<byte[]> RenderAsync(Guid projectId, Guid documentId, Guid frameId, long? revision = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadAsync(projectId, documentId, revision, cancellationToken);
        var bitmaps = await LoadBitmapsAsync(snapshot.Document, cancellationToken);
        var frame = snapshot.Document.Frames.SingleOrDefault(f => f.Id == frameId) ?? throw new InvalidOperationException("Frame not found.");
        return SpriteRaster.Composite(snapshot.Document, frame, hash => bitmaps[hash]).Encode().Data;
    }
}
