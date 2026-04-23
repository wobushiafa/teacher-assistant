using Avalonia;

namespace TeacherAssistant.Whiteboard.Models;

public readonly record struct StrokeSample(Point Point, long Timestamp, double Width);
