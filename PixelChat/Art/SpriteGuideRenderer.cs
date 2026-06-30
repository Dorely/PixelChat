namespace PixelChat.Art;

internal static class SpriteGuideRenderer
{
    public static byte[] Render(LayoutSpec layout, AnimationSpec animation, bool diagnostic = false)
    {
        var rgba = NewCanvas(layout.CanvasWidth, layout.CanvasHeight, 248, 250, 252, 255);
        foreach (var slot in layout.Slots)
        {
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, 180, 188, 198, diagnostic ? (byte)255 : (byte)120, 2);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.SafeRect, 126, 148, 166, diagnostic ? (byte)220 : (byte)90, 2);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect.X + 8, slot.BaselineY, slot.Rect.X + slot.Rect.Width - 8, slot.BaselineY, 70, 120, 170, diagnostic ? (byte)180 : (byte)95);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.SafeRect.Y, slot.Root.X, slot.BaselineY, 120, 120, 120, diagnostic ? (byte)160 : (byte)70);
            DrawCircle(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.Root.Y, Math.Max(5, slot.Rect.Width / 96), 40, 90, 150, diagnostic ? (byte)255 : (byte)130);

            var frame = animation.Frames.FirstOrDefault(item => item.Index == slot.FrameIndex);
            if (frame is not null)
                DrawGuideShape(rgba, layout.CanvasWidth, layout.CanvasHeight, slot, frame, animation, diagnostic);
        }

        return SpriteSheetPngCodec.EncodeRgba(layout.CanvasWidth, layout.CanvasHeight, rgba);
    }

    public static byte[] RenderLayoutOnly(LayoutSpec layout, bool diagnostic = false)
    {
        var rgba = NewCanvas(layout.CanvasWidth, layout.CanvasHeight, 248, 250, 252, 255);
        var activeSlots = layout.Slots.ToDictionary(slot => slot.FrameIndex);
        var slotCount = Math.Max(layout.Rows * layout.Columns, activeSlots.Count);
        for (var index = 0; index < slotCount; index++)
        {
            var gridSlot = CellRectForGuideGrid(index, layout.Columns, layout.Rows, layout.CanvasWidth, layout.CanvasHeight);
            FillRect(rgba, layout.CanvasWidth, layout.CanvasHeight, gridSlot, 255, 255, 255, 45);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, gridSlot, 17, 24, 39, diagnostic ? (byte)235 : (byte)185, 2);
            if (!activeSlots.TryGetValue(index, out var slot))
                continue;

            FillRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, 226, 232, 240, diagnostic ? (byte)70 : (byte)42);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, 15, 23, 42, diagnostic ? (byte)255 : (byte)235, 3);
            DrawDashedRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.SafeRect, 71, 85, 105, diagnostic ? (byte)230 : (byte)190, 2, 12, 8);
            DrawLabelBadge(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, index + 1, diagnostic);
        }

        return SpriteSheetPngCodec.EncodeRgba(layout.CanvasWidth, layout.CanvasHeight, rgba);
    }

    private static void DrawGuideShape(byte[] rgba, int width, int height, SlotSpec slot, FrameSpec frame, AnimationSpec animation, bool diagnostic)
    {
        var alpha = diagnostic ? (byte)210 : (byte)95;
        var rootX = slot.Root.X + frame.RootOffsetX;
        var rootY = slot.Root.Y + frame.RootOffsetY;
        if (frame.GuideShape is "tower_rotate")
        {
            var radius = Math.Min(slot.SafeRect.Width, slot.SafeRect.Height) / 4;
            DrawCircle(rgba, width, height, rootX, rootY - radius / 3, radius, 92, 132, 168, (byte)(alpha / 2));
            var angle = (Math.PI * 2d * frame.Phase) - Math.PI / 2d;
            var endX = rootX + (int)Math.Round(Math.Cos(angle) * radius * 1.4d);
            var endY = rootY - radius / 3 + (int)Math.Round(Math.Sin(angle) * radius * 1.4d);
            DrawLine(rgba, width, height, rootX, rootY - radius / 3, endX, endY, 70, 98, 138, alpha);
            DrawCircle(rgba, width, height, rootX, rootY - radius / 3, Math.Max(5, radius / 5), 70, 98, 138, alpha);
            return;
        }

        if (frame.GuideShape is "tower_fire")
        {
            var bodyW = slot.SafeRect.Width / 3;
            var bodyH = slot.SafeRect.Height / 3;
            DrawRect(rgba, width, height, new SpriteSheetRect(rootX - bodyW / 2, rootY - bodyH, bodyW, bodyH), 100, 132, 160, alpha, 3);
            var recoil = frame.PoseName.Contains("recoil", StringComparison.OrdinalIgnoreCase) ? -bodyW / 8 : 0;
            DrawLine(rgba, width, height, rootX + recoil, rootY - bodyH / 2, slot.SafeRect.X + slot.SafeRect.Width - bodyW / 6, rootY - bodyH / 2, 70, 98, 138, alpha);
            return;
        }

        if (frame.GuideShape is "radial_vfx")
        {
            var radius = Math.Max(8, (int)Math.Round(Math.Min(slot.SafeRect.Width, slot.SafeRect.Height) * (0.12d + (frame.Phase * 0.42d))));
            DrawCircle(rgba, width, height, rootX, rootY - slot.SafeRect.Height / 4, radius, 210, 120, 60, alpha);
            return;
        }

        if (frame.GuideShape is "projectile")
        {
            DrawLine(rgba, width, height, slot.SafeRect.X + slot.SafeRect.Width / 5, rootY - slot.SafeRect.Height / 3, slot.SafeRect.X + slot.SafeRect.Width * 4 / 5, rootY - slot.SafeRect.Height / 3, 80, 120, 180, alpha);
            DrawCircle(rgba, width, height, slot.SafeRect.X + slot.SafeRect.Width * 4 / 5, rootY - slot.SafeRect.Height / 3, Math.Max(5, slot.SafeRect.Width / 28), 80, 120, 180, alpha);
            return;
        }

        var headRadius = Math.Max(10, slot.SafeRect.Width / 16);
        var headX = rootX;
        var headY = slot.SafeRect.Y + slot.SafeRect.Height / 5 + frame.RootOffsetY;
        var torsoY = headY + headRadius * 2;
        var hipY = rootY - slot.SafeRect.Height / 5;
        var stride = (int)Math.Round(slot.SafeRect.Width * 0.12d * Math.Sin(frame.Phase * Math.PI * 2d));
        if (frame.PoseName.Contains("left", StringComparison.OrdinalIgnoreCase))
            stride = -Math.Abs(stride == 0 ? slot.SafeRect.Width / 10 : stride);
        if (frame.PoseName.Contains("right", StringComparison.OrdinalIgnoreCase))
            stride = Math.Abs(stride == 0 ? slot.SafeRect.Width / 10 : stride);
        var compression = frame.PoseName.Contains("down", StringComparison.OrdinalIgnoreCase) ? headRadius : 0;
        DrawCircle(rgba, width, height, headX, headY + compression, headRadius, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX, torsoY + compression, headX, hipY + compression, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX - slot.SafeRect.Width / 10, torsoY + compression, headX + slot.SafeRect.Width / 10, torsoY + compression, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX, hipY + compression, headX + stride, slot.BaselineY, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX, hipY + compression, headX - stride, slot.BaselineY - Math.Abs(stride) / 4, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX - slot.SafeRect.Width / 10, torsoY + compression, headX - stride / 2, hipY + compression, 94, 124, 164, alpha);
        DrawLine(rgba, width, height, headX + slot.SafeRect.Width / 10, torsoY + compression, headX + stride / 2, hipY + compression, 94, 124, 164, alpha);

        var cue = SpriteFacing.IsLeftFacing(animation.Facing) ? -1 : 1;
        DrawLine(rgba, width, height, headX, headY + compression, headX + cue * headRadius * 2, headY + compression, 70, 100, 140, alpha);
    }

    private static byte[] NewCanvas(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = r;
            rgba[index + 1] = g;
            rgba[index + 2] = b;
            rgba[index + 3] = a;
        }

        return rgba;
    }

    private static SpriteSheetRect CellRectForGuideGrid(int index, int columns, int rows, int canvasWidth, int canvasHeight)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        var row = Math.Clamp(index / columns, 0, rows - 1);
        var col = Math.Clamp(index % columns, 0, columns - 1);
        var x0 = col * canvasWidth / columns;
        var x1 = (col + 1) * canvasWidth / columns;
        var y0 = row * canvasHeight / rows;
        var y1 = (row + 1) * canvasHeight / rows;
        return new SpriteSheetRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private static void FillRect(byte[] rgba, int width, int height, SpriteSheetRect rect, byte r, byte g, byte b, byte a)
    {
        var x0 = Math.Clamp(rect.X, 0, width);
        var y0 = Math.Clamp(rect.Y, 0, height);
        var x1 = Math.Clamp(rect.X + rect.Width, 0, width);
        var y1 = Math.Clamp(rect.Y + rect.Height, 0, height);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
                Blend(rgba, width, height, x, y, r, g, b, a);
        }
    }

    private static void DrawDashedRect(
        byte[] rgba,
        int width,
        int height,
        SpriteSheetRect rect,
        byte r,
        byte g,
        byte b,
        byte a,
        int thickness,
        int dash,
        int gap)
    {
        DrawDashedLine(rgba, width, height, rect.X, rect.Y, rect.X + rect.Width - 1, rect.Y, r, g, b, a, thickness, dash, gap);
        DrawDashedLine(rgba, width, height, rect.X, rect.Y + rect.Height - 1, rect.X + rect.Width - 1, rect.Y + rect.Height - 1, r, g, b, a, thickness, dash, gap);
        DrawDashedLine(rgba, width, height, rect.X, rect.Y, rect.X, rect.Y + rect.Height - 1, r, g, b, a, thickness, dash, gap);
        DrawDashedLine(rgba, width, height, rect.X + rect.Width - 1, rect.Y, rect.X + rect.Width - 1, rect.Y + rect.Height - 1, r, g, b, a, thickness, dash, gap);
    }

    private static void DrawDashedLine(
        byte[] rgba,
        int width,
        int height,
        int x1,
        int y1,
        int x2,
        int y2,
        byte r,
        byte g,
        byte b,
        byte a,
        int thickness,
        int dash,
        int gap)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps <= 0)
            return;

        var period = Math.Max(1, dash + gap);
        for (var step = 0; step <= steps; step++)
        {
            if (step % period >= dash)
                continue;

            var x = x1 + (int)Math.Round(dx * (step / (double)steps));
            var y = y1 + (int)Math.Round(dy * (step / (double)steps));
            DrawCircle(rgba, width, height, x, y, Math.Max(1, thickness), r, g, b, a);
        }
    }

    private static void DrawLabelBadge(byte[] rgba, int width, int height, SpriteSheetRect frameRect, int frameNumber, bool diagnostic)
    {
        var scale = Math.Clamp(frameRect.Width / 92, 3, 7);
        var text = frameNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var glyphWidth = (3 * 2 + 1) * scale;
        var glyphHeight = 5 * scale;
        var paddingX = 4 * scale;
        var paddingY = 3 * scale;
        var badgeWidth = Math.Min(frameRect.Width, glyphWidth + (paddingX * 2));
        var badgeHeight = Math.Min(frameRect.Height, glyphHeight + (paddingY * 2));
        var badge = new SpriteSheetRect(frameRect.X, frameRect.Y, badgeWidth, badgeHeight);
        FillRect(rgba, width, height, badge, 15, 23, 42, diagnostic ? (byte)245 : (byte)230);
        DrawRect(rgba, width, height, badge, 255, 255, 255, diagnostic ? (byte)245 : (byte)220, 1);
        DrawText(rgba, width, height, badge.X + paddingX, badge.Y + paddingY, text, scale, 255, 255, 255, 255);
    }

    private static void DrawText(byte[] rgba, int width, int height, int x, int y, string text, int scale, byte r, byte g, byte b, byte a)
    {
        var cursor = x;
        foreach (var ch in text)
        {
            var glyph = DigitGlyph(ch);
            for (var gy = 0; gy < glyph.Length; gy++)
            {
                for (var gx = 0; gx < glyph[gy].Length; gx++)
                {
                    if (glyph[gy][gx] != '#')
                        continue;

                    for (var py = 0; py < scale; py++)
                    {
                        for (var px = 0; px < scale; px++)
                            Blend(rgba, width, height, cursor + (gx * scale) + px, y + (gy * scale) + py, r, g, b, a);
                    }
                }
            }

            cursor += (glyph[0].Length + 1) * scale;
        }
    }

    private static string[] DigitGlyph(char ch) =>
        ch switch
        {
            '0' => ["###", "# #", "# #", "# #", "###"],
            '1' => [" # ", "## ", " # ", " # ", "###"],
            '2' => ["###", "  #", "###", "#  ", "###"],
            '3' => ["###", "  #", " ##", "  #", "###"],
            '4' => ["# #", "# #", "###", "  #", "  #"],
            '5' => ["###", "#  ", "###", "  #", "###"],
            '6' => ["###", "#  ", "###", "# #", "###"],
            '7' => ["###", "  #", " # ", " # ", " # "],
            '8' => ["###", "# #", "###", "# #", "###"],
            '9' => ["###", "# #", "###", "  #", "###"],
            _ => ["   ", "   ", "   ", "   ", "   "],
        };

    private static void DrawRect(byte[] rgba, int width, int height, SpriteSheetRect rect, byte r, byte g, byte b, byte a, int thickness)
    {
        for (var offset = 0; offset < thickness; offset++)
        {
            DrawLine(rgba, width, height, rect.X, rect.Y + offset, rect.X + rect.Width - 1, rect.Y + offset, r, g, b, a);
            DrawLine(rgba, width, height, rect.X, rect.Y + rect.Height - 1 - offset, rect.X + rect.Width - 1, rect.Y + rect.Height - 1 - offset, r, g, b, a);
            DrawLine(rgba, width, height, rect.X + offset, rect.Y, rect.X + offset, rect.Y + rect.Height - 1, r, g, b, a);
            DrawLine(rgba, width, height, rect.X + rect.Width - 1 - offset, rect.Y, rect.X + rect.Width - 1 - offset, rect.Y + rect.Height - 1, r, g, b, a);
        }
    }

    private static void DrawLine(byte[] rgba, int width, int height, int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a)
    {
        var dx = Math.Abs(x2 - x1);
        var sx = x1 < x2 ? 1 : -1;
        var dy = -Math.Abs(y2 - y1);
        var sy = y1 < y2 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            Blend(rgba, width, height, x1, y1, r, g, b, a);
            if (x1 == x2 && y1 == y2)
                break;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }
    }

    private static void DrawCircle(byte[] rgba, int width, int height, int cx, int cy, int radius, byte r, byte g, byte b, byte a)
    {
        var radiusSquared = radius * radius;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                    Blend(rgba, width, height, x, y, r, g, b, a);
            }
        }
    }

    private static void Blend(byte[] rgba, int width, int height, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;
        var offset = ((y * width) + x) * 4;
        var sourceAlpha = a / 255d;
        var inverse = 1d - sourceAlpha;
        rgba[offset] = (byte)Math.Clamp((r * sourceAlpha) + (rgba[offset] * inverse), 0, 255);
        rgba[offset + 1] = (byte)Math.Clamp((g * sourceAlpha) + (rgba[offset + 1] * inverse), 0, 255);
        rgba[offset + 2] = (byte)Math.Clamp((b * sourceAlpha) + (rgba[offset + 2] * inverse), 0, 255);
        rgba[offset + 3] = 255;
    }
}
