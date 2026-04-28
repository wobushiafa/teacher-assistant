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
    private bool _isPanningViewport;
    private Point _lastPanScreenPoint;

    public WhiteboardView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
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
        var worldPoint = ViewModel.Surface.ScreenToWorld(pointerPos);
        var prop = e.GetCurrentPoint(this).Properties;
        Focus();

        if (prop.IsRightButtonPressed)
        {
            if (!ViewModel.Surface.IsInfiniteCanvasEnabled)
            {
                return;
            }

            _isPanningViewport = true;
            _lastPanScreenPoint = pointerPos;
            e.Pointer.Capture(this);
            Cursor = MoveCursor;
            return;
        }

        if (!prop.IsLeftButtonPressed) return;

        if (ViewModel.SelectedInteractionMode == WhiteboardInteractionMode.Mouse)
        {
            if (ViewModel.Surface.BeginImageDrag(worldPoint))
            {
                _isDraggingImage = true;
                e.Pointer.Capture(this);
                return;
            }
            ViewModel.Surface.DeselectImage();
            return;
        }

        _activeDrawingPointers.Add(e.Pointer.Id);
        ViewModel.BeginStroke(worldPoint, e.Pointer.Id);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is null) return;
        var pointerPos = e.GetPosition(this);
        var worldPoint = ViewModel.Surface.ScreenToWorld(pointerPos);

        if (_isPanningViewport)
        {
            var delta = pointerPos - _lastPanScreenPoint;
            ViewModel.Surface.PanViewport(delta);
            _lastPanScreenPoint = pointerPos;
            return;
        }

        if (_isDraggingImage)
        {
            ViewModel.Surface.ContinueImageDrag(worldPoint);
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
                ViewModel.ContinueStroke(ViewModel.Surface.ScreenToWorld(pt.Position), e.Pointer.Id);
            }
        }
        else
        {
            ViewModel.ContinueStroke(worldPoint, e.Pointer.Id);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanningViewport)
        {
            _isPanningViewport = false;
            Cursor = null;
        }
        else if (_isDraggingImage)
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

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var deltaY = e.Delta.Y;
        if (!ViewModel.Surface.IsInfiniteCanvasEnabled || Math.Abs(deltaY) <= double.Epsilon)
        {
            return;
        }

        var zoomFactor = Math.Pow(1.08, deltaY);
        ViewModel.Surface.ZoomAt(e.GetPosition(this), zoomFactor);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isPanningViewport)
        {
            _isPanningViewport = false;
            Cursor = null;
            return;
        }
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
        if (_activeDrawingPointers.Count > 0 || _isDraggingImage || _isPanningViewport) return;
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
        else if (e.Key == Key.D0 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel.Surface.IsInfiniteCanvasEnabled && ViewModel.ResetViewCommand.CanExecute(null)) ViewModel.ResetViewCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.D1 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel.Surface.IsInfiniteCanvasEnabled && ViewModel.FitToContentCommand.CanExecute(null)) ViewModel.FitToContentCommand.Execute(null);
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

        var (hit, rotation) = ViewModel.Surface.GetHitInfo(ViewModel.Surface.ScreenToWorld(point));
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
