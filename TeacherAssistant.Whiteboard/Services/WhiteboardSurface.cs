using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TeacherAssistant.Whiteboard.Interactions;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Undo;
using Avalonia.Threading;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class WhiteboardSurface : NotifyPropertyChangedObject, IDisposable
{
    private readonly WhiteboardDocument _document = new();
    private readonly HighlighterCompositor _highlighter = new();
    private readonly SurfaceRenderCoordinator _renderCoordinator = new();
    private readonly InkTileCache _inkTileCache = new();
    private readonly TransformInteractionService _transformInteraction;
    
    private readonly Stack<IUndoCommand> _undoStack = new();
    private readonly Stack<IUndoCommand> _redoStack = new();
    
    private (Point Center, double ScaleX, double ScaleY, double Rotation)? _initialTransform;
    
    private readonly List<LaserStroke> _laserStrokes = new();
    private DispatcherTimer? _laserTimer;
    private DispatcherTimer? _eraseRenderTimer;
    private DispatcherTimer? _tileRebuildTimer;
    private DispatcherTimer? _previewClearTimer;
    private bool _eraseRenderPending;
    
    private readonly Dictionary<long, WhiteboardStroke> _activeStrokes = new();
    private readonly Dictionary<long, (Point Pos, Point Trend)> _filters = new();
    private IToolSession? _activeSession;
    private bool _usePenNibEffect = true;
    private bool _useBezierSmoothing = true;
    private bool _isInfiniteCanvasEnabled = true;
    private Vector _lastPanDirection;
    private double _zoom = 1.0;

    private WhiteboardOptions _options = WhiteboardOptions.Default;
    private Point _viewportOrigin;
    private Size _viewportSize;

    public WhiteboardDocument Document => _document;
    public WhiteboardOptions Options { get => _options; set => SetProperty(ref _options, value ?? WhiteboardOptions.Default); }
    public IReadOnlyList<WhiteboardItemBase> Items => _document.Items;
    public WhiteboardItemBase? SelectedItem => _transformInteraction.SelectedItem;
    public WhiteboardImageItem? SelectedImage => _transformInteraction.SelectedImage;
    public WhiteboardShapeItem? SelectedShape => _transformInteraction.SelectedShape;
    public WriteableBitmap? PreviewBitmap => _renderCoordinator.PreviewBitmap;
    public double? ActiveSnapX => _transformInteraction.ActiveSnapX;
    public double? ActiveSnapY => _transformInteraction.ActiveSnapY;
    public WhiteboardStroke? ActiveStroke => _activeStrokes.Values.FirstOrDefault();
    public IReadOnlyList<LaserStroke> LaserStrokes => _laserStrokes;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public Point ViewportOrigin
    {
        get => _viewportOrigin;
        private set
        {
            if (SetProperty(ref _viewportOrigin, value))
            {
                RebuildActiveStrokePreview();
                NotifySurfaceChanged();
            }
        }
    }

    public Size ViewportSize => _viewportSize;
    public double Zoom
    {
        get => _zoom;
        private set
        {
            var clamped = Math.Clamp(value, 0.25, 4.0);
            if (SetProperty(ref _zoom, clamped))
            {
                RebuildActiveStrokePreview();
                EnsureTileRebuildScheduled();
                NotifySurfaceChanged();
            }
        }
    }

    public bool UsePenNibEffect 
    { 
        get => _usePenNibEffect; 
        set { if (SetProperty(ref _usePenNibEffect, value)) RasterizeAll(); } 
    }

    public bool IsInfiniteCanvasEnabled
    {
        get => _isInfiniteCanvasEnabled;
        set
        {
            if (!SetProperty(ref _isInfiniteCanvasEnabled, value))
            {
                return;
            }

            if (!value)
            {
                _lastPanDirection = default;
                _tileRebuildTimer?.Stop();
                Zoom = 1.0;
                ViewportOrigin = new Point(0, 0);
            }

            RasterizeAll();
        }
    }

    public bool UseBezierSmoothing 
    { 
        get => _useBezierSmoothing; 
        set { if (SetProperty(ref _useBezierSmoothing, value)) RasterizeAll(); } 
    }

    public WhiteboardSurface()
    {
        _transformInteraction = new TransformInteractionService(
            () => ScreenLengthToWorld(_options.HitTestPadding),
            () => ScreenLengthToWorld(_options.SnapThreshold),
            () => _options.SnapGridSize);
    }

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        var newSize = new Size(width, height);
        if (_viewportSize != newSize)
        {
            _viewportSize = newSize;
            OnPropertyChanged(nameof(ViewportSize));
        }
        _renderCoordinator.EnsureSize(width, height);
        _highlighter.EnsureSize(width, height);
        RasterizeAll();
    }

    public Point ScreenToWorld(Point point)
        => new((point.X / EffectiveZoom) + ViewportOrigin.X, (point.Y / EffectiveZoom) + ViewportOrigin.Y);

    public Point WorldToScreen(Point point)
        => new((point.X - ViewportOrigin.X) * EffectiveZoom, (point.Y - ViewportOrigin.Y) * EffectiveZoom);

    public Rect GetViewportWorldBounds() => new(ViewportOrigin, new Size(_viewportSize.Width / EffectiveZoom, _viewportSize.Height / EffectiveZoom));

    public Matrix GetWorldToScreenMatrix()
        => new(EffectiveZoom, 0, 0, EffectiveZoom, -ViewportOrigin.X * EffectiveZoom, -ViewportOrigin.Y * EffectiveZoom);

    public double ScreenLengthToWorld(double screenLength) => screenLength / EffectiveZoom;

    public void RenderCommittedInk(DrawingContext context)
    {
        var hasVisibleCacheMiss = _inkTileCache.Render(
            context,
            GetViewportWorldBounds(),
            _document,
            ConvertStrokeToWorldSpace,
            _useBezierSmoothing,
            _usePenNibEffect,
            EffectiveZoom,
            allowFallback: _activeStrokes.Count == 0);

        if (hasVisibleCacheMiss && _activeStrokes.Count == 0)
        {
            EnsureTileRebuildScheduled();
        }
    }

    public void PanViewport(Vector deltaScreen)
    {
        if (!IsInfiniteCanvasEnabled || deltaScreen == default)
        {
            return;
        }

        _lastPanDirection = deltaScreen;
        ViewportOrigin = new Point(
            ViewportOrigin.X - (deltaScreen.X / EffectiveZoom),
            ViewportOrigin.Y - (deltaScreen.Y / EffectiveZoom));
        EnsureTileRebuildScheduled();
    }

    public void ZoomAt(Point screenAnchor, double zoomFactor)
    {
        if (!IsInfiniteCanvasEnabled || zoomFactor <= 0)
        {
            return;
        }

        var worldAnchor = ScreenToWorld(screenAnchor);
        var oldZoom = Zoom;
        Zoom *= zoomFactor;

        if (Math.Abs(Zoom - oldZoom) <= double.Epsilon)
        {
            return;
        }

        ViewportOrigin = new Point(
            worldAnchor.X - (screenAnchor.X / Zoom),
            worldAnchor.Y - (screenAnchor.Y / Zoom));
        _lastPanDirection = default;
        EnsureTileRebuildScheduled();
    }

    public void ResetViewport()
    {
        if (!IsInfiniteCanvasEnabled)
        {
            return;
        }

        _lastPanDirection = default;
        Zoom = 1.0;
        ViewportOrigin = new Point(0, 0);
        EnsureTileRebuildScheduled();
    }

    public void FitToContent(double viewportPaddingScreen = 48)
    {
        if (!IsInfiniteCanvasEnabled)
        {
            return;
        }

        var contentBounds = GetContentBounds();
        if (contentBounds is null)
        {
            ResetViewport();
            return;
        }

        if (_viewportSize.Width <= 0 || _viewportSize.Height <= 0)
        {
            return;
        }

        var paddedWidth = Math.Max(1, _viewportSize.Width - (viewportPaddingScreen * 2));
        var paddedHeight = Math.Max(1, _viewportSize.Height - (viewportPaddingScreen * 2));
        var targetWidth = Math.Max(1, contentBounds.Value.Width);
        var targetHeight = Math.Max(1, contentBounds.Value.Height);

        var fitZoomX = paddedWidth / targetWidth;
        var fitZoomY = paddedHeight / targetHeight;
        var targetZoom = Math.Clamp(Math.Min(fitZoomX, fitZoomY), 0.25, 4.0);

        _lastPanDirection = default;
        Zoom = targetZoom;
        ViewportOrigin = new Point(
            contentBounds.Value.Center.X - ((_viewportSize.Width / Zoom) / 2.0),
            contentBounds.Value.Center.Y - ((_viewportSize.Height / Zoom) / 2.0));
        EnsureTileRebuildScheduled();
    }

    public void SetActiveSession(IToolSession? session) { _activeSession = session; }
    public IToolSession CreateToolSession(WhiteboardTool tool) => ToolSessionFactory.Create(_document, tool);

    public void BeginStroke(Point point, Color color, double thickness, WhiteboardTool tool, long pointerId)
    {
        DeselectImage();
        CancelDeferredPreviewClear();
        _tileRebuildTimer?.Stop();

        if (tool == WhiteboardTool.LaserPointer)
        {
            // 确保旧的同 ID 笔迹被标记为完成，防止点连接
            foreach (var existing in _laserStrokes)
            {
                if (existing.PointerId == pointerId) existing.IsFinished = true;
            }

            var ls = new LaserStroke(pointerId, color, thickness);
            ls.Points.Add(point);
            _laserStrokes.Add(ls);
            StartLaserTimer();
            NotifySurfaceChanged();
            return;
        }

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
        var ls = _laserStrokes.LastOrDefault(x => x.PointerId == pointerId && !x.IsFinished);
        if (ls != null)
        {
            if (AreClose(ls.Points[^1], rawPoint)) return;
            ls.Points.Add(rawPoint);
            NotifyRenderChanged();
            return;
        }

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
            _renderCoordinator.RenderStrokePreview(CreateViewportStroke(stroke), _useBezierSmoothing, _usePenNibEffect);
        }
        NotifyRenderChanged();
    }

    public void EndStroke(long pointerId)
    {
        var ls = _laserStrokes.LastOrDefault(x => x.PointerId == pointerId && !x.IsFinished);
        if (ls != null)
        {
            ls.IsFinished = true;
            return;
        }

        if (!_activeStrokes.TryGetValue(pointerId, out var stroke)) return;

        var element = ConvertToDocumentElement(stroke);
        var affectedBounds = element.Bounds;

        // 加入完成列表
        ExecuteCommand(new AddStrokeCommand(_document, element));

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
                    WhiteboardStrokeRenderer.DrawStroke(_renderCoordinator.PreviewBitmap!, CreateViewportStroke(active), _useBezierSmoothing, _usePenNibEffect);
                }
            }
        }
        else
        {
            ScheduleDeferredPreviewClear();
        }

        _highlighter.RebuildFromDocument(_document);
        _inkTileCache.Invalidate(affectedBounds);
        _inkTileCache.RebuildDirtyTiles(
            affectedBounds,
            _document,
            ConvertStrokeToWorldSpace,
            _useBezierSmoothing,
            _usePenNibEffect,
            EffectiveZoom);
        EnsureTileRebuildScheduled();
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
        _lastPanDirection = default;
        _inkTileCache.Invalidate();
        _renderCoordinator.RasterizeAll(_document, _highlighter);
        _eraseRenderPending = false;
        NotifySurfaceChanged();
    }

    private WhiteboardStroke ConvertStrokeToWorldSpace(StrokeElement element)
    {
        var stroke = new WhiteboardStroke(element.Color, element.Thickness, element.Tool);
        foreach (var sample in element.Samples)
        {
            var width = (_usePenNibEffect && element.Tool != WhiteboardTool.Highlighter) ? sample.Width : element.Thickness;
            stroke.Samples.Add(new StrokeSample(sample.Point, sample.Timestamp, width));
        }

        return stroke;
    }

    private WhiteboardStroke CreateViewportStroke(WhiteboardStroke source)
    {
        var stroke = new WhiteboardStroke(source.Color, source.Thickness, source.Tool);
        foreach (var sample in source.Samples)
        {
            stroke.Samples.Add(new StrokeSample(WorldToScreen(sample.Point), sample.Timestamp, sample.Width * EffectiveZoom));
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
    public void RenderHighlighterPreview(DrawingContext c, Rect b) => _highlighter.Render(c, b);
    public void EraseAt(Point p, double r)
    {
        var searchRect = new Rect(p.X - r, p.Y - r, r * 2, r * 2);
        var removed = new List<StrokeElement>();
        foreach (var stroke in _document.QueryStrokes(searchRect))
        {
            if (IsPointNearStroke(p, stroke, r))
            {
                removed.Add(stroke);
            }
        }

        if (removed.Count == 0)
        {
            return;
        }

        foreach (var stroke in removed)
        {
            _inkTileCache.Invalidate(stroke.Bounds);
            ExecuteCommand(new RemoveElementCommand(_document, stroke));
        }

        EnsureTileRebuildScheduled();
        ScheduleEraseRender();
    }
    public void Clear() { ExecuteCommand(new ClearCommand(_document)); _inkTileCache.Invalidate(); _renderCoordinator.ClearAll(); _highlighter.Reset(); DeselectImage(); NotifySurfaceChanged(); }
    public bool BeginImageDrag(Point point)
    {
        var item = _transformInteraction.SelectedItem;
        if (item != null)
        {
            _initialTransform = (item.Center, item.ScaleX, item.ScaleY, item.RotationDegrees);
        }

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
        var item = _transformInteraction.SelectedItem;
        _transformInteraction.EndDrag();
        
        if (item != null && _initialTransform.HasValue)
        {
            var initial = _initialTransform.Value;
            if (item.Center != initial.Center || item.ScaleX != initial.ScaleX || item.ScaleY != initial.ScaleY || item.RotationDegrees != initial.Rotation)
            {
                PushCommand(new TransformCommand(item, 
                    initial.Center, initial.ScaleX, initial.ScaleY, initial.Rotation,
                    item.Center, item.ScaleX, item.ScaleY, item.RotationDegrees));
            }
        }
        _initialTransform = null;
        NotifySurfaceChanged();
    }
    public void AddImage(Bitmap b) { var size = _renderCoordinator.PixelSize; var s = Math.Min(Math.Min(size.Width * 0.6 / b.PixelSize.Width, size.Height * 0.6 / b.PixelSize.Height), 1.0); var img = new WhiteboardImageItem(b, GetViewportWorldBounds().Center); img.SetScale(s, s); img.PropertyChanged += OnObjectPropertyChanged; ExecuteCommand(new AddItemCommand(_document, img)); _transformInteraction.Select(img); NotifySurfaceChanged(); }
    public void AddShape(WhiteboardShapeType t, Size sz, Color sc, Color fc, double st) { var sh = new WhiteboardShapeItem(t, GetViewportWorldBounds().Center, sz, sc, fc, st); sh.PropertyChanged += OnObjectPropertyChanged; ExecuteCommand(new AddItemCommand(_document, sh)); _transformInteraction.Select(sh); NotifySurfaceChanged(); }
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
            if (i.HitTest(p, ScreenLengthToWorld(_options.HitTestPadding)) != WhiteboardImageHitTest.None &&
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
        ExecuteCommand(new RemoveElementCommand(_document, removed));

        NotifySurfaceChanged();
    }

    private void ExecuteCommand(IUndoCommand command)
    {
        command.Execute();
        PushCommand(command);
    }

    private void PushCommand(IUndoCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        NotifyUndoRedoChanged();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        RefreshAfterDocumentCommand(command);
        NotifyUndoRedoChanged();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        command.Redo();
        _undoStack.Push(command);
        RefreshAfterDocumentCommand(command);
        NotifyUndoRedoChanged();
    }

    private void NotifyUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ScheduleEraseRender()
    {
        _eraseRenderPending = true;

        if (_eraseRenderTimer is null)
        {
            _eraseRenderTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Render,
                OnEraseRenderTimerTick);
        }

        if (!_eraseRenderTimer.IsEnabled)
        {
            _eraseRenderTimer.Start();
        }
    }

    private void OnEraseRenderTimerTick(object? sender, EventArgs e)
    {
        _eraseRenderTimer?.Stop();
        if (!_eraseRenderPending)
        {
            return;
        }

        _eraseRenderPending = false;
        _highlighter.RebuildFromDocument(_document);
        _lastPanDirection = default;
        EnsureTileRebuildScheduled();
        NotifySurfaceChanged();
    }

    private void StartLaserTimer()
    {
        if (_laserTimer != null && _laserTimer.IsEnabled) return;
        
        _laserTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, OnLaserTimerTick);
        _laserTimer.Start();
    }

    private void OnLaserTimerTick(object? sender, EventArgs e)
    {
        // 计算每帧淡出量 (假设每帧 30ms)
        double fadeDuration = _options.LaserPointerFadeDurationSeconds;
        double frameTime = 0.03; 
        double baseDecrement = frameTime / Math.Max(fadeDuration, 0.1);

        bool changed = false;
        for (int i = _laserStrokes.Count - 1; i >= 0; i--)
        {
            var s = _laserStrokes[i];
            // 抬起后加速淡出，书写时保留较长时间
            if (s.IsFinished) s.Opacity -= baseDecrement * 2.0; 
            else s.Opacity -= baseDecrement * 0.1;

            if (s.Opacity <= 0) _laserStrokes.RemoveAt(i);
            changed = true;
        }

        if (changed) NotifyRenderChanged();
        if (_laserStrokes.Count == 0) _laserTimer?.Stop();
    }

    private void EnsureTileRebuildScheduled()
    {
        if (!_inkTileCache.HasDirtyTiles || _activeStrokes.Count > 0)
        {
            return;
        }

        _tileRebuildTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Background,
            OnTileRebuildTimerTick);

        if (!_tileRebuildTimer.IsEnabled)
        {
            _tileRebuildTimer.Start();
        }
    }

    private void OnTileRebuildTimerTick(object? sender, EventArgs e)
    {
        if (_activeStrokes.Count > 0)
        {
            _tileRebuildTimer?.Stop();
            return;
        }

        var rebuiltCount = _inkTileCache.RebuildDirtyTilesNearViewport(
            GetViewportWorldBounds(),
            _document,
            ConvertStrokeToWorldSpace,
            _useBezierSmoothing,
            _usePenNibEffect,
            maxTilesPerPass: 3,
            prewarmMarginTiles: 1,
            preferredPanDirection: _lastPanDirection,
            renderScale: EffectiveZoom);

        if (rebuiltCount > 0)
        {
            NotifySurfaceChanged();
        }

        if (!_inkTileCache.HasDirtyTiles)
        {
            _tileRebuildTimer?.Stop();
        }
    }

    private void ScheduleDeferredPreviewClear()
    {
        _previewClearTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Background,
            OnPreviewClearTimerTick);

        _previewClearTimer.Stop();
        _previewClearTimer.Start();
    }

    private void CancelDeferredPreviewClear()
    {
        _previewClearTimer?.Stop();
    }

    private void OnPreviewClearTimerTick(object? sender, EventArgs e)
    {
        _previewClearTimer?.Stop();
        if (_activeStrokes.Count > 0)
        {
            return;
        }

        _renderCoordinator.ClearPreview();
        NotifyRenderChanged();
    }

    public void DeselectImage()
    {
        _transformInteraction.Deselect();
        NotifySurfaceChanged();
    }

    public void Dispose()
    {
        _eraseRenderTimer?.Stop();
        _tileRebuildTimer?.Stop();
        _previewClearTimer?.Stop();
        _renderCoordinator.Dispose();
        _inkTileCache.Dispose();
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
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ActiveStroke));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(SelectedShape));
        OnPropertyChanged(nameof(PreviewBitmap));
        OnPropertyChanged(nameof(ActiveSnapX));
        OnPropertyChanged(nameof(ActiveSnapY));
    }

    /// <summary>绘制热路径专用：仅通知渲染层必要的 3 个属性，减少 7 个无效事件</summary>
    private void NotifyRenderChanged()
    {
        OnPropertyChanged(nameof(ActiveStroke));
        OnPropertyChanged(nameof(PreviewBitmap));
    }

    private void RebuildActiveStrokePreview()
    {
        _renderCoordinator.ClearPreview();
        foreach (var active in _activeStrokes.Values)
        {
            if (active.Tool == WhiteboardTool.Highlighter)
            {
                continue;
            }

            WhiteboardStrokeRenderer.DrawStroke(_renderCoordinator.PreviewBitmap!, CreateViewportStroke(active), _useBezierSmoothing, _usePenNibEffect);
        }
    }

    private void RefreshAfterDocumentCommand(IUndoCommand command)
    {
        switch (command)
        {
            case AddStrokeCommand addStroke:
                RefreshAfterStrokeChange(addStroke.Stroke);
                break;
            case RemoveElementCommand removeElement when removeElement.Element is StrokeElement stroke:
                RefreshAfterStrokeChange(stroke);
                break;
            case ClearCommand:
                _lastPanDirection = default;
                _inkTileCache.Invalidate();
                _highlighter.RebuildFromDocument(_document);
                _renderCoordinator.ClearPreview();
                NotifySurfaceChanged();
                break;
            default:
                _lastPanDirection = default;
                RasterizeAll();
                break;
        }
    }

    private void RefreshAfterStrokeChange(StrokeElement stroke)
    {
        if (stroke.Tool == WhiteboardTool.Highlighter)
        {
            _highlighter.RebuildFromDocument(_document);
            _lastPanDirection = default;
            NotifySurfaceChanged();
            return;
        }

        _inkTileCache.Invalidate(stroke.Bounds);
        EnsureTileRebuildScheduled();
        NotifySurfaceChanged();
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

    private Rect? GetContentBounds()
    {
        Rect? bounds = null;

        foreach (var stroke in _document.Strokes)
        {
            bounds = Union(bounds, stroke.Bounds);
        }

        foreach (var item in _document.Items)
        {
            bounds = Union(bounds, item.Bounds);
        }

        return bounds;
    }

    private static Rect Union(Rect? current, Rect next)
    {
        if (current is null)
        {
            return next;
        }

        var existing = current.Value;
        var left = Math.Min(existing.Left, next.Left);
        var top = Math.Min(existing.Top, next.Top);
        var right = Math.Max(existing.Right, next.Right);
        var bottom = Math.Max(existing.Bottom, next.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private double EffectiveZoom => IsInfiniteCanvasEnabled ? Zoom : 1.0;

    private static bool AreClose(Point a, Point b) => Math.Abs(a.X - b.X) < 0.25 && Math.Abs(a.Y - b.Y) < 0.25;
    private static bool IsPointNearStroke(Point point, StrokeElement stroke, double radius) { var radiusSquared = radius * radius; foreach (var sample in stroke.Samples) { var dx = point.X - sample.Point.X; var dy = point.Y - sample.Point.Y; if (dx * dx + dy * dy <= radiusSquared) return true; } for (var i = 1; i < stroke.Samples.Count; i++) if (DistanceToSegment(point.X, point.Y, stroke.Samples[i - 1].Point, stroke.Samples[i].Point) <= radius) return true; return false; }
    private static double DistanceToSegment(double px, double py, Point a, Point b) { var vx = b.X - a.X; var vy = b.Y - a.Y; var wx = px - a.X; var wy = py - a.Y; var lenSq = vx * vx + vy * vy; if (lenSq <= double.Epsilon) return Math.Sqrt(wx * wx + wy * wy); var t = Math.Clamp((wx * vx + wy * vy) / lenSq, 0, 1); return Math.Sqrt((px - (a.X + t * vx)) * (px - (a.X + t * vx)) + (py - (a.Y + t * vy)) * (py - (a.Y + t * vy))); }
}
