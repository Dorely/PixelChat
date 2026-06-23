namespace PixelChat.Models;

public class AssetProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid CanonicalAssetId { get; set; }
    public ArtAsset CanonicalAsset { get; set; } = null!;

    public Guid? StyleAssetId { get; set; }
    public ArtAsset? StyleAsset { get; set; }

    public string Label { get; set; } = string.Empty;
    public string AssetType { get; set; } = "unit";
    public string StructureType { get; set; } = "biped";
    public string ChromaColor { get; set; } = "#ff00ff";
    public string PaletteJson { get; set; } = "[]";
    public string RequiredFeaturesJson { get; set; } = "[]";
    public string ForbiddenChangesJson { get; set; } = "[]";
    public bool Frozen { get; set; } = true;

    public ICollection<AssetAnimationJob> AnimationJobs { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
