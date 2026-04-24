using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class WhiteboardOptions
{
    // --- 吸附配置 ---
    public Color SnapLineColor { get; set; } = Color.Parse("#00BFFF"); // DeepSkyBlue
    public double SnapLineThickness { get; set; } = 1.0;
    public DashStyle SnapLineDashStyle { get; set; } = new DashStyle([4, 4], 0);
    public double SnapThreshold { get; set; } = 10.0;
    public double SnapGridSize { get; set; } = 20.0;

    // --- 选中与手柄配置 ---
    public IBrush SelectionStroke { get; set; } = Brushes.DeepSkyBlue;
    public double SelectionThickness { get; set; } = 1.5;
    public double HandleSize { get; set; } = 12.0;
    public IBrush HandleFill { get; set; } = Brushes.White;
    public IBrush RotationHandleFill { get; set; } = Brushes.Orange;
    public double HitTestPadding { get; set; } = 14.0;

    // --- 画布基础配置 ---
    public Color DefaultBackground { get; set; } = Colors.White;
    public Color DefaultStrokeColor { get; set; } = Colors.Black;
    public double DefaultStrokeThickness { get; set; } = 3.0;
    
    // --- 激光笔配置 ---
    public double LaserPointerFadeDurationSeconds { get; set; } = 1.5;

    public static WhiteboardOptions Default { get; } = new();
}
