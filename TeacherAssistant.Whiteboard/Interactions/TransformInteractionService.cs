using System;
using System.Collections.Generic;
using Avalonia;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Interactions;

public sealed class TransformInteractionService
{
    private readonly Func<double> _hitTestPaddingProvider;
    private readonly Func<double> _snapThresholdProvider;
    private readonly Func<double> _snapGridSizeProvider;

    private WhiteboardItemBase? _selectedItem;
    private WhiteboardImageHitTest _activeMode = WhiteboardImageHitTest.None;
    private Point _activeOffset;
    private Point _startPointer;
    private Point _startCenter;
    private double _startScaleX;
    private double _startScaleY;
    private double _startRotation;
    private bool _isSnappingEnabled = true;
    private double? _activeSnapX;
    private double? _activeSnapY;

    public TransformInteractionService(Func<double> hitTestPaddingProvider, Func<double> snapThresholdProvider, Func<double> snapGridSizeProvider)
    {
        _hitTestPaddingProvider = hitTestPaddingProvider;
        _snapThresholdProvider = snapThresholdProvider;
        _snapGridSizeProvider = snapGridSizeProvider;
    }

    public WhiteboardItemBase? SelectedItem => _selectedItem;
    public WhiteboardImageItem? SelectedImage => _selectedItem as WhiteboardImageItem;
    public WhiteboardShapeItem? SelectedShape => _selectedItem as WhiteboardShapeItem;
    public WhiteboardImageHitTest ActiveMode => _activeMode;
    public double? ActiveSnapX => _activeSnapX;
    public double? ActiveSnapY => _activeSnapY;

    public void SetSnappingEnabled(bool enabled) => _isSnappingEnabled = enabled;

    public bool BeginDrag(Point point, WhiteboardItemBase? currentSelectedItem, IReadOnlyList<WhiteboardItemBase> items)
    {
        _activeSnapX = null;
        _activeSnapY = null;
        var padding = _hitTestPaddingProvider();

        if (currentSelectedItem is not null)
        {
            var handle = currentSelectedItem.HitTest(point, padding);
            if (handle != WhiteboardImageHitTest.None)
            {
                _selectedItem = currentSelectedItem;
                _activeMode = handle;
                SetupDragInfo(point, currentSelectedItem);
                return true;
            }
        }

        var item = HitTestAll(point, items, padding);
        if (item is null)
        {
            Deselect();
            return false;
        }

        _selectedItem = item;
        _activeMode = WhiteboardImageHitTest.Move;
        SetupDragInfo(point, item);
        return true;
    }

    public void ContinueDrag(Point point, IReadOnlyList<WhiteboardItemBase> items)
    {
        if (_selectedItem is null)
        {
            return;
        }

        if (_activeMode == WhiteboardImageHitTest.Move)
        {
            if (_selectedItem.IsPositionLocked) return;
            var targetPos = point - _activeOffset;
            if (_isSnappingEnabled)
            {
                targetPos = SnapPoint(targetPos, _selectedItem, items);
            }
            _selectedItem.SetCenter(targetPos);
            return;
        }

        if (_activeMode == WhiteboardImageHitTest.RotateCenter)
        {
            if (_selectedItem.IsPositionLocked) return;
            var startAngle = Math.Atan2(_startPointer.Y - _startCenter.Y, _startPointer.X - _startCenter.X);
            var currentAngle = Math.Atan2(point.Y - _startCenter.Y, point.X - _startCenter.X);
            _selectedItem.SetRotation(_startRotation + (currentAngle - startAngle) * 180.0 / Math.PI);
            return;
        }

        ResizeActiveItem(point);
    }

    public void EndDrag()
    {
        _activeMode = WhiteboardImageHitTest.None;
        _activeSnapX = null;
        _activeSnapY = null;
    }

    public void Deselect()
    {
        _selectedItem = null;
        EndDrag();
    }

    public void Select(WhiteboardItemBase? item)
    {
        _selectedItem = item;
        _activeMode = WhiteboardImageHitTest.None;
        _activeSnapX = null;
        _activeSnapY = null;
    }

    public (WhiteboardImageHitTest Hit, double Rotation) GetHitInfo(Point point, IReadOnlyList<WhiteboardItemBase> items)
    {
        var padding = _hitTestPaddingProvider();
        if (_selectedItem is not null)
        {
            var handle = _selectedItem.HitTest(point, padding);
            if (handle != WhiteboardImageHitTest.None && handle != WhiteboardImageHitTest.Move)
            {
                return (handle, _selectedItem.RotationDegrees);
            }
        }

        var hit = HitTestAll(point, items, padding);
        if (hit is not null)
        {
            var handle = hit.HitTest(point, padding);
            if (handle == WhiteboardImageHitTest.Move || ReferenceEquals(hit, _selectedItem))
            {
                return (handle, hit.RotationDegrees);
            }
        }

        return (WhiteboardImageHitTest.None, 0);
    }

    public void BringToFront(List<WhiteboardItemBase> items)
    {
        if (_selectedItem is null)
        {
            return;
        }

        items.Remove(_selectedItem);
        items.Add(_selectedItem);
    }

    public void SendToBack(List<WhiteboardItemBase> items)
    {
        if (_selectedItem is null)
        {
            return;
        }

        items.Remove(_selectedItem);
        items.Insert(0, _selectedItem);
    }

    public WhiteboardItemBase? RemoveSelectedItem(List<WhiteboardItemBase> items)
    {
        if (_selectedItem is null)
        {
            return null;
        }

        var removed = _selectedItem;
        items.Remove(removed);
        Deselect();
        return removed;
    }

    private void SetupDragInfo(Point point, WhiteboardItemBase item)
    {
        _startPointer = point;
        _startCenter = item.Center;
        _startScaleX = item.ScaleX;
        _startScaleY = item.ScaleY;
        _startRotation = item.RotationDegrees;
        _activeOffset = point - item.Center;
    }

    private void ResizeActiveItem(Point point)
    {
        if (_selectedItem is null || _selectedItem.IsSizeLocked || _selectedItem.IsPositionLocked)
        {
            return;
        }

        var diff = point - _startCenter;
        var radians = -_startRotation * Math.PI / 180.0;
        var localPoint = new Point(
            diff.X * Math.Cos(radians) - diff.Y * Math.Sin(radians),
            diff.X * Math.Sin(radians) + diff.Y * Math.Cos(radians));

        var baseHalfWidth = _selectedItem.BaseSize.Width / 2.0;
        var baseHalfHeight = _selectedItem.BaseSize.Height / 2.0;
        var left = -baseHalfWidth * _startScaleX;
        var top = -baseHalfHeight * _startScaleY;
        var right = baseHalfWidth * _startScaleX;
        var bottom = baseHalfHeight * _startScaleY;

        (left, top, right, bottom) = _activeMode switch
        {
            WhiteboardImageHitTest.ResizeTopLeft => (Math.Min(localPoint.X, right - 16), Math.Min(localPoint.Y, bottom - 16), right, bottom),
            WhiteboardImageHitTest.ResizeTop => (left, Math.Min(localPoint.Y, bottom - 16), right, bottom),
            WhiteboardImageHitTest.ResizeTopRight => (left, Math.Min(localPoint.Y, bottom - 16), Math.Max(localPoint.X, left + 16), bottom),
            WhiteboardImageHitTest.ResizeRight => (left, top, Math.Max(localPoint.X, left + 16), bottom),
            WhiteboardImageHitTest.ResizeBottomRight => (left, top, Math.Max(localPoint.X, left + 16), Math.Max(localPoint.Y, top + 16)),
            WhiteboardImageHitTest.ResizeBottom => (left, top, right, Math.Max(localPoint.Y, top + 16)),
            WhiteboardImageHitTest.ResizeBottomLeft => (Math.Min(localPoint.X, right - 16), top, right, Math.Max(localPoint.Y, top + 16)),
            WhiteboardImageHitTest.ResizeLeft => (Math.Min(localPoint.X, right - 16), top, right, bottom),
            _ => (left, top, right, bottom),
        };

        var offset = RotateVector(new Point((left + right) / 2.0, (top + bottom) / 2.0), _startRotation);
        _selectedItem.SetCenter(_startCenter + offset);
        _selectedItem.SetScale((right - left) / _selectedItem.BaseSize.Width, (bottom - top) / _selectedItem.BaseSize.Height);
    }

    private Point SnapPoint(Point position, WhiteboardItemBase item, IReadOnlyList<WhiteboardItemBase> items)
    {
        var threshold = _snapThresholdProvider();
        var rx = position.X;
        var ry = position.Y;
        _activeSnapX = null;
        _activeSnapY = null;

        foreach (var other in items)
        {
            if (ReferenceEquals(other, item))
            {
                continue;
            }

            if (Math.Abs(position.X - other.Center.X) < threshold)
            {
                rx = other.Center.X;
                _activeSnapX = rx;
            }

            if (Math.Abs(position.Y - other.Center.Y) < threshold)
            {
                ry = other.Center.Y;
                _activeSnapY = ry;
            }
        }

        if (_activeSnapX is null)
        {
            var gx = Math.Round(rx / _snapGridSizeProvider()) * _snapGridSizeProvider();
            if (Math.Abs(rx - gx) < threshold / 2.0)
            {
                rx = gx;
            }
        }

        if (_activeSnapY is null)
        {
            var gy = Math.Round(ry / _snapGridSizeProvider()) * _snapGridSizeProvider();
            if (Math.Abs(ry - gy) < threshold / 2.0)
            {
                ry = gy;
            }
        }

        return new Point(rx, ry);
    }

    private static WhiteboardItemBase? HitTestAll(Point point, IReadOnlyList<WhiteboardItemBase> items, double padding)
    {
        WhiteboardItemBase? topHit = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.HitTest(point, padding) != WhiteboardImageHitTest.None)
            {
                topHit = item;
            }
        }
        return topHit;
    }

    private static Point RotateVector(Point vector, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(
            vector.X * Math.Cos(radians) - vector.Y * Math.Sin(radians),
            vector.X * Math.Sin(radians) + vector.Y * Math.Cos(radians));
    }
}
