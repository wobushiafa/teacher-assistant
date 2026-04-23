using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Services;

namespace TeacherAssistant.Whiteboard.Views;

public sealed class WhiteboardCanvas : Control
{
    private WhiteboardSurface? _surface;
    private bool _redrawQueued;

    public WhiteboardSurface? Surface
    {
        get => _surface;
        set
        {
            if (ReferenceEquals(_surface, value)) return;
            if (_surface is not null) _surface.PropertyChanged -= OnSurfacePropertyChanged;
            _surface = value;
            if (_surface is not null) _surface.PropertyChanged += OnSurfacePropertyChanged;
            RequestRedraw();
        }
    }

    public WhiteboardCanvas()
    {
        Loaded += (_, _) => RequestRedraw();
    }

    public override void Render(DrawingContext context)
    {
        if (Surface == null) return;
        var opts = Surface.Options;
        var bounds = new Rect(Bounds.Size);
        
        // 1. 背景绘制
        context.FillRectangle(new SolidColorBrush(opts.DefaultBackground), bounds);

        // 2. 元素绘制 (图片、形状)
        if (Surface.Items is not null)
        {
            foreach (var item in Surface.Items)
            {
                item.Render(context);
            }
        }

        // 3. 笔迹图层绘制
        
        // 底层：已完成笔迹 (主位图)
        if (Surface.Bitmap is not null)
        {
            context.DrawImage(Surface.Bitmap, new Rect(Surface.Bitmap.Size), bounds);
        }

        // 顶层：正在书写的实时预览 (全量位图预览，确保丝滑和一致)
        if (Surface.PreviewBitmap is not null)
        {
            context.DrawImage(Surface.PreviewBitmap, new Rect(Surface.PreviewBitmap.Size), bounds);
        }

        Surface.RenderHighlighterPreview(context, bounds);

        // 4. 选中叠加层绘制
        if (Surface.SelectedItem != null)
        {
            DrawSelectionOverlay(context, Surface.SelectedItem, opts);
        }

        // 5. 吸附辅助线绘制
        var snapPen = new Pen(new SolidColorBrush(opts.SnapLineColor), opts.SnapLineThickness, opts.SnapLineDashStyle);
        if (Surface.ActiveSnapX.HasValue)
        {
            var x = Surface.ActiveSnapX.Value;
            context.DrawLine(snapPen, new Point(x, 0), new Point(x, Bounds.Height));
        }
        if (Surface.ActiveSnapY.HasValue)
        {
            var y = Surface.ActiveSnapY.Value;
            context.DrawLine(snapPen, new Point(0, y), new Point(Bounds.Width, y));
        }

        // 6. 画板边框
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#22000000")), 1), bounds);
    }

    private void OnSurfacePropertyChanged(object? sender, PropertyChangedEventArgs e) => RequestRedraw();

    private void RequestRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawQueued = false;
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private static void DrawSelectionOverlay(DrawingContext context, WhiteboardItemBase item, WhiteboardOptions opts)
    {
        var corners = item.GetCorners();
        var pen = new Pen(opts.SelectionStroke, opts.SelectionThickness);
        for (var i = 0; i < corners.Length; i++) context.DrawLine(pen, corners[i], corners[(i + 1) % corners.Length]);

        var handles = new[] {
            WhiteboardImageHitTest.ResizeTopLeft, WhiteboardImageHitTest.ResizeTop, WhiteboardImageHitTest.ResizeTopRight,
            WhiteboardImageHitTest.ResizeRight, WhiteboardImageHitTest.ResizeBottomRight, WhiteboardImageHitTest.ResizeBottom,
            WhiteboardImageHitTest.ResizeBottomLeft, WhiteboardImageHitTest.ResizeLeft
        };

        foreach (var h in handles)
        {
            DrawHandle(context, item.GetHandlePoint(h), opts.HandleSize, true, opts.HandleFill, opts.SelectionStroke);
        }

        DrawHandle(context, item.Center, opts.HandleSize, false, opts.RotationHandleFill, opts.SelectionStroke);
    }

    private static void DrawHandle(DrawingContext context, Point point, double size, bool square, IBrush fill, IBrush strokeBrush)
    {
        var half = size / 2.0;
        var rect = new Rect(point.X - half, point.Y - half, size, size);
        var pen = new Pen(strokeBrush, 1);
        if (square) context.DrawRectangle(fill, pen, rect);
        else context.DrawEllipse(fill, pen, rect);
    }
}
