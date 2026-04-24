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
    
    private readonly Dictionary<long, WhiteboardStroke> _activeStrokes = new();
    private readonly Dictionary<long, (Point Pos, Point Trend)> _filters = new();
    private IToolSession? _activeSession;
    private bool _usePenNibEffect = true;
    private bool _useBezierSmoothing = true;

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
    public WhiteboardStroke? ActiveStroke => _activeStrokes.Values.FirstOrDefault();

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

    public void BeginStroke(Point point, Color color, double thickness, WhiteboardTool tool, long pointerId)
    {
        DeselectImage();
        _filters[pointerId] = (point, new Point(0, 0));

        var stroke = new WhiteboardStroke(GetStrokeColor(color, tool), thickness, tool);
        stroke.Samples.Add(new StrokeSample(point, Stopwatch.GetTimestamp(), _usePenNibEffect ? thickness * 0.5 : thickness));
        _activeStrokes[pointerId] = stroke;
        
        // 我们不在这里 ClearPreview，因为可能有其他正在进行的笔迹
        // 但我们需要确保 UI 知道需要重绘
        NotifySurfaceChanged();
    }

    public void ContinueStroke(Point rawPoint, long pointerId)
    {
        if (!_activeStrokes.TryGetValue(pointerId, out var stroke)) return;
        if (!_filters.TryGetValue(pointerId, out var filter)) return;

        const double alpha = 0.65; const double beta = 0.45;
        var prevPos = filter.Pos;
        var filterTrend = filter.Trend;
        var filterPos = rawPoint * alpha + (prevPos + filterTrend) * (1.0 - alpha);
        filterTrend = (filterPos - prevPos) * beta + filterTrend * (1.0 - beta);
        _filters[pointerId] = (filterPos, filterTrend);

        var smoothedPoint = filterPos;
        if (AreClose(stroke.Samples[^1].Point, smoothedPoint)) return;

        var ts = Stopwatch.GetTimestamp();
        var w = ComputeWidth(stroke.Samples[^1], smoothedPoint, ts, stroke.Thickness, _usePenNibEffect);
        stroke.Samples.Add(new StrokeSample(smoothedPoint, ts, w));

        SyncActiveStrokePreview(pointerId);
    }

    public void SyncActiveStrokePreview(long pointerId)
    {
        if (!_activeStrokes.TryGetValue(pointerId, out var stroke)) return;
        
        if (stroke.Tool == WhiteboardTool.Highlighter)
        {
            _highlighter.UpdatePreview(pointerId, stroke, _useBezierSmoothing);
        }
        else
        {
            _renderCoordinator.RenderStrokePreview(stroke, _useBezierSmoothing, _usePenNibEffect);
        }
        NotifyRenderChanged();
    }

    public void EndStroke(long pointerId)
    {
        if (!_activeStrokes.TryGetValue(pointerId, out var stroke)) return;

        // 烘焙到主位图层
        if (stroke.Tool != WhiteboardTool.Highlighter)
        {
            _renderCoordinator.CommitStroke(stroke, _useBezierSmoothing, _usePenNibEffect);
        }

        // 加入完成列表
        _document.AddStroke(ConvertToDocumentElement(stroke));

        // 清理状态
        _activeStrokes.Remove(pointerId);
        _filters.Remove(pointerId);
        _highlighter.CommitStroke(pointerId);

        // 如果还有其他正在进行的笔迹，我们需要重建预览层
        if (_activeStrokes.Count > 0)
        {
            _renderCoordinator.ClearPreview();
            foreach (var active in _activeStrokes.Values)
            {
                if (active.Tool != WhiteboardTool.Highlighter)
                {
                    // 重绘整个笔迹到预览层
                    WhiteboardStrokeRenderer.DrawStroke(_renderCoordinator.PreviewBitmap!, active, _useBezierSmoothing, _usePenNibEffect);
                }
            }
        }
        else
        {
            _renderCoordinator.ClearPreview();
        }

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

        // 提升基础粗细和最小值，补偿以前 CPU 抗锯齿 Bug 带来的视觉加粗（约 2px）
        var maxWidth = thickness * 1.5; 
        var minWidth = Math.Max(1.0, thickness * 0.4); 
        // 降低速度敏感度，使得正常书写速度下不会变得过细
        var velocityFactor = 1.0 - Math.Exp(-velocity / 1500.0);
        var targetWidth = maxWidth - ((maxWidth - minWidth) * velocityFactor);
        
        // 稍微加快粗细变化的响应速度
        return prev.Width + (targetWidth - prev.Width) * 0.35;
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
        // OnObjectPropertyChanged 会自动监听到位置变化并触发重绘，这里不需要重复触发全量通知
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
            // 位置或大小变化时，只需要触发重绘热路径（极轻量），千万不要触发 10 个全量属性的更新
            NotifyRenderChanged();
            return;
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
