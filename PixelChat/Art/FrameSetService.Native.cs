using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PixelChat.Models;
using PixelChat.Sprites;

namespace PixelChat.Art;

public sealed partial class FrameSetService
{
    private readonly Dictionary<Guid, SpriteSnapshot> _documents = [];
    private readonly Dictionary<string, SpriteBitmap> _bitmapContent = [];
    private readonly Dictionary<string, SpriteBitmap> _pendingBitmaps = [];

    private SpriteDocument Document(FrameSet set) => _documents[set.Id].Document;
    private SpriteFrame NativeFrame(Frame frame) => _documents[frame.FrameSetId].Document.Frames.Single(f => f.Id == frame.Id);

    private async Task LoadDocumentAsync(FrameSet set, CancellationToken cancellationToken)
    {
        var snapshot = await documents.ReadAsync(set.ProjectId, set.Id, cancellationToken: cancellationToken);
        _documents[set.Id] = snapshot;
        foreach (var bitmap in await documents.LoadBitmapsAsync(snapshot.Document, cancellationToken)) _bitmapContent[bitmap.Key] = bitmap.Value;
    }

    private void StageCell(Frame frame, byte[] rgba, int width, int height)
    {
        var doc = _documents[frame.FrameSetId].Document;
        if (doc.Layers.Count != 1 || doc.Layers[0].Locked)
            throw new InvalidOperationException("This whole-frame operation requires one unlocked layer. Use native layer commands for a layered document.");
        var target = NativeFrame(frame);
        var bitmap = new SpriteRaster(width, height, rgba).Encode();
        _pendingBitmaps[bitmap.Hash] = bitmap; _bitmapContent[bitmap.Hash] = bitmap;
        target.Cels[doc.Layers[0].Id] = bitmap.Hash;
        target.Width = width; target.Height = height;
        frame.LogicalWidth = width; frame.LogicalHeight = height;
    }

    private async Task SaveNativeAsync(FrameSet set, string label, CancellationToken cancellationToken)
    {
        var snapshot = _documents[set.Id];
        await documents.ReplaceAsync(set.ProjectId, set.Id, snapshot.Revision, snapshot.Document, _pendingBitmaps.Values.ToList(), label, cancellationToken: cancellationToken);
        _pendingBitmaps.Clear();
        await LoadDocumentAsync(set, cancellationToken);
    }

    private async Task<FrameSetView> ApplyNativeAsync(Guid projectId, Guid setId, string label, object operation, CancellationToken cancellationToken)
    {
        var snapshot = await documents.ReadAsync(projectId, setId, cancellationToken: cancellationToken);
        await documents.ApplyAsync(projectId, new(setId, snapshot.Revision, label, [JsonSerializer.SerializeToElement(operation, SpriteDocument.JsonOptions)]), cancellationToken);
        return await BuildFrameSetViewAsync(projectId, setId, cancellationToken);
    }

    private SpriteFrame ImportRegion(SpriteRegion region, SpriteDocument document, ArtAsset source)
    {
        var sourcePixels = SpriteRaster.Decode(source.Data);
        var width = Math.Max(document.Specification.Width, region.Width);
        var height = Math.Max(document.Specification.Height, region.Height);
        var raster = SpriteRaster.Blank(width, height);
        // Import keeps source coordinates and does not recenter or quantize artwork.
        for (var y = 0; y < Math.Min(height, region.Height); y++)
            for (var x = 0; x < Math.Min(width, region.Width); x++) raster.Put(x, y, sourcePixels.Get(region.X + x, region.Y + y));
        var bitmap = raster.Encode(); _pendingBitmaps[bitmap.Hash] = bitmap; _bitmapContent[bitmap.Hash] = bitmap;
        return new() { Name = region.Name, Width = width, Height = height, DurationMs = 125, SourceRegionId = region.Id,
            SourceRect = new(region.X, region.Y, region.Width, region.Height), Cels = new() { [document.Layers[0].Id] = bitmap.Hash } };
    }
}
