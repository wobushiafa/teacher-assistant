using System;
using Avalonia;
using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class WhiteboardShapeItem : WhiteboardItemBase
{
    private double _strokeThickness;
    private Color _strokeColor;
    private Color _fillColor;

    public WhiteboardShapeItem(WhiteboardShapeType shapeType, Point center, Size size, Color strokeColor, Color fillColor, double strokeThickness)
        : base(center)
    {
        ShapeType = shapeType;
        BaseSize = size;
        _strokeColor = strokeColor;
        _fillColor = fillColor;
        _strokeThickness = strokeThickness;
    }

    public WhiteboardShapeType ShapeType { get; }
    public override WhiteboardElementType ElementType => WhiteboardElementType.Shape;
    public override Size BaseSize { get; }

    public double StrokeThickness 
    { 
        get => _strokeThickness; 
        set => SetProperty(ref _strokeThickness, Math.Max(0, value)); 
    }

    public Color StrokeColor 
    { 
        get => _strokeColor; 
        set => SetProperty(ref _strokeColor, value); 
    }

    public Color FillColor 
    { 
        get => _fillColor; 
        set => SetProperty(ref _fillColor, value); 
    }

    public override void Render(DrawingContext context)
    {
        var radians = RotationDegrees * Math.PI / 180.0;
        var transform = Matrix.CreateRotation(radians) * Matrix.CreateTranslation(Center.X, Center.Y);
        using (context.PushTransform(transform))
        {
            var scaledWidth = BaseSize.Width * ScaleX;
            var scaledHeight = BaseSize.Height * ScaleY;
            var halfWidth = scaledWidth / 2.0;
            var halfHeight = scaledHeight / 2.0;
            
            var fill = new SolidColorBrush(FillColor);
            var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeThickness);

            if (ShapeType == WhiteboardShapeType.Ellipse)
            {
                context.DrawEllipse(fill, pen, new Rect(-halfWidth, -halfHeight, scaledWidth, scaledHeight));
                return;
            }
            if (ShapeType == WhiteboardShapeType.Rectangle)
            {
                context.DrawRectangle(fill, pen, new Rect(-halfWidth, -halfHeight, scaledWidth, scaledHeight));
                return;
            }

            if (ShapeType == WhiteboardShapeType.CoordinateSystem)
            {
                var strokeBrush = new SolidColorBrush(StrokeColor);
                var strokePen = new Pen(strokeBrush, StrokeThickness);
                RenderCoordinateSystem(context, strokePen, strokeBrush, halfWidth, halfHeight);
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                switch (ShapeType)
                {
                    case WhiteboardShapeType.Heart: RenderHeart(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Cloud: RenderCloud(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Semicircle: RenderSemicircle(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Sector: RenderSector(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Cylinder: RenderCylinder(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Cube: RenderCube(ctx, halfWidth, halfHeight); break;
                    case WhiteboardShapeType.Cone: RenderCone(ctx, halfWidth, halfHeight); break;
                    default:
                        var points = GetNormalizedPoints(ShapeType);
                        ctx.BeginFigure(new Point(points[0].X * halfWidth, points[0].Y * halfHeight), true);
                        for (int i = 1; i < points.Length; i++)
                        {
                            ctx.LineTo(new Point(points[i].X * halfWidth, points[i].Y * halfHeight));
                        }
                        ctx.EndFigure(true);
                        break;
                }
            }
            context.DrawGeometry(fill, pen, geometry);
        }
    }

    private static void RenderHeart(StreamGeometryContext ctx, double w, double h)
    {
        ctx.BeginFigure(new Point(0, h * 0.8), true);
        ctx.CubicBezierTo(new Point(-w * 1.5, -h * 0.6), new Point(-w * 0.6, -h * 1.5), new Point(0, -h * 0.4));
        ctx.CubicBezierTo(new Point(w * 0.6, -h * 1.5), new Point(w * 1.5, -h * 0.6), new Point(0, h * 0.8));
        ctx.EndFigure(true);
    }

    private static void RenderCloud(StreamGeometryContext ctx, double w, double h)
    {
        ctx.BeginFigure(new Point(-w * 0.6, h * 0.4), true);
        ctx.ArcTo(new Point(-w * 0.6, -h * 0.4), new Size(w * 0.4, h * 0.4), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(0, -h * 0.6), new Size(w * 0.5, h * 0.5), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(w * 0.6, -h * 0.4), new Size(w * 0.4, h * 0.4), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(w * 0.6, h * 0.4), new Size(w * 0.4, h * 0.4), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(-w * 0.6, h * 0.4), new Size(w * 0.8, h * 0.4), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
    }

    private static void RenderSemicircle(StreamGeometryContext ctx, double w, double h)
    {
        ctx.BeginFigure(new Point(-w, 0), true);
        ctx.ArcTo(new Point(w, 0), new Size(w, h), 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(-w, 0));
        ctx.EndFigure(true);
    }

    private static void RenderSector(StreamGeometryContext ctx, double w, double h)
    {
        ctx.BeginFigure(new Point(0, 0), true);
        ctx.LineTo(new Point(w, 0));
        ctx.ArcTo(new Point(0, -h), new Size(w, h), 0, false, SweepDirection.CounterClockwise);
        ctx.LineTo(new Point(0, 0));
        ctx.EndFigure(true);
    }

    private static void RenderCoordinateSystem(DrawingContext context, Pen pen, IBrush fill, double w, double h)
    {
        double arrowLen = 15;
        double arrowWing = 6;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // X Axis main line
            ctx.BeginFigure(new Point(-w, 0), false);
            ctx.LineTo(new Point(w, 0));
            ctx.EndFigure(false);

            // Y Axis main line
            ctx.BeginFigure(new Point(0, h), false);
            ctx.LineTo(new Point(0, -h));
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);

        var arrowGeometry = new StreamGeometry();
        using (var ctx = arrowGeometry.Open())
        {
            // X Axis Arrowhead (Closed triangle)
            ctx.BeginFigure(new Point(w, 0), true);
            ctx.LineTo(new Point(w - arrowLen, -arrowWing));
            ctx.LineTo(new Point(w - arrowLen, arrowWing));
            ctx.EndFigure(true);

            // Y Axis Arrowhead (Closed triangle)
            ctx.BeginFigure(new Point(0, -h), true);
            ctx.LineTo(new Point(-arrowWing, -h + arrowLen));
            ctx.LineTo(new Point(arrowWing, -h + arrowLen));
            ctx.EndFigure(true);
        }
        // Fill the arrows with the stroke brush
        context.DrawGeometry(fill, pen, arrowGeometry);
    }

    private static void RenderCylinder(StreamGeometryContext ctx, double w, double h)
    {
        double rh = h * 0.2; // ellipse height
        // Bottom ellipse
        ctx.BeginFigure(new Point(-w, h - rh), true);
        ctx.ArcTo(new Point(w, h - rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(-w, h - rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);

        // Sides
        ctx.BeginFigure(new Point(-w, h - rh), false);
        ctx.LineTo(new Point(-w, -h + rh));
        ctx.EndFigure(false);
        ctx.BeginFigure(new Point(w, h - rh), false);
        ctx.LineTo(new Point(w, -h + rh));
        ctx.EndFigure(false);

        // Top ellipse
        ctx.BeginFigure(new Point(-w, -h + rh), true);
        ctx.ArcTo(new Point(w, -h + rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(-w, -h + rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
    }

    private static void RenderCube(StreamGeometryContext ctx, double w, double h)
    {
        double offset = w * 0.4;
        double sw = w * 0.8;
        double sh = h * 0.8;
        
        double dx = -offset / 2.0;
        double dy = offset / 2.0;

        // Front face
        ctx.BeginFigure(new Point(-sw + dx, -sh + dy), true);
        ctx.LineTo(new Point(sw + dx, -sh + dy));
        ctx.LineTo(new Point(sw + dx, sh + dy));
        ctx.LineTo(new Point(-sw + dx, sh + dy));
        ctx.EndFigure(true);

        // Back face (partial)
        ctx.BeginFigure(new Point(-sw + offset + dx, -sh - offset + dy), true);
        ctx.LineTo(new Point(sw + offset + dx, -sh - offset + dy));
        ctx.LineTo(new Point(sw + offset + dx, sh - offset + dy));
        ctx.LineTo(new Point(-sw + offset + dx, sh - offset + dy));
        ctx.EndFigure(true);

        // Connecting lines
        ctx.BeginFigure(new Point(-sw + dx, -sh + dy), false); ctx.LineTo(new Point(-sw + offset + dx, -sh - offset + dy)); ctx.EndFigure(false);
        ctx.BeginFigure(new Point(sw + dx, -sh + dy), false); ctx.LineTo(new Point(sw + offset + dx, -sh - offset + dy)); ctx.EndFigure(false);
        ctx.BeginFigure(new Point(sw + dx, sh + dy), false); ctx.LineTo(new Point(sw + offset + dx, sh - offset + dy)); ctx.EndFigure(false);
        ctx.BeginFigure(new Point(-sw + dx, sh + dy), false); ctx.LineTo(new Point(-sw + offset + dx, sh - offset + dy)); ctx.EndFigure(false);
    }

    private static void RenderCone(StreamGeometryContext ctx, double w, double h)
    {
        double rh = h * 0.2;
        // Bottom ellipse
        ctx.BeginFigure(new Point(-w, h - rh), true);
        ctx.ArcTo(new Point(w, h - rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(-w, h - rh), new Size(w, rh), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);

        // Lines to apex
        ctx.BeginFigure(new Point(-w, h - rh), false);
        ctx.LineTo(new Point(0, -h));
        ctx.LineTo(new Point(w, h - rh));
        ctx.EndFigure(false);
    }

    private static readonly System.Collections.Generic.Dictionary<WhiteboardShapeType, Point[]> _normalizedPointsCache = new();

    private static Point[] GetNormalizedPoints(WhiteboardShapeType type)
    {
        if (_normalizedPointsCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        Point[] rawPoints = type switch
        {
            WhiteboardShapeType.Triangle => [new(0, -1), new(1, 1), new(-1, 1)],
            WhiteboardShapeType.RightTriangle => [new(-1, -1), new(1, 1), new(-1, 1)],
            WhiteboardShapeType.Diamond => [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)],
            WhiteboardShapeType.Pentagon => GetRegularPolygonPoints(5),
            WhiteboardShapeType.Hexagon => GetRegularPolygonPoints(6),
            WhiteboardShapeType.Octagon => GetRegularPolygonPoints(8),
            WhiteboardShapeType.Star => GetStarPoints(),
            WhiteboardShapeType.Trapezoid => [new(-0.7, -1), new(0.7, -1), new(1, 1), new(-1, 1)],
            WhiteboardShapeType.Parallelogram => [new(-0.7, -1), new(1, -1), new(0.7, 1), new(-1, 1)],
            WhiteboardShapeType.Cross => [
                new(-0.3, -1), new(0.3, -1), new(0.3, -0.3), new(1, -0.3),
                new(1, 0.3), new(0.3, 0.3), new(0.3, 1), new(-0.3, 1),
                new(-0.3, 0.3), new(-1, 0.3), new(-1, -0.3), new(-0.3, -0.3)
            ],
            WhiteboardShapeType.Arrow => [new(-1, -0.4), new(0.2, -0.4), new(0.2, -1), new(1, 0), new(0.2, 1), new(0.2, 0.4), new(-1, 0.4)],
            _ => [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)]
        };

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in rawPoints)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double offsetX = (maxX + minX) / 2.0;
        double offsetY = (maxY + minY) / 2.0;

        var normalized = new Point[rawPoints.Length];
        for (int i = 0; i < rawPoints.Length; i++)
        {
            normalized[i] = new Point(
                (rawPoints[i].X - offsetX) / (width / 2.0),
                (rawPoints[i].Y - offsetY) / (height / 2.0)
            );
        }
        
        _normalizedPointsCache[type] = normalized;
        return normalized;
    }

    private static Point[] GetRegularPolygonPoints(int sides)
    {
        var points = new Point[sides];
        for (int i = 0; i < sides; i++)
        {
            var angle = i * 2 * Math.PI / sides - Math.PI / 2;
            points[i] = new Point(Math.Cos(angle), Math.Sin(angle));
        }
        return points;
    }

    private static Point[] GetStarPoints()
    {
        var points = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            var angle = i * Math.PI / 5 - Math.PI / 2;
            var r = (i % 2 == 0) ? 1.0 : 0.4;
            points[i] = new Point(Math.Cos(angle) * r, Math.Sin(angle) * r);
        }
        return points;
    }
}
