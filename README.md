# Teacher Assistant (教师助手)

Teacher Assistant is a high-performance digital whiteboard application designed for educational environments. It is built with .NET 10.0 and Avalonia UI, featuring a powerful rendering engine capable of handling smooth strokes, shapes, and images.

## Key Features

- **High-Performance Whiteboard**: Real-time, smooth drawing experience using SkiaSharp for GPU-accelerated rendering.
- **Multi-Touch Support**: Supports simultaneous drawing with multiple fingers or pens, each with independent smoothing filters.
- **Smart Smoothing**: Implements a "smart smoothing" filter (灵动平滑) to ensure professional-grade stroke quality even with variable input speeds.
- **Rich Toolset**: includes Pen, Highlighter, and Eraser tools with customizable thickness and colors.
- **Object Manipulation**: Insert, move, rotate, and resize shapes (Rectangles, Ellipses, Stars, etc.) and images from local storage.
- **Efficient Deletion**: Quickly remove selected objects using the UI or keyboard shortcuts (`Delete`/`Backspace`).

## Project Architecture

The solution is divided into two main projects:

### 1. TeacherAssistant.Desktop
The main application entry point and UI layer.
- **UI Framework**: Avalonia UI (v12.0+).
- **Pattern**: MVVM using `CommunityToolkit.Mvvm`.
- **Styling**: Modern, clean UI utilizing the Fluent theme.

### 2. TeacherAssistant.Whiteboard
The core whiteboard engine and business logic.
- **Rendering**: Custom rendering using SkiaSharp for high-quality, anti-aliased strokes.
- **Performance**: Optimized pixel manipulation and incremental rendering to ensure zero UI lag.
- **Models**: Structured data storage for strokes, shapes, and images with spatial indexing support.

## Technology Stack

- **Language**: C# 13
- **Framework**: .NET 10.0
- **UI**: Avalonia UI
- **Graphics Engine**: SkiaSharp
- **MVVM**: CommunityToolkit.Mvvm

## Getting Started

### Prerequisites
- .NET 10.0 SDK or later

### Building and Running
1. Clone the repository.
2. Build the solution:
   ```bash
   dotnet build
   ```
3. Run the application:
   ```bash
   dotnet run --project TeacherAssistant.Desktop/TeacherAssistant.Desktop.csproj
   ```

## Controls and Shortcuts

- **Drawing**: Select "Pen" or "Highlighter" and draw with your mouse or touch screen.
- **Erasing**: Use the "Eraser" tool to remove strokes.
- **Selecting Objects**: In "Mouse" mode, click on an image or shape to select it.
- **Deleting Objects**: Press the `Delete` key (or `Backspace` in mouse mode) or use the "Delete Object" button in the properties panel.
- **Object Transformation**: Use the handles on a selected object to resize, rotate, or move it.
