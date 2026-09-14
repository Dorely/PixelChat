using System.Text.Json;

namespace PixelChat.Sprites;

/// <summary>Versioned native document state, owned by a FrameSet. Bitmap payloads are content addressed.</summary>
public sealed class SpriteDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Sprite";
    public SpriteSpecification Specification { get; set; } = new();
    public List<SpriteLayer> Layers { get; set; } = [];
    public List<SpriteFrame> Frames { get; set; } = [];
    public List<SpriteClip> Clips { get; set; } = [];
    public List<SpriteSlice> Slices { get; set; } = [];
    public SpriteSelection? Selection { get; set; }
    public SpriteClipboard? Clipboard { get; set; }
    public Dictionary<string, string> Provenance { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);
    public static SpriteDocument Deserialize(string json) => JsonSerializer.Deserialize<SpriteDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("Invalid sprite document.");
}

public sealed class SpriteSpecification
{
    public string ArtMode { get; set; } = "painted";
    public int Width { get; set; } = 64;
    public int Height { get; set; } = 64;
    public bool EnforcePalette { get; set; }
    public bool BinaryAlpha { get; set; }
    public List<string> Palette { get; set; } = [];
    public List<Guid> IdentityReferences { get; set; } = [];
    public string MotionRequirements { get; set; } = "";
}

public sealed class SpriteLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1;
}

public sealed class SpriteFrame
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Frame";
    public int Width { get; set; } = 64;
    public int Height { get; set; } = 64;
    public int DurationMs { get; set; } = 100;
    public bool HideFromOnionSkin { get; set; }
    public Dictionary<Guid, string> Cels { get; set; } = [];
    public Dictionary<string, SpritePoint> Pivots { get; set; } = [];
    public Guid? SourceRegionId { get; set; }
    public SpriteRect? SourceRect { get; set; }
    public SpritePoint ImportOffset { get; set; } = new(0, 0);
    public string CanvasTransformJson { get; set; } = "";
    public string CanvasFinalizationJson { get; set; } = "";
}

public sealed class SpriteClip
{
    public string Name { get; set; } = "Animation";
    public List<Guid> FrameIds { get; set; } = [];
    public string Direction { get; set; } = "forward";
    public bool Loop { get; set; } = true;
}

public sealed record SpritePoint(int X, int Y);
public sealed record SpriteRect(int X, int Y, int Width, int Height);
public sealed record SpriteSlice(string Name, SpriteRect Rect);
public sealed record SpriteSelection(Guid FrameId, List<SpritePoint> Polygon, string? Color = null);
public sealed record SpriteClipboard(string BitmapHash, int Width, int Height);
public sealed record SpriteSnapshot(Guid DocumentId, long Revision, SpriteDocument Document);
public sealed record SpriteEditorFocus(Guid FrameId, Guid LayerId, long Revision);
public sealed record SpriteBatch(Guid DocumentId, long ExpectedRevision, string Label, IReadOnlyList<JsonElement> Operations,
    string Source = "user", string? TaskId = null, string? Script = null);
public sealed record SpriteCommit(Guid DocumentId, long Revision, Guid HistoryId, int OperationCount);
public sealed class SpriteConflictException(long expected, long actual)
    : InvalidOperationException($"Sprite revision conflict: expected {expected}, current {actual}. Read the document and rebase your edit.")
{
    public long ExpectedRevision { get; } = expected;
    public long ActualRevision { get; } = actual;
}
