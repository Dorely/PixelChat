using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Sprites;

public sealed record SpriteExportView(Guid Id, Guid DocumentId, long Revision, string FileName, string ContentType, long Bytes, string Url, string ManifestJson, IReadOnlyList<string> Warnings);
public sealed record SpriteBundleManifest(string Format, int Version, Guid DocumentId, long Revision, SpriteDocument Document, IReadOnlyList<string> Bitmaps);
public sealed record SpriteImportResult(SpriteSnapshot Snapshot, IReadOnlyDictionary<Guid, Guid> FrameIdMap);

public sealed class SpriteExportService(AppDbContext db, ISpriteDocumentService documents)
{
    public const int MaxBundleBytes = 128 * 1024 * 1024;
    public async Task<IReadOnlyList<SpriteExportView>> ListAsync(Guid projectId, Guid documentId, CancellationToken token = default)
    {
        _ = await documents.ReadAsync(projectId, documentId, cancellationToken: token);
        var rows = await db.SpriteExports.AsNoTracking().Where(e => e.FrameSetId == documentId).OrderByDescending(e => e.CreatedAt).Take(20)
            .Select(e => new { e.Id, e.FrameSetId, e.Revision, e.FileName, e.ContentType, Bytes = e.Data.Length, e.ManifestJson }).ToListAsync(token);
        return rows.Select(e => new SpriteExportView(e.Id,e.FrameSetId,e.Revision,e.FileName,e.ContentType,e.Bytes,$"/media/projects/{projectId}/sprite-exports/{e.Id}",e.ManifestJson,
            e.ContentType == "image/gif" ? ["GIF quantizes colors, alpha and timing. Use native playback and PNG/JSON for exact data."] : [])).ToList();
    }
    public async Task<SpriteExportView> ExportAsync(Guid projectId, Guid id, long revision, SpriteExportSpec spec, CancellationToken token = default)
    {
        if (spec.Format is not ("bundle" or "atlas" or "frames" or "preview" or "metadata")) throw new InvalidOperationException("Export format must be bundle, atlas, frames, preview, or metadata.");
        var snapshot = await documents.ReadAsync(projectId, id, revision, token); var json = JsonSerializer.Serialize(spec, SpriteDocument.JsonOptions);
        var cached = await db.SpriteExports.AsNoTracking().FirstOrDefaultAsync(e => e.FrameSetId == id && e.Revision == revision && e.SpecificationJson == json, token);
        if (cached is not null) return View(projectId, cached);
        var bitmaps = await documents.LoadBitmapsAsync(snapshot.Document, token);
        byte[] data; string manifest; string extension; string contentType;
        if (spec.Format == "bundle")
        {
            manifest = JsonSerializer.Serialize(new SpriteBundleManifest("pixelchat-sprite", 1, id, revision, snapshot.Document, bitmaps.Keys.Order().ToList()), SpriteDocument.JsonOptions);
            data = Zip(writer => { Add(writer, "manifest.json", Encoding.UTF8.GetBytes(manifest)); foreach (var bitmap in bitmaps.Values.OrderBy(b => b.Hash)) { token.ThrowIfCancellationRequested(); Add(writer, $"bitmaps/{bitmap.Hash}.png", bitmap.Data); } });
            extension = "pixelchat.zip"; contentType = "application/zip";
        }
        else if (spec.Format == "preview")
        {
            var clip = spec.Clip is null ? snapshot.Document.Clips.FirstOrDefault() : snapshot.Document.Clips.SingleOrDefault(c => c.Name == spec.Clip) ?? throw new InvalidOperationException("Clip not found.");
            data = AnimatedGif(snapshot, bitmaps, clip, token);
            manifest = JsonSerializer.Serialize(new { format = "pixelchat-preview", version = 1, documentId = id, revision, clip,
                frames = SpriteTimeline.Sequence(snapshot.Document.Frames.Select(f => f.Id), clip).Select(frameId => new { frameId, durationMs = snapshot.Document.Frames.Single(f => f.Id == frameId).DurationMs }),
                warning = "GIF quantizes colors, alpha and timing to centiseconds. Native playback, PNGs and JSON retain exact data." }, SpriteDocument.JsonOptions);
            extension = "gif"; contentType = "image/gif";
        }
        else
        {
            var atlas = SpriteAtlasBuilder.Build(snapshot, bitmaps, spec, token);
            manifest = JsonSerializer.Serialize(atlas.Manifest, SpriteDocument.JsonOptions);
            if (spec.Format == "metadata") { data = Encoding.UTF8.GetBytes(manifest); extension = "json"; contentType = "application/json"; }
            else
            {
                data = Zip(writer =>
                {
                    Add(writer, "manifest.json", Encoding.UTF8.GetBytes(manifest));
                    if (spec.Format == "atlas") Add(writer, "atlas.png", atlas.Bitmap!.Data);
                    else foreach (var frame in snapshot.Document.Frames) { token.ThrowIfCancellationRequested(); Add(writer, atlas.Manifest.Frames.Single(f => f.Id == frame.Id).FileName, SpriteRaster.Composite(snapshot.Document, frame, h => bitmaps[h]).Encode().Data); }
                });
                extension = spec.Format + ".zip"; contentType = "application/zip";
            }
        }
        if (data.Length > MaxBundleBytes) throw new InvalidOperationException("Export exceeds 128 MB. Split the document into smaller production assets.");
        var export = new SpriteExport { FrameSetId = id, Revision = revision, SpecificationJson = json, Data = data, ManifestJson = manifest,
            FileName = $"{SafeName(snapshot.Document.Name)}-r{revision}.{extension}", ContentType = contentType };
        db.SpriteExports.Add(export); await db.SaveChangesAsync(token); return View(projectId, export);
    }

    public async Task<SpriteExport> ReadAsync(Guid projectId, Guid id, CancellationToken token = default) =>
        await db.SpriteExports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && db.FrameSets.Any(f => f.Id == e.FrameSetId && f.ProjectId == projectId), token) ?? throw new InvalidOperationException("Export not found.");

    public async Task<SpriteImportResult> ImportAsync(Guid projectId, byte[] bundle, string? name = null, CancellationToken token = default)
    {
        var (document, bitmaps) = DecodeBundle(bundle, token);
        var ids = document.Frames.ToDictionary(f => f.Id, _ => Guid.NewGuid());
        foreach (var frame in document.Frames) { var original = frame.Id; frame.Id = ids[original]; document.Provenance[$"import:frame:{frame.Id}"] = original.ToString(); }
        foreach (var clip in document.Clips) clip.FrameIds = clip.FrameIds.Select(id => ids[id]).ToList();
        if (document.Selection is { } selection) document.Selection = selection with { FrameId = ids[selection.FrameId] };
        if (!string.IsNullOrWhiteSpace(name)) document.Name = name;
        var snapshot = await documents.ImportAsync(projectId, null, document, bitmaps, token);
        return new(snapshot, ids);
    }

    public static (SpriteDocument Document, IReadOnlyList<SpriteBitmap> Bitmaps) DecodeBundle(byte[] data, CancellationToken token = default)
    {
        if (data.Length > MaxBundleBytes) throw new InvalidDataException("Bundle exceeds 128 MB.");
        using var stream = new MemoryStream(data); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 65540 || zip.Entries.Sum(e => e.Length) > MaxBundleBytes || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count) throw new InvalidDataException("Invalid or oversized bundle entries.");
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Bundle manifest missing.");
        if (entry.Length > 8_000_000) throw new InvalidDataException("Manifest too large.");
        var manifestBytes = Read(entry);
        using var parsed = JsonDocument.Parse(manifestBytes); var root = parsed.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported bundle version.");
        SpriteDocument doc; var bitmaps = new List<SpriteBitmap>();
        if (root.GetProperty("format").GetString() == "pixelchat-sprite")
        {
            var manifest = JsonSerializer.Deserialize<SpriteBundleManifest>(manifestBytes, SpriteDocument.JsonOptions) ?? throw new InvalidDataException("Invalid manifest.");
            doc = manifest.Document;
            foreach (var hash in manifest.Bitmaps)
            {
                token.ThrowIfCancellationRequested();
                if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid bitmap hash.");
                var bytes = Read(zip.GetEntry($"bitmaps/{hash}.png") ?? throw new InvalidDataException($"Missing bitmap {hash}."));
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash) throw new InvalidDataException("Bitmap hash mismatch.");
                var raster = SpriteRaster.Decode(bytes); bitmaps.Add(new() { Hash = hash, Data = bytes, Width = raster.Width, Height = raster.Height });
            }
        }
        else if (root.GetProperty("format").GetString() == "pixelchat-atlas")
        {
            var manifest = JsonSerializer.Deserialize<SpriteAtlasManifest>(manifestBytes, SpriteDocument.JsonOptions) ?? throw new InvalidDataException("Invalid atlas manifest.");
            var atlas = zip.GetEntry("atlas.png") is { } atlasEntry ? SpriteRaster.Decode(Read(atlasEntry)) : null;
            doc = new() { Name = manifest.Name, Layers = [new() { Name = "Imported composite" }], Clips = manifest.Clips.ToList(), Slices = manifest.Slices.ToList(),
                Specification = new() { Width = manifest.Frames.Max(f => f.LogicalWidth), Height = manifest.Frames.Max(f => f.LogicalHeight) } };
            foreach (var frame in manifest.Frames.OrderBy(f => f.Index))
            {
                token.ThrowIfCancellationRequested();
                var raster = SpriteRaster.Blank(frame.LogicalWidth, frame.LogicalHeight);
                if (atlas is null) raster = SpriteRaster.Decode(Read(zip.GetEntry(frame.FileName) ?? throw new InvalidDataException("Frame PNG missing.")));
                else
                {
                    if (frame.Rect.X < 0 || frame.Rect.Y < 0 || frame.Rect.X + (long)frame.Rect.Width > atlas.Width || frame.Rect.Y + (long)frame.Rect.Height > atlas.Height || frame.Rect.Width != frame.LogicalWidth || frame.Rect.Height != frame.LogicalHeight) throw new InvalidDataException("Frame rectangle outside atlas.");
                    for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) raster.Put(x,y,atlas.Get(frame.Rect.X+x,frame.Rect.Y+y));
                }
                if (Convert.ToHexStringLower(SHA256.HashData(raster.Pixels)) != frame.PixelHash) throw new InvalidDataException("Export reconstruction differs from its pixel hash.");
                var bitmap = raster.Encode(); bitmaps.Add(bitmap); doc.Frames.Add(new() { Id = frame.Id, Name = frame.Name, Width = frame.LogicalWidth, Height = frame.LogicalHeight, DurationMs = frame.DurationMs, Pivots = frame.Pivots.ToDictionary(), Cels = new() { [doc.Layers[0].Id] = bitmap.Hash } });
            }
        }
        else throw new InvalidDataException("Unsupported bundle format.");
        SpriteCommandEngine.ValidateStructure(doc);
        var byHash = bitmaps.DistinctBy(b => b.Hash).ToDictionary(b => b.Hash);
        foreach (var frame in doc.Frames) foreach (var hash in frame.Cels.Values)
            if (!byHash.TryGetValue(hash, out var bitmap) || bitmap.Width != frame.Width || bitmap.Height != frame.Height) throw new InvalidDataException("Missing or incorrectly sized cel content.");
        if (doc.Clipboard is { } clipboard && !byHash.ContainsKey(clipboard.BitmapHash)) throw new InvalidDataException("Clipboard bitmap missing.");
        return (doc, byHash.Values.ToList());
    }

    private static byte[] AnimatedGif(SpriteSnapshot snapshot, IReadOnlyDictionary<string, SpriteBitmap> bitmaps, SpriteClip? clip, CancellationToken token)
    {
        var doc = snapshot.Document; var ids = SpriteTimeline.Sequence(doc.Frames.Select(f => f.Id), clip);
        var width = doc.Frames.Max(f => f.Width); var height = doc.Frames.Max(f => f.Height);
        if ((long)width * height * ids.Count > 67_108_864) throw new InvalidOperationException("Animated preview exceeds 64 megapixels; use PNG frames or a shorter clip.");
        using var image = new Image<Rgba32>(width,height); image.Metadata.GetGifMetadata().RepeatCount = clip?.Loop == false ? (ushort)1 : (ushort)0;
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested(); var frame = doc.Frames.Single(f => f.Id == id); var raster = SpriteRaster.Composite(doc, frame, h => bitmaps[h]);
            using var cell = new Image<Rgba32>(width,height);
            for (var y=0;y<frame.Height;y++) for(var x=0;x<frame.Width;x++) cell[x,y] = raster.Get(x,y);
            var metadata = cell.Frames.RootFrame.Metadata.GetGifMetadata(); metadata.FrameDelay = Math.Max(1,(int)Math.Round(frame.DurationMs / 10d)); metadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
            image.Frames.AddFrame(cell.Frames.RootFrame);
        }
        image.Frames.RemoveFrame(0); using var stream = new MemoryStream(); image.SaveAsGif(stream); return stream.ToArray();
    }

    private static byte[] Zip(Action<ZipArchive> write) { using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) write(zip); return stream.ToArray(); }
    private static void Add(ZipArchive zip, string name, byte[] bytes) { var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new(1980,1,1,0,0,0,TimeSpan.Zero); using var output = entry.Open(); output.Write(bytes); }
    private static byte[] Read(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxBundleBytes) throw new InvalidDataException("Oversized bundle entry.");
        using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = input.Read(buffer)) > 0) { if (output.Length + count > entry.Length || output.Length + count > MaxBundleBytes) throw new InvalidDataException("Expanded entry exceeds its declared budget."); output.Write(buffer,0,count); }
        return output.ToArray();
    }
    private static string SafeName(string name) { var result = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-'); return string.IsNullOrEmpty(result) ? "sprite" : result[..Math.Min(result.Length,80)]; }
    private static SpriteExportView View(Guid projectId, SpriteExport export) => new(export.Id, export.FrameSetId, export.Revision, export.FileName, export.ContentType, export.Data.LongLength,
        $"/media/projects/{projectId}/sprite-exports/{export.Id}", export.ManifestJson, export.ContentType == "image/gif" ? ["GIF quantizes colors, alpha and timing. Use native playback and PNG/JSON for exact data."] : []);
}
