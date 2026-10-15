using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Layout.Tests;

/// <summary>
/// What one page of a saved flow paints: each glyph with its font, size, colour and position, filled paths and images, and the
/// artifact sequence (<c>Type/Subtype</c>) each sits in.
/// </summary>
internal sealed class FlowContentRecorder : ContentProcessor
{
    private readonly Stack<string?> _marked = new();

    public List<RecordedGlyph> Glyphs { get; } = [];

    public List<(PdfRectangle Bounds, PdfColor Color)> Fills { get; } = [];

    public List<Matrix> Images { get; } = [];

    /// <summary>Stroked paths: their bounds (without the line width) and line width.</summary>
    public List<(PdfRectangle Bounds, double LineWidth)> Strokes { get; } = [];

    /// <summary>Each artifact sequence begun, in order, as <c>Type/Subtype</c> from its property list (empty for an absent entry).</summary>
    public List<string> Artifacts { get; } = [];

    /// <summary>Each glyph, fill and image in painting order with the artifact sequence it sits in (null for real content).</summary>
    public List<(string Event, string? Artifact)> Order { get; } = [];

    public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Images | ContentEvents.MarkedContent;

    public static FlowContentRecorder Record(PdfPage page)
    {
        var recorder = new FlowContentRecorder();
        page.ProcessContent(recorder);
        return recorder;
    }

    /// <summary>The page's text in painting order.</summary>
    public string Text => string.Concat(Glyphs.Select(glyph => glyph.Text));

    /// <summary>The text of the glyphs outside artifacts.</summary>
    public string BodyText => string.Concat(Glyphs.Where(glyph => glyph.Artifact is null).Select(glyph => glyph.Text));

    private string? CurrentArtifact => _marked.FirstOrDefault(kind => kind is not null);

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
            glyph.DeviceAdvanceX,
            CurrentArtifact));
        Order.Add(("glyph", CurrentArtifact));
    }

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        if (path.Paint == PathPaint.Fill)
        {
            Fills.Add((path.Path.Bounds, context.State.FillColor));
            Order.Add(("fill", CurrentArtifact));
        }
        else if (path.Paint == PathPaint.Stroke)
        {
            Strokes.Add((path.Path.Bounds, context.State.LineWidth));
        }
    }

    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        Images.Add(image.Ctm);
        Order.Add(("image", CurrentArtifact));
    }

    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        string? kind = null;
        if (mc.Tag.SequenceEqual("Artifact"u8))
        {
            CosDictionary? properties = mc.Properties
                ?? (mc.InlineProperties.Kind == ContentOperandKind.Dictionary ? mc.InlineProperties.ToCosObject() as CosDictionary : null);
            kind = $"{NameOf(properties, "Type")}/{NameOf(properties, "Subtype")}";
            Artifacts.Add(kind);
        }

        _marked.Push(kind);
    }

    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context) => _marked.TryPop(out _);

    private static string NameOf(CosDictionary? properties, string key) =>
        properties is not null && properties.TryGetValue(new CosName(key), out CosObject? value) && value is CosName name ? name.Value : string.Empty;
}

internal readonly record struct RecordedGlyph(string Text, string Font, double Size, PdfColor Color, double X, double Y, double Advance, string? Artifact = null);
