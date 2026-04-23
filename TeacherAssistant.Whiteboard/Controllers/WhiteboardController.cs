using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Services;

namespace TeacherAssistant.Whiteboard.Controllers;

public sealed class WhiteboardController : NotifyPropertyChangedObject, IDisposable
{
    private Color _penColor = Colors.Black;
    private double _penThickness = 3;
    private bool _usePenNibEffect = true;
    private bool _useBezierSmoothing = true;
    private WhiteboardTool _selectedTool = WhiteboardTool.Pen;
    private WhiteboardInteractionMode _selectedInteractionMode = WhiteboardInteractionMode.Mouse;

    public WhiteboardSurface Surface { get; } = new();

    public Color PenColor
    {
        get => _penColor;
        set => SetProperty(ref _penColor, value);
    }

    public double PenThickness
    {
        get => _penThickness;
        set => SetProperty(ref _penThickness, value);
    }

    public bool UsePenNibEffect
    {
        get => _usePenNibEffect;
        set
        {
            if (SetProperty(ref _usePenNibEffect, value))
            {
                Surface.UsePenNibEffect = value;
            }
        }
    }

    public bool UseBezierSmoothing
    {
        get => _useBezierSmoothing;
        set
        {
            if (SetProperty(ref _useBezierSmoothing, value))
            {
                Surface.UseBezierSmoothing = value;
            }
        }
    }

    public WhiteboardTool SelectedTool
    {
        get => _selectedTool;
        set => SetProperty(ref _selectedTool, value);
    }

    public WhiteboardInteractionMode SelectedInteractionMode
    {
        get => _selectedInteractionMode;
        set => SetProperty(ref _selectedInteractionMode, value);
    }

    public WhiteboardController()
    {
        Surface.UsePenNibEffect = UsePenNibEffect;
        Surface.UseBezierSmoothing = UseBezierSmoothing;
    }

    public void EnsureSize(int width, int height) => Surface.EnsureSize(width, height);

    public void Clear() => Surface.Clear();

    public void AddShape(WhiteboardShapeType shapeType, Size size, Color strokeColor, Color fillColor, double strokeThickness)
        => Surface.AddShape(shapeType, size, strokeColor, fillColor, strokeThickness);

    public void BeginStroke(Point point)
    {
        if (SelectedInteractionMode != WhiteboardInteractionMode.Pen) return;

        if (SelectedTool == WhiteboardTool.Eraser)
        {
            Surface.EraseAt(point, PenThickness * 2);
            return;
        }

        // 核心修复：调用 Surface 的新引擎方法，激活滤波器和预览
        Surface.BeginStroke(point, PenColor, PenThickness, SelectedTool);
    }

    public void ContinueStroke(Point point)
    {
        if (SelectedInteractionMode != WhiteboardInteractionMode.Pen) return;

        if (SelectedTool == WhiteboardTool.Eraser)
        {
            Surface.EraseAt(point, PenThickness * 2);
            return;
        }

        Surface.ContinueStroke(point);
    }

    public void EndStroke()
    {
        if (SelectedTool == WhiteboardTool.Eraser) return;
        
        Surface.EndStroke();
    }

    public void Dispose() => Surface.Dispose();
}
