namespace TeacherAssistant.Whiteboard.Models;

public enum WhiteboardImageHitTest
{
    None = 0,
    Move = 1,
    ResizeTopLeft = 2,
    ResizeTop = 3,
    ResizeTopRight = 4,
    ResizeRight = 5,
    ResizeBottomRight = 6,
    ResizeBottom = 7,
    ResizeBottomLeft = 8,
    ResizeLeft = 9,
    RotateCenter = 10,
}
