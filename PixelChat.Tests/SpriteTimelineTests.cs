using PixelChat.Sprites;

namespace PixelChat.Tests;

public sealed class SpriteTimelineTests
{
    [Fact]
    public void UnequalDurationsAndOneShotHoldTheCorrectFrame()
    {
        var a = new SpriteFrame { DurationMs = 50 }; var b = new SpriteFrame { DurationMs = 200 }; var c = new SpriteFrame { DurationMs = 75 };
        var document = new SpriteDocument { Frames = [a, b, c] }; var clip = new SpriteClip { FrameIds = [a.Id, b.Id, c.Id], Loop = false };
        Assert.Equal(a.Id, SpriteTimeline.FrameAt(document, clip, 49)); Assert.Equal(b.Id, SpriteTimeline.FrameAt(document, clip, 50));
        Assert.Equal(b.Id, SpriteTimeline.FrameAt(document, clip, 249)); Assert.Equal(c.Id, SpriteTimeline.FrameAt(document, clip, 1000));
        clip.Loop = true; Assert.Equal(a.Id, SpriteTimeline.FrameAt(document, clip, 325));
    }
    [Fact]
    public void ReverseAndPingPongDoNotDuplicateEndpoints()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        Assert.Equal(new[] { ids[3], ids[2], ids[1], ids[0] }, SpriteTimeline.Sequence(ids, new() { FrameIds = ids, Direction = "reverse" }));
        Assert.Equal(new[] { ids[0], ids[1], ids[2], ids[3], ids[2], ids[1] }, SpriteTimeline.Sequence(ids, new() { FrameIds = ids, Direction = "pingpong" }));
        Assert.Equal(ids.Take(1), SpriteTimeline.Sequence(ids.Take(1), new() { FrameIds = ids.Take(1).ToList(), Direction = "pingpong" }));
    }
}
