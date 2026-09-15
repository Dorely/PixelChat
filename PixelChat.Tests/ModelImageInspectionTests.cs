using System.Text.Json;
using Microsoft.Extensions.AI;
using PixelChat.Art;
using PixelChat.Chat;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Tests;

public sealed class ModelImageInspectionTests
{
    [Fact]
    public void HiddenGreenRgbCannotAppearInCompositeAndSourceIsUnchanged()
    {
        using var source = new Image<Rgba32>(3, 1);
        source[0, 0] = new(53, 114, 16, 0);
        source[1, 0] = new(0, 255, 0, 128);
        source[2, 0] = new(10, 20, 30, 255);
        using var stream = new MemoryStream(); source.SaveAsPng(stream);
        var bytes = stream.ToArray(); var original = bytes.ToArray();
        var contents = ModelImageInspection.Create(bytes, "green.png", "#ff00ff");
        var metadata = Metadata(contents);
        Assert.True(ModelImageInspection.TryDescribe(Assert.Single(contents.OfType<TextContent>()).Text, out var description));
        Assert.Contains("Source alpha: 1 fully transparent, 1 partially transparent, and 1 opaque pixels", description);
        Assert.Equal("Inspection background #FF00FF · Source alpha: 1 fully transparent, 1 partially transparent, and 1 opaque pixels.", description);
        Assert.DoesNotContain("sourceSha256", description);
        Assert.True(metadata.GetProperty("hasTransparency").GetBoolean());
        Assert.Equal(1, metadata.GetProperty("fullyTransparentPixels").GetInt32());
        Assert.Equal(1, metadata.GetProperty("partiallyTransparentPixels").GetInt32());
        Assert.Equal(1, metadata.GetProperty("opaquePixels").GetInt32());
        Assert.Equal(0, metadata.GetProperty("minAlpha").GetInt32());
        Assert.Equal(255, metadata.GetProperty("maxAlpha").GetInt32());
        Assert.Equal("#FF00FF", metadata.GetProperty("inspectionBackground").GetString());
        using var preview = Image.Load<Rgba32>(Assert.Single(contents.OfType<DataContent>()).Data.Span);
        Assert.Equal(new Rgba32(255, 0, 255, 255), preview[0, 0]);
        Assert.Equal(new Rgba32(127, 128, 127, 255), preview[1, 0]);
        Assert.Equal(source[2, 0], preview[2, 0]);
        Assert.Equal(original, bytes);
        var white = ModelImageInspection.FromDataUrl(DataUrl.ToDataUrl("image/png", bytes), "green.png", "#FFFFFF");
        Assert.Equal(metadata.GetProperty("sourceSha256").GetString(), Metadata(white).GetProperty("sourceSha256").GetString());
        using var whitePreview = Image.Load<Rgba32>(Assert.Single(white.OfType<DataContent>()).Data.Span);
        Assert.Equal(new Rgba32(255, 255, 255, 255), whitePreview[0, 0]);
    }

    [Fact]
    public void OpaqueJpegIsNotReportedAsTransparent()
    {
        using var source = new Image<Rgba32>(4, 3, new Rgba32(12, 100, 23, 255));
        using var stream = new MemoryStream(); source.SaveAsJpeg(stream);
        var metadata = Metadata(ModelImageInspection.Create(stream.ToArray(), "opaque.jpg"));
        Assert.False(metadata.GetProperty("hasTransparency").GetBoolean());
        Assert.Equal(12, metadata.GetProperty("opaquePixels").GetInt32());
        Assert.Equal(255, metadata.GetProperty("minAlpha").GetInt32());
        Assert.Equal("#808080", metadata.GetProperty("inspectionBackground").GetString());
    }

    [Fact]
    public void AnimatedInputIdentifiesMeasurementScope()
    {
        using var source = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        source.Frames.AddFrame(source.Frames.RootFrame);
        using var stream = new MemoryStream(); source.SaveAsGif(stream);
        var metadata = Metadata(ModelImageInspection.Create(stream.ToArray(), "animated.gif"));
        Assert.Equal(2, metadata.GetProperty("sourceFrameCount").GetInt32());
        Assert.Equal(0, metadata.GetProperty("measuredFrameIndex").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("magenta")]
    [InlineData("#FFF")]
    [InlineData("#FF00FF00")]
    [InlineData("#GG00FF")]
    [InlineData(" #FF00FF")]
    [InlineData("# FFFFF")]
    [InlineData("#FFFFF ")]
    public void RejectsNonOpaqueOrInvalidBackgrounds(string background) =>
        Assert.Throws<ArgumentException>(() => ModelImageInspection.Background(background));

    [Theory]
    [InlineData("Older source-backed asset preview")]
    [InlineData("Measured SOURCE alpha, before compositing: {}")]
    public void OrdinaryCaptionsRemainSourceViews(string caption)
    {
        Assert.False(ModelImageInspection.TryDescribe(caption, out var description));
        Assert.Equal(caption, description);
    }

    internal static JsonElement Metadata(IEnumerable<AIContent> contents)
    {
        var text = string.Join('\n', contents.OfType<TextContent>().Select(t => t.Text));
        var start = text.IndexOf('{'); var end = text.IndexOf('}', start);
        return JsonSerializer.Deserialize<JsonElement>(text[start..(end + 1)]);
    }
}
