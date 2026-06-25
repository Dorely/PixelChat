namespace PixelChat.Art;

internal static class SpriteMotionArchetypes
{
    public static AnimationSpec Build(
        string assetType,
        string structureType,
        string animationKind,
        string facing,
        string rootMotion,
        int? requestedFrameCount,
        int fps,
        int targetCellWidth,
        int targetCellHeight)
    {
        var normalizedAssetType = Normalize(assetType, "unit");
        var normalizedStructure = Normalize(structureType, DefaultStructure(normalizedAssetType));
        var normalizedKind = Normalize(animationKind, DefaultAnimation(normalizedAssetType));
        var normalizedFacing = SpriteFacing.Normalize(facing, normalizedAssetType is "vfx" ? SpriteFacing.Center : SpriteFacing.SideRight);
        var normalizedRootMotion = Normalize(rootMotion, "in_place");
        var frameCount = Math.Clamp(requestedFrameCount ?? DefaultFrameCount(normalizedAssetType, normalizedKind), 1, 16);
        var duration = Math.Max(1, (int)Math.Round(1000d / Math.Clamp(fps, 1, 60)));

        var frames = normalizedAssetType switch
        {
            "tower" => TowerFrames(normalizedKind, frameCount, duration),
            "projectile" => ProjectileFrames(frameCount, duration),
            "vfx" => VfxFrames(normalizedKind, frameCount, duration),
            _ => UnitFrames(normalizedKind, frameCount, duration),
        };

        return new AnimationSpec(
            normalizedKind,
            normalizedAssetType,
            normalizedStructure,
            normalizedFacing,
            normalizedRootMotion,
            frames.Count,
            Math.Clamp(fps, 1, 60),
            targetCellWidth,
            targetCellHeight,
            Loop: normalizedKind is "idle" or "walk" or "run" or "rotate" or "aim_directions",
            frames);
    }

    private static IReadOnlyList<FrameSpec> UnitFrames(string kind, int frameCount, int duration)
    {
        var poses = kind switch
        {
            "idle" => new[] { "neutral", "inhale", "neutral", "exhale" },
            "run" => new[] { "right_contact", "down", "passing", "left_contact", "down_opposite", "passing_opposite" },
            "jump" => new[] { "anticipation", "ascent", "apex", "descent", "landing" },
            "attack" => new[] { "anticipation", "windup", "strike", "impact", "recovery" },
            "hit" => new[] { "neutral", "hit_recoil", "recover" },
            "death" => new[] { "hit", "falling", "landed", "settle" },
            _ => new[] { "right_contact", "down", "passing", "up", "left_contact", "recovery" },
        };
        return Enumerable.Range(0, frameCount)
            .Select(index =>
            {
                var pose = poses[index % poses.Length];
                var contacts = pose.Contains("contact", StringComparison.OrdinalIgnoreCase)
                    ? new[] { pose.StartsWith("right", StringComparison.OrdinalIgnoreCase) ? "right_foot" : "left_foot" }
                    : Array.Empty<string>();
                var y = pose.Contains("down", StringComparison.OrdinalIgnoreCase) ? 6
                    : pose.Contains("up", StringComparison.OrdinalIgnoreCase) || pose.Contains("apex", StringComparison.OrdinalIgnoreCase) ? -6
                    : 0;
                return new FrameSpec(index, pose, Phase(index, frameCount), contacts, 0, y, duration, index == 0 || pose.Contains("contact", StringComparison.OrdinalIgnoreCase), "unit");
            })
            .ToList();
    }

    private static IReadOnlyList<FrameSpec> TowerFrames(string kind, int frameCount, int duration)
    {
        if (kind is "fire" or "recoil")
        {
            var poses = new[] { "ready", "fire", "recoil", "recover" };
            return Enumerable.Range(0, frameCount)
                .Select(index => new FrameSpec(index, poses[index % poses.Length], Phase(index, frameCount), [], 0, 0, duration, index is 0 or 1, "tower_fire"))
                .ToList();
        }

        return Enumerable.Range(0, frameCount)
            .Select(index => new FrameSpec(index, $"aim_{index + 1}", Phase(index, frameCount), [], 0, 0, duration, true, "tower_rotate"))
            .ToList();
    }

    private static IReadOnlyList<FrameSpec> ProjectileFrames(int frameCount, int duration) =>
        Enumerable.Range(0, frameCount)
            .Select(index => new FrameSpec(index, $"projectile_{index + 1}", Phase(index, frameCount), [], 0, 0, duration, true, "projectile"))
            .ToList();

    private static IReadOnlyList<FrameSpec> VfxFrames(string kind, int frameCount, int duration) =>
        Enumerable.Range(0, frameCount)
            .Select(index => new FrameSpec(index, $"{kind}_{index + 1}", Phase(index, frameCount), [], 0, 0, duration, index is 0, "radial_vfx"))
            .ToList();

    private static double Phase(int index, int frameCount) =>
        frameCount <= 1 ? 0 : Math.Round(index / (double)frameCount, 4);

    private static int DefaultFrameCount(string assetType, string animationKind) =>
        assetType switch
        {
            "tower" when animationKind is "rotate" or "aim_directions" => 8,
            "tower" => 4,
            "vfx" => 6,
            "projectile" => 1,
            _ when animationKind is "idle" => 4,
            _ when animationKind is "attack" or "jump" => 5,
            _ => 6,
        };

    private static string DefaultAnimation(string assetType) =>
        assetType switch
        {
            "tower" => "aim_directions",
            "projectile" => "directional",
            "vfx" => "explosion",
            _ => "walk",
        };

    private static string DefaultStructure(string assetType) =>
        assetType switch
        {
            "tower" => "tower_pivot",
            "projectile" => "directional_projectile",
            "vfx" => "radial_vfx",
            _ => "biped",
        };

    private static string Normalize(string? value, string fallback)
    {
        var cleaned = value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
