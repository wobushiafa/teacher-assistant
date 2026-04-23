using Avalonia.Media;

namespace TeacherAssistant.Desktop.ViewModels;

public sealed record PenColorOption(string Name, Color Color)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color);
}
