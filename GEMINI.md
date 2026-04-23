# Gemini CLI Context: Teacher Assistant (zbnote)

This project, `zbnote`, is a **Teacher Assistant** desktop application built with **.NET 10.0** and **Avalonia UI**. Its primary feature is a high-performance digital whiteboard designed for educational use.

## Project Architecture

The solution is divided into two main projects:

### 1. TeacherAssistant.Desktop
The main application entry point and UI layer.
- **Technology:** Avalonia UI (v12+).
- **Pattern:** MVVM using `CommunityToolkit.Mvvm`.
- **Key Components:**
    - `ViewModels/`: Contains `MainWindowViewModel` and `WhiteboardViewModel`, managing UI state and commands.
    - `Views/`: XAML-based views including `MainWindow` and `WhiteboardView`.
    - `Services/`: UI-specific services.

### 2. TeacherAssistant.Whiteboard
The core whiteboard engine and business logic.
- **Purpose:** Handles stroke rendering, shape management, image manipulation, and whiteboard state.
- **Key Components:**
    - `Controllers/WhiteboardController`: Orchestrates tools (Pen, Eraser, Highlighter) and interaction sessions.
    - `Services/WhiteboardSurface`: The primary state container and renderer, utilizing custom bitmap manipulation for performance.
    - `Views/WhiteboardCanvas`: A custom Avalonia control that performs the actual rendering using `DrawingContext`.
    - `Models/`: Data structures for `WhiteboardStroke`, `WhiteboardShapeItem`, and `WhiteboardImageItem`.
- **Performance:** Uses `AllowUnsafeBlocks` for direct memory/bitmap manipulation during stroke rendering.

## Technology Stack

- **Language:** C# 13+
- **Framework:** .NET 10.0
- **UI Framework:** [Avalonia UI](https://avaloniaui.net/)
- **MVVM Library:** [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **Rendering:** Custom rendering with Avalonia's `DrawingContext` and bitmap-based stroke rendering.

## Development Workflow

### Building and Running
- **Build Solution:** `dotnet build`
- **Run Application:** `dotnet run --project TeacherAssistant.Desktop/TeacherAssistant.Desktop.csproj`

### Development Conventions
- **MVVM:** Strictly follow the MVVM pattern. Logic should reside in the `Whiteboard` library or ViewModels, not in code-behind.
- **Performance:** When modifying rendering logic in `TeacherAssistant.Whiteboard`, be mindful of performance. Use the existing bitmap-based rendering patterns for strokes.
- **Styling:** The project uses the Avalonia `FluentTheme`. Styles are primarily defined in `App.axaml` and individual views.

## Key Files
- `TeacherAssistant.Desktop/Program.cs`: Application entry point.
- `TeacherAssistant.Whiteboard/Controllers/WhiteboardController.cs`: Core logic for whiteboard interactions.
- `TeacherAssistant.Whiteboard/Views/WhiteboardCanvas.cs`: Custom rendering implementation.
- `TeacherAssistant.Whiteboard/Services/WhiteboardSurface.cs`: Central state and rendering orchestrator.
