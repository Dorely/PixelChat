using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Persistence;

namespace PixelChat.Sprites;

public sealed record SpriteFinding(string Kind, string Code, string Message, IReadOnlyList<Guid> FrameIds);
public sealed record SpriteFrameMeasurement(Guid FrameId, int Index, int Width, int Height, int DurationMs,
    int VisiblePixels, int PartialAlphaPixels, int Colors, int OutsidePalettePixels, SpriteRect? Bounds,
    double? CentroidX, double? CentroidY, bool TouchesEdge, string PixelHash);
public sealed record SpriteClipMeasurement(string Name, string Direction, bool Loop, IReadOnlyList<Guid> Sequence, long DurationMs);
public sealed record SpritePairMeasurement(string Clip, Guid FromFrameId, Guid ToFrameId, bool LoopSeam, int ChangedPixels, int VisibleUnionPixels, double DifferenceFraction);
public sealed record SpriteValidationResult(Guid DocumentId, long Revision, bool StructurallyValid,
    IReadOnlyList<SpriteFrameMeasurement> Frames, IReadOnlyList<SpriteClipMeasurement> Clips, IReadOnlyList<SpriteFinding> Findings, IReadOnlyList<SpritePairMeasurement> Pairs);

public sealed class SpriteValidationService(AppDbContext db, ISpriteDocumentService documents)
{
    public async Task<SpriteValidationResult> ValidateAsync(Guid projectId, Guid id, long revision,
        long? compareRevision = null, IReadOnlyList<SpriteFinding>? judgments = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await documents.ReadAsync(projectId, id, revision, cancellationToken);
        var doc = snapshot.Document; var findings = new List<SpriteFinding>();
        try { SpriteCommandEngine.ValidateStructure(doc); }
        catch (InvalidOperationException ex) { findings.Add(new("error", "structure", ex.Message, [])); }
        var bitmaps = await documents.LoadBitmapsAsync(doc, cancellationToken);
        var palette = doc.Specification.Palette.Select(p => SpriteRaster.Color(p).ToHex()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var measurements = new List<SpriteFrameMeasurement>();
        var previousColors = new HashSet<string>();
        foreach (var frame in doc.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raster = SpriteRaster.Composite(doc, frame, hash => bitmaps[hash]);
            var visible = 0; var partial = 0; var outside = 0; var minX = frame.Width; var minY = frame.Height; var maxX = -1; var maxY = -1;
            double sumX = 0, sumY = 0; var colors = new HashSet<string>();
            for (var y = 0; y < frame.Height; y++) for (var x = 0; x < frame.Width; x++)
            {
                var p = raster.Get(x, y); if (p.A == 0) continue;
                visible++; if (p.A < 255) partial++; var hex = p.ToHex(); colors.Add(hex);
                if (palette.Count > 0 && !palette.Contains(hex)) outside++;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); sumX += x; sumY += y;
            }
            var edge = visible > 0 && (minX == 0 || minY == 0 || maxX == frame.Width - 1 || maxY == frame.Height - 1);
            measurements.Add(new(frame.Id, measurements.Count, frame.Width, frame.Height, frame.DurationMs, visible, partial, colors.Count, outside,
                visible == 0 ? null : new(minX, minY, maxX - minX + 1, maxY - minY + 1), visible == 0 ? null : sumX / visible, visible == 0 ? null : sumY / visible,
                edge, Convert.ToHexStringLower(SHA256.HashData(raster.Pixels))));
            if (visible == 0) findings.Add(new("fact", "empty", "Frame has no visible pixels; this may be an intentional hold or effect boundary.", [frame.Id]));
            if (edge) findings.Add(new("heuristic", "edge-contact", "Visible pixels touch the canvas edge. Inspect possible clipping; no pixels were moved.", [frame.Id]));
            if (doc.Specification.BinaryAlpha && partial > 0) findings.Add(new("error", "binary-alpha", $"Composite has {partial} pixels with partial alpha.", [frame.Id]));
            if (doc.Specification.EnforcePalette && outside > 0) findings.Add(new("error", "palette", $"Composite has {outside} pixels outside the enforced palette.", [frame.Id]));
            if (previousColors.Count > 0 && colors.Except(previousColors).Count() > Math.Max(2, previousColors.Count / 2))
                findings.Add(new("heuristic", "palette-drift", "Many colors first appear in this frame. Review lighting or palette continuity.", [frame.Id]));
            previousColors = colors;
            foreach (var (name, pivot) in frame.Pivots)
                if (pivot.X < 0 || pivot.Y < 0 || pivot.X > frame.Width || pivot.Y > frame.Height)
                    findings.Add(new("fact", "external-pivot", $"Pivot '{name}' is outside the logical canvas; external attachment points may be intentional.", [frame.Id]));
            foreach (var hash in frame.Cels.Values)
                if (bitmaps[hash].Width != frame.Width || bitmaps[hash].Height != frame.Height)
                    findings.Add(new("error", "cel-size", "Cel dimensions differ from the logical frame.", [frame.Id]));
        }
        foreach (var duplicate in measurements.GroupBy(m => (m.Width, m.Height, m.PixelHash)).Where(g => g.Count() > 1))
            findings.Add(new("fact", "duplicate", "Identical composite pixels. Repeated holds are allowed.", duplicate.Select(m => m.FrameId).ToArray()));
        var byId = measurements.ToDictionary(m => m.FrameId);
        var clips = (doc.Clips.Count > 0 ? doc.Clips : [new SpriteClip { Name = "Document", FrameIds = doc.Frames.Select(f => f.Id).ToList() }])
            .Select(c => new SpriteClipMeasurement(c.Name, c.Direction, c.Loop, SpriteTimeline.Sequence([], c), SpriteTimeline.Sequence([], c).Sum(id => (long)byId[id].DurationMs))).ToList();
        var pairs = new List<SpritePairMeasurement>();
        foreach (var clip in clips)
        {
            for (var i = 1; i < clip.Sequence.Count; i++) MeasurePair(clip, clip.Sequence[i - 1], clip.Sequence[i], false);
            if (clip.Loop && clip.Sequence.Count > 1) MeasurePair(clip, clip.Sequence[^1], clip.Sequence[0], true);
        }
        void MeasurePair(SpriteClipMeasurement clip, Guid from, Guid to, bool seam)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckPair(byId[from], byId[to], seam, findings);
            var a = SpriteRaster.Composite(doc, doc.Frames.Single(f => f.Id == from), h => bitmaps[h]);
            var b = SpriteRaster.Composite(doc, doc.Frames.Single(f => f.Id == to), h => bitmaps[h]); var changed = 0; var union = 0;
            for (var y = 0; y < Math.Max(a.Height, b.Height); y++) for (var x = 0; x < Math.Max(a.Width, b.Width); x++)
            {
                var pa = a.Get(x,y); var pb = b.Get(x,y); if (pa.A == 0 && pb.A == 0) continue; union++; if (!pa.Equals(pb)) changed++;
            }
            var fraction = union == 0 ? 0 : (double)changed / union;
            pairs.Add(new(clip.Name, from, to, seam, changed, union, fraction));
            if (fraction > .65) findings.Add(new("heuristic", seam ? "loop-pixel-discontinuity" : "pixel-discontinuity", $"{fraction:P0} of visible-union pixels differ. Inspect against intended motion; fast effects may be valid.", [from,to]));
        }
        foreach (var slice in doc.Slices)
            if (slice.Rect.Width < 1 || slice.Rect.Height < 1) findings.Add(new("error", "slice", $"Slice '{slice.Name}' has invalid dimensions.", []));
        if (compareRevision is { } beforeRevision)
        {
            var before = await documents.ReadAsync(projectId, id, beforeRevision, cancellationToken);
            if (before.Document.Selection is { } selection)
            {
                var oldFrame = before.Document.Frames.Single(f => f.Id == selection.FrameId);
                var current = doc.Frames.SingleOrDefault(f => f.Id == selection.FrameId);
                if (current is null || current.Width != oldFrame.Width || current.Height != oldFrame.Height)
                    findings.Add(new("fact", "selection-comparison-unavailable", "Selected frame was deleted or resized; preservation cannot be compared at fixed coordinates.", [oldFrame.Id]));
                else
                {
                    var oldBits = await documents.LoadBitmapsAsync(before.Document, cancellationToken);
                    var a = SpriteRaster.Composite(before.Document, oldFrame, h => oldBits[h]); var b = SpriteRaster.Composite(doc, current, h => bitmaps[h]); var changes = 0;
                    for (var y = 0; y < a.Height; y++) for (var x = 0; x < a.Width; x++)
                    {
                        var index = y * selection.Width + x;
                        var selected = selection.PixelMask is { } mask ? index / 8 < mask.Length && (mask[index / 8] & (1 << (index % 8))) != 0 : SpriteRaster.InsidePolygon(x, y, selection.Polygon);
                        if (!selected && !a.Get(x, y).Equals(b.Get(x, y))) changes++;
                    }
                    findings.Add(new(changes == 0 ? "fact" : "heuristic", "selection-preservation", $"{changes} composite pixels changed outside the previous selection. AI masks are advisory; assess against task intent.", [current.Id]));
                }
            }
        }
        if (judgments is { Count: > 0 })
        {
            if (judgments.Count > 100 || judgments.Any(j => j.FrameIds.Any(f => !byId.ContainsKey(f)) || j.Message.Length > 4000)) throw new InvalidOperationException("Judgments require existing frame IDs and bounded text.");
            var normalized = judgments.Select(j => j with { Kind = "judgment" }).ToList();
            db.SpriteAssessments.Add(new() { FrameSetId = id, Revision = revision, Kind = "judgments", ResultJson = JsonSerializer.Serialize(normalized, SpriteDocument.JsonOptions) });
            findings.AddRange(normalized);
        }
        var result = new SpriteValidationResult(id, revision, !findings.Any(f => f.Kind == "error"), measurements, clips, findings, pairs);
        db.SpriteAssessments.Add(new() { FrameSetId = id, Revision = revision, ResultJson = JsonSerializer.Serialize(result, SpriteDocument.JsonOptions) });
        await db.SaveChangesAsync(cancellationToken); return result;
    }

    public async Task<IReadOnlyList<SpriteAssessment>> HistoryAsync(Guid projectId, Guid id, long revision, CancellationToken token = default)
    {
        _ = await documents.ReadAsync(projectId, id, revision, token);
        return await db.SpriteAssessments.AsNoTracking().Where(a => a.FrameSetId == id && a.Revision == revision).OrderByDescending(a => a.CreatedAt).Take(20).ToListAsync(token);
    }

    private static void CheckPair(SpriteFrameMeasurement a, SpriteFrameMeasurement b, bool seam, List<SpriteFinding> findings)
    {
        var prefix = seam ? "Loop seam: " : "";
        if (a.CentroidX is { } ax && a.CentroidY is { } ay && b.CentroidX is { } bx && b.CentroidY is { } by)
        {
            var distance = Math.Sqrt(Math.Pow(bx - ax, 2) + Math.Pow(by - ay, 2));
            if (distance > Math.Max(a.Width, a.Height) * .15)
                findings.Add(new("heuristic", seam ? "loop-seam" : "centroid-motion", $"{prefix}centroid moves {distance:0.##} pixels. Jumps, recoil, and travel may be intended; inspect motion requirements.", [a.FrameId, b.FrameId]));
        }
        if (Math.Min(a.VisiblePixels, b.VisiblePixels) > 0 && (double)Math.Max(a.VisiblePixels, b.VisiblePixels) / Math.Min(a.VisiblePixels, b.VisiblePixels) > 1.5)
            findings.Add(new("heuristic", "silhouette-change", $"{prefix}visible area changes by more than 50%. Squash/stretch, turns, and effects may explain this.", [a.FrameId, b.FrameId]));
    }
}
