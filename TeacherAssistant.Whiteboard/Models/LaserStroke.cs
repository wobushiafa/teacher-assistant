using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class LaserStroke
{
    public long PointerId { get; }
    public Color Color { get; }
    public List<Point> Points { get; } = new();
    public double Thickness { get; }
    public double Opacity { get; set; } = 1.0;
    public bool IsFinished { get; set; }

    public LaserStroke(long pointerId, Color color, double thickness)
    {
        PointerId = pointerId;
        Color = color;
        Thickness = thickness;
    }
}
