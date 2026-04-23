using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class SurfaceRenderCoordinator : IDisposable
{
    private WriteableBitmap? _bitmap;
    private WriteableBitmap? _previewBitmap;
    private PixelSize _pixelSize;

    public WriteableBitmap? Bitmap => _bitmap;
    public WriteableBitmap? PreviewBitmap => _previewBitmap;
    public PixelSize PixelSize => _pixelSize;

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (_bitmap is not null && _pixelSize.Width == width && _pixelSize.Height == height))
        {
            return;
        }

        _pixelSize = new PixelSize(width, height);
        _bitmap?.Dispose();
        _previewBitmap?.Dispose();
        var dpi = new Vector(96, 96);
        _bitmap = new WriteableBitmap(_pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        _previewBitmap = new WriteableBitmap(_pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    }

    public void ClearPreview()
    {
        if (_previewBitmap is not null)
        {
            WhiteboardStrokeRenderer.Clear(_previewBitmap, Colors.Transparent);
        }
    }

    public void CommitStroke(WhiteboardStroke stroke, bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (_bitmap is null)
        {
            return;
        }

        WhiteboardStrokeRenderer.DrawStroke(_bitmap, stroke, useBezierSmoothing, usePenNibEffect);
    }

    public void RenderStrokePreview(WhiteboardStroke stroke, bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (_previewBitmap is null)
        {
            return;
        }

        WhiteboardStrokeRenderer.DrawSegment(_previewBitmap, stroke, useBezierSmoothing, usePenNibEffect);
    }

    public void RasterizeAll(WhiteboardDocument document, Func<StrokeElement, WhiteboardStroke> convertToLegacyStroke, bool useBezierSmoothing, bool usePenNibEffect, HighlighterCompositor highlighter)
    {
        if (_bitmap is null)
        {
            return;
        }

        WhiteboardStrokeRenderer.Clear(_bitmap, Colors.Transparent);
        ClearPreview();

        foreach (var element in document.Strokes)
        {
            var stroke = convertToLegacyStroke(element);
            if (stroke.Tool != WhiteboardTool.Highlighter)
            {
                WhiteboardStrokeRenderer.DrawStroke(_bitmap, stroke, useBezierSmoothing, usePenNibEffect);
            }
        }

        highlighter.RebuildFromDocument(document);
    }

    public void ClearAll()
    {
        if (_bitmap is not null)
        {
            WhiteboardStrokeRenderer.Clear(_bitmap, Colors.Transparent);
        }

        ClearPreview();
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _previewBitmap?.Dispose();
    }
}
