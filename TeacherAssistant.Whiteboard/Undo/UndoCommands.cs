using System;
using System.Collections.Generic;
using Avalonia;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Services;
using TeacherAssistant.Whiteboard.Undo;

namespace TeacherAssistant.Whiteboard.Undo;

public interface IUndoCommand
{
    void Execute();
    void Undo();
    void Redo();
}

public class AddStrokeCommand : IUndoCommand
{
    private readonly WhiteboardDocument _document;
    private readonly StrokeElement _stroke;

    public StrokeElement Stroke => _stroke;

    public AddStrokeCommand(WhiteboardDocument document, StrokeElement stroke)
    {
        _document = document;
        _stroke = stroke;
    }

    public void Execute() => _document.AddStroke(_stroke);
    public void Undo() => _document.RemoveElement(_stroke.Id);
    public void Redo() => Execute();
}

public class AddItemCommand : IUndoCommand
{
    private readonly WhiteboardDocument _document;
    private readonly WhiteboardItemBase _item;

    public AddItemCommand(WhiteboardDocument document, WhiteboardItemBase item)
    {
        _document = document;
        _item = item;
    }

    public void Execute() => _document.AddItem(_item);
    public void Undo() => _document.RemoveElement(_item.Id);
    public void Redo() => Execute();
}

public class RemoveElementCommand : IUndoCommand
{
    private readonly WhiteboardDocument _document;
    private readonly IWhiteboardElement _element;
    private int _index;

    public IWhiteboardElement Element => _element;

    public RemoveElementCommand(WhiteboardDocument document, IWhiteboardElement element)
    {
        _document = document;
        _element = element;
    }

    public void Execute()
    {
        _index = _document.IndexOf(_element);
        _document.RemoveElement(_element.Id);
    }

    public void Undo()
    {
        if (_element is StrokeElement stroke) _document.InsertStroke(_index, stroke);
        else if (_element is WhiteboardItemBase item) _document.InsertItem(_index, item);
    }

    public void Redo() => Execute();
}

public class ClearCommand : IUndoCommand
{
    private readonly WhiteboardDocument _document;
    private readonly List<IWhiteboardElement> _elements;

    public ClearCommand(WhiteboardDocument document)
    {
        _document = document;
        _elements = new List<IWhiteboardElement>(document.Elements);
    }

    public void Execute() => _document.Clear();
    public void Undo()
    {
        foreach (var element in _elements)
        {
            if (element is StrokeElement stroke) _document.AddStroke(stroke);
            else if (element is WhiteboardItemBase item) _document.AddItem(item);
        }
    }
    public void Redo() => Execute();
}

public class TransformCommand : IUndoCommand
{
    private readonly WhiteboardItemBase _item;
    private readonly Point _oldCenter;
    private readonly double _oldScaleX, _oldScaleY, _oldRotation;
    private readonly Point _newCenter;
    private readonly double _newScaleX, _newScaleY, _newRotation;

    public TransformCommand(WhiteboardItemBase item, 
        Point oldCenter, double oldScaleX, double oldScaleY, double oldRotation,
        Point newCenter, double newScaleX, double newScaleY, double newRotation)
    {
        _item = item;
        _oldCenter = oldCenter; _oldScaleX = oldScaleX; _oldScaleY = oldScaleY; _oldRotation = oldRotation;
        _newCenter = newCenter; _newScaleX = newScaleX; _newScaleY = newScaleY; _newRotation = newRotation;
    }

    public void Execute() { }
    public void Undo() { _item.SetCenter(_oldCenter); _item.SetScale(_oldScaleX, _oldScaleY); _item.SetRotation(_oldRotation); }
    public void Redo() { _item.SetCenter(_newCenter); _item.SetScale(_newScaleX, _newScaleY); _item.SetRotation(_newRotation); }
}
