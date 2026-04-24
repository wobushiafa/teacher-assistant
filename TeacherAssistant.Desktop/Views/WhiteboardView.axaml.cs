using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TeacherAssistant.Desktop.ViewModels;
using TeacherAssistant.Whiteboard;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Views;

namespace TeacherAssistant.Desktop.Views;

public partial class WhiteboardView : UserControl
{
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ResizeDiagonalCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor ResizeAntiDiagonalCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor ResizeVerticalCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor ResizeHorizontalCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor RotateCursor = new(StandardCursorType.Hand);

    private readonly HashSet<long> _activeDrawingPointers = new();
    private bool _isDraggingImage;

    public WhiteboardView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerExitedEvent, OnPointerLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        
        DataContextChanged += (_, _) => SyncSurface();
        Loaded += (_, _) => EnsureSurfaceSize();
        SizeChanged += OnSizeChanged;
        SyncSurface();
    }

    private WhiteboardViewModel? ViewModel => DataContext as WhiteboardViewModel;

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => EnsureSurfaceSize();

    private void SyncSurface()
    {
        Canvas.Surface = ViewModel?.Surface;
    }

    private void EnsureSurfaceSize()
    {
        var width = (int)Math.Ceiling(Bounds.Width);
        var height = (int)Math.Ceiling(Bounds.Height);
        ViewModel?.EnsureSize(width, height);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null) return;
        var pointerPos = e.GetPosition(this);
        var prop = e.GetCurrentPoint(this).Properties;
        if (!prop.IsLeftButtonPressed) return;
        Focus();

        if (ViewModel.SelectedInteractionMode == WhiteboardInteractionMode.Mouse)
        {
            if (ViewModel.Surface.BeginImageDrag(pointerPos))
            {
                _isDraggingImage = true;
                e.Pointer.Capture(this);
                return;
            }
            ViewModel.Surface.DeselectImage();
            return;
        }

        _activeDrawingPointers.Add(e.Pointer.Id);
        ViewModel.BeginStroke(pointerPos, e.Pointer.Id);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is null) return;
        var pointerPos = e.GetPosition(this);

        if (_isDraggingImage)
        {
            ViewModel.Surface.ContinueImageDrag(pointerPos);
            UpdateCursor(pointerPos);
            return;
        }

        if (!_activeDrawingPointers.Contains(e.Pointer.Id))
        {
            UpdateCursor(pointerPos);
            return;
        }

        // 核心修复：获取所有中间采样点，不再只取当前帧的一个点
        var intermediatePoints = e.GetIntermediatePoints(this);
        if (intermediatePoints != null)
        {
            foreach (var pt in intermediatePoints)
            {
                ViewModel.ContinueStroke(pt.Position, e.Pointer.Id);
            }
        }
        else
        {
            ViewModel.ContinueStroke(pointerPos, e.Pointer.Id);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingImage)
        {
            ViewModel?.Surface.EndImageDrag();
            _isDraggingImage = false;
        }
        else if (_activeDrawingPointers.Remove(e.Pointer.Id))
        {
            ViewModel?.EndStroke(e.Pointer.Id);
        }
        e.Pointer.Capture(null);
        UpdateCursor(e.GetPosition(this));
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDraggingImage)
        {
            ViewModel?.Surface.EndImageDrag();
            _isDraggingImage = false;
            return;
        }
        _activeDrawingPointers.Remove(e.Pointer.Id);
        ViewModel?.EndStroke(e.Pointer.Id);
    }

    private void OnPointerLeave(object? sender, PointerEventArgs e)
    {
        if (_activeDrawingPointers.Count > 0 || _isDraggingImage) return;
        Cursor = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel == null) return;

        if (e.Key == Key.Delete || (e.Key == Key.Back && ViewModel.SelectedInteractionMode == WhiteboardInteractionMode.Mouse))
        {
            if (ViewModel.RemoveSelectedItemCommand.CanExecute(null))
            {
                ViewModel.RemoveSelectedItemCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel.UndoCommand.CanExecute(null)) ViewModel.UndoCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel.RedoCommand.CanExecute(null)) ViewModel.RedoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void UpdateCursor(Point point)
    {
        if (ViewModel is null || ViewModel.SelectedInteractionMode != WhiteboardInteractionMode.Mouse || _activeDrawingPointers.Count > 0)
        {
            Cursor = null;
            return;
        }

        var (hit, rotation) = ViewModel.Surface.GetHitInfo(point);
        Cursor = hit switch
        {
            WhiteboardImageHitTest.None => null,
            WhiteboardImageHitTest.Move => MoveCursor,
            WhiteboardImageHitTest.RotateCenter => RotateCursor,
            _ => GetCursorForHandle(hit, rotation)
        };
    }

    private static Cursor GetCursorForHandle(WhiteboardImageHitTest handle, double rotation)
    {
        var baseAngle = handle switch
        {
            WhiteboardImageHitTest.ResizeTop => -90.0,
            WhiteboardImageHitTest.ResizeTopRight => -45.0,
            WhiteboardImageHitTest.ResizeRight => 0.0,
            WhiteboardImageHitTest.ResizeBottomRight => 45.0,
            WhiteboardImageHitTest.ResizeBottom => 90.0,
            WhiteboardImageHitTest.ResizeBottomLeft => 135.0,
            WhiteboardImageHitTest.ResizeLeft => 180.0,
            WhiteboardImageHitTest.ResizeTopLeft => 225.0,
            _ => 0.0
        };
        var angle = (baseAngle + rotation) % 180.0;
        if (angle < 0) angle += 180.0;
        if (angle < 22.5 || angle >= 157.5) return ResizeHorizontalCursor;
        if (angle < 67.5) return ResizeDiagonalCursor;
        if (angle < 112.5) return ResizeVerticalCursor;
        return ResizeAntiDiagonalCursor;
    }
}
