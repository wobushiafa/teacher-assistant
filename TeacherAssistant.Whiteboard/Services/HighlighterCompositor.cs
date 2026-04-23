using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class HighlighterCompositor : IDisposable
{
    private readonly List<(Pen pen, StreamGeometry geometry)> _committedGeometries = [];
    private WhiteboardStroke? _activeStroke;
    private bool _useBezierSmoothing;

    public void EnsureSize(int width, int height)
    {
    }

    public void Reset()
    {
        _committedGeometries.Clear();
        _activeStroke = null;
    }

    public void ClearPreview()
    {
        _activeStroke = null;
    }

    public void UpdatePreview(WhiteboardStroke stroke, bool useBezierSmoothing)
    {
        if (stroke.Samples.Count < 2)
        {
            return;
        }

        _activeStroke = CloneStroke(stroke);
        _useBezierSmoothing = useBezierSmoothing;
    }

    public void CommitStroke(WhiteboardStroke stroke)
    {
        _activeStroke = null;
    }

    public void RebuildFromDocument(WhiteboardDocument document)
    {
        _committedGeometries.Clear();

        foreach (var stroke in document.Strokes)
        {
            if (stroke.Tool == WhiteboardTool.Highlighter && stroke.Samples.Count >= 2)
            {
                var pen = CreatePen(stroke.Color, stroke.Thickness);
                var geometry = new StreamGeometry();
                using var ctx = geometry.Open();
                AppendToContext(ctx, ConvertStroke(stroke), _useBezierSmoothing);
                _committedGeometries.Add((pen, geometry));
            }
        }

        _activeStroke = null;
    }

    public void Render(DrawingContext context, Rect bounds)
    {
        foreach (var item in _committedGeometries)
        {
            context.DrawGeometry(null, item.pen, item.geometry);
        }

        if (_activeStroke is not null)
        {
            var geometry = BuildGeometry(_activeStroke, _useBezierSmoothing);
            if (geometry is not null)
            {
                var pen = CreatePen(_activeStroke.Color, _activeStroke.Thickness);
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }

    private static Pen CreatePen(Color color, double thickness)
        => new(new SolidColorBrush(color), Math.Max(10.0, thickness * 1.24))
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

    private static void AppendToContext(StreamGeometryContext ctx, WhiteboardStroke stroke, bool useBezierSmoothing)
    {
        if (!useBezierSmoothing || stroke.Samples.Count < 3)
        {
            ctx.BeginFigure(stroke.Samples[0].Point, false);
            for (var i = 1; i < stroke.Samples.Count; i++)
            {
                ctx.LineTo(stroke.Samples[i].Point);
            }

            ctx.EndFigure(false);
            return;
        }

        var start = Midpoint(stroke.Samples[0].Point, stroke.Samples[1].Point);
        ctx.BeginFigure(start, false);

        for (var i = 1; i < stroke.Samples.Count - 1; i++)
        {
            var control = stroke.Samples[i].Point;
            var end = Midpoint(stroke.Samples[i].Point, stroke.Samples[i + 1].Point);
            ctx.QuadraticBezierTo(control, end);
        }

        ctx.LineTo(stroke.Samples[^1].Point);
        ctx.EndFigure(false);
    }

    private static StreamGeometry? BuildGeometry(WhiteboardStroke stroke, bool useBezierSmoothing)
    {
        if (stroke.Samples.Count < 2)
        {
            return null;
        }

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        AppendToContext(ctx, stroke, useBezierSmoothing);
        return geometry;
    }

    private static WhiteboardStroke ConvertStroke(StrokeElement element)
    {
        var stroke = new WhiteboardStroke(element.Color, element.Thickness, element.Tool);
        foreach (var sample in element.Samples)
        {
            stroke.Samples.Add(sample);
        }

        return stroke;
    }

    private static WhiteboardStroke CloneStroke(WhiteboardStroke source)
    {
        var clone = new WhiteboardStroke(source.Color, source.Thickness, source.Tool);
        foreach (var sample in source.Samples)
        {
            clone.Samples.Add(sample);
        }

        return clone;
    }

    private static Point Midpoint(Point a, Point b)
        => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    public void Dispose()
    {
    }
}
