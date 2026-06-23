namespace PixelChat.Art;

internal static class SpriteQualityInspector
{
    public static IReadOnlyList<AssetAnimationFrameStatusView> InspectRawCandidate(byte[] candidateData, LayoutSpec layout)
    {
        if (!SpriteSheetPngCodec.TryReadRgba(candidateData, out var width, out var height, out var rgba))
        {
            return layout.Slots
                .Select(slot => Failure(slot.FrameIndex, "reject", "candidate is not a readable PNG", SpriteRepairAction.RegenerateStrip))
                .ToList();
        }

        var sourceLayout = SpriteLayoutScaler.ScaleToSource(layout, width, height).Layout;
        var background = SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height, layout.BackgroundColor);
        var results = new List<AssetAnimationFrameStatusView>();
        foreach (var slot in sourceLayout.Slots)
        {
            var failures = new List<SpriteFailure>();
            if (slot.Rect.Width < 4 || slot.Rect.Height < 4 || slot.Rect.X + slot.Rect.Width > width || slot.Rect.Y + slot.Rect.Height > height)
            {
                failures.Add(SpriteFailure.MissingFrame);
            }
            else
            {
                var crop = Crop(rgba, width, height, slot.Rect);
                var bounds = SpriteSheetImageAnalyzer.ForegroundBounds(crop, slot.Rect.Width, slot.Rect.Height, background);
                if (bounds is null)
                    failures.Add(SpriteFailure.MissingFrame);
                else
                {
                    var safe = new SpriteSheetRect(
                        Math.Max(0, slot.SafeRect.X - slot.Rect.X),
                        Math.Max(0, slot.SafeRect.Y - slot.Rect.Y),
                        Math.Min(slot.Rect.Width, slot.SafeRect.Width),
                        Math.Min(slot.Rect.Height, slot.SafeRect.Height));
                    if (safe.Width > 0
                        && safe.Height > 0
                        && (bounds.X < safe.X
                            || bounds.Y < safe.Y
                            || bounds.X + bounds.Width > safe.X + safe.Width
                            || bounds.Y + bounds.Height > safe.Y + safe.Height))
                    {
                        failures.Add(SpriteFailure.SlotCrossing);
                    }

                    if (bounds.X <= 4 || bounds.Y <= 4 || bounds.X + bounds.Width >= slot.Rect.Width - 4 || bounds.Y + bounds.Height >= slot.Rect.Height - 4)
                        failures.Add(SpriteFailure.Clipped);
                    if (bounds.Width < slot.Rect.Width / 12 || bounds.Height < slot.Rect.Height / 12)
                        failures.Add(SpriteFailure.LowMotion);
                }
            }

            var action = SpriteRepairRouter.Recommend(failures);
            results.Add(failures.Count == 0
                ? new AssetAnimationFrameStatusView(slot.FrameIndex + 1, slot.FrameIndex, "pending", "", "extract_animation_fixed_slots")
                : new AssetAnimationFrameStatusView(slot.FrameIndex + 1, slot.FrameIndex, "repair_requested", string.Join(", ", failures), ToToken(action)));
        }

        return results;
    }

    public static IReadOnlyList<AssetAnimationFrameStatusView> InspectExtracted(
        SpriteFixedSlotExtractionResult extraction,
        IReadOnlyList<AssetAnimationFrameStatusView>? rawCandidateStatuses = null,
        IReadOnlySet<int>? repairedFrameIndexes = null)
    {
        var rawByIndex = rawCandidateStatuses?
            .GroupBy(status => status.Index)
            .ToDictionary(group => group.Key, group => group.First())
            ?? [];
        var repaired = repairedFrameIndexes ?? new HashSet<int>();
        var results = new List<AssetAnimationFrameStatusView>();
        foreach (var frame in extraction.Frames)
        {
            var status = frame.SpriteRect.Width <= 1 || frame.SpriteRect.Height <= 1
                ? "repair_requested"
                : "accepted";
            var reason = status == "accepted" ? "" : "no foreground after fixed-slot extraction";
            var action = status == "accepted" ? "package" : "regenerate_animation_frames";
            if (status == "accepted"
                && !repaired.Contains(frame.Index)
                && rawByIndex.TryGetValue(frame.Index, out var raw)
                && IsBlockingRawStatus(raw))
            {
                status = "repair_requested";
                reason = $"raw candidate QA: {raw.Reason}";
                action = NormalizeAction(raw.RecommendedAction);
            }

            results.Add(new AssetAnimationFrameStatusView(frame.Index + 1, frame.Index, status, reason, action));
        }

        return results;
    }

    private static AssetAnimationFrameStatusView Failure(int index, string status, string reason, SpriteRepairAction action) =>
        new(index + 1, index, status, reason, ToToken(action));

    private static bool IsBlockingRawStatus(AssetAnimationFrameStatusView raw) =>
        raw.Status.Equals("repair_requested", StringComparison.OrdinalIgnoreCase)
        || raw.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        if (normalized.Contains("strip", StringComparison.Ordinal) || normalized.Contains("switch", StringComparison.Ordinal))
            return "run_animation_candidates";
        if (normalized.Contains("frame", StringComparison.Ordinal))
            return "regenerate_animation_frames";
        return "run_animation_candidates";
    }

    private static string ToToken(SpriteRepairAction action) =>
        action switch
        {
            SpriteRepairAction.RegenerateFrame => "regenerate_animation_frames",
            SpriteRepairAction.RegenerateAdjacentFrames => "regenerate_animation_frames",
            SpriteRepairAction.RegenerateStrip => "run_animation_candidates",
            SpriteRepairAction.SwitchStrategy => "run_animation_candidates",
            SpriteRepairAction.ReextractFixedSlots => "extract_animation_fixed_slots",
            SpriteRepairAction.AcceptWithWarnings => "mark_animation_frames",
            _ => "none",
        };

    private static byte[] Crop(byte[] rgba, int width, int height, SpriteSheetRect rect)
    {
        var output = new byte[checked(rect.Width * rect.Height * 4)];
        for (var y = 0; y < rect.Height; y++)
        {
            var sourceY = rect.Y + y;
            if (sourceY < 0 || sourceY >= height)
                continue;
            for (var x = 0; x < rect.Width; x++)
            {
                var sourceX = rect.X + x;
                if (sourceX < 0 || sourceX >= width)
                    continue;
                var sourceOffset = ((sourceY * width) + sourceX) * 4;
                var targetOffset = ((y * rect.Width) + x) * 4;
                output[targetOffset] = rgba[sourceOffset];
                output[targetOffset + 1] = rgba[sourceOffset + 1];
                output[targetOffset + 2] = rgba[sourceOffset + 2];
                output[targetOffset + 3] = rgba[sourceOffset + 3];
            }
        }

        return output;
    }
}
