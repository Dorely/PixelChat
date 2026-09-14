using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PixelChat.Art;
using PixelChat.Models;
using PixelChat.Persistence;
using PixelChat.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class SpriteExportTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!; private SpriteDocumentService _documents = null!; private SpriteExportService _exports = null!; private Guid _project;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync(); _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, NullLogger<AppDbContext>.Instance);
        await _db.Database.EnsureCreatedAsync(); var p = new Project { Name = "Export" }; _db.Projects.Add(p); await _db.SaveChangesAsync(); _project = p.Id; _documents = new(_db); _exports = new(_db,_documents);
    }
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
    private async Task<SpriteSnapshot> Fixture()
    {
        var doc = new SpriteDocument { Name = "Reconstruction", Layers = [new() { Name = "Body" },new() { Name = "Accessory" }], Specification = new() { Width = 8, Height = 8, ArtMode = "painted" }, Slices = [new("Hitbox",new(1,2,4,5))] };
        var bitmaps = new List<SpriteBitmap>();
        for (var i=0;i<3;i++)
        {
            var raster = SpriteRaster.Blank(8-i,8); raster.Put(1,1,new(255,80,20,180)); raster.Put(2,2,new(12,34,56,0));
            var body = raster.Encode(); bitmaps.Add(body); var accessory = SpriteRaster.Blank(8-i,8); accessory.Put(1,1,new(20,80,255,128)); var extra = accessory.Encode(); bitmaps.Add(extra);
            doc.Frames.Add(new() { Name=$"Frame {i}",Width=8-i,Height=8,DurationMs=80+i*30,Cels=new() {[doc.Layers[0].Id]=body.Hash,[doc.Layers[1].Id]=extra.Hash},Pivots=new() {["hand"]=new(3,4)} });
        }
        doc.Clips.Add(new() { Name="Action",FrameIds=doc.Frames.Select(f=>f.Id).ToList(),Direction="pingpong",Loop=false });
        doc.Provenance["fixture"]="Two layers, partial alpha, hidden RGB, variable widths, timing and pivots";
        return await _documents.ImportAsync(_project,null,doc,bitmaps);
    }

    [Theory]
    [InlineData("bundle")]
    [InlineData("atlas")]
    [InlineData("frames")]
    public async Task ExportReimportReconstructsPixelsTimingPivotsSlicesAndClips(string format)
    {
        var s=await Fixture(); var spec=new SpriteExportSpec(format,2,2,3,4);
        var result=await _exports.ExportAsync(_project,s.DocumentId,0,spec);
        Assert.Equal(result.Id,(await _exports.ExportAsync(_project,s.DocumentId,0,spec)).Id);
        var bytes=(await _exports.ReadAsync(_project,result.Id)).Data;
        var decoded=SpriteExportService.DecodeBundle(bytes);
        if(format=="bundle") { Assert.Equal(s.Document.Serialize(),decoded.Document.Serialize()); Assert.Equal(2,decoded.Document.Layers.Count); }
        var imported=await _exports.ImportAsync(_project,bytes);
        Assert.NotEqual(s.DocumentId,imported.Snapshot.DocumentId);
        foreach(var frame in s.Document.Frames)
        {
            Assert.Equal(SpriteRaster.Decode(await _documents.RenderAsync(_project,s.DocumentId,frame.Id)).Pixels,
                SpriteRaster.Decode(await _documents.RenderAsync(_project,imported.Snapshot.DocumentId,imported.FrameIdMap[frame.Id])).Pixels);
            var restored=imported.Snapshot.Document.Frames.Single(f=>f.Id==imported.FrameIdMap[frame.Id]);
            Assert.Equal(frame.DurationMs,restored.DurationMs); Assert.Equal(frame.Pivots["hand"],restored.Pivots["hand"]);
        }
        Assert.Equal("pingpong", imported.Snapshot.Document.Clips[0].Direction); Assert.False(imported.Snapshot.Document.Clips[0].Loop);
        Assert.Equal(s.Document.Slices,imported.Snapshot.Document.Slices);
    }

    [Fact]
    public async Task PaddingGutterAndMarginAffectPixelsAndBothBuildPathsUseIdenticalGeometry()
    {
        var s=await Fixture(); var bits=await _documents.LoadBitmapsAsync(s.Document); var packing=SpriteAtlasBuilder.Build(s,bits,new("atlas",2,2,3,4));
        Assert.NotNull(packing.Bitmap); Assert.Equal(35,packing.Bitmap.Width); Assert.Equal(35,packing.Bitmap.Height);
        Assert.Equal(new SpriteRect(6,6,8,8),packing.Manifest.Frames[0].Rect);
        Assert.Equal(new SpriteRect(21,6,7,8),packing.Manifest.Frames[1].Rect);
        var raster=SpriteRaster.Decode(packing.Bitmap.Data); Assert.Equal((byte)0,raster.Get(4,4).A); Assert.Equal((byte)0,raster.Get(16,8).A);
        var setService=new FrameSetService(_db,_documents);
        var built=await setService.BuildSheetAsync(_project,new(s.DocumentId,Columns:2,Padding:2,Gutter:3,OuterMargin:4,HorizontalAnchor:"left",VerticalAnchor:"top"));
        Assert.Equal(packing.Bitmap.Data,(await _db.ArtAssets.SingleAsync(a=>a.Id==built.OutputAssetId)).Data);
        Assert.Contains("durationMs",built.ManifestJson);
    }

    [Fact]
    public async Task AnimatedPreviewUsesClipDirectionUnequalDurationsAndOneShotPolicy()
    {
        var s=await Fixture(); var result=await _exports.ExportAsync(_project,s.DocumentId,0,new("preview",Clip:"Action"));
        using var gif=Image.Load<Rgba32>((await _exports.ReadAsync(_project,result.Id)).Data);
        Assert.Equal(4,gif.Frames.Count); Assert.Equal((ushort)1,gif.Metadata.GetGifMetadata().RepeatCount);
        Assert.Equal(new[]{8,11,14,11},gif.Frames.Select(f=>f.Metadata.GetGifMetadata().FrameDelay));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task TamperedBundleFailsWithoutCreatingDocument()
    {
        var s=await Fixture(); var result=await _exports.ExportAsync(_project,s.DocumentId,0,new()); var original=(await _exports.ReadAsync(_project,result.Id)).Data;
        using var input=new ZipArchive(new MemoryStream(original),ZipArchiveMode.Read); using var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) foreach(var entry in input.Entries)
        {
            using var output=zip.CreateEntry(entry.FullName).Open();
            if(entry.FullName.StartsWith("bitmaps/")) output.Write(Encoding.UTF8.GetBytes("tampered")); else { using var bytes=entry.Open(); bytes.CopyTo(output); }
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>_exports.ImportAsync(_project,stream.ToArray()));
        Assert.Equal(1,await _db.FrameSets.CountAsync());
    }
}
