namespace PixelChat.Sprites;

public static class SpriteTimeline
{
    public static IReadOnlyList<Guid> Sequence(IEnumerable<Guid> frames, SpriteClip? clip)
    {
        var ids = clip?.FrameIds.ToList() ?? frames.ToList();
        if (clip?.Direction == "reverse") ids.Reverse();
        else if (clip?.Direction == "pingpong" && ids.Count > 2) ids.AddRange(ids.Skip(1).Take(ids.Count - 2).Reverse().ToList());
        return ids;
    }

    public static Guid? FrameAt(SpriteDocument document, SpriteClip? clip, long elapsedMs)
    {
        var sequence = Sequence(document.Frames.Select(f => f.Id), clip);
        if (sequence.Count == 0) return null;
        var frames = document.Frames.ToDictionary(f => f.Id);
        var total = sequence.Sum(id => (long)frames[id].DurationMs);
        var elapsed = Math.Max(0, elapsedMs);
        if (clip?.Loop != false) elapsed %= total;
        else if (elapsed >= total) return sequence[^1];
        foreach (var id in sequence) { elapsed -= frames[id].DurationMs; if (elapsed < 0) return id; }
        return sequence[^1];
    }
}
