using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PixelChat.Art;

public enum EditCanvasResampleMode
{
    NearestNeighbor,
    Smooth,
}

public sealed record EditCanvasOptions(
    int PaddingTop = 0,
    int PaddingRight = 0,
    int PaddingBottom = 0,
    int PaddingLeft = 0,
    bool AllowScaleDown = true,
    EditCanvasResampleMode ResampleMode = EditCanvasResampleMode.NearestNeighbor,
    int SeamOverlapPixels = 32)
{
    public bool HasPadding => PaddingTop > 0 || PaddingRight > 0 || PaddingBottom > 0 || PaddingLeft > 0;
}

public sealed record EditCanvasTransform(
    int OriginalWidth,
    int OriginalHeight,
    int LogicalWidth,
    int LogicalHeight,
    int ProviderWidth,
    int ProviderHeight,
    int ProviderLogicalWidth,
    int ProviderLogicalHeight,
    int SourceOffsetX,
    int SourceOffsetY,
    double Scale,
    int PaddingTop,
    int PaddingRight,
    int PaddingBottom,
    int PaddingLeft,
    int SeamOverlapPixels,
    EditCanvasResampleMode ResampleMode,
    bool UsedScaleFallback,
    Guid? OriginalMaskId = null,
    int LogicalSourceBackgroundNormalizedPixelCount = 0);

public sealed record EditCanvasFinalization(
    int ObservedProviderWidth,
    int ObservedProviderHeight,
    int ExpectedProviderWidth,
    int ExpectedProviderHeight,
    bool ProviderOutputNormalized,
    int FinalLogicalWidth,
    int FinalLogicalHeight,
    bool ScaleRestored,
    int BackgroundNormalizedPixelCount);

public sealed record PreparedEditCanvas(
    byte[] LogicalSourcePng,
    byte[]? LogicalMaskPng,
    byte[] ProviderSourcePng,
    byte[]? ProviderMaskPng,
    byte[] PreviewPng,
    EditCanvasTransform Transform,
    string OutputSize);

public sealed record FinalizedEditCanvas(
    byte[] Png,
    EditCanvasFinalization Finalization);

public interface IImageEditCanvasService
{
    EditCanvasTransform Estimate(
        int sourceWidth,
        int sourceHeight,
        EditCanvasOptions options,
        ImageProviderCapabilities capabilities);

    PreparedEditCanvas Prepare(
        byte[] sourceData,
        byte[]? maskData,
        string background,
        EditCanvasOptions options,
        ImageProviderCapabilities capabilities);

    FinalizedEditCanvas Finalize(
        byte[] generatedData,
        byte[] logicalSourcePng,
        byte[]? logicalMaskPng,
        EditCanvasTransform transform,
        string background);
}

public sealed class ImageEditCanvasService : IImageEditCanvasService
{
    private const int MaximumPaddingPerSide = 8192;
    private const int MaximumLogicalEdge = 8192;
    private const long MaximumLogicalPixels = 16_777_216;

    public EditCanvasTransform Estimate(
        int sourceWidth,
        int sourceHeight,
        EditCanvasOptions options,
        ImageProviderCapabilities capabilities)
    {
        ValidateOptions(options);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException("Edit source dimensions must be positive.");

        var logicalWidth = checked(sourceWidth + options.PaddingLeft + options.PaddingRight);
        var logicalHeight = checked(sourceHeight + options.PaddingTop + options.PaddingBottom);
        if (logicalWidth > MaximumLogicalEdge
            || logicalHeight > MaximumLogicalEdge
            || (long)logicalWidth * logicalHeight > MaximumLogicalPixels)
        {
            throw new InvalidOperationException(
                $"The logical edit canvas is too large. Keep each edge at or below {MaximumLogicalEdge:N0}px " +
                $"and the total area at or below {MaximumLogicalPixels:N0} pixels.");
        }
        var sizing = CalculateProviderSizing(logicalWidth, logicalHeight, options.AllowScaleDown, capabilities);
        return new EditCanvasTransform(
            sourceWidth,
            sourceHeight,
            logicalWidth,
            logicalHeight,
            sizing.ProviderWidth,
            sizing.ProviderHeight,
            sizing.ProviderLogicalWidth,
            sizing.ProviderLogicalHeight,
            options.PaddingLeft,
            options.PaddingTop,
            sizing.Scale,
            options.PaddingTop,
            options.PaddingRight,
            options.PaddingBottom,
            options.PaddingLeft,
            options.SeamOverlapPixels,
            options.ResampleMode,
            sizing.Scale < 0.999999d);
    }

    public PreparedEditCanvas Prepare(
        byte[] sourceData,
        byte[]? maskData,
        string background,
        EditCanvasOptions options,
        ImageProviderCapabilities capabilities)
    {
        ValidateOptions(options);
        if (!ImageRgbaDecoder.TryReadRgba(sourceData, out var sourceWidth, out var sourceHeight, out var sourceRgba))
            throw new InvalidOperationException("Edit canvas preparation requires a readable PNG or JPEG source image.");

        byte[]? maskRgba = null;
        if (maskData is not null)
        {
            if (!ImageRgbaDecoder.TryReadRgba(maskData, out var maskWidth, out var maskHeight, out maskRgba)
                || maskWidth != sourceWidth
                || maskHeight != sourceHeight)
            {
                throw new InvalidOperationException("The edit mask must match the source image dimensions before canvas preparation.");
            }
        }

        var transform = Estimate(sourceWidth, sourceHeight, options, capabilities);
        var fill = ResolveFill(sourceRgba, sourceWidth, sourceHeight, background);

        // Logical space is the authoritative output space. The source is never scaled here.
        var logicalSource = NewFilledCanvas(transform.LogicalWidth, transform.LogicalHeight, fill);
        Blit(
            sourceRgba,
            sourceWidth,
            sourceHeight,
            logicalSource,
            transform.LogicalWidth,
            transform.LogicalHeight,
            transform.SourceOffsetX,
            transform.SourceOffsetY);
        if (string.Equals(background?.Trim(), "removable", StringComparison.OrdinalIgnoreCase))
        {
            transform = transform with
            {
                LogicalSourceBackgroundNormalizedPixelCount = FloodNormalizeEditableMagenta(
                    logicalSource,
                    mask: null,
                    transform.LogicalWidth,
                    transform.LogicalHeight),
            };
        }

        byte[]? logicalMask = null;
        if (maskRgba is not null || options.HasPadding)
        {
            logicalMask = NewMaskCanvas(transform.LogicalWidth, transform.LogicalHeight, editable: true);
            if (maskRgba is null)
            {
                FillMaskRect(
                    logicalMask,
                    transform.LogicalWidth,
                    transform.LogicalHeight,
                    transform.SourceOffsetX,
                    transform.SourceOffsetY,
                    sourceWidth,
                    sourceHeight,
                    alpha: byte.MaxValue);
            }
            else
            {
                Blit(
                    maskRgba,
                    sourceWidth,
                    sourceHeight,
                    logicalMask,
                    transform.LogicalWidth,
                    transform.LogicalHeight,
                    transform.SourceOffsetX,
                    transform.SourceOffsetY);
            }

            BinarizeAndDilateEditableRegion(
                logicalMask,
                transform.LogicalWidth,
                transform.LogicalHeight,
                options.SeamOverlapPixels);
        }

        var providerSource = NewFilledCanvas(transform.ProviderWidth, transform.ProviderHeight, fill);
        var providerLogicalSource = transform.ProviderLogicalWidth == transform.LogicalWidth
            && transform.ProviderLogicalHeight == transform.LogicalHeight
                ? logicalSource
                : ResizeRgba(
                    logicalSource,
                    transform.LogicalWidth,
                    transform.LogicalHeight,
                    transform.ProviderLogicalWidth,
                    transform.ProviderLogicalHeight,
                    options.ResampleMode,
                    isMask: false);
        Blit(
            providerLogicalSource,
            transform.ProviderLogicalWidth,
            transform.ProviderLogicalHeight,
            providerSource,
            transform.ProviderWidth,
            transform.ProviderHeight,
            0,
            0);

        byte[]? providerMask = null;
        if (logicalMask is not null)
        {
            providerMask = NewMaskCanvas(transform.ProviderWidth, transform.ProviderHeight, editable: true);
            var providerLogicalMask = transform.ProviderLogicalWidth == transform.LogicalWidth
                && transform.ProviderLogicalHeight == transform.LogicalHeight
                    ? logicalMask
                    : ResizeRgba(
                        logicalMask,
                        transform.LogicalWidth,
                        transform.LogicalHeight,
                        transform.ProviderLogicalWidth,
                        transform.ProviderLogicalHeight,
                        EditCanvasResampleMode.NearestNeighbor,
                        isMask: true);
            Blit(
                providerLogicalMask,
                transform.ProviderLogicalWidth,
                transform.ProviderLogicalHeight,
                providerMask,
                transform.ProviderWidth,
                transform.ProviderHeight,
                0,
                0);
        }

        var logicalSourcePng = SpriteSheetPngCodec.EncodeRgba(transform.LogicalWidth, transform.LogicalHeight, logicalSource);
        var logicalMaskPng = logicalMask is null
            ? null
            : SpriteSheetPngCodec.EncodeRgba(transform.LogicalWidth, transform.LogicalHeight, logicalMask);
        return new PreparedEditCanvas(
            logicalSourcePng,
            logicalMaskPng,
            SpriteSheetPngCodec.EncodeRgba(transform.ProviderWidth, transform.ProviderHeight, providerSource),
            providerMask is null ? null : SpriteSheetPngCodec.EncodeRgba(transform.ProviderWidth, transform.ProviderHeight, providerMask),
            BuildPreviewPng(logicalSource, logicalMask, transform.LogicalWidth, transform.LogicalHeight),
            transform,
            $"{transform.ProviderWidth}x{transform.ProviderHeight}");
    }

    public FinalizedEditCanvas Finalize(
        byte[] generatedData,
        byte[] logicalSourcePng,
        byte[]? logicalMaskPng,
        EditCanvasTransform transform,
        string background)
    {
        if (!ImageRgbaDecoder.TryReadRgba(generatedData, out var observedWidth, out var observedHeight, out var generated))
            throw new InvalidOperationException("Generated edit output could not be decoded for canvas finalization.");
        if (!ImageRgbaDecoder.TryReadRgba(logicalSourcePng, out var sourceWidth, out var sourceHeight, out var logicalSource)
            || sourceWidth != transform.LogicalWidth
            || sourceHeight != transform.LogicalHeight)
        {
            throw new InvalidOperationException("The logical edit source snapshot is missing or does not match its canvas transform.");
        }

        byte[]? logicalMask = null;
        if (logicalMaskPng is not null)
        {
            if (!ImageRgbaDecoder.TryReadRgba(logicalMaskPng, out var maskWidth, out var maskHeight, out logicalMask)
                || maskWidth != transform.LogicalWidth
                || maskHeight != transform.LogicalHeight)
            {
                throw new InvalidOperationException("The logical edit mask snapshot is missing or does not match its canvas transform.");
            }
        }

        var providerOutputNormalized = observedWidth != transform.ProviderWidth || observedHeight != transform.ProviderHeight;
        if (providerOutputNormalized)
        {
            generated = ResizeRgba(
                generated,
                observedWidth,
                observedHeight,
                transform.ProviderWidth,
                transform.ProviderHeight,
                transform.ResampleMode,
                isMask: false);
        }

        var providerLogical = CropRgba(
            generated,
            transform.ProviderWidth,
            transform.ProviderHeight,
            0,
            0,
            transform.ProviderLogicalWidth,
            transform.ProviderLogicalHeight);
        var logicalGenerated = transform.ProviderLogicalWidth == transform.LogicalWidth
            && transform.ProviderLogicalHeight == transform.LogicalHeight
                ? providerLogical
                : ResizeRgba(
                    providerLogical,
                    transform.ProviderLogicalWidth,
                    transform.ProviderLogicalHeight,
                    transform.LogicalWidth,
                    transform.LogicalHeight,
                    transform.ResampleMode,
                    isMask: false);

        if (logicalMask is not null)
            CompositePreservedLogicalPixels(logicalGenerated, logicalSource, logicalMask);

        var normalizedBackgroundPixels = string.Equals(background?.Trim(), "removable", StringComparison.OrdinalIgnoreCase)
            ? FloodNormalizeEditableMagenta(logicalGenerated, logicalMask, transform.LogicalWidth, transform.LogicalHeight)
            : 0;

        return new FinalizedEditCanvas(
            SpriteSheetPngCodec.EncodeRgba(transform.LogicalWidth, transform.LogicalHeight, logicalGenerated),
            new EditCanvasFinalization(
                observedWidth,
                observedHeight,
                transform.ProviderWidth,
                transform.ProviderHeight,
                providerOutputNormalized,
                transform.LogicalWidth,
                transform.LogicalHeight,
                transform.ProviderLogicalWidth != transform.LogicalWidth
                    || transform.ProviderLogicalHeight != transform.LogicalHeight,
                checked(transform.LogicalSourceBackgroundNormalizedPixelCount + normalizedBackgroundPixels)));
    }

    public static bool TryValidateOptions(EditCanvasOptions options, out string error)
    {
        var paddings = new[] { options.PaddingTop, options.PaddingRight, options.PaddingBottom, options.PaddingLeft };
        if (paddings.Any(value => value < 0 || value > MaximumPaddingPerSide))
        {
            error = $"Canvas padding must be between 0 and {MaximumPaddingPerSide} pixels per side.";
            return false;
        }
        if (options.SeamOverlapPixels < 0 || options.SeamOverlapPixels > 1024)
        {
            error = "Canvas seam overlap must be between 0 and 1024 pixels.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void ValidateOptions(EditCanvasOptions options)
    {
        if (!TryValidateOptions(options, out var error))
            throw new InvalidOperationException(error);
    }

    private static ProviderSizing CalculateProviderSizing(
        int logicalWidth,
        int logicalHeight,
        bool allowScaleDown,
        ImageProviderCapabilities capabilities)
    {
        var constraints = capabilities.SizeConstraints;
        var reliableMaximumPixels = capabilities.ReliableEditMaximumPixels is > 0
            ? Math.Min(constraints.MaximumPixels, capabilities.ReliableEditMaximumPixels.Value)
            : constraints.MaximumPixels;

        var sizing = SizeAtScale(logicalWidth, logicalHeight, 1d, constraints);
        if (FitsMaximum(sizing, constraints, reliableMaximumPixels))
        {
            if ((long)sizing.ProviderWidth * sizing.ProviderHeight >= constraints.MinimumPixels)
                return sizing;

            var low = 1d;
            var high = Math.Max(1d, Math.Sqrt(constraints.MinimumPixels / (double)Math.Max(1L, (long)logicalWidth * logicalHeight)) * 1.1d);
            while ((long)SizeAtScale(logicalWidth, logicalHeight, high, constraints).ProviderWidth
                    * SizeAtScale(logicalWidth, logicalHeight, high, constraints).ProviderHeight < constraints.MinimumPixels)
            {
                high *= 1.25d;
            }

            for (var iteration = 0; iteration < 64; iteration++)
            {
                var mid = (low + high) / 2d;
                var candidate = SizeAtScale(logicalWidth, logicalHeight, mid, constraints);
                if ((long)candidate.ProviderWidth * candidate.ProviderHeight >= constraints.MinimumPixels)
                    high = mid;
                else
                    low = mid;
            }

            sizing = SizeAtScale(logicalWidth, logicalHeight, high, constraints);
            if (!FitsMaximum(sizing, constraints, reliableMaximumPixels))
                throw new InvalidOperationException("The edit canvas cannot satisfy the image provider's minimum and maximum size constraints.");
            return sizing;
        }

        if (!allowScaleDown)
            throw new InvalidOperationException("The expanded edit canvas exceeds the reliable image-provider edit budget. Enable scale fallback or reduce padding.");

        var lower = 0d;
        var upper = 1d;
        for (var iteration = 0; iteration < 72; iteration++)
        {
            var mid = (lower + upper) / 2d;
            var candidate = SizeAtScale(logicalWidth, logicalHeight, mid, constraints);
            if (candidate.ProviderLogicalWidth > 0
                && candidate.ProviderLogicalHeight > 0
                && FitsMaximum(candidate, constraints, reliableMaximumPixels))
            {
                lower = mid;
            }
            else
            {
                upper = mid;
            }
        }

        sizing = SizeAtScale(logicalWidth, logicalHeight, lower, constraints);
        while (!FitsMaximum(sizing, constraints, reliableMaximumPixels) && lower > 0d)
        {
            lower = Math.Max(0d, lower - 0.000001d);
            sizing = SizeAtScale(logicalWidth, logicalHeight, lower, constraints);
        }
        if (sizing.ProviderLogicalWidth <= 0
            || sizing.ProviderLogicalHeight <= 0
            || (long)sizing.ProviderWidth * sizing.ProviderHeight < constraints.MinimumPixels
            || !FitsMaximum(sizing, constraints, reliableMaximumPixels))
        {
            throw new InvalidOperationException("The expanded edit canvas cannot fit within the image provider limits.");
        }

        return sizing;
    }

    private static ProviderSizing SizeAtScale(
        int logicalWidth,
        int logicalHeight,
        double scale,
        ImageSizeConstraints constraints)
    {
        var providerLogicalWidth = Math.Max(1, (int)Math.Round(logicalWidth * scale, MidpointRounding.AwayFromZero));
        var providerLogicalHeight = Math.Max(1, (int)Math.Round(logicalHeight * scale, MidpointRounding.AwayFromZero));
        var providerWidth = RoundUp(providerLogicalWidth, constraints.DimensionMultiple);
        var providerHeight = RoundUp(providerLogicalHeight, constraints.DimensionMultiple);
        NormalizeAspect(ref providerWidth, ref providerHeight, constraints);
        var effectiveScale = Math.Min(
            providerLogicalWidth / (double)logicalWidth,
            providerLogicalHeight / (double)logicalHeight);
        return new ProviderSizing(
            providerWidth,
            providerHeight,
            providerLogicalWidth,
            providerLogicalHeight,
            effectiveScale);
    }

    private static bool FitsMaximum(ProviderSizing sizing, ImageSizeConstraints constraints, long maximumPixels) =>
        sizing.ProviderWidth <= constraints.MaximumEdge
        && sizing.ProviderHeight <= constraints.MaximumEdge
        && (long)sizing.ProviderWidth * sizing.ProviderHeight <= maximumPixels;

    private static void NormalizeAspect(ref int width, ref int height, ImageSizeConstraints constraints)
    {
        if (width > height * constraints.MaximumAspectRatio)
            height = RoundUp((int)Math.Ceiling(width / constraints.MaximumAspectRatio), constraints.DimensionMultiple);
        else if (height > width * constraints.MaximumAspectRatio)
            width = RoundUp((int)Math.Ceiling(height / constraints.MaximumAspectRatio), constraints.DimensionMultiple);
    }

    private static SpriteSheetBackground ResolveFill(byte[] rgba, int width, int height, string background)
    {
        if (string.Equals(background?.Trim(), "removable", StringComparison.OrdinalIgnoreCase))
            return new SpriteSheetBackground("color", 255, 0, 255, 255);
        return SpriteSheetImageAnalyzer.ResolveBackground(rgba, width, height);
    }

    private static byte[] NewFilledCanvas(int width, int height, SpriteSheetBackground fill)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = fill.R;
            rgba[offset + 1] = fill.G;
            rgba[offset + 2] = fill.B;
            rgba[offset + 3] = fill.A;
        }
        return rgba;
    }

    private static byte[] NewMaskCanvas(int width, int height, bool editable)
    {
        var rgba = new byte[checked(width * height * 4)];
        var alpha = editable ? (byte)0 : byte.MaxValue;
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = byte.MaxValue;
            rgba[offset + 1] = byte.MaxValue;
            rgba[offset + 2] = byte.MaxValue;
            rgba[offset + 3] = alpha;
        }
        return rgba;
    }

    private static void BinarizeAndDilateEditableRegion(byte[] mask, int width, int height, int radius)
    {
        var horizontal = new bool[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            var editableCount = 0;
            for (var x = 0; x <= Math.Min(width - 1, radius); x++)
            {
                if (mask[((y * width + x) * 4) + 3] < 128)
                    editableCount++;
            }

            for (var x = 0; x < width; x++)
            {
                horizontal[(y * width) + x] = editableCount > 0;
                var leaving = x - radius;
                if (leaving >= 0 && mask[((y * width + leaving) * 4) + 3] < 128)
                    editableCount--;
                var entering = x + radius + 1;
                if (entering < width && mask[((y * width + entering) * 4) + 3] < 128)
                    editableCount++;
            }
        }

        var verticalCounts = new int[width];
        for (var y = 0; y <= Math.Min(height - 1, radius); y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (horizontal[row + x])
                    verticalCounts[x]++;
            }
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var offset = (row + x) * 4;
                mask[offset] = byte.MaxValue;
                mask[offset + 1] = byte.MaxValue;
                mask[offset + 2] = byte.MaxValue;
                mask[offset + 3] = verticalCounts[x] > 0 ? (byte)0 : byte.MaxValue;
            }

            var leaving = y - radius;
            if (leaving >= 0)
            {
                var leavingRow = leaving * width;
                for (var x = 0; x < width; x++)
                {
                    if (horizontal[leavingRow + x])
                        verticalCounts[x]--;
                }
            }
            var entering = y + radius + 1;
            if (entering < height)
            {
                var enteringRow = entering * width;
                for (var x = 0; x < width; x++)
                {
                    if (horizontal[enteringRow + x])
                        verticalCounts[x]++;
                }
            }
        }
    }

    private static byte[] BuildPreviewPng(byte[] source, byte[]? mask, int width, int height)
    {
        var preview = source.ToArray();
        if (mask is null)
            return SpriteSheetPngCodec.EncodeRgba(width, height, preview);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                if (mask[offset + 3] != 0)
                    continue;

                var boundary = HasPreservedNeighbor(mask, width, height, x, y);
                var overlayR = boundary ? 255 : 0;
                var overlayG = boundary ? 215 : 210;
                var overlayB = boundary ? 0 : 255;
                var overlayWeight = boundary ? 170 : 82;
                preview[offset] = Blend(preview[offset], overlayR, overlayWeight);
                preview[offset + 1] = Blend(preview[offset + 1], overlayG, overlayWeight);
                preview[offset + 2] = Blend(preview[offset + 2], overlayB, overlayWeight);
                preview[offset + 3] = byte.MaxValue;
            }
        }

        return SpriteSheetPngCodec.EncodeRgba(width, height, preview);
    }

    private static bool HasPreservedNeighbor(byte[] mask, int width, int height, int x, int y)
    {
        if (x > 0 && mask[(((y * width) + x - 1) * 4) + 3] != 0)
            return true;
        if (x + 1 < width && mask[(((y * width) + x + 1) * 4) + 3] != 0)
            return true;
        if (y > 0 && mask[((((y - 1) * width) + x) * 4) + 3] != 0)
            return true;
        return y + 1 < height && mask[((((y + 1) * width) + x) * 4) + 3] != 0;
    }

    private static byte Blend(byte original, int overlay, int overlayWeight) =>
        (byte)((original * (255 - overlayWeight) + overlay * overlayWeight + 127) / 255);

    private static void CompositePreservedLogicalPixels(byte[] generated, byte[] source, byte[] mask)
    {
        for (var offset = 0; offset < generated.Length; offset += 4)
        {
            var preserve = mask[offset + 3];
            if (preserve == 0)
                continue;
            if (preserve == byte.MaxValue)
            {
                Buffer.BlockCopy(source, offset, generated, offset, 4);
                continue;
            }

            var generate = byte.MaxValue - preserve;
            for (var channel = 0; channel < 4; channel++)
                generated[offset + channel] = (byte)((source[offset + channel] * preserve + generated[offset + channel] * generate + 127) / 255);
        }
    }

    private static int FloodNormalizeEditableMagenta(byte[] rgba, byte[]? mask, int width, int height)
    {
        var visited = new bool[checked(width * height)];
        var queue = new Queue<int>();

        void TryEnqueue(int x, int y)
        {
            var pixel = (y * width) + x;
            if (visited[pixel])
                return;
            visited[pixel] = true;
            var offset = pixel * 4;
            if (mask is not null && mask[offset + 3] != 0)
                return;
            if (!IsMagentaFamily(rgba[offset], rgba[offset + 1], rgba[offset + 2]))
                return;
            queue.Enqueue(pixel);
        }

        for (var x = 0; x < width; x++)
        {
            TryEnqueue(x, 0);
            if (height > 1)
                TryEnqueue(x, height - 1);
        }
        for (var y = 1; y + 1 < height; y++)
        {
            TryEnqueue(0, y);
            if (width > 1)
                TryEnqueue(width - 1, y);
        }

        var normalized = 0;
        while (queue.TryDequeue(out var pixel))
        {
            var x = pixel % width;
            var y = pixel / width;
            var offset = pixel * 4;
            if (rgba[offset] != byte.MaxValue || rgba[offset + 1] != 0 || rgba[offset + 2] != byte.MaxValue || rgba[offset + 3] != byte.MaxValue)
                normalized++;
            rgba[offset] = byte.MaxValue;
            rgba[offset + 1] = 0;
            rgba[offset + 2] = byte.MaxValue;
            rgba[offset + 3] = byte.MaxValue;

            if (x > 0)
                TryEnqueue(x - 1, y);
            if (x + 1 < width)
                TryEnqueue(x + 1, y);
            if (y > 0)
                TryEnqueue(x, y - 1);
            if (y + 1 < height)
                TryEnqueue(x, y + 1);
        }

        return normalized;
    }

    private static bool IsMagentaFamily(byte red, byte green, byte blue) =>
        red >= 160 && blue >= 160 && green <= 112 && red + blue >= green * 3;

    private static void FillMaskRect(byte[] rgba, int width, int height, int x, int y, int rectWidth, int rectHeight, byte alpha)
    {
        var left = Math.Clamp(x, 0, width);
        var top = Math.Clamp(y, 0, height);
        var right = Math.Clamp(x + rectWidth, left, width);
        var bottom = Math.Clamp(y + rectHeight, top, height);
        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var offset = ((py * width) + px) * 4;
                rgba[offset] = byte.MaxValue;
                rgba[offset + 1] = byte.MaxValue;
                rgba[offset + 2] = byte.MaxValue;
                rgba[offset + 3] = alpha;
            }
        }
    }

    private static void Blit(byte[] source, int sourceWidth, int sourceHeight, byte[] destination, int destinationWidth, int destinationHeight, int offsetX, int offsetY)
    {
        for (var y = 0; y < sourceHeight; y++)
        {
            var destinationY = y + offsetY;
            if (destinationY < 0 || destinationY >= destinationHeight)
                continue;
            for (var x = 0; x < sourceWidth; x++)
            {
                var destinationX = x + offsetX;
                if (destinationX < 0 || destinationX >= destinationWidth)
                    continue;
                var sourceOffset = ((y * sourceWidth) + x) * 4;
                var destinationOffset = ((destinationY * destinationWidth) + destinationX) * 4;
                Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, 4);
            }
        }
    }

    private static byte[] CropRgba(byte[] rgba, int width, int height, int x, int y, int cropWidth, int cropHeight)
    {
        if (x < 0 || y < 0 || cropWidth <= 0 || cropHeight <= 0 || x + cropWidth > width || y + cropHeight > height)
            throw new InvalidOperationException("The provider-logical bounds fall outside the expected provider canvas.");
        var output = new byte[checked(cropWidth * cropHeight * 4)];
        for (var row = 0; row < cropHeight; row++)
        {
            var sourceOffset = (((y + row) * width) + x) * 4;
            var destinationOffset = row * cropWidth * 4;
            Buffer.BlockCopy(rgba, sourceOffset, output, destinationOffset, cropWidth * 4);
        }
        return output;
    }

    private static byte[] ResizeRgba(byte[] rgba, int width, int height, int outputWidth, int outputHeight, EditCanvasResampleMode mode, bool isMask)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        var sampler = mode == EditCanvasResampleMode.NearestNeighbor || isMask
            ? KnownResamplers.NearestNeighbor
            : KnownResamplers.Lanczos3;
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(outputWidth, outputHeight),
            Mode = ResizeMode.Stretch,
            Sampler = sampler,
        }));
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        image.CopyPixelDataTo(output);
        return output;
    }

    private static int RoundUp(int value, int multiple) => checked(((value + multiple - 1) / multiple) * multiple);

    private sealed record ProviderSizing(
        int ProviderWidth,
        int ProviderHeight,
        int ProviderLogicalWidth,
        int ProviderLogicalHeight,
        double Scale);
}
