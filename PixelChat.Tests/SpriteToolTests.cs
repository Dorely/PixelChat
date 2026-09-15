using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class SpriteToolTests
{
    [Fact]
    public async Task AgentBatchReturnsActualRevisionPngAndCachedInspection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, NullLogger<AppDbContext>.Instance);
        await db.Database.EnsureCreatedAsync(); var project = new Project { Name = "Tools" }; db.Projects.Add(project); await db.SaveChangesAsync();
        var documents = new SpriteDocumentService(db); var scripts = new SpriteScriptService(documents); var inspections = new SpriteInspectionService(db, documents);
        var registry = new SpriteToolRegistry(documents, scripts, inspections, null!);
        var snapshot = await documents.CreateAsync(project.Id, "Fixture", 8, 8, "pixel");
        var frame = snapshot.Document.Frames[0].Id; var layer = snapshot.Document.Layers[0].Id;
        var tool = registry.Build(project.Id).OfType<AIFunction>().Single(t => t.Name == "sprite_apply");
        var arguments = new AIFunctionArguments
        {
            ["documentId"] = snapshot.DocumentId, ["expectedRevision"] = 0L, ["label"] = "Draw pixel", ["returnPreview"] = true,
            ["operations"] = new[] { JsonSerializer.SerializeToElement(new { op = "pencil", frameId = frame, layerId = layer, x = 2, y = 3, color = "#ff0000ff" }) },
        };
        var value = await tool.InvokeAsync(arguments);
        var result = value is JsonElement element ? element.GetString()! : Assert.IsType<string>(value);
        var contents = await registry.ImageContentsAsync(project.Id, result, CancellationToken.None);
        var png = Assert.Single(contents.OfType<DataContent>());
        Assert.Equal("image/png", png.MediaType);
        var image = SpriteRaster.Decode(png.Data.ToArray());
        Assert.Equal(new Rgba32(255,0,0,255), image.Get(2, 27));
        Assert.Contains(frame.ToString(), Assert.Single(contents.OfType<TextContent>()).Text);
        var cached = await inspections.RenderAsync(project.Id, new(snapshot.DocumentId, 1, "contact"));
        Assert.Single(await db.SpriteInspections.ToListAsync());
        Assert.Single(await inspections.ListAsync(project.Id, snapshot.DocumentId, 1));
        var skipped = JsonSerializer.Serialize(new { artifacts = new[] { new { id = cached.Artifacts[0].Id, sendImage = false } } });
        Assert.Empty(await registry.ImageContentsAsync(project.Id, skipped, CancellationToken.None));
        // A known raw artifact must still be sent when inspecting a different background.
        var renderTool = registry.Build(project.Id).OfType<AIFunction>().Single(t => t.Name == "sprite_render");
        var backgroundResult = await renderTool.InvokeAsync(new AIFunctionArguments
        {
            ["documentId"] = snapshot.DocumentId, ["revision"] = 1L, ["kind"] = "contact",
            ["knownArtifacts"] = new[] { cached.Artifacts[0].Id }, ["backgroundColor"] = "#FF00FF",
        });
        var backgroundJson = backgroundResult is JsonElement e ? e.GetString()! : (string)backgroundResult!;
        var backgroundContents = await registry.ImageContentsAsync(project.Id, backgroundJson, CancellationToken.None);
        var backgroundImage = SpriteRaster.Decode(Assert.Single(backgroundContents.OfType<DataContent>()).Data.ToArray());
        Assert.Equal(new Rgba32(255, 0, 255, 255), backgroundImage.Get(0, 24));
        Assert.True(ModelImageInspectionTests.Metadata(backgroundContents).GetProperty("hasTransparency").GetBoolean());
        Assert.Single(await db.SpriteInspections.ToListAsync());
        Assert.Contains("sprite.apply", SpriteToolRegistry.Help("scripting"));
        Assert.Empty(await registry.ImageContentsAsync(project.Id, SpriteToolRegistry.Help("commands"), CancellationToken.None));
    }

    [Fact]
    public async Task ScriptFailureAfterQueuedDrawingCommitsNothing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, NullLogger<AppDbContext>.Instance);
        await db.Database.EnsureCreatedAsync(); var project = new Project { Name = "Script failure" }; db.Projects.Add(project); await db.SaveChangesAsync();
        var documents = new SpriteDocumentService(db); var snapshot = await documents.CreateAsync(project.Id, "Fixture", 8, 8, "pixel");
        var scripts = new SpriteScriptService(documents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scripts.RunAsync(project.Id, snapshot.DocumentId, 0,
            "sprite.apply({op:'addLayer',name:'must not commit'}); throw new Error('fail after draw');", "Failure", "test", CancellationToken.None));
        Assert.Equal(0, (await documents.ReadAsync(project.Id, snapshot.DocumentId)).Revision);
        Assert.Single(await documents.ListHistoryAsync(project.Id, snapshot.DocumentId));
    }
}
