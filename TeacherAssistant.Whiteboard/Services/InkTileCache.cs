using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class InkTileCache : IDisposable
{
    private const int DefaultTileSize = 512;
    private const int MaxCachedTiles = 256;
    private const double TileOverlapPadding = 24;

    private readonly Dictionary<TileKey, WriteableBitmap> _tiles = [];
    private readonly HashSet<TileKey> _dirtyTiles = [];
    private readonly Dictionary<TileKey, long> _lastAccessTicks = [];
    private long _accessTick;
    private double _renderScale = 1.0;

    public int TileSize => DefaultTileSize;
    public bool HasDirtyTiles => _dirtyTiles.Count > 0;

    public void Invalidate()
    {
        foreach (var bitmap in _tiles.Values)
        {
            bitmap.Dispose();
        }

        _tiles.Clear();
        _dirtyTiles.Clear();
    }

    public void Invalidate(Rect bounds)
    {
        foreach (var key in GetTileKeys(ExpandByPixels(bounds, TileOverlapPadding)))
        {
            _dirtyTiles.Add(key);
        }
    }

    public bool Render(
        DrawingContext context,
        Rect viewportWorldBounds,
        WhiteboardDocument document,
        Func<StrokeElement, WhiteboardStroke> convertStroke,
        bool useBezierSmoothing,
        bool usePenNibEffect,
        double renderScale,
        bool allowFallback)
    {
        EnsureRenderScale(renderScale);
        var hasVisibleCacheMiss = false;

        var firstTileX = (int)Math.Floor(viewportWorldBounds.Left / TileSize);
        var lastTileX = (int)Math.Floor((viewportWorldBounds.Right - 0.001) / TileSize);
        var firstTileY = (int)Math.Floor(viewportWorldBounds.Top / TileSize);
        var lastTileY = (int)Math.Floor((viewportWorldBounds.Bottom - 0.001) / TileSize);

        for (var tileY = firstTileY; tileY <= lastTileY; tileY++)
        {
            for (var tileX = firstTileX; tileX <= lastTileX; tileX++)
            {
                var key = new TileKey(tileX, tileY);
                if (!_tiles.TryGetValue(key, out var bitmap))
                {
                    _dirtyTiles.Add(key);
                    hasVisibleCacheMiss = true;
                    continue;
                }

                Touch(key);
                var dest = new Rect(tileX * TileSize, tileY * TileSize, TileSize, TileSize);
                context.DrawImage(bitmap, new Rect(bitmap.Size), dest);
            }
        }

        if (hasVisibleCacheMiss && allowFallback)
        {
            RenderFallback(context, viewportWorldBounds, document, convertStroke, useBezierSmoothing);
        }

        PruneCache(viewportWorldBounds);
        return hasVisibleCacheMiss;
    }

    public int RebuildDirtyTilesNearViewport(
        Rect viewportWorldBounds,
        WhiteboardDocument document,
        Func<StrokeElement, WhiteboardStroke> convertStroke,
        bool useBezierSmoothing,
        bool usePenNibEffect,
        int maxTilesPerPass,
        int prewarmMarginTiles,
        Vector preferredPanDirection,
        double renderScale)
    {
        EnsureRenderScale(renderScale);

        if (_dirtyTiles.Count == 0 || maxTilesPerPass <= 0)
        {
            return 0;
        }

        var rebuilt = 0;
        foreach (var key in GetDirtyKeysInPriorityOrder(viewportWorldBounds, prewarmMarginTiles, preferredPanDirection))
        {
            if (rebuilt >= maxTilesPerPass)
            {
                break;
            }

            var bitmap = RasterizeTile(key.X, key.Y, document, convertStroke, useBezierSmoothing, usePenNibEffect);
            if (_tiles.Remove(key, out var oldBitmap))
            {
                oldBitmap.Dispose();
            }

            _tiles[key] = bitmap;
            _dirtyTiles.Remove(key);
            Touch(key);
            rebuilt++;
        }

        return rebuilt;
    }

    public int RebuildDirtyTiles(
        Rect bounds,
        WhiteboardDocument document,
        Func<StrokeElement, WhiteboardStroke> convertStroke,
        bool useBezierSmoothing,
        bool usePenNibEffect,
        double renderScale)
    {
        EnsureRenderScale(renderScale);

        var rebuilt = 0;
        foreach (var key in GetTileKeys(ExpandByPixels(bounds, TileOverlapPadding)))
        {
            if (!_dirtyTiles.Contains(key))
            {
                continue;
            }

            var bitmap = RasterizeTile(key.X, key.Y, document, convertStroke, useBezierSmoothing, usePenNibEffect);
            if (_tiles.Remove(key, out var oldBitmap))
            {
                oldBitmap.Dispose();
            }

            _tiles[key] = bitmap;
            _dirtyTiles.Remove(key);
            Touch(key);
            rebuilt++;
        }

        return rebuilt;
    }

    private WriteableBitmap RasterizeTile(
        int tileX,
        int tileY,
        WhiteboardDocument document,
        Func<StrokeElement, WhiteboardStroke> convertStroke,
        bool useBezierSmoothing,
        bool usePenNibEffect)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(GetRasterTilePixelSize(), GetRasterTilePixelSize()),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        WhiteboardStrokeRenderer.Clear(bitmap, Colors.Transparent);

        var worldRect = new Rect(tileX * TileSize, tileY * TileSize, TileSize, TileSize);
        var queryRect = ExpandByPixels(worldRect, TileOverlapPadding);
        foreach (var element in document.QueryStrokes(queryRect))
        {
            if (element.Tool == WhiteboardTool.Highlighter)
            {
                continue;
            }

            var shiftedStroke = ShiftStroke(convertStroke(element), -worldRect.X, -worldRect.Y, _renderScale);
            WhiteboardStrokeRenderer.DrawStroke(bitmap, shiftedStroke, useBezierSmoothing, usePenNibEffect);
        }

        return bitmap;
    }

    private WhiteboardStroke ShiftStroke(WhiteboardStroke source, double dx, double dy, double scale)
    {
        var shifted = new WhiteboardStroke(source.Color, source.Thickness, source.Tool);
        foreach (var sample in source.Samples)
        {
            shifted.Samples.Add(new StrokeSample(
                new Point((sample.Point.X + dx) * scale, (sample.Point.Y + dy) * scale),
                sample.Timestamp,
                sample.Width * scale));
        }

        return shifted;
    }

    private void EnsureRenderScale(double renderScale)
    {
        var targetScale = QuantizeRenderScale(renderScale);
        if (Math.Abs(_renderScale - targetScale) <= 0.001)
        {
            return;
        }

        _renderScale = targetScale;
        foreach (var key in _tiles.Keys)
        {
            _dirtyTiles.Add(key);
        }
    }

    private int GetRasterTilePixelSize()
        => Math.Max(TileSize, (int)Math.Ceiling(TileSize * _renderScale));

    private static double QuantizeRenderScale(double renderScale)
    {
        var clamped = Math.Clamp(renderScale, 1.0, 4.0);
        if (clamped <= 1.25) return 1.0;
        if (clamped <= 1.75) return 1.5;
        if (clamped <= 2.5) return 2.0;
        if (clamped <= 3.5) return 3.0;
        return 4.0;
    }

    public void Dispose() => Invalidate();

    private IEnumerable<TileKey> GetDirtyKeysInPriorityOrder(Rect viewportWorldBounds, int prewarmMarginTiles, Vector preferredPanDirection)
    {
        var yielded = new HashSet<TileKey>();

        foreach (var key in GetTileKeys(viewportWorldBounds))
        {
            if (_dirtyTiles.Contains(key) && yielded.Add(key))
            {
                yield return key;
            }
        }

        if (prewarmMarginTiles <= 0)
        {
            yield break;
        }

        var expandedBounds = ExpandByTiles(viewportWorldBounds, prewarmMarginTiles);
        var outerKeys = new List<TileKey>();
        foreach (var key in GetTileKeys(expandedBounds))
        {
            if (_dirtyTiles.Contains(key) && !IntersectsViewportKey(key, viewportWorldBounds))
            {
                outerKeys.Add(key);
            }
        }

        if (preferredPanDirection != default)
        {
            var normalizedDirection = NormalizeDirection(preferredPanDirection);
            outerKeys.Sort((a, b) =>
            {
                var scoreA = ScoreDirectionalPriority(a, viewportWorldBounds, normalizedDirection);
                var scoreB = ScoreDirectionalPriority(b, viewportWorldBounds, normalizedDirection);
                return scoreB.CompareTo(scoreA);
            });
        }

        foreach (var key in outerKeys)
        {
            if (yielded.Add(key))
            {
                yield return key;
            }
        }
    }

    private IEnumerable<TileKey> GetTileKeys(Rect bounds)
    {
        var normalized = Normalize(bounds);
        var firstTileX = (int)Math.Floor(normalized.Left / TileSize);
        var lastTileX = (int)Math.Floor((normalized.Right - 0.001) / TileSize);
        var firstTileY = (int)Math.Floor(normalized.Top / TileSize);
        var lastTileY = (int)Math.Floor((normalized.Bottom - 0.001) / TileSize);

        for (var tileY = firstTileY; tileY <= lastTileY; tileY++)
        {
            for (var tileX = firstTileX; tileX <= lastTileX; tileX++)
            {
                yield return new TileKey(tileX, tileY);
            }
        }
    }

    private static Rect Normalize(Rect bounds)
    {
        var left = Math.Min(bounds.Left, bounds.Right);
        var top = Math.Min(bounds.Top, bounds.Bottom);
        var right = Math.Max(bounds.Left, bounds.Right);
        var bottom = Math.Max(bounds.Top, bounds.Bottom);
        return new Rect(left, top, Math.Max(0.001, right - left), Math.Max(0.001, bottom - top));
    }

    private Rect ExpandByTiles(Rect bounds, int tileMargin)
        => new(
            bounds.X - tileMargin * TileSize,
            bounds.Y - tileMargin * TileSize,
            bounds.Width + tileMargin * TileSize * 2,
            bounds.Height + tileMargin * TileSize * 2);

    private static Rect ExpandByPixels(Rect bounds, double pixelPadding)
    {
        var normalized = Normalize(bounds);
        return new Rect(
            normalized.X - pixelPadding,
            normalized.Y - pixelPadding,
            normalized.Width + pixelPadding * 2,
            normalized.Height + pixelPadding * 2);
    }

    private static void RenderFallback(
        DrawingContext context,
        Rect viewportWorldBounds,
        WhiteboardDocument document,
        Func<StrokeElement, WhiteboardStroke> convertStroke,
        bool useBezierSmoothing)
    {
        foreach (var element in document.QueryStrokes(ExpandByPixels(viewportWorldBounds, TileOverlapPadding)))
        {
            if (element.Tool == WhiteboardTool.Highlighter)
            {
                continue;
            }

            var stroke = convertStroke(element);
            if (stroke.Samples.Count < 2)
            {
                continue;
            }

            var geometry = BuildGeometry(stroke, useBezierSmoothing);
            var pen = new Pen(new SolidColorBrush(stroke.Color), stroke.Thickness)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static StreamGeometry BuildGeometry(WhiteboardStroke stroke, bool useBezierSmoothing)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        if (!useBezierSmoothing || stroke.Samples.Count < 3)
        {
            ctx.BeginFigure(stroke.Samples[0].Point, false);
            for (var i = 1; i < stroke.Samples.Count; i++)
            {
                ctx.LineTo(stroke.Samples[i].Point);
            }

            ctx.EndFigure(false);
            return geometry;
        }

        var start = Midpoint(stroke.Samples[0].Point, stroke.Samples[1].Point);
        ctx.BeginFigure(start, false);
        for (var i = 1; i < stroke.Samples.Count - 1; i++)
        {
            var control = stroke.Samples[i].Point;
            var end = Midpoint(stroke.Samples[i].Point, stroke.Samples[i + 1].Point);
            ctx.QuadraticBezierTo(control, end);
        }

        ctx.LineTo(stroke.Samples[^1].Point);
        ctx.EndFigure(false);
        return geometry;
    }

    private static Point Midpoint(Point a, Point b)
        => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private bool IntersectsViewportKey(TileKey key, Rect viewportWorldBounds)
    {
        var keyRect = new Rect(key.X * TileSize, key.Y * TileSize, TileSize, TileSize);
        var viewport = Normalize(viewportWorldBounds);
        return keyRect.X < viewport.Right &&
               keyRect.Right > viewport.Left &&
               keyRect.Y < viewport.Bottom &&
               keyRect.Bottom > viewport.Top;
    }

    private void Touch(TileKey key)
    {
        _accessTick++;
        _lastAccessTicks[key] = _accessTick;
    }

    private void PruneCache(Rect viewportWorldBounds)
    {
        if (_tiles.Count <= MaxCachedTiles)
        {
            return;
        }

        var protectedBounds = ExpandByTiles(viewportWorldBounds, 1);
        var candidates = new List<(TileKey Key, long Tick)>();
        foreach (var key in _tiles.Keys)
        {
            var keyRect = new Rect(key.X * TileSize, key.Y * TileSize, TileSize, TileSize);
            if (Intersects(protectedBounds, keyRect))
            {
                continue;
            }

            candidates.Add((key, _lastAccessTicks.GetValueOrDefault(key)));
        }

        candidates.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        var removeCount = Math.Min(_tiles.Count - MaxCachedTiles, candidates.Count);
        for (var i = 0; i < removeCount; i++)
        {
            var key = candidates[i].Key;
            if (_tiles.Remove(key, out var bitmap))
            {
                bitmap.Dispose();
            }
            _lastAccessTicks.Remove(key);
            _dirtyTiles.Remove(key);
        }
    }

    private static bool Intersects(Rect a, Rect b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na.X < nb.Right &&
               na.Right > nb.Left &&
               na.Y < nb.Bottom &&
               na.Bottom > nb.Top;
    }

    private static Vector NormalizeDirection(Vector direction)
    {
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length <= double.Epsilon)
        {
            return default;
        }

        return new Vector(direction.X / length, direction.Y / length);
    }

    private double ScoreDirectionalPriority(TileKey key, Rect viewportWorldBounds, Vector direction)
    {
        var tileCenter = new Point(
            (key.X * TileSize) + (TileSize / 2.0),
            (key.Y * TileSize) + (TileSize / 2.0));
        var viewportCenter = viewportWorldBounds.Center;
        var offset = new Vector(tileCenter.X - viewportCenter.X, tileCenter.Y - viewportCenter.Y);
        return (offset.X * direction.X) + (offset.Y * direction.Y);
    }

    private readonly record struct TileKey(int X, int Y);
}
