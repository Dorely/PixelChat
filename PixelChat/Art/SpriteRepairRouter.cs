namespace PixelChat.Art;

internal static class SpriteRepairRouter
{
    public static SpriteRepairAction Recommend(IReadOnlyList<SpriteFailure> failures)
    {
        if (failures.Count == 0 || failures.All(failure => failure == SpriteFailure.None))
            return SpriteRepairAction.None;
        if (failures.Any(failure => failure is SpriteFailure.MissingFrame or SpriteFailure.Clipped or SpriteFailure.WrongFacing or SpriteFailure.IdentityDrift))
            return SpriteRepairAction.RegenerateFrame;
        if (failures.Any(failure => failure is SpriteFailure.SlotCrossing or SpriteFailure.GuideLeakage or SpriteFailure.DirtyBackground))
            return SpriteRepairAction.RegenerateStrip;
        if (failures.Any(failure => failure is SpriteFailure.RootDrift or SpriteFailure.ScaleDrift))
            return SpriteRepairAction.ReextractFixedSlots;
        return SpriteRepairAction.AcceptWithWarnings;
    }
}
