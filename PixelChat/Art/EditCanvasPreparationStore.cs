namespace PixelChat.Art;

public static class EditCanvasPreparationTargetKinds
{
    public const string Asset = "asset";
    public const string Frame = "frame";
}

public sealed record EditCanvasPreparation(
    Guid Id,
    Guid ProjectId,
    string TargetKind,
    Guid TargetId,
    Guid? ParentTargetId,
    long TargetRevisionTicks,
    string Background,
    EditCanvasOptions Options,
    Guid? OriginalMaskId,
    byte[]? OriginalMaskPng,
    PreparedEditCanvas Canvas,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public interface IEditCanvasPreparationStore
{
    EditCanvasPreparation Add(
        Guid projectId,
        string targetKind,
        Guid targetId,
        Guid? parentTargetId,
        long targetRevisionTicks,
        string background,
        EditCanvasOptions options,
        Guid? originalMaskId,
        byte[]? originalMaskPng,
        PreparedEditCanvas canvas);

    bool TryGet(
        Guid projectId,
        Guid preparationId,
        string targetKind,
        Guid targetId,
        Guid? parentTargetId,
        long targetRevisionTicks,
        out EditCanvasPreparation preparation,
        out string error);

    bool TryPeek(Guid projectId, Guid preparationId, out EditCanvasPreparation preparation);
    void Remove(Guid preparationId);
}

public sealed class EditCanvasPreparationStore : IEditCanvasPreparationStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private const int MaximumPreparationsPerProject = 4;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, EditCanvasPreparation> _preparations = [];

    public EditCanvasPreparation Add(
        Guid projectId,
        string targetKind,
        Guid targetId,
        Guid? parentTargetId,
        long targetRevisionTicks,
        string background,
        EditCanvasOptions options,
        Guid? originalMaskId,
        byte[]? originalMaskPng,
        PreparedEditCanvas canvas)
    {
        var now = DateTime.UtcNow;
        var preparation = new EditCanvasPreparation(
            Guid.NewGuid(),
            projectId,
            targetKind,
            targetId,
            parentTargetId,
            targetRevisionTicks,
            background,
            options,
            originalMaskId,
            originalMaskPng,
            canvas,
            now,
            now.Add(Lifetime));

        lock (_lock)
        {
            RemoveExpired(now);
            var overflow = _preparations.Values
                .Where(item => item.ProjectId == projectId)
                .OrderBy(item => item.CreatedAt)
                .Take(Math.Max(0, _preparations.Values.Count(item => item.ProjectId == projectId) - MaximumPreparationsPerProject + 1))
                .Select(item => item.Id)
                .ToList();
            foreach (var id in overflow)
                _preparations.Remove(id);
            _preparations[preparation.Id] = preparation;
        }

        return preparation;
    }

    public bool TryGet(
        Guid projectId,
        Guid preparationId,
        string targetKind,
        Guid targetId,
        Guid? parentTargetId,
        long targetRevisionTicks,
        out EditCanvasPreparation preparation,
        out string error)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            RemoveExpired(now);
            if (!_preparations.TryGetValue(preparationId, out preparation!))
            {
                error = "Canvas preparation was not found or has expired. Preview the edit canvas again.";
                return false;
            }
            if (preparation.ProjectId != projectId
                || !string.Equals(preparation.TargetKind, targetKind, StringComparison.Ordinal)
                || preparation.TargetId != targetId
                || preparation.ParentTargetId != parentTargetId)
            {
                error = "Canvas preparation does not match this edit target. Preview the intended target again.";
                return false;
            }
            if (preparation.TargetRevisionTicks != targetRevisionTicks)
            {
                error = "Canvas preparation is stale because its source changed. Preview the canvas again.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    public bool TryPeek(Guid projectId, Guid preparationId, out EditCanvasPreparation preparation)
    {
        lock (_lock)
        {
            RemoveExpired(DateTime.UtcNow);
            return _preparations.TryGetValue(preparationId, out preparation!)
                && preparation.ProjectId == projectId;
        }
    }

    public void Remove(Guid preparationId)
    {
        lock (_lock)
            _preparations.Remove(preparationId);
    }

    private void RemoveExpired(DateTime now)
    {
        foreach (var id in _preparations.Values.Where(item => item.ExpiresAt <= now).Select(item => item.Id).ToList())
            _preparations.Remove(id);
    }
}
