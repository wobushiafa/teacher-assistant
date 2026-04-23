using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class RenderCache : IDisposable
{
    private WriteableBitmap? _inkCache;
    private WriteableBitmap? _previewCache;
    private WriteableBitmap? _previewTailCache;
    private WriteableBitmap? _objectCache;
    private PixelSize _pixelSize;

    public WriteableBitmap? InkCache => _inkCache;
    public WriteableBitmap? PreviewCache => _previewCache;
    public WriteableBitmap? PreviewTailCache => _previewTailCache;
    public WriteableBitmap? ObjectCache => _objectCache;

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_inkCache is not null && _pixelSize.Width == width && _pixelSize.Height == height) return;

        _pixelSize = new PixelSize(width, height);

        _inkCache?.Dispose();
        _previewCache?.Dispose();
        _previewTailCache?.Dispose();
        _objectCache?.Dispose();

        _inkCache = new WriteableBitmap(_pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        _previewCache = new WriteableBitmap(_pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        _previewTailCache = new WriteableBitmap(_pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        _objectCache = new WriteableBitmap(_pixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    }

    public void RenderInkCache(WhiteboardDocument document, bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (_inkCache is null) return;

        WhiteboardStrokeRenderer.Clear(_inkCache, Colors.Transparent);

        foreach (var stroke in document.Strokes)
        {
            WhiteboardStrokeRenderer.DrawStroke(_inkCache, ConvertToLegacyStroke(stroke), useBezierSmoothing, usePenNibEffect);
        }
    }

    public void RenderStrokePreview(StrokeElement? stablePrefix, StrokeElement? tail, bool useBezierSmoothing)
    {
        if (_previewCache is null || _previewTailCache is null) return;

        WhiteboardStrokeRenderer.Clear(_previewCache, Colors.Transparent);
        WhiteboardStrokeRenderer.Clear(_previewTailCache, Colors.Transparent);

        if (stablePrefix is not null && stablePrefix.Samples.Count >= 2)
        {
            WhiteboardStrokeRenderer.DrawStroke(_previewCache, ConvertToLegacyStroke(stablePrefix), useBezierSmoothing, false);
        }

        if (tail is not null && tail.Samples.Count >= 2)
        {
            WhiteboardStrokeRenderer.DrawStroke(_previewTailCache, ConvertToLegacyStroke(tail), useBezierSmoothing, false);
        }
    }

    public void RenderObjectCache(WhiteboardDocument document)
    {
    }

    public void Dispose()
    {
        _inkCache?.Dispose();
        _previewCache?.Dispose();
        _previewTailCache?.Dispose();
        _objectCache?.Dispose();
    }

    private static WhiteboardStroke ConvertToLegacyStroke(StrokeElement element)
    {
        var stroke = new WhiteboardStroke(element.Color, element.Thickness, element.Tool);
        foreach (var sample in element.Samples)
        {
            stroke.Samples.Add(sample);
        }

        return stroke;
    }
}
