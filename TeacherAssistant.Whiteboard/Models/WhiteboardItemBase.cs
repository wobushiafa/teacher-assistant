using System;
using Avalonia;
using Avalonia.Media;

namespace TeacherAssistant.Whiteboard.Models;

public abstract class WhiteboardItemBase : NotifyPropertyChangedObject, IWhiteboardElement
{
    private Point _center;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _rotationDegrees = 0;
    private bool _isSizeLocked;
    private bool _isPositionLocked;

    protected WhiteboardItemBase(Point center)
    {
        _center = center;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public abstract WhiteboardElementType ElementType { get; }

    public abstract Size BaseSize { get; }

    public Point Center
    {
        get => _center;
        set
        {
            if (SetProperty(ref _center, value))
            {
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
            }
        }
    }

    public double ScaleX
    {
        get => _scaleX;
        set
        {
            if (SetProperty(ref _scaleX, Math.Clamp(value, 0.01, 100)))
            {
                OnPropertyChanged(nameof(Width));
            }
        }
    }

    public double ScaleY
    {
        get => _scaleY;
        set
        {
            if (SetProperty(ref _scaleY, Math.Clamp(value, 0.01, 100)))
            {
                OnPropertyChanged(nameof(Height));
            }
        }
    }

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set => SetProperty(ref _rotationDegrees, NormalizeDegrees(value));
    }

    public bool IsSizeLocked
    {
        get => _isSizeLocked;
        set => SetProperty(ref _isSizeLocked, value);
    }

    public bool IsPositionLocked
    {
        get => _isPositionLocked;
        set => SetProperty(ref _isPositionLocked, value);
    }

    public double Width
    {
        get => BaseSize.Width * ScaleX;
        set => ScaleX = value / BaseSize.Width;
    }

    public double Height
    {
        get => BaseSize.Height * ScaleY;
        set => ScaleY = value / BaseSize.Height;
    }

    public double X
    {
        get => Center.X;
        set => Center = new Point(value, Center.Y);
    }

    public double Y
    {
        get => Center.Y;
        set => Center = new Point(Center.X, value);
    }

    public Rect Bounds
    {
        get
        {
            var corners = GetCorners();
            var minX = Math.Min(Math.Min(corners[0].X, corners[1].X), Math.Min(corners[2].X, corners[3].X));
            var maxX = Math.Max(Math.Max(corners[0].X, corners[1].X), Math.Max(corners[2].X, corners[3].X));
            var minY = Math.Min(Math.Min(corners[0].Y, corners[1].Y), Math.Min(corners[2].Y, corners[3].Y));
            var maxY = Math.Max(Math.Max(corners[0].Y, corners[1].Y), Math.Max(corners[2].Y, corners[3].Y));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public virtual void SetCenter(Point center) { Center = center; }

    public virtual void SetScale(double scaleX, double scaleY)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public virtual void SetRotation(double degrees) => RotationDegrees = degrees;

    public bool Contains(Point point)
    {
        var local = ToLocal(point);
        var halfWidth = BaseSize.Width / 2.0;
        var halfHeight = BaseSize.Height / 2.0;
        return local.X >= -halfWidth && local.X <= halfWidth && local.Y >= -halfHeight && local.Y <= halfHeight;
    }

    public WhiteboardImageHitTest HitTest(Point point, double handleRadius)
    {
        if (DistanceSquared(point, Center) <= handleRadius * handleRadius)
        {
            return WhiteboardImageHitTest.RotateCenter;
        }

        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeTopLeft)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeTopLeft;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeTop)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeTop;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeTopRight)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeTopRight;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeRight)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeRight;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeBottomRight)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeBottomRight;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeBottom)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeBottom;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeBottomLeft)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeBottomLeft;
        if (DistanceSquared(point, GetHandlePoint(WhiteboardImageHitTest.ResizeLeft)) <= handleRadius * handleRadius) return WhiteboardImageHitTest.ResizeLeft;

        return Contains(point) ? WhiteboardImageHitTest.Move : WhiteboardImageHitTest.None;
    }

    public Point[] GetCorners()
    {
        var halfWidth = BaseSize.Width / 2.0;
        var halfHeight = BaseSize.Height / 2.0;
        return
        [
            ToWorld(new Point(-halfWidth, -halfHeight)),
            ToWorld(new Point(halfWidth, -halfHeight)),
            ToWorld(new Point(halfWidth, halfHeight)),
            ToWorld(new Point(-halfWidth, halfHeight)),
        ];
    }

    public Point GetHandlePoint(WhiteboardImageHitTest handle) => handle switch
    {
        WhiteboardImageHitTest.ResizeTopLeft => ToWorld(new Point(-BaseSize.Width / 2.0, -BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeTop => ToWorld(new Point(0, -BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeTopRight => ToWorld(new Point(BaseSize.Width / 2.0, -BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeRight => ToWorld(new Point(BaseSize.Width / 2.0, 0)),
        WhiteboardImageHitTest.ResizeBottomRight => ToWorld(new Point(BaseSize.Width / 2.0, BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeBottom => ToWorld(new Point(0, BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeBottomLeft => ToWorld(new Point(-BaseSize.Width / 2.0, BaseSize.Height / 2.0)),
        WhiteboardImageHitTest.ResizeLeft => ToWorld(new Point(-BaseSize.Width / 2.0, 0)),
        _ => Center,
    };

    public Point ToWorld(Point local)
    {
        var radians = RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = local.X * ScaleX;
        var y = local.Y * ScaleY;
        return new Point(x * cos - y * sin + Center.X, x * sin + y * cos + Center.Y);
    }

    public Point ToLocal(Point world)
    {
        var dx = world.X - Center.X;
        var dy = world.Y - Center.Y;
        var radians = -RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = dx * cos - dy * sin;
        var y = dx * sin + dy * cos;
        return new Point(x / ScaleX, y / ScaleY);
    }

    public Matrix GetTransform()
    {
        var radians = RotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Matrix(cos * ScaleX, sin * ScaleX, -sin * ScaleY, cos * ScaleY, Center.X, Center.Y);
    }

    public abstract void Render(DrawingContext context);

    public void Invalidate()
    {
    }

    protected static double DistanceSquared(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    protected static double NormalizeDegrees(double degrees)
    {
        while (degrees < -180) degrees += 360;
        while (degrees > 180) degrees -= 360;
        return degrees;
    }
}
