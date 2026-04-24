using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using TeacherAssistant.Whiteboard;
using TeacherAssistant.Whiteboard.Controllers;
using TeacherAssistant.Whiteboard.Models;
using TeacherAssistant.Whiteboard.Services;

namespace TeacherAssistant.Desktop.ViewModels;

public partial class WhiteboardViewModel : ViewModelBase, IDisposable
{
    private readonly WhiteboardController _controller = new();
    private PenColorOption _selectedPenColor;
    private WhiteboardToolOption _selectedToolOption;
    private WhiteboardInteractionOption _selectedInteractionOption;
    private WhiteboardItemBase? _selectedObject;

    [ObservableProperty]
    private Color _penColor = Colors.Black;

    [ObservableProperty]
    private double _penThickness = 3;

    [ObservableProperty]
    private bool _usePenNibEffect = true;

    [ObservableProperty]
    private bool _useBezierSmoothing = true;

    [ObservableProperty]
    private WhiteboardTool _selectedTool = WhiteboardTool.Pen;

    [ObservableProperty]
    private WhiteboardInteractionMode _selectedInteractionMode = WhiteboardInteractionMode.Mouse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsObjectSelected))]
    private WhiteboardShapeItem? _selectedShape;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsObjectSelected))]
    private WhiteboardImageItem? _selectedImage;

    public bool IsObjectSelected => SelectedShape is not null || SelectedImage is not null;
    public WhiteboardSurface Surface => _controller.Surface;

    public double SelectedObjectX
    {
        get => _selectedObject is null ? 0 : _selectedObject.Bounds.X;
        set
        {
            if (_selectedObject is null) return;
            var bounds = _selectedObject.Bounds;
            _selectedObject.SetCenter(new Point(value + bounds.Width / 2.0, _selectedObject.Center.Y));
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public double SelectedObjectY
    {
        get => _selectedObject is null ? 0 : _selectedObject.Bounds.Y;
        set
        {
            if (_selectedObject is null) return;
            var bounds = _selectedObject.Bounds;
            _selectedObject.SetCenter(new Point(_selectedObject.Center.X, value + bounds.Height / 2.0));
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public double SelectedObjectWidth
    {
        get => _selectedObject?.Width ?? 0;
        set
        {
            if (_selectedObject is null || _selectedObject.IsSizeLocked) return;
            _selectedObject.Width = value;
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public double SelectedObjectHeight
    {
        get => _selectedObject?.Height ?? 0;
        set
        {
            if (_selectedObject is null || _selectedObject.IsSizeLocked) return;
            _selectedObject.Height = value;
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public double SelectedObjectRotationDegrees
    {
        get => _selectedObject?.RotationDegrees ?? 0;
        set
        {
            if (_selectedObject is null) return;
            _selectedObject.RotationDegrees = value;
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public bool SelectedObjectIsSizeLocked
    {
        get => _selectedObject?.IsSizeLocked ?? false;
        set
        {
            if (_selectedObject is null) return;
            _selectedObject.IsSizeLocked = value;
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    public bool IsSelectedShape => SelectedShape is not null;
    public bool CanUndo => _controller.Surface.CanUndo;
    public bool CanRedo => _controller.Surface.CanRedo;

    public IReadOnlyList<WhiteboardInteractionOption> InteractionOptions { get; } =
    [
        new("鼠标操作", WhiteboardInteractionMode.Mouse),
        new("画笔涂鸦", WhiteboardInteractionMode.Pen),
    ];

    public IReadOnlyList<WhiteboardToolOption> ToolOptions { get; } =
    [
        new("画笔", WhiteboardTool.Pen),
        new("荧光笔", WhiteboardTool.Highlighter),
        new("橡皮擦", WhiteboardTool.Eraser),
    ];

    public IReadOnlyList<PenColorOption> PenColorOptions { get; } =
    [
        new("黑色", Colors.Black),
        new("黄色", Color.Parse("#F6E94B")),
        new("蓝色", Colors.DodgerBlue),
        new("红色", Colors.IndianRed),
        new("绿色", Colors.SeaGreen),
        new("橙色", Colors.DarkOrange),
        new("紫色", Colors.MediumPurple),
    ];

    public PenColorOption SelectedPenColor
    {
        get => _selectedPenColor;
        set
        {
            if (SetProperty(ref _selectedPenColor, value))
            {
                PenColor = value.Color;
            }
        }
    }

    public WhiteboardToolOption SelectedToolOption
    {
        get => _selectedToolOption;
        set
        {
            if (SetProperty(ref _selectedToolOption, value))
            {
                SelectedTool = value.Tool;
            }
        }
    }

    public WhiteboardInteractionOption SelectedInteractionOption
    {
        get => _selectedInteractionOption;
        set
        {
            if (SetProperty(ref _selectedInteractionOption, value))
            {
                SelectedInteractionMode = value.Mode;
            }
        }
    }

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand RemoveSelectedItemCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand<WhiteboardShapeType> AddShapeCommand { get; }
    public IRelayCommand AddRectangleCommand { get; }
    public IRelayCommand AddEllipseCommand { get; }
    public IRelayCommand AddTriangleCommand { get; }
    public IRelayCommand AddStarCommand { get; }
    public IRelayCommand AddRightTriangleCommand { get; }
    public IRelayCommand AddDiamondCommand { get; }
    public IRelayCommand AddPentagonCommand { get; }
    public IRelayCommand AddHexagonCommand { get; }
    public IRelayCommand AddArrowCommand { get; }

    public WhiteboardViewModel()
    {
        SelectedPenColor = PenColorOptions[0];
        SelectedToolOption = ToolOptions[0];
        SelectedInteractionOption = InteractionOptions[0];
        ClearCommand = new RelayCommand(_controller.Clear);
        RemoveSelectedItemCommand = new RelayCommand(RemoveSelectedItem);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);

        AddShapeCommand = new RelayCommand<WhiteboardShapeType>(type =>
            _controller.AddShape(type, new Size(100, 100), SelectedPenColor.Color, Colors.Transparent, PenThickness));

        AddRectangleCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Rectangle));
        AddEllipseCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Ellipse));
        AddTriangleCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Triangle));
        AddStarCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Star));
        AddRightTriangleCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.RightTriangle));
        AddDiamondCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Diamond));
        AddPentagonCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Pentagon));
        AddHexagonCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Hexagon));
        AddArrowCommand = new RelayCommand(() => AddShapeCommand.Execute(WhiteboardShapeType.Arrow));

        _controller.UsePenNibEffect = UsePenNibEffect;
        _controller.UseBezierSmoothing = UseBezierSmoothing;

        Surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    private void OnSurfacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WhiteboardSurface.SelectedShape)) OnPropertyChanged(nameof(IsSelectedShape));
        if (e.PropertyName == nameof(WhiteboardSurface.CanUndo))
        {
            OnPropertyChanged(nameof(CanUndo));
            UndoCommand.NotifyCanExecuteChanged();
        }
        if (e.PropertyName == nameof(WhiteboardSurface.CanRedo))
        {
            OnPropertyChanged(nameof(CanRedo));
            RedoCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(WhiteboardSurface.SelectedShape) || e.PropertyName == nameof(WhiteboardSurface.SelectedImage))
        {
            UpdateSelectedObjectReferences();
            return;
        }

        if (_selectedObject is not null &&
            (e.PropertyName == nameof(WhiteboardSurface.Bitmap) ||
             e.PropertyName == nameof(WhiteboardSurface.PreviewBitmap)))
        {
            RaiseSelectedObjectPropertiesChanged();
        }
    }

    private void UpdateSelectedObjectReferences()
    {
        if (_selectedObject is not null)
        {
            _selectedObject.PropertyChanged -= OnSelectedObjectPropertyChanged;
        }

        SelectedShape = Surface.SelectedShape;
        SelectedImage = Surface.SelectedImage;
        _selectedObject = (WhiteboardItemBase?)SelectedShape ?? SelectedImage;

        if (_selectedObject is not null)
        {
            _selectedObject.PropertyChanged += OnSelectedObjectPropertyChanged;
        }

        RaiseSelectedObjectPropertiesChanged();
        OnPropertyChanged(nameof(IsObjectSelected));
        OnPropertyChanged(nameof(IsSelectedShape));
    }

    private void OnSelectedObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSelectedObjectPropertiesChanged();
    }

    private void RaiseSelectedObjectPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedObjectX));
        OnPropertyChanged(nameof(SelectedObjectY));
        OnPropertyChanged(nameof(SelectedObjectWidth));
        OnPropertyChanged(nameof(SelectedObjectHeight));
        OnPropertyChanged(nameof(SelectedObjectRotationDegrees));
        OnPropertyChanged(nameof(SelectedObjectIsSizeLocked));
        OnPropertyChanged(nameof(IsSelectedShape));
    }

    public void RemoveSelectedItem() => _controller.RemoveSelectedItem();
    public void Undo() => _controller.Undo();
    public void Redo() => _controller.Redo();
    public void Clear() => _controller.Clear();
    public void EnsureSize(int width, int height) => _controller.EnsureSize(width, height);
    public void BeginStroke(Point point, long pointerId = 0) => _controller.BeginStroke(point, pointerId);
    public void ContinueStroke(Point point, long pointerId = 0) => _controller.ContinueStroke(point, pointerId);
    public void EndStroke(long pointerId = 0) => _controller.EndStroke(pointerId);

    partial void OnUsePenNibEffectChanged(bool value) => _controller.UsePenNibEffect = value;
    partial void OnUseBezierSmoothingChanged(bool value) => _controller.UseBezierSmoothing = value;
    partial void OnPenColorChanged(Color value) => _controller.PenColor = value;
    partial void OnPenThicknessChanged(double value) => _controller.PenThickness = value;

    partial void OnSelectedToolChanged(WhiteboardTool value)
    {
        _controller.SelectedTool = value;

        var option = value switch
        {
            WhiteboardTool.Highlighter => ToolOptions[1],
            WhiteboardTool.Eraser => ToolOptions[2],
            _ => ToolOptions[0],
        };
        if (!Equals(_selectedToolOption, option))
        {
            _selectedToolOption = option;
            OnPropertyChanged(nameof(SelectedToolOption));
        }
    }

    partial void OnSelectedInteractionModeChanged(WhiteboardInteractionMode value)
    {
        _controller.SelectedInteractionMode = value;

        var option = value == WhiteboardInteractionMode.Mouse ? InteractionOptions[0] : InteractionOptions[1];
        if (!Equals(_selectedInteractionOption, option))
        {
            _selectedInteractionOption = option;
            OnPropertyChanged(nameof(SelectedInteractionOption));
        }
    }

    public void Dispose()
    {
        if (_selectedObject is not null)
        {
            _selectedObject.PropertyChanged -= OnSelectedObjectPropertyChanged;
        }
        Surface.PropertyChanged -= OnSurfacePropertyChanged;
        _controller.Dispose();
    }
}
