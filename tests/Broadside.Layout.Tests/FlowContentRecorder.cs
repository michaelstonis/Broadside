using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Tests;

/// <summary>What one page of a saved flow paints: each glyph with its font, size, colour and position, filled paths and images.</summary>
internal sealed class FlowContentRecorder : ContentProcessor
{
    public List<RecordedGlyph> Glyphs { get; } = [];

    public List<(PdfRectangle Bounds, PdfColor Color)> Fills { get; } = [];

    public List<Matrix> Images { get; } = [];

    /// <summary>Stroked paths: their bounds (without the line width) and line width.</summary>
    public List<(PdfRectangle Bounds, double LineWidth)> Strokes { get; } = [];

    public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Images;

    public static FlowContentRecorder Record(PdfPage page)
    {
        var recorder = new FlowContentRecorder();
        page.ProcessContent(recorder);
        return recorder;
    }

    /// <summary>The page's text in painting order.</summary>
    public string Text => string.Concat(Glyphs.Select(glyph => glyph.Text));

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        PdfFont? font = context.State.Font;
        Glyphs.Add(new RecordedGlyph(
            glyph.Unicode.ToString(),
            font?.BaseFont ?? string.Empty,
            context.State.FontSize,
            context.State.FillColor,
            glyph.DeviceOrigin.X,
            glyph.DeviceOrigin.Y,
            glyph.DeviceAdvanceX));
    }

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        if (path.Paint == PathPaint.Fill)
        {
            Fills.Add((path.Path.Bounds, context.State.FillColor));
        }
        else if (path.Paint == PathPaint.Stroke)
        {
            Strokes.Add((path.Path.Bounds, context.State.LineWidth));
        }
    }

    public override void PaintImage(in ImageEvent image, ContentContext context) => Images.Add(image.Ctm);
}

internal readonly record struct RecordedGlyph(string Text, string Font, double Size, PdfColor Color, double X, double Y, double Advance);
