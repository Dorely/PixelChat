using System.Text.Json;
using PixelChat.Models;
using SixLabors.ImageSharp.PixelFormats;

namespace PixelChat.Sprites;

/// <summary>Edits temporary state only. The document service commits the complete result atomically.</summary>
public sealed class SpriteCommandEngine(SpriteDocument document, Func<string, SpriteBitmap> resolve, CancellationToken cancellationToken)
{
    private readonly Dictionary<string, SpriteBitmap> _created = [];
    private long _allocatedPixels;
    public IReadOnlyDictionary<string, SpriteBitmap> Created => _created;
    public SpriteBitmap Bitmap(string hash) => _created.TryGetValue(hash, out var bitmap) ? bitmap : resolve(hash);

    public string Store(SpriteRaster raster)
    {
        _allocatedPixels += (long)raster.Width * raster.Height;
        if (_allocatedPixels > 67_108_864) throw new InvalidOperationException("Batch raster budget exceeded (64 megapixels).");
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = raster.Encode();
        _created.TryAdd(bitmap.Hash, bitmap);
        return bitmap.Hash;
    }

    public void Apply(IReadOnlyList<JsonElement> operations)
    {
        if (operations.Count is < 1 or > 10000) throw new InvalidOperationException("A batch must contain 1–10000 operations.");
        foreach (var op in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyOne(op);
        }
        ValidateStructure(document);
    }

    public static void ValidateStructure(SpriteDocument doc)
    {
        if (doc.FormatVersion != 1) throw new InvalidOperationException("Unsupported document version.");
        SpriteRaster.CheckSize(doc.Specification.Width, doc.Specification.Height);
        if (doc.Specification.ArtMode is not ("pixel" or "painted")) throw new InvalidOperationException("Art mode must be pixel or painted.");
        if (doc.Frames.Count is < 1 or > 512 || doc.Layers.Count is < 1 or > 128) throw new InvalidOperationException("Documents support 1–512 frames and 1–128 layers.");
        if (doc.Frames.Select(f => f.Id).Distinct().Count() != doc.Frames.Count || doc.Layers.Select(l => l.Id).Distinct().Count() != doc.Layers.Count)
            throw new InvalidOperationException("Frame and layer IDs must be unique.");
        foreach (var layer in doc.Layers)
            if (!double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1) throw new InvalidOperationException("Layer opacity must be 0–1.");
        var layers = doc.Layers.Select(l => l.Id).ToHashSet();
        foreach (var frame in doc.Frames)
        {
            SpriteRaster.CheckSize(frame.Width, frame.Height);
            if (frame.DurationMs is < 1 or > 60000) throw new InvalidOperationException("Frame duration must be 1–60000 milliseconds.");
            if (frame.Cels.Keys.Any(id => !layers.Contains(id))) throw new InvalidOperationException("Cel references a missing layer.");
        }
        foreach (var clip in doc.Clips)
            if (clip.Direction is not ("forward" or "reverse" or "pingpong") || clip.FrameIds.Count == 0 || clip.FrameIds.Any(id => doc.Frames.All(f => f.Id != id)))
                throw new InvalidOperationException("Invalid clip direction or frame reference.");
        if (doc.Clips.Select(c => c.Name).Distinct().Count() != doc.Clips.Count) throw new InvalidOperationException("Clip names must be unique.");
        if (doc.Specification.EnforcePalette && doc.Specification.Palette.Count == 0) throw new InvalidOperationException("An enforced palette cannot be empty.");
        foreach (var color in doc.Specification.Palette) _ = SpriteRaster.Color(color);
    }

    private void ApplyOne(JsonElement op)
    {
        var name = Text(op, "op");
        switch (name)
        {
            case "setProvenance":
            {
                var key = Text(op, "key"); var value = Text(op, "value");
                if (key.Length is < 1 or > 200 || value.Length > 100000) throw new InvalidOperationException("Provenance key/value exceeds limits.");
                document.Provenance[key] = value; return;
            }
            case "addLayer":
                document.Layers.Add(new() { Id = Id(op, "id", Guid.NewGuid()), Name = Text(op, "name", "Layer") }); return;
            case "setLayer":
            {
                var layer = Layer(op);
                if (op.TryGetProperty("name", out var n)) layer.Name = n.GetString() ?? "Layer";
                if (op.TryGetProperty("visible", out var v)) layer.Visible = v.GetBoolean();
                if (op.TryGetProperty("locked", out var l)) layer.Locked = l.GetBoolean();
                if (op.TryGetProperty("opacity", out var a)) layer.Opacity = a.GetDouble();
                return;
            }
            case "reorderLayer": Move(document.Layers, Layer(op), Int(op, "index")); return;
            case "deleteLayer":
            {
                var layer = EditableLayer(op);
                if (document.Layers.Count == 1) throw new InvalidOperationException("Cannot delete the last layer.");
                document.Layers.Remove(layer);
                foreach (var f in document.Frames) f.Cels.Remove(layer.Id);
                return;
            }
            case "duplicateLayer":
            {
                var source = Layer(op);
                var copy = new SpriteLayer { Id = Id(op, "id", Guid.NewGuid()), Name = Text(op, "name", source.Name + " copy"), Opacity = source.Opacity, Visible = source.Visible };
                document.Layers.Insert(document.Layers.IndexOf(source) + 1, copy);
                foreach (var f in document.Frames) if (f.Cels.TryGetValue(source.Id, out var hash)) f.Cels[copy.Id] = hash;
                return;
            }
            case "mergeLayer":
            {
                var top = EditableLayer(op); var index = document.Layers.IndexOf(top);
                if (index == 0) throw new InvalidOperationException("The bottom layer has no layer below it.");
                var bottom = document.Layers[index - 1];
                if (bottom.Locked || !bottom.Visible || !top.Visible) throw new InvalidOperationException("Merge requires visible, unlocked layers.");
                foreach (var f in document.Frames)
                {
                    var pair = new SpriteDocument { Layers = [bottom, top] };
                    f.Cels[bottom.Id] = Store(SpriteRaster.Composite(pair, f, Bitmap));
                    f.Cels.Remove(top.Id);
                }
                bottom.Opacity = 1; document.Layers.Remove(top); return;
            }
            case "addFrame":
            {
                var f = new SpriteFrame { Id = Id(op, "id", Guid.NewGuid()), Name = Text(op, "name", "Frame"), Width = document.Specification.Width,
                    Height = document.Specification.Height, DurationMs = Int(op, "durationMs", 100) };
                document.Frames.Insert(Math.Clamp(Int(op, "index", document.Frames.Count), 0, document.Frames.Count), f); return;
            }
            case "duplicateFrame":
            {
                var source = Frame(op);
                var copy = JsonSerializer.Deserialize<SpriteFrame>(JsonSerializer.Serialize(source, SpriteDocument.JsonOptions), SpriteDocument.JsonOptions)!;
                copy.Id = Id(op, "id", Guid.NewGuid()); copy.Name = Text(op, "name", source.Name + " copy");
                document.Frames.Insert(document.Frames.IndexOf(source) + 1, copy); return;
            }
            case "deleteFrame":
            {
                var f = Frame(op);
                if (document.Frames.Count == 1) throw new InvalidOperationException("Cannot delete the last frame.");
                document.Frames.Remove(f);
                foreach (var c in document.Clips) c.FrameIds.RemoveAll(id => id == f.Id);
                document.Clips.RemoveAll(c => c.FrameIds.Count == 0);
                if (document.Selection?.FrameId == f.Id) document.Selection = null;
                return;
            }
            case "reorderFrame": Move(document.Frames, Frame(op), Int(op, "index")); return;
            case "setDuration": Frame(op).DurationMs = Int(op, "durationMs"); return;
            case "setFrame":
            {
                var f = Frame(op);
                if (op.TryGetProperty("name", out var n)) f.Name = n.GetString() ?? "Frame";
                if (op.TryGetProperty("hideFromOnionSkin", out var h)) f.HideFromOnionSkin = h.GetBoolean();
                return;
            }
            case "setClip":
            {
                var clip = op.GetProperty("clip").Deserialize<SpriteClip>(SpriteDocument.JsonOptions)!;
                document.Clips.RemoveAll(c => c.Name == clip.Name); document.Clips.Add(clip); return;
            }
            case "deleteClip": document.Clips.RemoveAll(c => c.Name == Text(op, "name")); return;
            case "setPivot": Frame(op).Pivots[Text(op, "name")] = new(Int(op, "x"), Int(op, "y")); return;
            case "deletePivot": Frame(op).Pivots.Remove(Text(op, "name")); return;
            case "setSlice":
                document.Slices.RemoveAll(s => s.Name == Text(op, "name")); document.Slices.Add(new(Text(op, "name"), Rect(op))); return;
            case "deleteSlice": document.Slices.RemoveAll(s => s.Name == Text(op, "name")); return;
            case "setSpecification":
            {
                var specification = op.GetProperty("specification").Deserialize<SpriteSpecification>(SpriteDocument.JsonOptions)!;
                if (specification.ArtMode == "pixel" && document.Specification.ArtMode != "pixel" && !Bool(op, "convert"))
                    throw new InvalidOperationException("Converting existing artwork to pixel mode requires convert: true.");
                document.Specification = specification;
                if (Bool(op, "convert"))
                    foreach (var f in document.Frames)
                        foreach (var id in f.Cels.Keys.ToList())
                        {
                            if (document.Layers.Single(l => l.Id == id).Locked) throw new InvalidOperationException("Unlock layers before conversion.");
                            var converted = SpriteRaster.Decode(Bitmap(f.Cels[id]).Data);
                            for (var y = 0; y < converted.Height; y++) for (var x = 0; x < converted.Width; x++) converted.Put(x, y, Constrain(converted.Get(x, y), true));
                            f.Cels[id] = Store(converted);
                        }
                return;
            }
            case "select":
            {
                var f = Frame(op);
                var points = op.TryGetProperty("polygon", out var p) ? p.Deserialize<List<SpritePoint>>(SpriteDocument.JsonOptions)! : RectanglePoints(Rect(op));
                var selectedColor = op.TryGetProperty("color", out var c) ? c.GetString() : null;
                byte[]? mask = null;
                if (selectedColor is not null)
                {
                    var visible = SpriteRaster.Composite(document, f, Bitmap);
                    var expected = SpriteRaster.Color(selectedColor);
                    mask = new byte[(f.Width * f.Height + 7) / 8];
                    for (var y = 0; y < f.Height; y++) for (var x = 0; x < f.Width; x++)
                        if (SpriteRaster.InsidePolygon(x, y, points) && visible.Get(x, y).Equals(expected))
                        { var i = y * f.Width + x; mask[i / 8] |= (byte)(1 << (i % 8)); }
                }
                document.Selection = new(f.Id, points, selectedColor, f.Width, mask); return;
            }
            case "clearSelection": document.Selection = null; return;
        }

        var target = op.TryGetProperty("target", out var targetValue) ? targetValue : op;
        var frame = Frame(target); var targetLayer = EditableLayer(target);
        var raster = frame.Cels.TryGetValue(targetLayer.Id, out var cel) ? SpriteRaster.Decode(Bitmap(cel).Data) : SpriteRaster.Blank(frame.Width, frame.Height);
        bool Selected(int x, int y)
        {
            var selection = document.Selection;
            return selection is null || selection.FrameId != frame.Id ||
                (selection.PixelMask is { } mask
                    ? (y * selection.Width + x) / 8 < mask.Length && (mask[(y * selection.Width + x) / 8] & (1 << ((y * selection.Width + x) % 8))) != 0
                    : SpriteRaster.InsidePolygon(x, y, selection.Polygon));
        }
        var selectionMask = new bool[raster.Width * raster.Height];
        for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) selectionMask[y * raster.Width + x] = Selected(x, y);
        bool Allowed(int x, int y) => x >= 0 && y >= 0 && x < raster.Width && y < raster.Height && selectionMask[y * raster.Width + x];
        var color = name is "pencil" or "brush" or "line" or "rectangle" or "ellipse" or "fill"
            ? Constrain(SpriteRaster.Color(Text(op, "color", "#000000ff")), false) : default;
        void Paint(int x, int y, double coverage = 1)
        {
            if (!Allowed(x, y)) return;
            if (name is "erase" or "cut") raster.Put(x, y, default);
            else if (document.Specification.ArtMode == "painted" && coverage < 1) raster.Blend(x, y, color, coverage);
            else raster.Put(x, y, color);
        }
        void Brush(int x, int y)
        {
            var size = Math.Clamp(Int(op, "size", 1), 1, 512);
            if (size == 1) { Paint(x, y); return; }
            var radius = size / 2d;
            for (var dy = -(size / 2); dy <= size / 2; dy++) for (var dx = -(size / 2); dx <= size / 2; dx++)
            {
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= radius) Paint(x + dx, y + dy, document.Specification.ArtMode == "painted" ? Math.Min(1, radius - distance) : 1);
            }
        }
        switch (name)
        {
            case "pencil": case "brush": case "erase": case "line":
            {
                var points = op.TryGetProperty("points", out var p) ? p.Deserialize<List<SpritePoint>>(SpriteDocument.JsonOptions)! : new List<SpritePoint> { new(Int(op, "x"), Int(op, "y")) };
                if (name == "line") points.Add(new(Int(op, "x2"), Int(op, "y2")));
                if (points.Count is < 1 or > 10000 || points.Any(p => Math.Abs((long)p.X) > 16384 || Math.Abs((long)p.Y) > 16384)) throw new InvalidOperationException("Invalid stroke coordinates.");
                Brush(points[0].X, points[0].Y);
                for (var i = 1; i < points.Count; i++) Line(points[i - 1], points[i], Brush);
                break;
            }
            case "rectangle": case "ellipse":
            {
                var rect = Rect(op); var filled = Bool(op, "filled");
                for (var y = Math.Max(0, rect.Y); y < Math.Min(raster.Height, (long)rect.Y + rect.Height); y++)
                    for (var x = Math.Max(0, rect.X); x < Math.Min(raster.Width, (long)rect.X + rect.Width); x++)
                    {
                        if (name == "rectangle") { if (filled || x == rect.X || y == rect.Y || x == rect.X + rect.Width - 1 || y == rect.Y + rect.Height - 1) Paint(x, y); }
                        else
                        {
                            var dx = (x + .5 - rect.X - rect.Width / 2d) / (rect.Width / 2d);
                            var dy = (y + .5 - rect.Y - rect.Height / 2d) / (rect.Height / 2d);
                            var distance = dx * dx + dy * dy;
                            var border = 1 - 2d / Math.Max(1, Math.Min(rect.Width, rect.Height));
                            if (distance <= 1 && (filled || distance >= border * border)) Paint(x, y);
                        }
                    }
                break;
            }
            case "fill":
            {
                var x = Int(op, "x"); var y = Int(op, "y");
                if (!Allowed(x, y)) break;
                var original = raster.Get(x, y); var visited = new bool[raster.Width * raster.Height]; var queue = new Queue<SpritePoint>();
                queue.Enqueue(new(x, y));
                while (queue.TryDequeue(out var p))
                {
                    if (!Allowed(p.X, p.Y)) continue;
                    var index = p.Y * raster.Width + p.X;
                    if (visited[index] || !raster.Get(p.X, p.Y).Equals(original)) continue;
                    visited[index] = true; Paint(p.X, p.Y);
                    queue.Enqueue(new(p.X - 1, p.Y)); queue.Enqueue(new(p.X + 1, p.Y)); queue.Enqueue(new(p.X, p.Y - 1)); queue.Enqueue(new(p.X, p.Y + 1));
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                }
                break;
            }
            case "copy": case "cut":
            {
                var points = document.Selection?.FrameId == frame.Id ? document.Selection.Polygon : RectanglePoints(new(0, 0, raster.Width, raster.Height));
                var left = Math.Clamp(points.Min(p => p.X), 0, raster.Width - 1); var top = Math.Clamp(points.Min(p => p.Y), 0, raster.Height - 1);
                var right = Math.Clamp(points.Max(p => p.X), left + 1, raster.Width); var bottom = Math.Clamp(points.Max(p => p.Y), top + 1, raster.Height);
                var copy = SpriteRaster.Blank(right - left, bottom - top);
                for (var y = top; y < bottom; y++) for (var x = left; x < right; x++) if (Allowed(x, y)) copy.Put(x - left, y - top, raster.Get(x, y));
                document.Clipboard = new(Store(copy), copy.Width, copy.Height);
                if (name == "copy") return;
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) if (Allowed(x, y)) raster.Put(x, y, default);
                break;
            }
            case "clear":
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) if (Allowed(x, y)) raster.Put(x, y, default);
                break;
            case "replaceCel":
            {
                var bitmap = Bitmap(Text(op, "bitmapHash")); var replacement = SpriteRaster.Decode(bitmap.Data);
                if (replacement.Width != frame.Width || replacement.Height != frame.Height) throw new InvalidOperationException("Replacement cel must match frame dimensions.");
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++) if (Allowed(x, y)) raster.Put(x, y, Constrain(replacement.Get(x, y), false));
                break;
            }
            case "stamp": case "paste":
            {
                if (name == "paste" && !op.TryGetProperty("source", out _))
                {
                    var clipboard = document.Clipboard ?? throw new InvalidOperationException("Copy pixels before pasting.");
                    var pasted = SpriteRaster.Decode(Bitmap(clipboard.BitmapHash).Data);
                    var dx = Int(op, "x"); var dy = Int(op, "y");
                    for (var y = 0; y < pasted.Height; y++) for (var x = 0; x < pasted.Width; x++) if (Allowed(dx + x, dy + y)) raster.Put(dx + x, dy + y, Constrain(pasted.Get(x, y), false));
                    break;
                }
                var source = op.GetProperty("source"); var sf = Frame(source); var sl = Layer(source);
                var sr = sf.Cels.TryGetValue(sl.Id, out var hash) ? SpriteRaster.Decode(Bitmap(hash).Data) : SpriteRaster.Blank(sf.Width, sf.Height);
                var rect = op.GetProperty("sourceRect").Deserialize<SpriteRect>(SpriteDocument.JsonOptions)!;
                var dest = op.GetProperty("destination").Deserialize<SpritePoint>(SpriteDocument.JsonOptions)!;
                SpriteRaster.CheckSize(rect.Width, rect.Height);
                for (var y = 0; y < rect.Height; y++) for (var x = 0; x < rect.Width; x++)
                    if (Allowed(dest.X + x, dest.Y + y)) raster.Put(dest.X + x, dest.Y + y, Constrain(sr.Get(rect.X + x, rect.Y + y), false));
                break;
            }
            case "translate": case "flip": case "rotate":
            {
                var before = new SpriteRaster(raster.Width, raster.Height, (byte[])raster.Pixels.Clone());
                var degrees = Int(op, "degrees", 90);
                if (name == "rotate" && degrees % 90 != 0) throw new InvalidOperationException("Typed rotation supports exact multiples of 90 degrees.");
                var turns = ((degrees / 90) % 4 + 4) % 4;
                for (var y = 0; y < raster.Height; y++) for (var x = 0; x < raster.Width; x++)
                {
                    if (!Allowed(x, y)) continue;
                    var sx = x; var sy = y;
                    if (name == "translate") { sx -= Int(op, "dx"); sy -= Int(op, "dy"); }
                    else if (name == "flip") { if (Text(op, "axis", "horizontal") == "horizontal") sx = raster.Width - 1 - x; else sy = raster.Height - 1 - y; }
                    else
                    {
                        var dx = x - (raster.Width - 1) / 2d; var dy = y - (raster.Height - 1) / 2d;
                        for (var t = 0; t < turns; t++) (dx, dy) = (dy, -dx);
                        sx = (int)Math.Round(dx + (raster.Width - 1) / 2d); sy = (int)Math.Round(dy + (raster.Height - 1) / 2d);
                    }
                    raster.Put(x, y, Allowed(sx, sy) ? before.Get(sx, sy) : default);
                }
                break;
            }
            case "resize": case "crop":
            {
                if (document.Selection is not null) throw new InvalidOperationException("Clear the selection before changing frame dimensions.");
                var width = Int(op, "width"); var height = Int(op, "height"); SpriteRaster.CheckSize(width, height);
                var resampling = Text(op, "resampling");
                if (name == "resize" && document.Specification.ArtMode == "pixel" && resampling != "nearest") throw new InvalidOperationException("Pixel mode requires nearest resampling.");
                foreach (var layer in document.Layers)
                {
                    if (layer.Locked) throw new InvalidOperationException("Unlock all layers before changing frame dimensions.");
                    if (!frame.Cels.TryGetValue(layer.Id, out var hash)) continue;
                    var source = SpriteRaster.Decode(Bitmap(hash).Data); var result = SpriteRaster.Blank(width, height);
                    if (name == "resize") result = source.Resize(width, height, resampling);
                    else for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) result.Put(x, y, source.Get(x + Int(op, "x"), y + Int(op, "y")));
                    frame.Cels[layer.Id] = Store(result);
                }
                foreach (var key in frame.Pivots.Keys.ToList())
                {
                    var p = frame.Pivots[key];
                    frame.Pivots[key] = name == "crop" ? new(p.X - Int(op, "x"), p.Y - Int(op, "y")) : new((int)Math.Round(p.X * width / (double)frame.Width), (int)Math.Round(p.Y * height / (double)frame.Height));
                }
                frame.Width = width; frame.Height = height; return;
            }
            default: throw new InvalidOperationException($"Unknown sprite operation '{name}'. Load sprite_help for the command reference.");
        }
        frame.Cels[targetLayer.Id] = Store(raster);
    }

    private Rgba32 Constrain(Rgba32 color, bool convert)
    {
        var spec = document.Specification;
        if (spec.BinaryAlpha && color.A is > 0 and < 255)
        {
            if (!convert) throw new InvalidOperationException("Binary alpha requires opaque or transparent pixels.");
            color.A = color.A >= 128 ? (byte)255 : (byte)0;
        }
        if (color.A == 0) return color;
        if (spec.EnforcePalette)
        {
            var palette = spec.Palette.Select(SpriteRaster.Color).ToList();
            if (!palette.Contains(color))
            {
                if (!convert) throw new InvalidOperationException("Color is outside the document palette.");
                if (palette.Count == 0) throw new InvalidOperationException("Palette cannot be empty.");
                color = palette.MinBy(p => Math.Pow(p.R - color.R, 2) + Math.Pow(p.G - color.G, 2) + Math.Pow(p.B - color.B, 2) + Math.Pow(p.A - color.A, 2));
            }
        }
        return color;
    }

    private SpriteFrame Frame(JsonElement op) => document.Frames.SingleOrDefault(f => f.Id == Id(op, "frameId")) ?? throw new InvalidOperationException("Frame does not exist.");
    private SpriteLayer Layer(JsonElement op) => document.Layers.SingleOrDefault(l => l.Id == Id(op, "layerId")) ?? throw new InvalidOperationException("Layer does not exist.");
    private SpriteLayer EditableLayer(JsonElement op) { var layer = Layer(op); return !layer.Locked ? layer : throw new InvalidOperationException("Layer is locked."); }
    private static string Text(JsonElement op, string name, string fallback = "") => op.TryGetProperty(name, out var p) ? p.GetString() ?? fallback : fallback;
    private static int Int(JsonElement op, string name, int fallback = 0) => op.TryGetProperty(name, out var p) ? p.GetInt32() : fallback;
    private static bool Bool(JsonElement op, string name) => op.TryGetProperty(name, out var p) && p.GetBoolean();
    private static Guid Id(JsonElement op, string name, Guid fallback = default) => op.TryGetProperty(name, out var p) ? p.GetGuid() : fallback;
    private static SpriteRect Rect(JsonElement op) => new(Int(op, "x"), Int(op, "y"), Int(op, "width"), Int(op, "height"));
    private static List<SpritePoint> RectanglePoints(SpriteRect r) => [new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height), new(r.X, r.Y + r.Height)];
    private static void Move<T>(List<T> list, T item, int index) { list.Remove(item); list.Insert(Math.Clamp(index, 0, list.Count), item); }
    private static void Line(SpritePoint a, SpritePoint b, Action<int, int> paint)
    {
        var x = a.X; var y = a.Y; var dx = Math.Abs(b.X - x); var sx = x < b.X ? 1 : -1;
        var dy = -Math.Abs(b.Y - y); var sy = y < b.Y ? 1 : -1; var error = dx + dy;
        while (true)
        {
            paint(x, y); if (x == b.X && y == b.Y) break;
            var twice = 2 * error;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
        }
    }
}
