namespace PixelChat.Art;

public sealed record AnimationSpec(
    string AnimationKind,
    string AssetType,
    string StructureType,
    string Facing,
    string RootMotion,
    int FrameCount,
    int Fps,
    int TargetCellWidth,
    int TargetCellHeight,
    bool Loop,
    IReadOnlyList<FrameSpec> Frames,
    string? GuideRenderer = null,
    string? GuideRenderStyle = null,
    string? MotionClipId = null,
    double? GuideCameraYawDegrees = null,
    string? MotionValidationProfile = null,
    string? GuideSourcePackage = null,
    string? GuideSourceLicense = null);

public sealed record FrameSpec(
    int Index,
    string PoseName,
    double Phase,
    IReadOnlyList<string> Contacts,
    int RootOffsetX,
    int RootOffsetY,
    int DurationMs,
    bool IsKeyframe,
    string GuideShape);

public sealed record LayoutSpec(
    int CanvasWidth,
    int CanvasHeight,
    int Rows,
    int Columns,
    int GuideCellWidth,
    int GuideCellHeight,
    int TargetCellWidth,
    int TargetCellHeight,
    string BackgroundColor,
    IReadOnlyList<SlotSpec> Slots);

public sealed record SlotSpec(
    int FrameIndex,
    SpriteSheetRect Rect,
    SpriteSheetPoint Root,
    int BaselineY,
    SpriteSheetRect SafeRect);
