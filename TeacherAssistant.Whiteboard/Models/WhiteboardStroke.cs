using System.Collections.Generic;
using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class WhiteboardStroke
{
    public WhiteboardStroke(Color color, double thickness, WhiteboardTool tool)
    {
        Color = color;
        Thickness = thickness;
        Tool = tool;
    }

    public Color Color { get; }
    public double Thickness { get; }
    public WhiteboardTool Tool { get; }
    public List<StrokeSample> Samples { get; } = [];
}
