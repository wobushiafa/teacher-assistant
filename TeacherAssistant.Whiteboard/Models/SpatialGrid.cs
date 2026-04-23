using System;
using System.Collections.Generic;
using Avalonia;

namespace TeacherAssistant.Whiteboard.Models;

public sealed class SpatialGrid<T> where T : class
{
    private readonly Dictionary<GridCell, HashSet<T>> _buckets = [];
    private readonly Dictionary<T, GridCell[]> _elementCells = [];
    private readonly Func<T, Rect> _boundsSelector;

    public SpatialGrid(Func<T, Rect> boundsSelector, double cellSize = 256)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        _boundsSelector = boundsSelector ?? throw new ArgumentNullException(nameof(boundsSelector));
        CellSize = cellSize;
    }

    public double CellSize { get; }

    public void Add(T element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Remove(element);

        var cells = GetCellsForBounds(_boundsSelector(element));
        _elementCells[element] = cells;

        foreach (var cell in cells)
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
            {
                bucket = [];
                _buckets[cell] = bucket;
            }

            bucket.Add(element);
        }
    }

    public bool Remove(T element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!_elementCells.TryGetValue(element, out var cells))
            return false;

        foreach (var cell in cells)
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            bucket.Remove(element);
            if (bucket.Count == 0)
                _buckets.Remove(cell);
        }

        _elementCells.Remove(element);
        return true;
    }

    public void Update(T element) => Add(element);

    public void Clear()
    {
        _buckets.Clear();
        _elementCells.Clear();
    }

    public IEnumerable<T> Query(Point point)
        => Query(new Rect(point.X, point.Y, 0.001, 0.001));

    public IEnumerable<T> Query(Rect bounds)
    {
        var results = new HashSet<T>();

        foreach (var cell in GetCellsForBounds(bounds))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            foreach (var element in bucket)
            {
                if (results.Add(element) && Intersects(_boundsSelector(element), bounds))
                {
                    continue;
                }

                if (!Intersects(_boundsSelector(element), bounds))
                    results.Remove(element);
            }
        }

        return results;
    }

    private GridCell[] GetCellsForBounds(Rect bounds)
    {
        var normalized = Normalize(bounds);
        var left = GetCellIndex(normalized.X);
        var top = GetCellIndex(normalized.Y);
        var right = GetCellIndex(normalized.X + normalized.Width);
        var bottom = GetCellIndex(normalized.Y + normalized.Height);
        var cells = new GridCell[(right - left + 1) * (bottom - top + 1)];
        var index = 0;

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                cells[index++] = new GridCell(x, y);
            }
        }

        return cells;
    }

    private int GetCellIndex(double coordinate)
        => (int)Math.Floor(coordinate / CellSize);

    private static Rect Normalize(Rect bounds)
    {
        var x = Math.Min(bounds.X, bounds.X + bounds.Width);
        var y = Math.Min(bounds.Y, bounds.Y + bounds.Height);
        var right = Math.Max(bounds.X, bounds.X + bounds.Width);
        var bottom = Math.Max(bounds.Y, bounds.Y + bounds.Height);
        var width = Math.Max(0.001, right - x);
        var height = Math.Max(0.001, bottom - y);
        return new Rect(x, y, width, height);
    }

    private static bool Intersects(Rect a, Rect b)
    {
        var normalizedA = Normalize(a);
        var normalizedB = Normalize(b);
        return normalizedA.X < normalizedB.X + normalizedB.Width
            && normalizedA.X + normalizedA.Width > normalizedB.X
            && normalizedA.Y < normalizedB.Y + normalizedB.Height
            && normalizedA.Y + normalizedA.Height > normalizedB.Y;
    }

    private readonly record struct GridCell(int X, int Y);
}
