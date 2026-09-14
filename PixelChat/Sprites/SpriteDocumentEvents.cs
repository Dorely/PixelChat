namespace PixelChat.Sprites;

public sealed class SpriteDocumentEvents(ILogger<SpriteDocumentEvents> logger)
{
    public event Action<Guid, long>? Changed;
    public void Publish(Guid documentId, long revision)
    {
        foreach (var subscriber in Changed?.GetInvocationList() ?? [])
            try { ((Action<Guid, long>)subscriber)(documentId, revision); }
            catch (Exception ex) { logger.LogWarning(ex, "Sprite view notification failed for {DocumentId} revision {Revision}.", documentId, revision); }
    }
}
