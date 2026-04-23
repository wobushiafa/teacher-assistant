using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class WhiteboardDocument
{
    private readonly List<IWhiteboardElement> _elements = [];
    private readonly SpatialGrid<StrokeElement> _strokeGrid = new(s => s.Bounds, 256.0);
    private readonly SpatialGrid<WhiteboardItemBase> _itemGrid = new(i => i.Bounds, 256.0);
    private readonly List<StrokeElement> _strokes = [];
    private readonly List<ImageElement> _images = [];
    private readonly List<WhiteboardItemBase> _items = [];

    public IReadOnlyList<IWhiteboardElement> Elements => _elements;

    public IReadOnlyList<StrokeElement> Strokes => _strokes;

    public IEnumerable<StrokeElement> StrokesReversed => _strokes.AsEnumerable().Reverse();

    public IReadOnlyList<ImageElement> Images => _images;
    public IReadOnlyList<WhiteboardItemBase> Items => _items;

    public event Action? DocumentChanged;

    public void AddStroke(StrokeElement stroke)
    {
        _strokes.Add(stroke);
        _elements.Add(stroke);
        _strokeGrid.Add(stroke);
        NotifyChanged();
    }

    public void AddImage(ImageElement image)
    {
        _images.Add(image);
        _elements.Add(image);
        NotifyChanged();
    }

    public void AddItem(WhiteboardItemBase item)
    {
        _items.Add(item);
        _elements.Add(item);
        _itemGrid.Add(item);
        NotifyChanged();
    }

    public void RemoveElement(Guid id)
    {
        for (var i = _elements.Count - 1; i >= 0; i--)
        {
            if (_elements[i].Id == id)
            {
                var element = _elements[i];
                _elements.RemoveAt(i);

                if (element is StrokeElement stroke)
                {
                    _strokes.Remove(stroke);
                    _strokeGrid.Remove(stroke);
                }
                else if (element is ImageElement image)
                {
                    _images.Remove(image);
                }
                else if (element is WhiteboardItemBase item)
                {
                    _items.Remove(item);
                    _itemGrid.Remove(item);
                }

                NotifyChanged();
                return;
            }
        }
    }

    public void Clear()
    {
        _elements.Clear();
        _strokes.Clear();
        _images.Clear();
        _items.Clear();
        _strokeGrid.Clear();
        _itemGrid.Clear();
        NotifyChanged();
    }

    public T? FindElement<T>(Guid id) where T : IWhiteboardElement
    {
        foreach (var element in _elements)
        {
            if (element is T typed && element.Id == id)
                return typed;
        }

        return default;
    }

    public IEnumerable<StrokeElement> QueryStrokes(Rect bounds)
        => _strokeGrid.Query(bounds);

    public IEnumerable<WhiteboardItemBase> QueryItems(Rect bounds)
        => _itemGrid.Query(bounds);

    public void BringToFront(WhiteboardItemBase item)
    {
        if (!_items.Remove(item))
        {
            return;
        }

        _items.Add(item);
        _elements.Remove(item);
        _elements.Add(item);
        NotifyChanged();
    }

    public void SendToBack(WhiteboardItemBase item)
    {
        if (!_items.Remove(item))
        {
            return;
        }

        _items.Insert(0, item);
        _elements.Remove(item);
        _elements.Insert(0, item);
        NotifyChanged();
    }

    private void NotifyChanged() => DocumentChanged?.Invoke();
}
