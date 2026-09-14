using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class SpriteDocumentTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private SpriteDocumentService _service = null!;
    private Guid _projectId;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = NewContext();
        await _db.Database.EnsureCreatedAsync();
        var project = new Project { Name = "Fixture" }; _db.Projects.Add(project); await _db.SaveChangesAsync();
        _projectId = project.Id; _service = new(_db);
    }
    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, NullLogger<AppDbContext>.Instance);
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
    private static JsonElement Op(object value) => JsonSerializer.SerializeToElement(value, SpriteDocument.JsonOptions);
    private Task<SpriteSnapshot> Create() => _service.CreateAsync(_projectId, "Sprite", 8, 8, "pixel");
    private Task<SpriteCommit> Apply(SpriteSnapshot s, params object[] ops) => _service.ApplyAsync(_projectId, new(s.DocumentId, s.Revision, "Test", ops.Select(Op).ToList()));
    private async Task<SpriteRaster> Render(SpriteSnapshot s) => SpriteRaster.Decode(await _service.RenderAsync(_projectId, s.DocumentId, s.Document.Frames[0].Id));

    [Fact]
    public async Task DrawingAndFillArePixelExactAndClipToCanvas()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Apply(s, new { op = "rectangle", frameId = f, layerId = l, x = 1, y = 1, width = 6, height = 6, color = "#ff0000ff" },
            new { op = "fill", frameId = f, layerId = l, x = 3, y = 3, color = "#00ff00ff" },
            new { op = "line", frameId = f, layerId = l, x = -3, y = 0, x2 = 12, y2 = 0, color = "#0000ffff" });
        var result = await Render(s);
        Assert.Equal(new Rgba32(0, 255, 0, 255), result.Get(3, 3));
        Assert.Equal(new Rgba32(255, 0, 0, 255), result.Get(1, 1));
        Assert.Equal(new Rgba32(0, 0, 255, 255), result.Get(7, 0));
        Assert.Equal((byte)0, result.Get(0, 7).A);
    }

    [Fact]
    public async Task FailedBatchCommitsNeitherPixelsNorHistory()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s,
            new { op = "pencil", frameId = f, layerId = l, x = 1, y = 1, color = "#ff0000ff" }, new { op = "unknown" }));
        Assert.Equal(0, (await _service.ReadAsync(_projectId, s.DocumentId)).Revision);
        Assert.Single(await _service.ListHistoryAsync(_projectId, s.DocumentId));
        Assert.All((await Render(s)).Pixels, b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public async Task StaleRevisionCannotOverwriteManualPixels()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Apply(s, new { op = "pencil", frameId = f, layerId = l, x = 2, y = 3, color = "#ff0000ff" });
        await Assert.ThrowsAsync<SpriteConflictException>(() => Apply(s, new { op = "clear", frameId = f, layerId = l }));
        Assert.Equal((byte)255, (await Render(s)).Get(2, 3).R);
    }

    [Fact]
    public async Task HistorySurvivesContextRestartAndRedoRestoresPixels()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Apply(s, new { op = "pencil", frameId = f, layerId = l, x = 2, y = 3, color = "#ff0000ff" });
        await _db.DisposeAsync(); _db = NewContext(); _service = new(_db);
        await _service.HistoryAsync(_projectId, s.DocumentId, 1, "undo");
        Assert.Equal((byte)0, (await Render(s)).Get(2, 3).A);
        await _service.HistoryAsync(_projectId, s.DocumentId, 2, "redo");
        Assert.Equal((byte)255, (await Render(s)).Get(2, 3).R);
        Assert.Equal(3, (await _service.ReadAsync(_projectId, s.DocumentId)).Revision);
    }

    [Fact]
    public async Task TaskUndoStopsAtInterleavedManualWork()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id;
        async Task Set(int duration, string source, string? task)
        {
            var current = await _service.ReadAsync(_projectId, s.DocumentId);
            await _service.ApplyAsync(_projectId, new(s.DocumentId, current.Revision, "Duration", [Op(new { op = "setDuration", frameId = f, durationMs = duration })], source, task));
        }
        await Set(80, "agent", "walk"); await Set(90, "agent", "walk"); await Set(120, "user", null); await Set(150, "agent", "walk");
        await _service.HistoryAsync(_projectId, s.DocumentId, 4, "undo", true);
        Assert.Equal(120, (await _service.ReadAsync(_projectId, s.DocumentId)).Document.Frames[0].DurationMs);
    }

    [Fact]
    public async Task ReorderAndDeletionUndoPreserveFrameIdentity()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var second = Guid.NewGuid();
        await Apply(s, new { op = "duplicateFrame", frameId = f, id = second });
        s = await _service.ReadAsync(_projectId, s.DocumentId);
        await Apply(s, new { op = "reorderFrame", frameId = second, index = 0 }, new { op = "deleteFrame", frameId = f });
        await _service.HistoryAsync(_projectId, s.DocumentId, 2, "undo");
        var restored = await _service.ReadAsync(_projectId, s.DocumentId);
        Assert.Equal(new[] { f, second }, restored.Document.Frames.Select(x => x.Id));
        Assert.Equal(2, await _db.Frames.CountAsync());
    }

    [Fact]
    public async Task SelectionPreservesUnselectedPixelsAndLayerLockRejectsEdits()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Apply(s, new { op = "select", frameId = f, x = 2, y = 2, width = 2, height = 2 },
            new { op = "rectangle", frameId = f, layerId = l, x = 0, y = 0, width = 8, height = 8, filled = true, color = "#ff0000ff" });
        var result = await Render(s);
        Assert.Equal(4, Enumerable.Range(0, 64).Count(i => result.Pixels[i * 4 + 3] == 255));
        s = await _service.ReadAsync(_projectId, s.DocumentId);
        await Apply(s, new { op = "setLayer", layerId = l, locked = true });
        s = await _service.ReadAsync(_projectId, s.DocumentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s, new { op = "clear", frameId = f, layerId = l }));
    }

    [Fact]
    public async Task DuplicateCelsReuseContentAndCompositingUsesAlpha()
    {
        var s = await _service.CreateAsync(_projectId, "Painted", 8, 8, "painted"); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id; var top = Guid.NewGuid();
        await Apply(s, new { op = "pencil", frameId = f, layerId = l, x = 0, y = 0, color = "#ff0000ff" },
            new { op = "duplicateFrame", frameId = f }, new { op = "addLayer", id = top },
            new { op = "pencil", frameId = f, layerId = top, x = 0, y = 0, color = "#0000ff80" });
        var pixel = (await Render(s)).Get(0, 0);
        Assert.Equal(new Rgba32(127, 0, 128, 255), pixel);
        Assert.Equal(2, await _db.SpriteBitmaps.CountAsync());
    }

    [Fact]
    public async Task PaletteAndBinaryAlphaAreEnforcedWithoutSilentConversion()
    {
        var s = await Create(); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s, new { op = "pencil", frameId = f, layerId = l, x = 0, y = 0, color = "#ff000080" }));
        s.Document.Specification.Palette = ["#ff0000ff"]; s.Document.Specification.EnforcePalette = true;
        await Apply(s, new { op = "setSpecification", specification = s.Document.Specification });
        s = await _service.ReadAsync(_projectId, s.DocumentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s, new { op = "pencil", frameId = f, layerId = l, x = 0, y = 0, color = "#00ff00ff" }));
        await Apply(s, new { op = "clear", frameId = f, layerId = l });
    }

    [Fact]
    public async Task CancellationDoesNotAdvanceRevision()
    {
        var s = await Create(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ApplyAsync(_projectId, new(s.DocumentId, 0, "Cancelled", [Op(new { op = "addLayer" })]), cancel.Token));
        Assert.Equal(0, (await _service.ReadAsync(_projectId, s.DocumentId)).Revision);
    }

    [Fact]
    public async Task VisibleColorSelectionRemainsFixedAcrossLayerEditsAndCutPaste()
    {
        var s = await _service.CreateAsync(_projectId, "Blend selection", 8, 8, "painted"); var f = s.Document.Frames[0].Id; var bottom = s.Document.Layers[0].Id; var top = Guid.NewGuid();
        await Apply(s, new { op = "pencil", frameId = f, layerId = bottom, x = 2, y = 2, color = "#ff0000ff" },
            new { op = "addLayer", id = top }, new { op = "pencil", frameId = f, layerId = top, x = 2, y = 2, color = "#0000ff80" },
            new { op = "select", frameId = f, x = 0, y = 0, width = 8, height = 8, color = "#7f0080ff" },
            new { op = "rectangle", frameId = f, layerId = top, x = 0, y = 0, width = 8, height = 8, filled = true, color = "#00ff00ff" },
            new { op = "cut", frameId = f, layerId = top }, new { op = "clearSelection" }, new { op = "paste", frameId = f, layerId = top, x = 3, y = 3 });
        var result = await Render(s);
        Assert.Equal(new Rgba32(255,0,0,255), result.Get(2,2));
        Assert.Equal(new Rgba32(0,255,0,255), result.Get(5,5));
        Assert.Equal((byte)0, result.Get(0,0).A);
    }
}
