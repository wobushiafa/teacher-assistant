using System;
using CommunityToolkit.Mvvm.Input;

namespace TeacherAssistant.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public string Title { get; } = "教师助手";

    public WhiteboardViewModel Whiteboard { get; } = new();

    public IRelayCommand ClearWhiteboardCommand { get; }

    public MainWindowViewModel()
    {
        ClearWhiteboardCommand = new RelayCommand(Whiteboard.Clear);
    }

    public void Dispose() => Whiteboard.Dispose();
}
