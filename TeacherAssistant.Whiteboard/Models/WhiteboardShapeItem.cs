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
            var rect = new Rect(-halfWidth, -halfHeight, scaledWidth, scaledHeight);
            var fill = new SolidColorBrush(FillColor);
            var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeThickness);

            switch (ShapeType)
            {
                case WhiteboardShapeType.Ellipse:
                    context.DrawEllipse(fill, pen, rect);
                    break;
                case WhiteboardShapeType.Rectangle:
                    context.DrawRectangle(fill, pen, rect);
                    break;
                default:
                {
                    var points = GetNormalizedPoints(ShapeType);
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(points[0].X * halfWidth, points[0].Y * halfHeight), true);
                        for (int i = 1; i < points.Length; i++)
                        {
                            ctx.LineTo(new Point(points[i].X * halfWidth, points[i].Y * halfHeight));
                        }
                        ctx.EndFigure(true);
                    }
                    context.DrawGeometry(fill, pen, geometry);
                    break;
                }
            }
        }
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
            WhiteboardShapeType.Star => GetStarPoints(),
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
