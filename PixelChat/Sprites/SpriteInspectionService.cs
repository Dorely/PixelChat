using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Sprites;

public sealed record SpriteRenderRequest(Guid DocumentId, long Revision, string Kind = "frame", IReadOnlyList<Guid>? FrameIds = null,
    SpriteRect? Crop = null, int Scale = 1, int Page = 0, int PageSize = 12, long? CompareRevision = null);
public sealed record SpriteArtifactView(Guid Id, string Label, string Url, int Width, int Height, long Revision);
public sealed record SpriteRenderResult(Guid DocumentId, long Revision, int TotalFrames, int? NextPage, IReadOnlyList<SpriteArtifactView> Artifacts);

public sealed class SpriteInspectionService(AppDbContext db, ISpriteDocumentService documents)
{
    public async Task<SpriteRenderResult> RenderAsync(Guid projectId, SpriteRenderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Kind is not ("frame" or "contact" or "onion" or "difference")) throw new InvalidOperationException("Render kind must be frame, contact, onion, or difference.");
        var snapshot = await documents.ReadAsync(projectId, request.DocumentId, request.Revision, cancellationToken);
        var bitmaps = await documents.LoadBitmapsAsync(snapshot.Document, cancellationToken);
        var all = snapshot.Document.Frames.Where(f => request.FrameIds is null || request.FrameIds.Count == 0 || request.FrameIds.Contains(f.Id)).ToList();
        if (all.Count == 0) throw new InvalidOperationException("No matching frames.");
        var size = Math.Clamp(request.PageSize, 1, 24); var page = Math.Max(0, request.Page);
        var frames = request.Kind == "contact" ? all.Skip(page * size).Take(size).ToList() : all.Take(1).ToList();
        if (frames.Count == 0) throw new InvalidOperationException("Render page is beyond the document.");
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, SpriteDocument.JsonOptions))));
        var existing = await db.SpriteInspections.AsNoTracking().FirstOrDefaultAsync(i => i.FrameSetId == request.DocumentId && i.Revision == request.Revision && i.CacheKey == key, cancellationToken);
        if (existing is not null) return new(request.DocumentId, request.Revision, all.Count, request.Kind == "contact" && (page + 1) * size < all.Count ? page + 1 : null, [await ViewAsync(projectId, existing, cancellationToken)]);

        SpriteSnapshot? comparison = null; Dictionary<string, SpriteBitmap>? compareBitmaps = null;
        if (request.Kind == "difference")
        {
            if (request.CompareRevision is null) throw new InvalidOperationException("Difference rendering requires compareRevision.");
            comparison = await documents.ReadAsync(projectId, request.DocumentId, request.CompareRevision, cancellationToken);
            compareBitmaps = await documents.LoadBitmapsAsync(comparison.Document, cancellationToken);
        }
        var scale = Math.Clamp(request.Scale, 1, 16); var rendered = new List<(SpriteFrame Frame, SpriteRaster Image, SpriteRect Crop)>();
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raster = SpriteRaster.Composite(snapshot.Document, frame, hash => bitmaps[hash]);
            if (request.Kind == "onion")
            {
                var onion = SpriteRaster.Blank(frame.Width, frame.Height); var index = snapshot.Document.Frames.IndexOf(frame);
                foreach (var adjacent in snapshot.Document.Frames.Where((f, i) => Math.Abs(i - index) == 1 && !f.HideFromOnionSkin))
                {
                    var other = SpriteRaster.Composite(snapshot.Document, adjacent, hash => bitmaps[hash]);
                    for (var y = 0; y < onion.Height; y++) for (var x = 0; x < onion.Width; x++) onion.Blend(x, y, other.Get(x, y), .25);
                }
                for (var y = 0; y < onion.Height; y++) for (var x = 0; x < onion.Width; x++) onion.Blend(x, y, raster.Get(x, y));
                raster = onion;
            }
            if (comparison is not null)
            {
                var before = comparison.Document.Frames.FirstOrDefault(f => f.Id == frame.Id);
                var source = before is null ? SpriteRaster.Blank(frame.Width, frame.Height) : SpriteRaster.Composite(comparison.Document, before, hash => compareBitmaps![hash]);
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) raster.Put(x, y, raster.Get(x, y).Equals(source.Get(x, y)) ? new Rgba32(30, 35, 45, 255) : new Rgba32(255, 65, 90, 255));
            }
            var rect = request.Crop ?? new(0, 0, raster.Width, raster.Height);
            SpriteRaster.CheckSize(rect.Width, rect.Height);
            var cropped = SpriteRaster.Blank(rect.Width, rect.Height);
            for (var y = 0; y < rect.Height; y++) for (var x = 0; x < rect.Width; x++) cropped.Put(x, y, raster.Get(rect.X + x, rect.Y + y));
            rendered.Add((frame, scale == 1 ? cropped : cropped.Resize(checked(rect.Width * scale), checked(rect.Height * scale), "nearest"), rect));
        }
        var columns = request.Kind == "contact" ? Math.Min(4, rendered.Count) : 1;
        var slotWidth = Math.Max(144, rendered.Max(r => r.Image.Width)); var slotHeight = rendered.Max(r => r.Image.Height) + 24;
        var canvas = SpriteRaster.Blank(checked(columns * slotWidth), checked(((rendered.Count + columns - 1) / columns) * slotHeight));
        var labels = new List<string>();
        for (var i = 0; i < rendered.Count; i++)
        {
            var (frame, raster, crop) = rendered[i]; var x = i % columns * slotWidth; var y = i / columns * slotHeight;
            var ordinal = snapshot.Document.Frames.IndexOf(frame) + 1;
            SpriteRasterLabel.Draw(canvas, $"F{ordinal} {frame.Id.ToString("N")[..6]} {frame.DurationMs}MS", x, y);
            SpriteRasterLabel.Draw(canvas, $"R{request.Revision} X{crop.X} Y{crop.Y} {scale}X", x, y + 10);
            for (var ry = 0; ry < raster.Height; ry++) for (var rx = 0; rx < raster.Width; rx++) canvas.Put(x + rx, y + 24 + ry, raster.Get(rx, ry));
            labels.Add($"Frame {frame.Id} ({frame.Name}), {frame.DurationMs}ms, crop ({crop.X},{crop.Y},{crop.Width},{crop.Height}), {scale}x");
        }
        var bitmap = canvas.Encode();
        if (!await db.SpriteBitmaps.AnyAsync(b => b.Hash == bitmap.Hash, cancellationToken)) db.SpriteBitmaps.Add(bitmap);
        var inspection = new SpriteInspection { FrameSetId = request.DocumentId, Revision = request.Revision, CacheKey = key, BitmapHash = bitmap.Hash,
            Label = $"Revision {request.Revision} {request.Kind}: {string.Join("; ", labels)}" };
        db.SpriteInspections.Add(inspection); await db.SaveChangesAsync(cancellationToken);
        return new(request.DocumentId, request.Revision, all.Count, request.Kind == "contact" && (page + 1) * size < all.Count ? page + 1 : null, [await ViewAsync(projectId, inspection, cancellationToken)]);
    }

    public async Task<(byte[] Data, string Label)> ReadArtifactAsync(Guid projectId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        var inspection = await db.SpriteInspections.AsNoTracking().SingleOrDefaultAsync(i => i.Id == artifactId && db.FrameSets.Any(f => f.Id == i.FrameSetId && f.ProjectId == projectId), cancellationToken)
            ?? throw new InvalidOperationException("Inspection artifact not found.");
        var bitmap = await db.SpriteBitmaps.AsNoTracking().SingleAsync(b => b.Hash == inspection.BitmapHash, cancellationToken);
        return (bitmap.Data, inspection.Label);
    }

    public async Task<IReadOnlyList<SpriteArtifactView>> ListAsync(Guid projectId, Guid documentId, long revision, CancellationToken cancellationToken = default)
    {
        _ = await documents.ReadAsync(projectId, documentId, revision, cancellationToken);
        var records = await db.SpriteInspections.AsNoTracking().Where(i => i.FrameSetId == documentId && i.Revision == revision).OrderByDescending(i => i.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var result = new List<SpriteArtifactView>(); foreach (var record in records) result.Add(await ViewAsync(projectId, record, cancellationToken)); return result;
    }
    private async Task<SpriteArtifactView> ViewAsync(Guid projectId, SpriteInspection inspection, CancellationToken token)
    {
        var bitmap = await db.SpriteBitmaps.AsNoTracking().Where(b => b.Hash == inspection.BitmapHash).Select(b => new { b.Width, b.Height }).SingleAsync(token);
        return new(inspection.Id, inspection.Label, $"/media/projects/{projectId}/sprite-inspections/{inspection.Id}", bitmap.Width, bitmap.Height, inspection.Revision);
    }
}

internal static class SpriteRasterLabel
{
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['0']="111101101101111",['1']="010110010010111",['2']="111001111100111",['3']="111001111001111",['4']="101101111001001",['5']="111100111001111",['6']="111100111101111",['7']="111001010010010",['8']="111101111101111",['9']="111101111001111",
        ['A']="010101111101101",['B']="110101110101110",['C']="111100100100111",['D']="110101101101110",['E']="111100110100111",['F']="111100110100100",['M']="101111111101101",['S']="111100111001111",['R']="110101110101101",['X']="101101010101101",['Y']="101101010010010",['-']="000000111000000",
    };
    public static void Draw(SpriteRaster raster, string label, int x, int y)
    {
        foreach (var character in label.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(character, out var glyph))
                for (var row = 0; row < 5; row++) for (var col = 0; col < 3; col++) if (glyph[row * 3 + col] == '1') raster.Put(x + col, y + row, new(240, 245, 255, 255));
            x += 4;
        }
    }
}
