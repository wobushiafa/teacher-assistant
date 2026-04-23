using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class WhiteboardImageItem : WhiteboardItemBase, IDisposable
{
    public WhiteboardImageItem(Bitmap bitmap, Point center)
        : base(center)
    {
        Bitmap = bitmap;
    }

    public Bitmap Bitmap { get; }

    public override WhiteboardElementType ElementType => WhiteboardElementType.Image;

    public override Size BaseSize => new(Bitmap.PixelSize.Width, Bitmap.PixelSize.Height);

    public override void Render(DrawingContext context)
    {
        var transform = GetTransform();
        using (context.PushTransform(transform))
        {
            var halfWidth = BaseSize.Width / 2.0;
            var halfHeight = BaseSize.Height / 2.0;
            context.DrawImage(
                Bitmap,
                new Rect(new Size(Bitmap.PixelSize.Width, Bitmap.PixelSize.Height)),
                new Rect(-halfWidth, -halfHeight, BaseSize.Width, BaseSize.Height));
        }
    }

    public void Dispose() => Bitmap.Dispose();
}
