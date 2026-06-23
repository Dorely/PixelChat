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

        var background = SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height, layout.BackgroundColor);
        var results = new List<AssetAnimationFrameStatusView>();
        foreach (var slot in layout.Slots)
        {
            var failures = new List<SpriteFailure>();
            if (slot.Rect.X + slot.Rect.Width > width || slot.Rect.Y + slot.Rect.Height > height)
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
                    if (bounds.X <= 4 || bounds.Y <= 4 || bounds.X + bounds.Width >= slot.Rect.Width - 4 || bounds.Y + bounds.Height >= slot.Rect.Height - 4)
                        failures.Add(SpriteFailure.Clipped);
                    if (bounds.Width < slot.Rect.Width / 12 || bounds.Height < slot.Rect.Height / 12)
                        failures.Add(SpriteFailure.LowMotion);
                }
            }

            var action = SpriteRepairRouter.Recommend(failures);
            results.Add(failures.Count == 0
                ? new AssetAnimationFrameStatusView(slot.FrameIndex + 1, slot.FrameIndex, "pending", "", "extract_fixed_slots")
                : new AssetAnimationFrameStatusView(slot.FrameIndex + 1, slot.FrameIndex, "repair_requested", string.Join(", ", failures), ToToken(action)));
        }

        return results;
    }

    public static IReadOnlyList<AssetAnimationFrameStatusView> InspectExtracted(SpriteFixedSlotExtractionResult extraction)
    {
        var results = new List<AssetAnimationFrameStatusView>();
        foreach (var frame in extraction.Frames)
        {
            var status = frame.SpriteRect.Width <= 1 || frame.SpriteRect.Height <= 1
                ? "repair_requested"
                : "accepted";
            var reason = status == "accepted" ? "" : "no foreground after fixed-slot extraction";
            results.Add(new AssetAnimationFrameStatusView(frame.Index + 1, frame.Index, status, reason, status == "accepted" ? "package" : "regenerate_frame"));
        }

        return results;
    }

    private static AssetAnimationFrameStatusView Failure(int index, string status, string reason, SpriteRepairAction action) =>
        new(index + 1, index, status, reason, ToToken(action));

    private static string ToToken(SpriteRepairAction action) =>
        action.ToString().ToLowerInvariant();

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
