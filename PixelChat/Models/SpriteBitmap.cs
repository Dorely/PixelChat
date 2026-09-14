namespace PixelChat.Models;

/// <summary>Immutable PNG payload shared across cels and document history.</summary>
public sealed class SpriteBitmap
{
    public required string Hash { get; set; }
    public required byte[] Data { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
