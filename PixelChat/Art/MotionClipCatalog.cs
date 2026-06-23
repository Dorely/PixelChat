using System.Text.Json;

namespace PixelChat.Art;

internal sealed class MotionClipCatalog
{
    public const string DefaultHumanoidWalkClipId = "quaternius.ual2.walk.carry.loop";
    public const string RendererId = "quaternius_gltf";
    public const string SkinnedMannequinRenderStyle = "skinned_mannequin";

    private const string MotionClipRoot = "Assets/MotionClips";
    private const string ManifestRelativePath = $"{MotionClipRoot}/manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private MotionClipCatalog(string contentRootPath, IReadOnlyList<MotionClipDefinition> clips)
    {
        ContentRootPath = contentRootPath;
        Clips = clips;
    }

    public string ContentRootPath { get; }
    public IReadOnlyList<MotionClipDefinition> Clips { get; }

    public static MotionClipCatalog Load(string contentRootPath)
    {
        var manifestPath = Path.Combine(contentRootPath, ManifestRelativePath);
        if (!File.Exists(manifestPath))
            return new MotionClipCatalog(contentRootPath, []);

        var manifest = JsonSerializer.Deserialize<MotionClipManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? new MotionClipManifest();
        return new MotionClipCatalog(contentRootPath, manifest.Clips);
    }

    public MotionClipDefinition? Find(string? clipId)
    {
        if (string.IsNullOrWhiteSpace(clipId))
            return null;

        var normalized = Normalize(clipId);
        return Clips.FirstOrDefault(clip => clip.MatchesId(normalized));
    }

    public MotionClipDefinition? ResolveDefault(AnimationSpec spec) =>
        Clips.FirstOrDefault(clip => clip.MatchesId(DefaultHumanoidWalkClipId) && clip.Supports(spec))
        ?? Clips.FirstOrDefault(clip => clip.Supports(spec));

    public string ResolveAssetPath(MotionClipDefinition clip) =>
        Path.Combine(ContentRootPath, MotionClipRoot, clip.AssetPath.Replace('/', Path.DirectorySeparatorChar));

    public static bool IsExternalMotionSpec(AnimationSpec spec) =>
        string.Equals(spec.GuideRenderer, RendererId, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(spec.MotionClipId);

    internal static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private sealed class MotionClipManifest
    {
        public List<MotionClipDefinition> Clips { get; init; } = [];
    }
}

internal sealed class MotionClipDefinition
{
    public string ClipId { get; init; } = string.Empty;
    public List<string> Aliases { get; init; } = [];
    public string DisplayName { get; init; } = string.Empty;
    public string SourcePackage { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string AssetPath { get; init; } = string.Empty;
    public string AnimationName { get; init; } = string.Empty;
    public string? MeshNodeName { get; init; }
    public int? SkinIndex { get; init; }
    public List<string> SupportedAnimationKinds { get; init; } = [];
    public List<string> SupportedAssetTypes { get; init; } = [];
    public int DefaultFps { get; init; } = 8;
    public List<int> AllowedSampleCounts { get; init; } = [];
    public Dictionary<string, string> BoneMap { get; init; } = [];

    public bool MatchesId(string clipId)
    {
        var normalized = MotionClipCatalog.Normalize(clipId);
        return string.Equals(MotionClipCatalog.Normalize(ClipId), normalized, StringComparison.Ordinal)
            || Aliases.Any(alias => string.Equals(MotionClipCatalog.Normalize(alias), normalized, StringComparison.Ordinal));
    }

    public bool Supports(AnimationSpec spec)
    {
        var kind = MotionClipCatalog.Normalize(spec.AnimationKind);
        if (SupportedAnimationKinds.Count > 0 && !SupportedAnimationKinds.Any(item => string.Equals(MotionClipCatalog.Normalize(item), kind, StringComparison.Ordinal)))
            return false;

        var assetType = MotionClipCatalog.Normalize(spec.AssetType);
        var structure = MotionClipCatalog.Normalize(spec.StructureType);
        if (SupportedAssetTypes.Count == 0)
            return true;

        if (SupportedAssetTypes.Any(item => string.Equals(MotionClipCatalog.Normalize(item), assetType, StringComparison.Ordinal)))
            return true;

        return assetType.Contains("unit", StringComparison.Ordinal)
            || assetType.Contains("character", StringComparison.Ordinal)
            || structure.Contains("biped", StringComparison.Ordinal)
            || structure.Contains("humanoid", StringComparison.Ordinal);
    }

    public int ResolveSampleCount(int requested)
    {
        if (AllowedSampleCounts.Count == 0)
            return Math.Clamp(requested, 1, 16);

        return AllowedSampleCounts
            .Where(count => count > 0)
            .OrderBy(count => Math.Abs(count - requested))
            .ThenBy(count => count)
            .FirstOrDefault(Math.Clamp(requested, 1, 16));
    }
}
