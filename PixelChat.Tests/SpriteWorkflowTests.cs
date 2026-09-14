using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PixelChat.Art;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class SpriteWorkflowTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    private SpriteDocumentService _documents = null!;
    private Guid _project;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync(); _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, NullLogger<AppDbContext>.Instance);
        await _db.Database.EnsureCreatedAsync(); var p = new Project { Name = "Workflow" }; _db.Projects.Add(p); await _db.SaveChangesAsync(); _project = p.Id; _documents = new(_db);
    }
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
    private static JsonElement Op(object value) => JsonSerializer.SerializeToElement(value, SpriteDocument.JsonOptions);
    private SpriteGenerationService Generation() => new(_db, _documents, null!, new ImageEditCanvasService(), new EditCanvasPreparationStore(), new FixtureProvider(), new(_db, _documents));

    [Fact]
    public async Task ValidationIncludesFrame32AndPreservesIntentionalJumpAndSquash()
    {
        var doc = new SpriteDocument { Name = "Jump", Specification = new() { Width = 32, Height = 32, ArtMode = "pixel", BinaryAlpha = true, MotionRequirements = "Jump and squash/stretch intentionally." }, Layers = [new()] };
        var bitmaps = new List<SpriteBitmap>();
        for (var i = 0; i < 32; i++)
        {
            var raster = SpriteRaster.Blank(32, 32);
            var height = i == 15 ? 2 : 8; var y0 = i == 15 ? 4 : 20;
            for (var y = y0; y < y0 + height; y++) for (var x = 8; x < 16; x++) raster.Put(x, y, new(220,90,30,255));
            if (i == 31) raster.Put(0, 0, new(12,34,56,128));
            var bitmap = raster.Encode(); bitmaps.Add(bitmap); doc.Frames.Add(new() { Name = $"Frame {i + 1}", Width = 32, Height = 32, DurationMs = i == 15 ? 200 : 80, Cels = new() { [doc.Layers[0].Id] = bitmap.Hash } });
        }
        doc.Clips.Add(new() { Name = "Jump", FrameIds = doc.Frames.Select(f => f.Id).ToList(), Loop = false, Direction = "reverse" });
        var s = await _documents.ImportAsync(_project, null, doc, bitmaps);
        var validation = new SpriteValidationService(_db, _documents);
        var result = await validation.ValidateAsync(_project, s.DocumentId, 0);
        Assert.Equal(32, result.Frames.Count);
        Assert.Contains(result.Findings, f => f.Code == "binary-alpha" && f.FrameIds.Contains(doc.Frames[31].Id));
        Assert.Contains(result.Findings, f => f.Kind == "heuristic" && f.Code == "centroid-motion");
        Assert.Contains(result.Findings, f => f.Kind == "heuristic" && f.Code == "silhouette-change");
        Assert.DoesNotContain(result.Findings, f => f.Code == "loop-seam");
        Assert.Equal(doc.Frames[^1].Id, result.Clips[0].Sequence[0]); Assert.Equal(31 * 80 + 200, result.Clips[0].DurationMs);
        Assert.Equal(doc.Serialize(), (await _documents.ReadAsync(_project, s.DocumentId)).Document.Serialize());
        var judgment = await validation.ValidateAsync(_project, s.DocumentId, 0, judgments: [new("pass", "contact", "Right contact phase needs visual review.", [doc.Frames[10].Id])]);
        Assert.Contains(judgment.Findings, f => f.Kind == "judgment");
        Assert.Equal(3, (await validation.HistoryAsync(_project, s.DocumentId, 0)).Count);
    }

    [Fact]
    public async Task DelayedAiCandidateCannotOverwriteManualRevisionAndRawBytesRemain()
    {
        var s = await _documents.CreateAsync(_project, "Candidate", 8, 8, "painted"); var frame = s.Document.Frames[0].Id; var layer = s.Document.Layers[0].Id;
        var candidate = await Candidate(s, new(0,0,255,180));
        await _documents.ApplyAsync(_project, new(s.DocumentId, 0, "Manual", [Op(new { op = "pencil", frameId = frame, layerId = layer, x = 2, y = 3, color = "#ff0000ff" })]));
        await Assert.ThrowsAsync<SpriteConflictException>(() => Generation().ApplyAsync(_project, candidate.SourceBatchId!.Value, candidate.Id));
        Assert.Equal(new Rgba32(255,0,0,255), SpriteRaster.Decode(await _documents.RenderAsync(_project, s.DocumentId, frame)).Get(2,3));
        Assert.Equal(candidate.RawProviderData, (await _db.ArtAssets.AsNoTracking().SingleAsync(a => a.Id == candidate.Id)).RawProviderData);
    }

    [Fact]
    public async Task CandidateApplicationIsUndoableAndDoesNotPasteBackMaskedPixels()
    {
        var s = await _documents.CreateAsync(_project, "Candidate", 8, 8, "painted"); var f = s.Document.Frames[0].Id; var l = s.Document.Layers[0].Id;
        await _documents.ApplyAsync(_project, new(s.DocumentId, 0, "Select", [Op(new { op = "select", frameId = f, x = 0, y = 0, width = 1, height = 1 })]));
        s = await _documents.ReadAsync(_project, s.DocumentId); var candidate = await Candidate(s, new(0,0,255,180));
        await Generation().ApplyAsync(_project, candidate.SourceBatchId!.Value, candidate.Id);
        Assert.Equal(new Rgba32(0,0,255,180), SpriteRaster.Decode(await _documents.RenderAsync(_project, s.DocumentId, f)).Get(7,7));
        Assert.Contains((await _documents.ReadAsync(_project, s.DocumentId)).Document.Provenance.Keys, k => k.StartsWith("ai:"));
        await _documents.HistoryAsync(_project, s.DocumentId, 2, "undo");
        Assert.Equal((byte)0, SpriteRaster.Decode(await _documents.RenderAsync(_project, s.DocumentId, f)).Get(7,7).A);
        Assert.NotNull((await _documents.ReadAsync(_project, s.DocumentId)).Document.Selection);
    }

    [Fact]
    public async Task PreparationIsBoundToNativeLayerAndRevisionAndProducesActualImages()
    {
        var s = await _documents.CreateAsync(_project, "Blank", 8, 8, "painted");
        var preparations = new EditCanvasPreparationStore(); var inspections = new SpriteInspectionService(_db, _documents);
        var generation = new SpriteGenerationService(_db, _documents, null!, new ImageEditCanvasService(), preparations, new FixtureProvider(), inspections);
        var request = new SpriteGenerationRequest(s.DocumentId, 0, s.Document.Frames[0].Id, s.Document.Layers[0].Id, "Add a hat", CanvasOptions: new(2,0,0,1));
        var result = JsonSerializer.SerializeToElement(await generation.PrepareAsync(_project, request), SpriteDocument.JsonOptions);
        var id = result.GetProperty("canvasPreparationId").GetGuid(); Assert.True(preparations.TryPeek(_project, id, out var preparation));
        Assert.Equal(0, preparation.TargetRevisionTicks); Assert.Equal(s.DocumentId, preparation.ParentTargetId);
        Assert.Equal(9, preparation.Canvas.Transform.LogicalWidth); Assert.Equal(10, preparation.Canvas.Transform.LogicalHeight);
        Assert.False(preparations.TryGet(_project, id, SpriteGenerationService.TargetKind(request.LayerId), request.FrameId, request.DocumentId, 1, out _, out _));
        Assert.Equal(2, result.GetProperty("artifacts").GetArrayLength());
    }

    private async Task<ArtAsset> Candidate(SpriteSnapshot s, Rgba32 color)
    {
        var raster = SpriteRaster.Blank(8,8); for (var y=0;y<8;y++) for(var x=0;x<8;x++) raster.Put(x,y,color);
        var png = raster.Encode().Data;
        var batch = new GenerationBatch { ProjectId = _project, Status = GenerationBatchStatus.Succeeded, Count = 1,
            SpriteTargetJson = JsonSerializer.Serialize(new SpriteGenerationTarget(s.DocumentId, s.Revision, s.Document.Frames[0].Id, s.Document.Layers[0].Id, "edit", [], "task", "", 0, 0), SpriteDocument.JsonOptions) };
        var asset = new ArtAsset { ProjectId = _project, Label = "Candidate", ContentType = "image/png", Data = png, RawProviderData = png, RawProviderContentType = "image/png", Width = 8, Height = 8, SourceBatchId = batch.Id };
        _db.GenerationBatches.Add(batch); _db.ArtAssets.Add(asset); await _db.SaveChangesAsync(); return asset;
    }

    [Fact]
    public async Task NativeProviderPipelineUsesCapturedPixelsAndRetainsRawOutputWithoutSourceAsset()
    {
        var s = await _documents.CreateAsync(_project, "Provider fixture", 8, 8, "painted");
        var preparations = new EditCanvasPreparationStore(); var provider = new FixtureProvider();
        var generation = new SpriteGenerationService(_db, _documents, null!, new ImageEditCanvasService(), preparations, provider, new(_db, _documents));
        var request = new SpriteGenerationRequest(s.DocumentId, 0, s.Document.Frames[0].Id, s.Document.Layers[0].Id, "Paint blue");
        var prepared = JsonSerializer.SerializeToElement(await generation.PrepareAsync(_project, request), SpriteDocument.JsonOptions);
        using var services = new ServiceCollection().AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, NullLogger<AppDbContext>.Instance)).BuildServiceProvider();
        var options = Options.Create(new ImageGenerationOptions());
        var selection = new ImageModelSelectionService(services.GetRequiredService<IServiceScopeFactory>(), options);
        var workflow = new ArtWorkflowService(_db, provider, selection, new ImageEditCanvasService(), preparations, _documents, null!, options, Options.Create(new SpriteAnimationOptions()), null!, NullLogger<ArtWorkflowService>.Instance);
        var batch = await workflow.StartSpriteGenerationAsync(_project, request with { CanvasPreparationId = prepared.GetProperty("canvasPreparationId").GetGuid() });
        Assert.Empty(await _db.ArtAssets.ToListAsync());
        var output = await workflow.GenerateEditBatchOutputAsync(_project, batch.Id, 0);
        var asset = await _db.ArtAssets.SingleAsync(a => a.Id == output.Id);
        Assert.Equal(provider.Output, asset.RawProviderData);
        Assert.Null(asset.ParentAssetId);
        Assert.Equal("gpt-image-2.5-sunburst", provider.LastRequest!.ImageModel);
        Assert.Equal(0, (await _documents.ReadAsync(_project, s.DocumentId)).Revision);
        await generation.ApplyAsync(_project, batch.Id, asset.Id);
        Assert.Equal(new Rgba32(0,0,255,180), SpriteRaster.Decode(await _documents.RenderAsync(_project, s.DocumentId, request.FrameId)).Get(3,3));
    }

    private sealed class FixtureProvider : IImageProvider
    {
        public byte[]? Output { get; private set; }
        public ImageProviderEditRequest? LastRequest { get; private set; }
        public ImageProviderCapabilities DescribeCapabilities() => new(true, true, 4, [], ["png"], new(1, 8192, 1, 16_777_216, 100));
        public Task<ImageProviderResult> GenerateAsync(ImageProviderGenerateRequest request, CancellationToken cancellationToken = default, IProgress<ImageProviderProgress>? progress = null) => throw new NotSupportedException();
        public Task<ImageProviderResult> EditAsync(ImageProviderEditRequest request, CancellationToken cancellationToken = default, IProgress<ImageProviderProgress>? progress = null)
        {
            LastRequest = request;
            var raster = SpriteRaster.Decode(request.SourceImage.Data);
            for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) raster.Put(x,y,new(0,0,255,180));
            Output = raster.Encode().Data;
            return Task.FromResult(new ImageProviderResult([new(Output,"image/png","png",null,"fixture-response","fixture-call")], "fixture", request.MainlineModel!, request.ImageModel!, "{}"));
        }
    }
}
