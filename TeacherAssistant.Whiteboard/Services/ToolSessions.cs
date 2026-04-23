using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public interface IToolSession
{
    WhiteboardTool Tool { get; }
    void Begin(Point point, Color color, double thickness);
    void Continue(Point point);
    void End();
    void Cancel();
    ToolPreviewData? BuildPreview();
    IWhiteboardElement? Commit();
}

public abstract class ToolPreviewData
{
    public abstract ToolPreviewType PreviewType { get; }
}

public enum ToolPreviewType
{
    Stroke,
    Image,
    Shape,
    None,
}

public sealed class StrokePreviewData : ToolPreviewData
{
    public override ToolPreviewType PreviewType => ToolPreviewType.Stroke;
    public StrokeElement? ActiveStroke { get; init; }
    public StrokeElement? StablePrefix { get; init; }
    public bool HasStablePrefix => StablePrefix is not null;
}

public sealed class ImagePreviewData : ToolPreviewData
{
    public override ToolPreviewType PreviewType => ToolPreviewType.Image;
    public ImageElement? ActiveImage { get; init; }
    public WhiteboardImageHitTest HitTest { get; init; }
}

public static class ToolSessionFactory
{
    public static IToolSession Create(WhiteboardDocument document, WhiteboardTool tool)
        => tool switch
        {
            WhiteboardTool.Pen => new PenSession(document),
            WhiteboardTool.Highlighter => new HighlighterSession(document),
            WhiteboardTool.Eraser => new EraserSession(document),
            _ => throw new ArgumentException($"Unknown tool: {tool}"),
        };
}

public class PenSession : IToolSession
{
    private readonly WhiteboardDocument _document;
    private StrokeElement? _activeStroke;

    public virtual WhiteboardTool Tool => WhiteboardTool.Pen;

    public PenSession(WhiteboardDocument document)
    {
        _document = document;
    }

    public virtual void Begin(Point point, Color color, double thickness)
    {
        _activeStroke = new StrokeElement(color, thickness, Tool);
        _activeStroke.Samples.Add(new StrokeSample(point, System.Diagnostics.Stopwatch.GetTimestamp(), 0.35));
    }

    public void Continue(Point point)
    {
        if (_activeStroke is null) return;

        var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var lastSample = _activeStroke.Samples[^1];
        var width = ComputeWidth(lastSample, point, timestamp, _activeStroke.Thickness, usePenNibEffect: true);
        _activeStroke.Samples.Add(new StrokeSample(point, timestamp, width));
    }

    public void End()
    {
    }

    public void Cancel()
    {
        _activeStroke = null;
    }

    public ToolPreviewData? BuildPreview()
    {
        if (_activeStroke is null || _activeStroke.Samples.Count < 2)
            return null;

        return new StrokePreviewData { ActiveStroke = _activeStroke };
    }

    public IWhiteboardElement? Commit()
    {
        if (_activeStroke is null || _activeStroke.Samples.Count < 2)
        {
            _activeStroke = null;
            return null;
        }

        var stroke = _activeStroke;
        _document.AddStroke(stroke);
        _activeStroke = null;
        return stroke;
    }

    private static double ComputeWidth(StrokeSample previous, Point currentPoint, long currentTimestamp, double thickness, bool usePenNibEffect)
    {
        if (!usePenNibEffect) return thickness;

        var dt = Math.Max(1.0 / 1000.0, (currentTimestamp - previous.Timestamp) / (double)System.Diagnostics.Stopwatch.Frequency);
        var dx = currentPoint.X - previous.Point.X;
        var dy = currentPoint.Y - previous.Point.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var velocity = distance / dt;

        var maxWidth = thickness * 1.2;
        var minWidth = Math.Max(0.2, thickness * 0.1);
        var velocityFactor = 1.0 - Math.Exp(-velocity / 600.0);
        var targetWidth = maxWidth - ((maxWidth - minWidth) * velocityFactor);

        return previous.Width + ((targetWidth - previous.Width) * 0.25);
    }
}

public sealed class HighlighterSession : PenSession
{
    public override WhiteboardTool Tool => WhiteboardTool.Highlighter;

    public HighlighterSession(WhiteboardDocument document) : base(document)
    {
    }

    public override void Begin(Point point, Color color, double thickness)
    {
        var highlighterColor = Color.FromArgb(96, color.R, color.G, color.B);
        var highlighterThickness = Math.Max(10.0, thickness * 2.8);
        base.Begin(point, highlighterColor, highlighterThickness);
    }
}

public sealed class EraserSession : IToolSession
{
    private readonly WhiteboardDocument _document;
    private Rect? _dirtyBounds;

    public WhiteboardTool Tool => WhiteboardTool.Eraser;

    public EraserSession(WhiteboardDocument document)
    {
        _document = document;
    }

    public void Begin(Point point, Color color, double thickness)
    {
        EraseAt(point, thickness * 2);
    }

    public void Continue(Point point)
    {
        EraseAt(point, 20);
    }

    public void End()
    {
    }

    public void Cancel()
    {
    }

    public ToolPreviewData? BuildPreview() => null;

    public Rect? ConsumeDirtyBounds()
    {
        var bounds = _dirtyBounds;
        _dirtyBounds = null;
        return bounds;
    }

    public IWhiteboardElement? Commit() => null;

    private void EraseAt(Point point, double radius)
    {
        var removedIds = new List<Guid>();
        var hasDirtyBounds = false;
        var dirtyBounds = default(Rect);
        var searchRect = new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2);

        foreach (var stroke in _document.QueryStrokes(searchRect))
        {
            if (IsPointNearStroke(point, stroke, radius))
            {
                removedIds.Add(stroke.Id);

                if (!hasDirtyBounds)
                {
                    dirtyBounds = stroke.Bounds;
                    hasDirtyBounds = true;
                }
                else
                {
                    dirtyBounds = UnionRect(dirtyBounds, stroke.Bounds);
                }
            }
        }

        if (hasDirtyBounds)
        {
            _dirtyBounds = _dirtyBounds is null ? dirtyBounds : UnionRect(_dirtyBounds.Value, dirtyBounds);
        }

        foreach (var id in removedIds)
        {
            _document.RemoveElement(id);
        }
    }

    private static bool IsPointNearStroke(Point point, StrokeElement stroke, double radius)
    {
        var queryBounds = new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        if (!Intersects(stroke.Bounds, queryBounds))
            return false;

        var radiusSquared = radius * radius;

        foreach (var sample in stroke.Samples)
        {
            var dx = point.X - sample.Point.X;
            var dy = point.Y - sample.Point.Y;
            if (dx * dx + dy * dy <= radiusSquared)
                return true;
        }

        for (var i = 1; i < stroke.Samples.Count; i++)
        {
            var a = stroke.Samples[i - 1].Point;
            var b = stroke.Samples[i].Point;
            var dist = DistanceToSegment(point.X, point.Y, a, b);
            if (dist <= radius)
                return true;
        }

        return false;
    }

    private static double DistanceToSegment(double px, double py, Point a, Point b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var wx = px - a.X;
        var wy = py - a.Y;
        var lengthSquared = vx * vx + vy * vy;
        if (lengthSquared <= double.Epsilon)
            return Math.Sqrt(wx * wx + wy * wy);

        var t = Math.Clamp((wx * vx + wy * vy) / lengthSquared, 0, 1);
        var closestX = a.X + t * vx;
        var closestY = a.Y + t * vy;
        return Math.Sqrt((px - closestX) * (px - closestX) + (py - closestY) * (py - closestY));
    }

    private static Rect UnionRect(Rect a, Rect b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool Intersects(Rect a, Rect b)
        => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
}
