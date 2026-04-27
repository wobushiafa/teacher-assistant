using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TeacherAssistant.Desktop.ViewModels;
using TeacherAssistant.Desktop.Views;

namespace TeacherAssistant.Desktop;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            MainWindowViewModel => new MainWindow(),
            WhiteboardViewModel => new WhiteboardView(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
