using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class SpriteMigrationTests
{
    [Fact]
    public async Task EmptyLegacySetGetsVisibleBlankFrame()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, NullLogger<AppDbContext>.Instance);
        await db.GetService<IMigrator>().MigrateAsync("20260914205129_NativeSpriteDocuments");
        var project = new Project { Name = "Empty legacy" }; var set = new FrameSet { ProjectId = project.Id, Name = "Empty" };
        db.Add(project); db.Add(set); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await DatabaseMigrationBootstrapper.MigrateAsync(db);
        var snapshot = await new SpriteDocumentService(db).ReadAsync(project.Id, set.Id);
        Assert.Equal(snapshot.Document.Frames.Single().Id, (await db.Frames.SingleAsync()).Id);
    }
    [Theory]
    [InlineData(false, 2, 2)]
    [InlineData(true, 2, 2)]
    [InlineData(true, 4, 4)]
    public async Task MigrationPreservesRenderedPixelsIdsGeometryAndMasks(bool edited, int contentWidth, int contentHeight)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, NullLogger<AppDbContext>.Instance);
        await db.GetService<IMigrator>().MigrateAsync("20260914205129_NativeSpriteDocuments");
        var project = new Project { Name = "Migration" };
        var source = SpriteRaster.Blank(4, 4); source.Put(0, 0, new(255, 0, 0, 255)); source.Put(1, 1, new(1, 2, 3, 100));
        var asset = new ArtAsset { ProjectId = project.Id, Label = "Source", FileName = "source.png", Data = source.Encode().Data, ContentType = "image/png", Width = 4, Height = 4 };
        var frameId = Guid.NewGuid();
        var set = new FrameSet { ProjectId = project.Id, Name = "Existing", SourceAssetId = asset.Id, DefaultCellWidth = 4, DefaultCellHeight = 4, OrderedFrameIdsJson = $"[\"{frameId}\"]" };
        db.Projects.Add(project); db.ArtAssets.Add(asset); db.FrameSets.Add(set); await db.SaveChangesAsync();
        var working = SpriteRaster.Blank(contentWidth, contentHeight); working.Put(0, 0, new(0, 255, 0, 255)); working.Put(1, 1, new(20, 30, 40, 0));
        var values = new Dictionary<string, object?>
        {
            ["Id"] = frameId.ToString().ToUpperInvariant(), ["ProjectId"] = project.Id.ToString().ToUpperInvariant(), ["FrameSetId"] = set.Id.ToString().ToUpperInvariant(),
            ["Name"] = "Old frame", ["SourceWidth"] = 2, ["SourceHeight"] = 2, ["LogicalWidth"] = 4, ["LogicalHeight"] = 4,
            ["ContentOffsetX"] = 1, ["ContentOffsetY"] = 1, ["DurationMs"] = 175,
            ["WorkingData"] = edited ? working.Encode().Data : Array.Empty<byte>(), ["WorkingWidth"] = edited ? contentWidth : 0, ["WorkingHeight"] = edited ? contentHeight : 0,
            ["WorkingState"] = edited ? "edited" : "none", ["PreviewData"] = source.Encode().Data,
            ["CreatedAt"] = DateTime.UtcNow, ["UpdatedAt"] = DateTime.UtcNow,
        };
        var columns = new List<(string Name, string Type, bool Required, string? Default)>();
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(Frames)";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0, reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        await using (var insert = connection.CreateCommand())
        {
            var supplied = columns.Where(c => values.ContainsKey(c.Name) || c.Required && c.Default is null).ToList();
            insert.CommandText = $"INSERT INTO Frames ({string.Join(',', supplied.Select(c => $"\"{c.Name}\""))}) VALUES ({string.Join(',', supplied.Select((_, i) => $"$p{i}"))})";
            for (var i = 0; i < supplied.Count; i++)
            {
                var c = supplied[i]; object fallback = c.Type == "INTEGER" ? 0 : c.Type == "BLOB" ? Array.Empty<byte>() : "";
                insert.Parameters.AddWithValue($"$p{i}", values.GetValueOrDefault(c.Name) ?? fallback);
            }
            await insert.ExecuteNonQueryAsync();
        }
        var mask = new ImageMask { ProjectId = project.Id, AssetId = asset.Id, Label = "Mask", OwnerKind = "frame", OwnerId = frameId, Width = 4, Height = 4, Data = source.Encode().Data };
        db.ImageMasks.Add(mask); await db.SaveChangesAsync();
        var sessionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SpriteEditSessions (Id, ProjectId, TargetFrameSetId, TargetFrameId, TargetSourceAssetId, Prompt, Count, ModalOpen, PreviewOverlayActive, CreatedAt, UpdatedAt) VALUES ({sessionId}, {project.Id}, {set.Id}, {frameId}, {asset.Id}, {"Preserve this draft provenance"}, 1, 1, 0, {DateTime.UtcNow}, {DateTime.UtcNow})");
        db.ChangeTracker.Clear();
        await DatabaseMigrationBootstrapper.MigrateAsync(db);
        var service = new SpriteDocumentService(db);
        var snapshot = await service.ReadAsync(project.Id, set.Id);
        Assert.Equal(frameId, snapshot.Document.Frames.Single().Id);
        Assert.Equal(new SpritePoint(1, 1), snapshot.Document.Frames[0].ImportOffset);
        Assert.Equal(175, snapshot.Document.Frames[0].DurationMs);
        Assert.Equal("painted", snapshot.Document.Specification.ArtMode);
        var rendered = SpriteRaster.Decode(await service.RenderAsync(project.Id, set.Id, frameId));
        var whole = edited && contentWidth == 4 && contentHeight == 4;
        var expected = SpriteRaster.Blank(4, 4);
        var content = edited ? working : source;
        for (var y = 0; y < (edited ? contentHeight : 2); y++) for (var x = 0; x < (edited ? contentWidth : 2); x++) expected.Put(x + (whole ? 0 : 1), y + (whole ? 0 : 1), content.Get(x, y));
        Assert.Equal(expected.Pixels, rendered.Pixels);
        Assert.Equal(mask.Id, (await db.ImageMasks.SingleAsync()).Id);
        Assert.Contains("Preserve this draft provenance", (await db.SpriteAssessments.SingleAsync(a => a.Kind == "imported-session-provenance")).ResultJson);
        Assert.Contains("importedEditSessions", (await db.ArtAssets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).SourceMetadataJson);
        Assert.Single(await service.ListHistoryAsync(project.Id, set.Id));
        await DatabaseMigrationBootstrapper.MigrateAsync(db);
        Assert.Single(await service.ListHistoryAsync(project.Id, set.Id));
    }
}
