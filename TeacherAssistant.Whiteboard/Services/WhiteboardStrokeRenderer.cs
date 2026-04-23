using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public static unsafe class WhiteboardStrokeRenderer
{
    public static void Clear(WriteableBitmap bitmap, Color color)
    {
        using var frame = bitmap.Lock();
        var pixels = (uint*)frame.Address;
        var count = frame.Size.Width * frame.Size.Height;
        var val = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
        // Span.Fill → JIT 向量化为 AVX2/SSE2，比手写循环快 4-8×
        new Span<uint>(pixels, count).Fill(val);

    }

    public static void ClearRegion(WriteableBitmap bitmap, PixelRect rect)
    {
        var clippedX = Math.Max(0, rect.X);
        var clippedY = Math.Max(0, rect.Y);
        var clippedRight = Math.Min(bitmap.PixelSize.Width, rect.X + rect.Width);
        var clippedBottom = Math.Min(bitmap.PixelSize.Height, rect.Y + rect.Height);
        if (clippedRight <= clippedX || clippedBottom <= clippedY) return;

        using var frame = bitmap.Lock();
        var pixels = (uint*)frame.Address;
        for (var y = clippedY; y < clippedBottom; y++)
        {
            var row = pixels + (y * (frame.RowBytes / 4));
            for (var x = clippedX; x < clippedRight; x++) row[x] = 0;
        }
    }

    public static void DrawStroke(WriteableBitmap bitmap, WhiteboardStroke stroke, bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (stroke.Samples.Count < 2) return;

        using var frame = bitmap.Lock();
        var pixels = (byte*)frame.Address;
        var rb = frame.RowBytes;
        var w = frame.Size.Width;
        var h = frame.Size.Height;

        if (!useBezierSmoothing || stroke.Samples.Count == 2)
        {
            for (int i = 1; i < stroke.Samples.Count; i++)
            {
                DrawVariableSegment(pixels, rb, w, h, stroke.Samples[i - 1].Point, stroke.Samples[i].Point, stroke.Samples[i - 1].Width / 2.0, stroke.Samples[i].Width / 2.0, stroke.Color);
            }
            return;
        }

        DrawStartSegment(pixels, rb, w, h, stroke, true, usePenNibEffect);
        for (int i = 1; i < stroke.Samples.Count - 1; i++)
        {
            DrawIntermediateSegment(pixels, rb, w, h, stroke, i, true, usePenNibEffect);
        }
        DrawEndSegment(pixels, rb, w, h, stroke, true, usePenNibEffect);
    }

    public static void DrawSegment(WriteableBitmap bitmap, WhiteboardStroke stroke, bool useBezierSmoothing, bool usePenNibEffect)
    {
        var count = stroke.Samples.Count;
        if (count < 2) return;

        using var frame = bitmap.Lock();
        var pixels = (byte*)frame.Address;
        var rb = frame.RowBytes;
        var w = frame.Size.Width;
        var h = frame.Size.Height;

        if (!useBezierSmoothing)
        {
            var p1 = stroke.Samples[count - 2];
            var p2 = stroke.Samples[count - 1];
            DrawVariableSegment(pixels, rb, w, h, p1.Point, p2.Point, p1.Width / 2.0, p2.Width / 2.0, stroke.Color);
            return;
        }

        if (count == 2) DrawStartSegment(pixels, rb, w, h, stroke, true, usePenNibEffect);
        else DrawIntermediateSegment(pixels, rb, w, h, stroke, count - 2, true, usePenNibEffect);
    }

    private static void DrawStartSegment(byte* pixels, int rb, int w, int h, WhiteboardStroke stroke, bool smooth, bool useNib)
    {
        var p0 = stroke.Samples[0];
        var p1 = stroke.Samples[1];
        if (!smooth || stroke.Samples.Count < 3)
        {
            DrawVariableSegment(pixels, rb, w, h, p0.Point, p1.Point, p0.Width / 2.0, p1.Width / 2.0, stroke.Color);
        }
        else
        {
            var mid = Midpoint(p0.Point, p1.Point);
            DrawVariableSegment(pixels, rb, w, h, p0.Point, mid, p0.Width / 2.0, (p0.Width + p1.Width) / 4.0, stroke.Color);
        }
    }

    private static void DrawIntermediateSegment(byte* pixels, int rb, int w, int h, WhiteboardStroke stroke, int index, bool smooth, bool useNib)
    {
        var pPrev = stroke.Samples[index - 1];
        var pCurr = stroke.Samples[index];
        var pNext = stroke.Samples[index + 1];
        var start = Midpoint(pPrev.Point, pCurr.Point);
        var end = Midpoint(pCurr.Point, pNext.Point);
        DrawBezierVariableWidth(pixels, rb, w, h, start, pCurr.Point, end, (pPrev.Width + pCurr.Width) / 4.0, pCurr.Width / 2.0, (pCurr.Width + pNext.Width) / 4.0, stroke.Color);
    }

    private static void DrawEndSegment(byte* pixels, int rb, int w, int h, WhiteboardStroke stroke, bool smooth, bool useNib)
    {
        var count = stroke.Samples.Count;
        if (count < 3) return;
        var pPrev = stroke.Samples[count - 2];
        var pLast = stroke.Samples[count - 1];
        DrawVariableSegment(pixels, rb, w, h, Midpoint(pPrev.Point, pLast.Point), pLast.Point, (pPrev.Width + pLast.Width) / 4.0, pLast.Width / 2.0, stroke.Color);
    }

    private static void DrawVariableSegment(byte* pixels, int rb, int w, int h, Point a, Point b, double rA, double rB, Color color)
    {
        var dist = Distance(a, b);
        var maxR = Math.Max(rA, rB);
        var spacing = Math.Max(0.5, maxR * 0.5); // 0.5 倍半径间距：视觉无差异但 StampBrush 调用减少 3×
        var steps = Math.Max(1, (int)Math.Ceiling(dist / spacing));
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            StampBrush(pixels, rb, w, h, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, rA + (rB - rA) * t, color);
        }
    }

    private static void DrawBezierVariableWidth(byte* pixels, int rb, int w, int h, Point start, Point ctrl, Point end, double rStart, double rMid, double rEnd, Color color)
    {
        var chord = Distance(start, end);
        var maxR = Math.Max(rStart, Math.Max(rMid, rEnd));
        var spacing = Math.Max(0.5, maxR * 0.5); // 同上
        var steps = Math.Max(2, (int)Math.Ceiling(chord / spacing));
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double mt = 1.0 - t;
            double x = mt * mt * start.X + 2 * mt * t * ctrl.X + t * t * end.X;
            double y = mt * mt * start.Y + 2 * mt * t * ctrl.Y + t * t * end.Y;
            double r = mt * mt * rStart + 2 * mt * t * rMid + t * t * rEnd;
            StampBrush(pixels, rb, w, h, x, y, r, color);
        }
    }

    private static void StampBrush(byte* pixels, int rb, int w, int h, double cx, double cy, double r, Color color)
    {
        // 优化：将每行分为 3 个区域处理
        // [左AA边缘] [内核全覆盖 → Span.Fill SIMD批量写] [右AA边缘]
        // 仅 AA 边缘（约 2px 宽）需要 sqrt，内核完全跳过
        const double feather = 1.0;
        double outerR = r + feather;
        double innerR = Math.Max(0.0, r - feather);
        double outerR2 = outerR * outerR;
        double innerR2 = innerR * innerR;

        // 预打包颜色为 Bgra8888 uint，供 Span.Fill 向量化写入
        uint solidPacked = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
        bool isOpaque = color.A == 255;

        int minX = Math.Max(0, (int)(cx - outerR));
        int maxX = Math.Min(w - 1, (int)(cx + outerR + 1));
        int minY = Math.Max(0, (int)(cy - outerR));
        int maxY = Math.Min(h - 1, (int)(cy + outerR + 1));

        for (int y = minY; y <= maxY; y++)
        {
            byte* row = pixels + y * rb;
            double dy = y + 0.5 - cy;
            double dy2 = dy * dy;
            if (dy2 > outerR2) continue;

            // 计算内核在当前行的 x 区间（像素中心在 innerR 圆内）
            double innerRowR2 = innerR2 - dy2;
            int solidMinX, solidMaxX;
            if (innerRowR2 > 0)
            {
                double s = Math.Sqrt(innerRowR2);
                solidMinX = Math.Max(minX, (int)Math.Ceiling(cx - s - 0.5));
                solidMaxX = Math.Min(maxX, (int)Math.Floor(cx + s - 0.5));
            }
            else
            {
                solidMinX = maxX + 1;  // 空区间
                solidMaxX = maxX;
            }

            // 左侧 AA 边缘
            for (int x = minX; x < solidMinX; x++)
                StampEdgePixel(row, x, x + 0.5 - cx, dy2, outerR2, innerR2, outerR, feather, color.A, color);

            // 内核：JIT 将 Span.Fill 自动向量化为 AVX2/SSE2（等效 memset）
            if (solidMinX <= solidMaxX)
            {
                int count = solidMaxX - solidMinX + 1;
                if (isOpaque)
                    new Span<uint>((uint*)(row + solidMinX * 4), count).Fill(solidPacked);
                else
                {
                    // 预提取 sa=color.A（cov=1.0），避免每像素做一次 double 乘法
                    int csa = color.A;
                    for (int x = solidMinX; x <= solidMaxX; x++)
                        BlendPixelSa(row + x * 4, color, csa);
                }
            }

            // 右侧 AA 边缘
            for (int x = Math.Max(minX, solidMaxX + 1); x <= maxX; x++)
                StampEdgePixel(row, x, x + 0.5 - cx, dy2, outerR2, innerR2, outerR, feather, color.A, color);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void StampEdgePixel(byte* row, int x, double dx, double dy2,
        double outerR2, double innerR2, double outerR, double feather, int colorAlpha, Color color)
    {
        double d2 = dx * dx + dy2;
        if (d2 > outerR2) return;
        // 全覆盖：直接用预计算的 colorAlpha，跳过 double 乘法
        if (d2 <= innerR2) { BlendPixelSa(row + x * 4, color, colorAlpha); return; }
        double d = Math.Sqrt(d2);
        double t = Math.Clamp((outerR - d) / feather, 0.0, 1.0);
        double cov = t * t * (3.0 - 2.0 * t);
        int sa = (int)(colorAlpha * cov + 0.5);
        if (sa > 0) BlendPixelSa(row + x * 4, color, sa);
    }


    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void BlendPixel(byte* p, Color c, double cov)
        => BlendPixelSa(p, c, (int)(c.A * cov + 0.5));

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void BlendPixelSa(byte* p, Color c, int sa)
    {
        if (sa <= 0) return;
        if (sa >= 255)
        {
            p[0] = c.B; p[1] = c.G; p[2] = c.R; p[3] = 255;
            return;
        }

        int da = p[3];
        if (da == 0)
        {
            p[0] = c.B; p[1] = c.G; p[2] = c.R; p[3] = (byte)sa;
            return;
        }

        int invSa = 255 - sa;
        int outA = sa * 255 + da * invSa;
        if (outA == 0) return;

        float invOutA = 1.0f / outA;
        p[0] = (byte)((c.B * sa * 255 + p[0] * da * invSa) * invOutA);
        p[1] = (byte)((c.G * sa * 255 + p[1] * da * invSa) * invOutA);
        p[2] = (byte)((c.R * sa * 255 + p[2] * da * invSa) * invOutA);
        p[3] = (byte)(outA / 255);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
}
