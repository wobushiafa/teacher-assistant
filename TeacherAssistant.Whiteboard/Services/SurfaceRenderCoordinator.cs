using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public sealed class SurfaceRenderCoordinator : IDisposable
{
    private WriteableBitmap? _previewBitmap;
    private PixelSize _pixelSize;

    public WriteableBitmap? PreviewBitmap => _previewBitmap;
    public PixelSize PixelSize => _pixelSize;

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (_previewBitmap is not null && _pixelSize.Width == width && _pixelSize.Height == height))
        {
            return;
        }

        _pixelSize = new PixelSize(width, height);
        _previewBitmap?.Dispose();
        var dpi = new Vector(96, 96);
        _previewBitmap = new WriteableBitmap(_pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    }

    public void ClearPreview()
    {
        if (_previewBitmap is not null)
        {
            WhiteboardStrokeRenderer.Clear(_previewBitmap, Colors.Transparent);
        }
    }

    public void RenderStrokePreview(WhiteboardStroke stroke, bool useBezierSmoothing, bool usePenNibEffect)
    {
        if (_previewBitmap is null)
        {
            return;
        }

        WhiteboardStrokeRenderer.DrawSegment(_previewBitmap, stroke, useBezierSmoothing, usePenNibEffect);
    }

    public void RasterizeAll(WhiteboardDocument document, HighlighterCompositor highlighter)
    {
        ClearPreview();
        highlighter.RebuildFromDocument(document);
    }

    public void ClearAll()
    {
        ClearPreview();
    }

    public void Dispose()
    {
        _previewBitmap?.Dispose();
    }
}
