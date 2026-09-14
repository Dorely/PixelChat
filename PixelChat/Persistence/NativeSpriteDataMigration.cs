using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PixelChat.Art;
using PixelChat.Models;
using PixelChat.Sprites;

namespace PixelChat.Persistence;

/// <summary>One-time bridge between the additive and destructive schema migrations.</summary>
internal static class NativeSpriteDataMigration
{
    public static async Task MaterializeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var sets = await db.FrameSets.Where(f => f.DocumentJson == "").ToListAsync(cancellationToken);
        foreach (var set in sets)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var source = set.SourceAssetId is { } id ? await db.ArtAssets.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) : null;
            var sourcePixels = source is null ? null : SpriteRaster.Decode(source.Data);
            var background = sourcePixels is null ? new SpriteSheetBackground("transparent", 0, 0, 0, 0) :
                SpriteSheetImageAnalyzer.ResolveBackground(sourcePixels.Pixels, sourcePixels.Width, sourcePixels.Height);
            var document = new SpriteDocument { Name = set.Name, Specification = new() { ArtMode = "painted", Width = Math.Max(1, set.DefaultCellWidth), Height = Math.Max(1, set.DefaultCellHeight) }, Layers = [new() { Name = "Imported artwork" }] };
            var rows = new List<Dictionary<string, object>>();
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "SELECT * FROM Frames WHERE FrameSetId = $id ORDER BY \"Index\"";
                var parameter = command.CreateParameter(); parameter.ParameterName = "$id"; parameter.Value = set.Id.ToString().ToUpperInvariant(); command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                    rows.Add(row);
                }
            }
            foreach (var row in rows)
            {
                int N(string key) => Convert.ToInt32(row[key]);
                string S(string key) => row[key] is DBNull ? "" : Convert.ToString(row[key])!;
                byte[] B(string key) => row[key] is byte[] bytes ? bytes : [];
                var frameId = Guid.Parse(S("Id"));
                var width = Math.Max(1, N("LogicalWidth") > 0 ? N("LogicalWidth") : set.DefaultCellWidth);
                var height = Math.Max(1, N("LogicalHeight") > 0 ? N("LogicalHeight") : set.DefaultCellHeight);
                var canvas = SpriteRaster.Blank(width, height);
                var working = B("WorkingData");
                if (sourcePixels is null)
                {
                    var preview = B("PreviewData");
                    if (preview.Length == 0) throw new InvalidDataException($"Cannot migrate frame {frameId}: source and preview are missing. Original database has been retained.");
                    canvas = SpriteRaster.Decode(preview);
                }
                else
                {
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) canvas.Put(x, y, new(background.R, background.G, background.B, background.A));
                    SpriteRaster content;
                    if (working.Length > 0 && SpriteSheetPngCodec.TryReadRgba(working, out var ww, out var wh, out var rgba)) content = new(ww, wh, rgba);
                    else
                    {
                        var x = Math.Clamp(N("SourceX"), 0, sourcePixels.Width - 1); var y = Math.Clamp(N("SourceY"), 0, sourcePixels.Height - 1);
                        content = SpriteRaster.Blank(Math.Clamp(N("SourceWidth"), 1, sourcePixels.Width - x), Math.Clamp(N("SourceHeight"), 1, sourcePixels.Height - y));
                        for (var cy = 0; cy < content.Height; cy++) for (var cx = 0; cx < content.Width; cx++) content.Put(cx, cy, sourcePixels.Get(x + cx, y + cy));
                    }
                    var whole = working.Length > 0 && content.Width == width && content.Height == height;
                    for (var y = 0; y < content.Height; y++) for (var x = 0; x < content.Width; x++) canvas.Put(x + (whole ? 0 : N("ContentOffsetX")), y + (whole ? 0 : N("ContentOffsetY")), content.Get(x, y));
                }
                var bitmap = canvas.Encode();
                if (!db.SpriteBitmaps.Local.Any(b => b.Hash == bitmap.Hash) && !await db.SpriteBitmaps.AnyAsync(b => b.Hash == bitmap.Hash, cancellationToken)) db.SpriteBitmaps.Add(bitmap);
                var frame = new SpriteFrame { Id = frameId, Name = S("Name"), Width = canvas.Width, Height = canvas.Height,
                    DurationMs = Math.Max(1, N("DurationMs")), HideFromOnionSkin = N("HideFromOnionSkin") != 0,
                    SourceRegionId = Guid.TryParse(S("SourceRegionId"), out var regionId) ? regionId : null,
                    SourceRect = new(N("SourceX"), N("SourceY"), N("SourceWidth"), N("SourceHeight")), ImportOffset = new(N("ContentOffsetX"), N("ContentOffsetY")),
                    CanvasTransformJson = S("WorkingCanvasTransformJson"), CanvasFinalizationJson = S("WorkingCanvasFinalizationJson"),
                    Cels = new() { [document.Layers[0].Id] = bitmap.Hash } };
                var anchors = await db.Anchors.IgnoreQueryFilters().Where(a => a.FrameId == frameId).ToListAsync(cancellationToken);
                foreach (var anchor in anchors) frame.Pivots[anchor.Name] = new(anchor.X, anchor.Y);
                document.Frames.Add(frame);
                document.Provenance[$"frame:{frameId}:geometry"] = JsonSerializer.Serialize(new { logicalWidth = width, logicalHeight = height, shape = S("ShapeJson"), bitmapRevisionAssetId = S("BitmapRevisionAssetId") });
            }
            if (document.Frames.Count == 0) document.Frames.Add(new() { Width = document.Specification.Width, Height = document.Specification.Height });
            var order = JsonSerializer.Deserialize<List<Guid>>(set.OrderedFrameIdsJson) ?? [];
            document.Frames = document.Frames.OrderBy(f => order.IndexOf(f.Id) is var i && i >= 0 ? i : int.MaxValue).ToList();
            document.Provenance["sourceAssetId"] = set.SourceAssetId?.ToString() ?? "";
            document.Provenance["import"] = "Materialized from pre-document rendered cells; original geometry retained per frame.";
            document.Clips.Add(new() { Name = "Animation", FrameIds = document.Frames.Select(f => f.Id).ToList() });
            set.DocumentJson = document.Serialize();
            set.UndoStackJson = "[0]";
            set.RedoStackJson = "[]";
            db.SpriteRevisions.Add(new() { FrameSetId = set.Id, Number = 0, DocumentJson = set.DocumentJson, Label = "Import existing rendered frames", Source = "migration" });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
