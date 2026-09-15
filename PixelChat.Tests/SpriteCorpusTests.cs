using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;

namespace PixelChat.Tests;

public sealed class SpriteCorpusTests
{
    [Fact]
    public async Task FixedCorpusExercisesScriptInspectValidateAndReconstruct()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"sprite-corpus.js"));
        var output = Environment.GetEnvironmentVariable("PIXELCHAT_CORPUS_OUTPUT");
        var results = new List<object>();
        foreach(var kind in new[]{"prop","walk","idle","impact","painted"})
        {
            await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,NullLogger<AppDbContext>.Instance);await db.Database.EnsureCreatedAsync();
            var project=new Project{Name="Corpus"};db.Projects.Add(project);await db.SaveChangesAsync();
            var documents=new SpriteDocumentService(db);var start=await documents.CreateAsync(project.Id,kind,64,64,kind=="painted"?"painted":"pixel");
            var watch=Stopwatch.StartNew();
            await new SpriteScriptService(documents).RunAsync(project.Id,start.DocumentId,0,$"const kind='{kind}';\n"+script,"Corpus drawing","corpus",CancellationToken.None);
            var snapshot=await documents.ReadAsync(project.Id,start.DocumentId);
            var validation=await new SpriteValidationService(db,documents).ValidateAsync(project.Id,start.DocumentId,snapshot.Revision);
            Assert.True(validation.StructurallyValid);Assert.Equal(kind=="prop"?1:8,validation.Frames.Count);
            var exports=new SpriteExportService(db,documents);var bundle=await exports.ExportAsync(project.Id,start.DocumentId,snapshot.Revision,new());
            var decoded=SpriteExportService.DecodeBundle((await exports.ReadAsync(project.Id,bundle.Id)).Data);Assert.Equal(snapshot.Document.Serialize(),decoded.Document.Serialize());
            var inspections=new SpriteInspectionService(db,documents);var rendered=await inspections.RenderAsync(project.Id,new(start.DocumentId,snapshot.Revision,"contact",Scale:2));
            var preview=await exports.ExportAsync(project.Id,start.DocumentId,snapshot.Revision,new("preview"));watch.Stop();
            results.Add(new{kind,workflow="direct script",frames=validation.Frames.Count,operations=(await documents.ListHistoryAsync(project.Id,start.DocumentId))[0].OperationsJson,
                elapsedMs=watch.ElapsedMilliseconds,scriptCalls=1,inspectionCalls=1,validationCalls=1,exportCalls=2,retries=0,interventions=0,
                measuredChecks="passed",artisticAcceptance="requires visual review",imageProviderCalls=0,usage="not available; local deterministic execution",findings=validation.Findings});
            if(output is not null)
            {
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(Path.Combine(output,kind+".png"),(await inspections.ReadArtifactAsync(project.Id,rendered.Artifacts[0].Id,CancellationToken.None)).Data);
                await File.WriteAllBytesAsync(Path.Combine(output,kind+".gif"),(await exports.ReadAsync(project.Id,preview.Id)).Data);
                await File.WriteAllBytesAsync(Path.Combine(output,kind+".pixelchat.zip"),(await exports.ReadAsync(project.Id,bundle.Id)).Data);
            }
        }
        if(output is not null)await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions(SpriteDocument.JsonOptions){WriteIndented=true}));
    }
}
