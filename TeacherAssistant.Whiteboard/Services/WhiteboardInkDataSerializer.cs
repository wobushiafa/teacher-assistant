using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using TeacherAssistant.Whiteboard.Models;

namespace TeacherAssistant.Whiteboard.Services;

public static class WhiteboardInkDataSerializer
{
    public static string Serialize(IEnumerable<StrokeElement> strokes)
    {
        var document = new WhiteboardInkDocumentDto
        {
            Version = 1,
            ExportedAtUtc = DateTime.UtcNow,
            Strokes = BuildStrokeDtos(strokes),
        };

        return JsonSerializer.Serialize(document, WhiteboardInkDataJsonContext.Default.WhiteboardInkDocumentDto);
    }

    public static IReadOnlyList<StrokeElement> Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize(json, WhiteboardInkDataJsonContext.Default.WhiteboardInkDocumentDto)
            as WhiteboardInkDocumentDto
            ?? throw new InvalidOperationException("笔迹数据为空或格式无效。");

        var strokes = new List<StrokeElement>(document.Strokes.Count);
        foreach (var strokeDto in document.Strokes)
        {
            var tool = Enum.TryParse<WhiteboardTool>(strokeDto.Tool, ignoreCase: true, out var parsedTool)
                ? parsedTool
                : WhiteboardTool.Pen;

            var stroke = new StrokeElement(
                Color.FromUInt32(strokeDto.Argb),
                strokeDto.Thickness,
                tool);

            foreach (var sampleDto in strokeDto.Samples)
            {
                stroke.Samples.Add(new StrokeSample(
                    new Point(sampleDto.X, sampleDto.Y),
                    sampleDto.Timestamp,
                    sampleDto.Width));
            }

            strokes.Add(stroke);
        }

        return strokes;
    }

    private static List<StrokeDto> BuildStrokeDtos(IEnumerable<StrokeElement> strokes)
    {
        var result = new List<StrokeDto>();
        foreach (var stroke in strokes)
        {
            var dto = new StrokeDto
            {
                Tool = stroke.Tool.ToString(),
                Argb = stroke.Color.ToUInt32(),
                Thickness = stroke.Thickness,
                Samples = [],
            };

            foreach (var sample in stroke.Samples)
            {
                dto.Samples.Add(new StrokeSampleDto
                {
                    X = sample.Point.X,
                    Y = sample.Point.Y,
                    Timestamp = sample.Timestamp,
                    Width = sample.Width,
                });
            }

            result.Add(dto);
        }

        return result;
    }

    internal sealed class WhiteboardInkDocumentDto
    {
        public int Version { get; set; }
        public DateTime ExportedAtUtc { get; set; }
        public List<StrokeDto> Strokes { get; set; } = [];
    }

    internal sealed class StrokeDto
    {
        public string Tool { get; set; } = WhiteboardTool.Pen.ToString();
        public uint Argb { get; set; }
        public double Thickness { get; set; }
        public List<StrokeSampleDto> Samples { get; set; } = [];
    }

    internal sealed class StrokeSampleDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public long Timestamp { get; set; }
        public double Width { get; set; }
    }
}
