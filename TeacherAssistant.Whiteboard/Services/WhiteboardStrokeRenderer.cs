using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

/// <summary>
/// 使用 SkiaSharp 进行高质量笔迹渲染。
/// 常规笔使用 SKPath + SKPaint（Skia 原生 AA，质量远超手写 CPU 栅格化）。
/// 笔锋模式使用 Skia 圆形印章（避免 unsafe 像素操作）。
/// 公共 API 与旧版完全兼容，调用方无需修改。
/// </summary>
public static class WhiteboardStrokeRenderer
{
    // ── 公共 API ──────────────────────────────────────────────────────────

    public static void Clear(WriteableBitmap bitmap, Color color)
    {
        using var frame = bitmap.Lock();
        using var surface = CreateSurface(frame);
        surface.Canvas.Clear(ToSk(color));
    }

    public static void ClearRegion(WriteableBitmap bitmap, PixelRect rect)
    {
        using var frame = bitmap.Lock();
        using var surface = CreateSurface(frame);
        var canvas = surface.Canvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));
        canvas.Clear(SKColors.Transparent);
        canvas.Restore();
    }

    /// <summary>将完整笔迹提交到位图（EndStroke / RasterizeAll 时调用）。</summary>
    public static void DrawStroke(WriteableBitmap bitmap, WhiteboardStroke stroke,
        bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (stroke.Samples.Count < 2) return;
        using var frame = bitmap.Lock();
        using var surface = CreateSurface(frame);
        RenderStroke(surface.Canvas, stroke, useBezierSmoothing, usePenNibEffect,
            0, stroke.Samples.Count - 1);
    }

    /// <summary>仅绘制最新一段（ContinueStroke 实时预览时调用，增量写入）。</summary>
    public static void DrawSegment(WriteableBitmap bitmap, WhiteboardStroke stroke,
        bool useBezierSmoothing, bool usePenNibEffect)
    {
        var count = stroke.Samples.Count;
        if (count < 2) return;
        using var frame = bitmap.Lock();
        using var surface = CreateSurface(frame);
        var canvas = surface.Canvas;
        var skColor = ToSk(stroke.Color);

        if (!usePenNibEffect)
        {
            // 每次只画恰好一段，与旧逻辑完全一致，避免 AA 像素重叠累积
            using var paint = MakeStrokePaint(skColor, (float)stroke.Thickness);
            using var path  = BuildSingleSegmentPath(stroke.Samples, useBezierSmoothing, count);
            canvas.DrawPath(path, paint);
        }
        else
        {
            using var paint = new SKPaint { Color = skColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            StampSingleSegment(canvas, paint, stroke.Samples, useBezierSmoothing, count);
        }
    }

    // ── 核心渲染 ──────────────────────────────────────────────────────────

    private static void RenderStroke(SKCanvas canvas, WhiteboardStroke stroke,
        bool bezier, bool penNib, int from, int to)
    {
        var samples = stroke.Samples;
        if (to - from < 1) return;

        if (!penNib)
        {
            // ── 等宽笔：单条 SKPath，Skia 原生 AA，质量最高 ──────────────
            using var paint = MakeStrokePaint(ToSk(stroke.Color), (float)stroke.Thickness);
            using var path  = BuildPath(samples, bezier, from, to);
            canvas.DrawPath(path, paint);
        }
        else
        {
            // ── 笔锋（变宽）：圆形印章序列，走 Skia AA ───────────────────
            using var paint = new SKPaint
            {
                Color       = ToSk(stroke.Color),
                Style       = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            StampSegment(canvas, paint, samples, bezier, from, to);
        }
    }

    // ── 路径构建 ──────────────────────────────────────────────────────────

    private static SKPath BuildPath(IList<StrokeSample> samples, bool bezier, int from, int to)
    {
        var path = new SKPath();

        if (!bezier || (to - from) < 2)
        {
            path.MoveTo(ToSk(samples[from].Point));
            for (int i = from + 1; i <= to; i++)
                path.LineTo(ToSk(samples[i].Point));
            return path;
        }

        // 完整路径：起始直线段 + 二次贝塞尔中间段 + 结束直线段
        // 与旧版 DrawStartSegment / DrawIntermediateSegment / DrawEndSegment 完全对应
        path.MoveTo(ToSk(samples[from].Point));                                  // 从 samples[from] 出发
        path.LineTo(Midpoint(samples[from].Point, samples[from + 1].Point));     // 起始直线段
        for (int i = from + 1; i < to; i++)                                      // 中间贝塞尔段
        {
            var ctrl = ToSk(samples[i].Point);
            var end  = Midpoint(samples[i].Point, samples[i + 1].Point);
            path.QuadTo(ctrl, end);
        }
        path.LineTo(ToSk(samples[to].Point));                                    // 结束直线段
        return path;
    }

    /// <summary>仅构建最新一段路径（供 DrawSegment 增量预览）。</summary>
    private static SKPath BuildSingleSegmentPath(IList<StrokeSample> samples, bool bezier, int count)
    {
        var path = new SKPath();
        if (count == 2)
        {
            // 起始段：samples[0] → midpoint(0,1)
            path.MoveTo(ToSk(samples[0].Point));
            path.LineTo(bezier ? Midpoint(samples[0].Point, samples[1].Point) : ToSk(samples[1].Point));
            return path;
        }

        // 中间贝塞尔段：midpoint(count-3, count-2) → ctrl(count-2) → midpoint(count-2, count-1)
        var start = Midpoint(samples[count - 3].Point, samples[count - 2].Point);
        path.MoveTo(start);
        if (bezier)
        {
            var ctrl = ToSk(samples[count - 2].Point);
            var end  = Midpoint(samples[count - 2].Point, samples[count - 1].Point);
            path.QuadTo(ctrl, end);
        }
        else
        {
            path.LineTo(ToSk(samples[count - 2].Point));
        }
        return path;
    }

    // ── 圆形印章（笔锋模式）────────────────────────────────────────────────

    private static void StampSegment(SKCanvas canvas, SKPaint paint,
        IList<StrokeSample> samples, bool bezier, int from, int to)
    {
        if (!bezier || (to - from) < 2)
        {
            for (int i = from; i < to; i++)
                StampLinear(canvas, paint, samples[i].Point, samples[i + 1].Point, samples[i].Width / 2.0, samples[i + 1].Width / 2.0);
            return;
        }

        // 起始段：从真实起点到第一个中点
        var p0 = samples[from];
        var p1 = samples[from + 1];
        var mid01 = Midpoint(p0.Point, p1.Point);
        StampLinear(canvas, paint, ToSk(p0.Point), mid01, p0.Width / 2.0, (p0.Width + p1.Width) / 4.0);

        // 中间贝塞尔段
        for (int i = from + 1; i < to; i++)
        {
            var pPrev = samples[i - 1];
            var pCurr = samples[i];
            var pNext = samples[i + 1];
            var start = Midpoint(pPrev.Point, pCurr.Point);
            var end   = Midpoint(pCurr.Point, pNext.Point);
            StampQuadBezier(canvas, paint, start, ToSk(pCurr.Point), end, 
                (pPrev.Width + pCurr.Width) / 4.0, pCurr.Width / 2.0, (pCurr.Width + pNext.Width) / 4.0);
        }

        // 结束段：从最后一个中点到真实终点
        var pLastPrev = samples[to - 1];
        var pLast = samples[to];
        var midLast = Midpoint(pLastPrev.Point, pLast.Point);
        StampLinear(canvas, paint, midLast, ToSk(pLast.Point), (pLastPrev.Width + pLast.Width) / 4.0, pLast.Width / 2.0);
    }

    /// <summary>仅印章最新一段（供 DrawSegment 增量预览）。</summary>
    private static void StampSingleSegment(SKCanvas canvas, SKPaint paint,
        IList<StrokeSample> samples, bool bezier, int count)
    {
        if (count == 2)
        {
            var p0 = samples[0];
            var p1 = samples[1];
            if (bezier)
            {
                StampLinear(canvas, paint, ToSk(p0.Point), Midpoint(p0.Point, p1.Point), p0.Width / 2.0, (p0.Width + p1.Width) / 4.0);
            }
            else
            {
                StampLinear(canvas, paint, samples[0].Point, samples[1].Point, p0.Width / 2.0, p1.Width / 2.0);
            }
            return;
        }

        var pPrev = samples[count - 3];
        var pCurr = samples[count - 2];
        var pNext = samples[count - 1];

        if (bezier)
        {
            var start = Midpoint(pPrev.Point, pCurr.Point);
            var end   = Midpoint(pCurr.Point, pNext.Point);
            StampQuadBezier(canvas, paint, start, ToSk(pCurr.Point), end, 
                (pPrev.Width + pCurr.Width) / 4.0, pCurr.Width / 2.0, (pCurr.Width + pNext.Width) / 4.0);
        }
        else
        {
            StampLinear(canvas, paint, pCurr.Point, pNext.Point, pCurr.Width / 2.0, pNext.Width / 2.0);
        }
    }

    private static void StampLinear(SKCanvas canvas, SKPaint paint, Point a, Point b, double rA, double rB)
        => StampLinear(canvas, paint, ToSk(a), ToSk(b), rA, rB);

    private static void StampLinear(SKCanvas canvas, SKPaint paint, SKPoint a, SKPoint b, double rA, double rB)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var maxR = Math.Max(rA, rB);
        var spacing = Math.Max(0.5, maxR * 0.5);
        var steps = Math.Max(1, (int)Math.Ceiling(dist / spacing));
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float r = (float)(rA + (rB - rA) * t);
            if (r > 0) canvas.DrawCircle(a.X + dx * t, a.Y + dy * t, r, paint);
        }
    }

    private static void StampQuadBezier(SKCanvas canvas, SKPaint paint, SKPoint start, SKPoint ctrl, SKPoint end, double rStart, double rMid, double rEnd)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        var chord = Math.Sqrt(dx * dx + dy * dy);
        var maxR = Math.Max(rStart, Math.Max(rMid, rEnd));
        var spacing = Math.Max(0.5, maxR * 0.5);
        var steps = Math.Max(2, (int)Math.Ceiling(chord / spacing));
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float mt = 1.0f - t;
            float cx = mt * mt * start.X + 2 * mt * t * ctrl.X + t * t * end.X;
            float cy = mt * mt * start.Y + 2 * mt * t * ctrl.Y + t * t * end.Y;
            float r = (float)(mt * mt * rStart + 2 * mt * t * rMid + t * t * rEnd);
            if (r > 0) canvas.DrawCircle(cx, cy, r, paint);
        }
    }

    // ── 工具方法 ──────────────────────────────────────────────────────────

    private static SKSurface CreateSurface(ILockedFramebuffer frame)
    {
        var info = new SKImageInfo(
            frame.Size.Width, frame.Size.Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul);
        return SKSurface.Create(info, frame.Address, frame.RowBytes);
    }

    private static SKPaint MakeStrokePaint(SKColor color, float width) => new()
    {
        Color       = color,
        StrokeWidth = width,
        Style       = SKPaintStyle.Stroke,
        StrokeCap   = SKStrokeCap.Round,
        StrokeJoin  = SKStrokeJoin.Round,
        IsAntialias = true,
    };

    private static SKColor ToSk(Color c)   => new(c.R, c.G, c.B, c.A);
    private static SKPoint ToSk(Point p)   => new((float)p.X, (float)p.Y);
    private static SKPoint Midpoint(Point a, Point b)
        => new((float)((a.X + b.X) / 2.0), (float)((a.Y + b.Y) / 2.0));
    private static double Distance(Point a, Point b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
