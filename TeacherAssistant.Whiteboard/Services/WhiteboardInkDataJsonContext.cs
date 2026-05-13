using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TeacherAssistant.Whiteboard.Services;

[JsonSerializable(typeof(WhiteboardInkDataSerializer.WhiteboardInkDocumentDto))]
[JsonSerializable(typeof(List<WhiteboardInkDataSerializer.StrokeDto>))]
[JsonSerializable(typeof(List<WhiteboardInkDataSerializer.StrokeSampleDto>))]
internal sealed partial class WhiteboardInkDataJsonContext : JsonSerializerContext
{
}
