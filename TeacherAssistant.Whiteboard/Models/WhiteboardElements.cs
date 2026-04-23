using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public interface IWhiteboardElement
{
    Guid Id { get; }

    WhiteboardElementType ElementType { get; }

    Rect Bounds { get; }

    void Invalidate();
}

public enum WhiteboardElementType
{
    Stroke,
    Image,
    Shape,
}

public sealed class StrokeElement : IWhiteboardElement
{
    public Guid Id { get; } = Guid.NewGuid();

    public WhiteboardElementType ElementType => WhiteboardElementType.Stroke;

    public Color Color { get; }

    public double Thickness { get; }

    public WhiteboardTool Tool { get; }

    public List<StrokeSample> Samples { get; } = [];

    public StrokeElement(Color color, double thickness, WhiteboardTool tool)
    {
        Color = color;
        Thickness = thickness;
        Tool = tool;
    }

    public Rect Bounds
    {
        get
        {
            if (Samples.Count == 0)
            {
                return new Rect(0, 0, 0, 0);
            }

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var sample in Samples)
            {
                minX = Math.Min(minX, sample.Point.X);
                minY = Math.Min(minY, sample.Point.Y);
                maxX = Math.Max(maxX, sample.Point.X);
                maxY = Math.Max(maxY, sample.Point.Y);
            }

            return new Rect(minX - (Thickness / 2), minY - (Thickness / 2), maxX - minX + Thickness, maxY - minY + Thickness);
        }
    }

    public void Invalidate()
    {
    }
}

public sealed class ImageElement : IWhiteboardElement
{
    public Guid Id { get; } = Guid.NewGuid();

    public WhiteboardElementType ElementType => WhiteboardElementType.Image;

    public Avalonia.Media.Imaging.Bitmap Bitmap { get; }

    public Point Center { get; private set; }

    public double ScaleX { get; private set; }

    public double ScaleY { get; private set; }

    public double RotationDegrees { get; private set; }

    public Size BaseSize => new(Bitmap.PixelSize.Width, Bitmap.PixelSize.Height);

    public ImageElement(Avalonia.Media.Imaging.Bitmap bitmap, Point center)
    {
        Bitmap = bitmap;
        Center = center;
        ScaleX = 1;
        ScaleY = 1;
        RotationDegrees = 0;
    }

    public Rect Bounds
        => new(Center.X - (BaseSize.Width * ScaleX / 2), Center.Y - (BaseSize.Height * ScaleY / 2), BaseSize.Width * ScaleX, BaseSize.Height * ScaleY);

    public void SetCenter(Point center) => Center = center;

    public void SetScale(double scaleX, double scaleY)
    {
        ScaleX = Math.Clamp(scaleX, 0.05, 20);
        ScaleY = Math.Clamp(scaleY, 0.05, 20);
    }

    public void SetRotation(double degrees) => RotationDegrees = NormalizeDegrees(degrees);

    public void Invalidate()
    {
    }

    private static double NormalizeDegrees(double degrees)
    {
        while (degrees < 0) degrees += 360;
        while (degrees >= 360) degrees -= 360;
        return degrees;
    }
}

public abstract class ShapeElement : IWhiteboardElement
{
    public abstract Guid Id { get; }

    public abstract WhiteboardElementType ElementType { get; }

    public abstract Rect Bounds { get; }

    public abstract void Invalidate();
}
