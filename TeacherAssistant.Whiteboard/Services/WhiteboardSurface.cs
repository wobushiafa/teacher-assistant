using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Interactions;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class WhiteboardSurface : NotifyPropertyChangedObject, IDisposable
{
    private readonly WhiteboardDocument _document = new();
    private readonly RenderCache _renderCache = new();
    private readonly HighlighterCompositor _highlighter = new();
    private readonly SurfaceRenderCoordinator _renderCoordinator = new();
    private readonly TransformInteractionService _transformInteraction;
    
    private IToolSession? _activeSession;
    private WhiteboardStroke? _activeStroke;
    private bool _usePenNibEffect = true;
    private bool _useBezierSmoothing = true;

    // --- 灵动平滑滤波器状态 ---
    private Point _filterPos;
    private Point _filterTrend;

    private WhiteboardOptions _options = WhiteboardOptions.Default;

    public WhiteboardDocument Document => _document;
    public WhiteboardOptions Options { get => _options; set => SetProperty(ref _options, value ?? WhiteboardOptions.Default); }
    public IReadOnlyList<WhiteboardStroke> CompletedStrokes => _document.Strokes.Select(ConvertToLegacyStroke).ToList();
    public IReadOnlyList<WhiteboardItemBase> Items => _document.Items;
    public WhiteboardItemBase? SelectedItem => _transformInteraction.SelectedItem;
    public WhiteboardImageItem? SelectedImage => _transformInteraction.SelectedImage;
    public WhiteboardShapeItem? SelectedShape => _transformInteraction.SelectedShape;
    public WriteableBitmap? Bitmap => _renderCoordinator.Bitmap;
    public WriteableBitmap? PreviewBitmap => _renderCoordinator.PreviewBitmap;
    public double? ActiveSnapX => _transformInteraction.ActiveSnapX;
    public double? ActiveSnapY => _transformInteraction.ActiveSnapY;
    public WhiteboardStroke? ActiveStroke => _activeStroke;

    public bool UsePenNibEffect 
    { 
        get => _usePenNibEffect; 
        set { if (SetProperty(ref _usePenNibEffect, value)) RasterizeAll(); } 
    }

    public bool UseBezierSmoothing 
    { 
        get => _useBezierSmoothing; 
        set { if (SetProperty(ref _useBezierSmoothing, value)) RasterizeAll(); } 
    }

    public WhiteboardSurface()
    {
        _transformInteraction = new TransformInteractionService(
            () => _options.HitTestPadding,
            () => _options.SnapThreshold,
            () => _options.SnapGridSize);
    }

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _renderCoordinator.EnsureSize(width, height);
        _highlighter.EnsureSize(width, height); _renderCache.EnsureSize(width, height); RasterizeAll();
    }

    public void SetActiveSession(IToolSession? session) { _activeSession = session; }
    public IToolSession CreateToolSession(WhiteboardTool tool) => ToolSessionFactory.Create(_document, tool);

    public void BeginStroke(Point point, Color color, double thickness, WhiteboardTool tool)
    {
        DeselectImage();
        _filterPos = point; _filterTrend = new Point(0, 0);

        var stroke = new WhiteboardStroke(GetStrokeColor(color, tool), thickness, tool);
        stroke.Samples.Add(new StrokeSample(point, Stopwatch.GetTimestamp(), _usePenNibEffect ? thickness * 0.1 : thickness));
        _activeStroke = stroke;
        _renderCoordinator.ClearPreview();
        NotifySurfaceChanged();
    }

    public void ContinueStroke(Point rawPoint)
    {
        if (_activeStroke is null) return;

        const double alpha = 0.65; const double beta = 0.45;
        var prevPos = _filterPos;
        _filterPos = rawPoint * alpha + (prevPos + _filterTrend) * (1.0 - alpha);
        _filterTrend = (_filterPos - prevPos) * beta + _filterTrend * (1.0 - beta);

        var smoothedPoint = _filterPos;
        if (AreClose(_activeStroke.Samples[^1].Point, smoothedPoint)) return;

        var ts = Stopwatch.GetTimestamp();
        var w = ComputeWidth(_activeStroke.Samples[^1], smoothedPoint, ts, _activeStroke.Thickness, _usePenNibEffect);
        _activeStroke.Samples.Add(new StrokeSample(smoothedPoint, ts, w));

        // ActiveStroke 将由 SyncActiveStrokePreview 内的 NotifyRenderChanged 通知，无需重复触发
        SyncActiveStrokePreview();
    }

    public void SyncActiveStrokePreview()
    {
        if (_activeStroke == null) return;
        
        if (_activeStroke.Tool == WhiteboardTool.Highlighter)
        {
            // 荧光笔使用矢量渲染，避免透明像素重复叠加变暗，且走 GPU 加速
            _highlighter.UpdatePreview(_activeStroke, _useBezierSmoothing);
        }
        else
        {
            _renderCoordinator.RenderStrokePreview(_activeStroke, _useBezierSmoothing, _usePenNibEffect);
        }
        // 绘制中仅通知渲染相关属性，避免无效的 SelectedItem/Items 等 7 个事件
        NotifyRenderChanged();
    }

    public void EndStroke()
    {
        if (_activeStroke == null) return;

        // 核心修复 1：将当前笔迹烘焙到主位图层
        if (_activeStroke.Tool != WhiteboardTool.Highlighter)
        {
            _renderCoordinator.CommitStroke(_activeStroke, _useBezierSmoothing, _usePenNibEffect);
        }

        // 核心修复 2：加入完成列表，并同步到后端 Document（供橡皮擦使用）
        _document.AddStroke(ConvertToDocumentElement(_activeStroke));

        // 核心修复 3：清理预览
        _activeStroke = null;
        _renderCoordinator.ClearPreview();
        _highlighter.RebuildFromDocument(_document);
        NotifySurfaceChanged();
    }

    private StrokeElement ConvertToDocumentElement(WhiteboardStroke s)
    {
        var e = new StrokeElement(s.Color, s.Thickness, s.Tool);
        foreach (var sm in s.Samples) e.Samples.Add(sm);
        return e;
    }

    public void RasterizeAll()
    {
        _renderCoordinator.RasterizeAll(_document, ConvertToLegacyStroke, _useBezierSmoothing, _usePenNibEffect, _highlighter);
        NotifySurfaceChanged();
    }

    private WhiteboardStroke ConvertToLegacyStroke(StrokeElement element)
    {
        var stroke = new WhiteboardStroke(element.Color, element.Thickness, element.Tool);
        foreach (var s in element.Samples)
        {
            var w = (_usePenNibEffect && element.Tool != WhiteboardTool.Highlighter) ? s.Width : element.Thickness;
            stroke.Samples.Add(new StrokeSample(s.Point, s.Timestamp, w));
        }
        return stroke;
    }

    private static double ComputeWidth(StrokeSample prev, Point curr, long ts, double thickness, bool useNib)
    {
        if (!useNib) return thickness;
        var dt = Math.Max(1.0 / 1000.0, (ts - prev.Timestamp) / (double)Stopwatch.Frequency);
        var dist = Math.Sqrt((curr.X - prev.Point.X) * (curr.X - prev.Point.X) + (curr.Y - prev.Point.Y) * (curr.Y - prev.Point.Y));
        var velocity = dist / dt;
        var maxWidth = thickness * 1.2; var minWidth = Math.Max(0.2, thickness * 0.1);
        var velocityFactor = 1.0 - Math.Exp(-velocity / 600.0);
        var targetWidth = maxWidth - ((maxWidth - minWidth) * velocityFactor);
        return prev.Width + (targetWidth - prev.Width) * 0.25;
    }

    private static Color GetStrokeColor(Color c, WhiteboardTool t) { if (t != WhiteboardTool.Highlighter) return c; return Color.FromArgb((byte)Math.Min((int)c.A, 96), c.R, c.G, c.B); }
    public void RefreshRegion(Rect d) => RasterizeAll();
    public void RenderHighlighterPreview(DrawingContext c, Rect b) => _highlighter.Render(c, b);
    public void EraseAt(Point p, double r) { var searchRect = new Rect(p.X - r, p.Y - r, r * 2, r * 2); var removedIds = new List<Guid>(); foreach (var s in _document.QueryStrokes(searchRect)) if (IsPointNearStroke(p, s, r)) removedIds.Add(s.Id); if (removedIds.Count > 0) { foreach (var id in removedIds) _document.RemoveElement(id); RasterizeAll(); } }
    public void Clear() { _document.Clear(); _renderCoordinator.ClearAll(); _highlighter.Reset(); DeselectImage(); NotifySurfaceChanged(); }
    public bool BeginImageDrag(Point point)
    {
        var started = _transformInteraction.BeginDrag(point, _transformInteraction.SelectedItem, _document.Items);
        if (!started)
        {
            DeselectImage();
            return false;
        }

        NotifySurfaceChanged();
        return true;
    }

    public void ContinueImageDrag(Point point)
    {
        _transformInteraction.ContinueDrag(point, _document.Items);
        NotifySurfaceChanged();
    }

    public void EndImageDrag()
    {
        _transformInteraction.EndDrag();
        NotifySurfaceChanged();
    }
    public void AddImage(Bitmap b) { var size = _renderCoordinator.PixelSize; var s = Math.Min(Math.Min(size.Width * 0.6 / b.PixelSize.Width, size.Height * 0.6 / b.PixelSize.Height), 1.0); var img = new WhiteboardImageItem(b, new Point(size.Width / 2.0, size.Height / 2.0)); img.SetScale(s, s); img.PropertyChanged += OnObjectPropertyChanged; _document.AddItem(img); _transformInteraction.Select(img); NotifySurfaceChanged(); }
    public void AddShape(WhiteboardShapeType t, Size sz, Color sc, Color fc, double st) { var size = _renderCoordinator.PixelSize; var sh = new WhiteboardShapeItem(t, new Point(size.Width / 2.0, size.Height / 2.0), sz, sc, fc, st); sh.PropertyChanged += OnObjectPropertyChanged; _document.AddItem(sh); _transformInteraction.Select(sh); NotifySurfaceChanged(); }
    private void OnObjectPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (s is WhiteboardItemBase i &&
            (e.PropertyName == nameof(WhiteboardItemBase.X) ||
             e.PropertyName == nameof(WhiteboardItemBase.Y) ||
             e.PropertyName == nameof(WhiteboardItemBase.Width) ||
             e.PropertyName == nameof(WhiteboardItemBase.Height) ||
             e.PropertyName == nameof(WhiteboardItemBase.RotationDegrees)))
        {
            // Spatial index is owned by the document now.
        }

        NotifySurfaceChanged();
    }

    public (WhiteboardImageHitTest Hit, double Rotation) GetHitInfo(Point p)
        => _transformInteraction.GetHitInfo(p, _document.Items);

    private WhiteboardItemBase? HitTestAll(Point p)
    {
        WhiteboardItemBase? topHit = null;
        foreach (var i in _document.QueryItems(new Rect(p.X, p.Y, 0.001, 0.001)))
        {
            if (i.HitTest(p, _options.HitTestPadding) != WhiteboardImageHitTest.None &&
                (topHit == null || GetItemIndex(i) > GetItemIndex(topHit)))
            {
                topHit = i;
            }
        }

        return topHit;
    }

    public void BringToFront()
    {
        if (_transformInteraction.SelectedItem is { } item)
        {
            _document.BringToFront(item);
        }
        NotifySurfaceChanged();
    }

    public void SendToBack()
    {
        if (_transformInteraction.SelectedItem is { } item)
        {
            _document.SendToBack(item);
        }
        NotifySurfaceChanged();
    }

    public void RemoveSelectedItem()
    {
        var removed = _transformInteraction.RemoveSelectedItem(new List<WhiteboardItemBase>(_document.Items));
        if (removed is null)
        {
            return;
        }

        removed.PropertyChanged -= OnObjectPropertyChanged;
        if (removed is IDisposable d)
        {
            d.Dispose();
        }
        _document.RemoveElement(removed.Id);

        NotifySurfaceChanged();
    }

    public void DeselectImage()
    {
        _transformInteraction.Deselect();
        NotifySurfaceChanged();
    }

    public void Dispose()
    {
        _renderCoordinator.Dispose();
        _highlighter.Dispose();
        foreach (var i in _document.Items)
        {
            i.PropertyChanged -= OnObjectPropertyChanged;
            if (i is IDisposable d)
            {
                d.Dispose();
            }
        }
    }

    private void NotifySurfaceChanged()
    {
        OnPropertyChanged(nameof(CompletedStrokes));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ActiveStroke));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(SelectedShape));
        OnPropertyChanged(nameof(Bitmap));
        OnPropertyChanged(nameof(PreviewBitmap));
        OnPropertyChanged(nameof(ActiveSnapX));
        OnPropertyChanged(nameof(ActiveSnapY));
    }

    /// <summary>绘制热路径专用：仅通知渲染层必要的 3 个属性，减少 7 个无效事件</summary>
    private void NotifyRenderChanged()
    {
        OnPropertyChanged(nameof(ActiveStroke));
        OnPropertyChanged(nameof(Bitmap));
        OnPropertyChanged(nameof(PreviewBitmap));
    }

    private int GetItemIndex(WhiteboardItemBase item)
    {
        for (var i = 0; i < _document.Items.Count; i++)
        {
            if (ReferenceEquals(_document.Items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool AreClose(Point a, Point b) => Math.Abs(a.X - b.X) < 0.25 && Math.Abs(a.Y - b.Y) < 0.25;
    private static bool IsPointNearStroke(Point point, StrokeElement stroke, double radius) { var radiusSquared = radius * radius; foreach (var sample in stroke.Samples) { var dx = point.X - sample.Point.X; var dy = point.Y - sample.Point.Y; if (dx * dx + dy * dy <= radiusSquared) return true; } for (var i = 1; i < stroke.Samples.Count; i++) if (DistanceToSegment(point.X, point.Y, stroke.Samples[i - 1].Point, stroke.Samples[i].Point) <= radius) return true; return false; }
    private static double DistanceToSegment(double px, double py, Point a, Point b) { var vx = b.X - a.X; var vy = b.Y - a.Y; var wx = px - a.X; var wy = py - a.Y; var lenSq = vx * vx + vy * vy; if (lenSq <= double.Epsilon) return Math.Sqrt(wx * wx + wy * wy); var t = Math.Clamp((wx * vx + wy * vy) / lenSq, 0, 1); return Math.Sqrt((px - (a.X + t * vx)) * (px - (a.X + t * vx)) + (py - (a.Y + t * vy)) * (py - (a.Y + t * vy))); }
}
