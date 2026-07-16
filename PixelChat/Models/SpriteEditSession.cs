namespace PixelChat.Models;

public class SpriteEditSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Status { get; set; } = "pending";
    public bool ModalOpen { get; set; }
    public string TargetKind { get; set; } = "source";
    public Guid? TargetSourceAssetId { get; set; }
    public Guid? TargetFrameSetId { get; set; }
    public Guid? TargetFrameId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? MaskId { get; set; }
    public Guid? SelectedCandidateAssetId { get; set; }
    public int? SelectedOutputIndex { get; set; }
    public bool PreviewOverlayActive { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Count { get; set; } = 2;
    public string CanvasOptionsJson { get; set; } = "{}";
    public Guid? CanvasPreparationId { get; set; }
    public string CanvasPreparationTransformJson { get; set; } = string.Empty;
    public DateTime? CanvasPreparationExpiresAt { get; set; }
    public string CropJson { get; set; } = "{}";
    public string CandidateAssetIdsJson { get; set; } = "[]";
    public string OutputStatesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
