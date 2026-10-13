using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.TestSupport;

/// <summary>
/// A backend that draws nothing and writes down every call a display list's replay makes, one line per call, so a test can compare
/// what reached the backend contract (ADR 0006) with an expected sequence. It reads only what the call hands it.
/// </summary>
/// <remarks>
/// Lines: <c>save</c>, <c>restore</c>, <c>transform [a b c d e f]</c>,
/// <c>fill nonzero|evenodd M x y L x y Q … C … Z | Family c0 c1 …</c>,
/// <c>stroke width Cap Join miterLimit [dash …] phase [adjust] M … | Family c0 c1 …</c>,
/// <c>clip nonzero|evenodd M x y L … Z</c>, <c>image WxH Family bpc N[ interpolate]</c>, and
/// <c>glyphs Mode Format gid [text matrix][ xScale] … [| fill Family c0 …][| stroke width Cap Join miterLimit [dash …] phase[ adjust] Family c0 …]</c>
/// (Format is the program's format, <c>none</c> without a program). Numbers are written round-trippable and culture-invariant.
/// </remarks>
public sealed class RecordingBackend : IRenderingBackend
{
    /// <summary>Gets the calls, in replay order.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>Gets the colours of the fills, in replay order: the space as the list hands it over and a copy of the components.</summary>
    public List<(PdfColorSpace ColorSpace, float[] Components, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Fills { get; } = [];

    /// <summary>Gets the colours of the strokes, in replay order, like <see cref="Fills"/>.</summary>
    public List<(PdfColorSpace ColorSpace, float[] Components, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Strokes { get; } = [];

    /// <summary>Gets the image paints, in replay order: the interned image and what the paint converts it with.</summary>
    public List<(DisplayImage Image, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Images { get; } = [];

    /// <summary>Gets the glyphs drawn, in replay order: the program, glyph id, glyph-to-user transform and the outline as <see cref="Describe"/> writes it.</summary>
    public List<(Broadside.Fonts.FontProgram? Program, int GlyphId, Matrix Transform, string Outline)> Glyphs { get; } = [];

    /// <inheritdoc/>
    public void SaveState() => Operations.Add("save");

    /// <inheritdoc/>
    public void RestoreState() => Operations.Add("restore");

    /// <inheritdoc/>
    public void SetTransform(in Matrix transform) => Operations.Add("transform " + transform);

    /// <inheritdoc/>
    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        var text = new StringBuilder("fill ").Append(rule == FillRule.EvenOdd ? "evenodd" : "nonzero");
        Describe(path, text);
        text.Append(" | ").Append(color.ColorSpace.Family.ToString());
        foreach (float component in color.Components)
        {
            text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
        }

        Operations.Add(text.ToString());
        Fills.Add((color.ColorSpace, color.Components.ToArray(), color.DefaultColorSpaces, color.Conversion));
    }

    /// <inheritdoc/>
    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
        var text = new StringBuilder("stroke ")
            .Append(Number(style.LineWidth)).Append(' ').Append(style.LineCap).Append(' ').Append(style.LineJoin).Append(' ')
            .Append(Number(style.MiterLimit)).Append(" [");
        for (int i = 0; i < style.DashArray.Length; i++)
        {
            text.Append(i == 0 ? string.Empty : " ").Append(Number(style.DashArray[i]));
        }

        text.Append("] ").Append(Number(style.DashPhase));
        if (style.StrokeAdjustment)
        {
            text.Append(" adjust");
        }

        Describe(path, text);
        text.Append(" | ").Append(color.ColorSpace.Family.ToString());
        foreach (float component in color.Components)
        {
            text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
        }

        Operations.Add(text.ToString());
        Strokes.Add((color.ColorSpace, color.Components.ToArray(), color.DefaultColorSpaces, color.Conversion));
    }

    /// <inheritdoc/>
    public void IntersectClip(PathView path, FillRule rule)
    {
        var text = new StringBuilder("clip ").Append(rule == FillRule.EvenOdd ? "evenodd" : "nonzero");
        Describe(path, text);
        Operations.Add(text.ToString());
    }

    /// <inheritdoc/>
    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(image);
        Operations.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"image {image.Width}x{image.Height} {image.ColorSpace.Family} bpc {image.BitsPerComponent}{(image.Interpolate ? " interpolate" : string.Empty)}"));
        Images.Add((image, defaultColorSpaces, conversion));
    }

    /// <inheritdoc/>
    public void DrawGlyphs(in GlyphRun run)
    {
        var text = new StringBuilder("glyphs ").Append(run.RenderingMode).Append(' ').Append(run.Program?.Format.ToString() ?? "none");
        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            DisplayGlyph glyph = run.Glyphs[i];
            text.Append(' ').Append(glyph.GlyphId).Append(' ').Append(glyph.TextMatrix.ToString());
            if (glyph.HorizontalScale != 1)
            {
                text.Append(" x").Append(Number(glyph.HorizontalScale));
            }

            var outline = new StringBuilder();
            Describe(run.GetOutline(i), outline);
            Glyphs.Add((run.Program, glyph.GlyphId, run.GetGlyphTransform(i), outline.ToString().Trim()));
        }

        if (run.Fills)
        {
            text.Append(" | fill ").Append(run.FillColor.ColorSpace.Family.ToString());
            foreach (float component in run.FillColor.Components)
            {
                text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
            }

            Fills.Add((run.FillColor.ColorSpace, run.FillColor.Components.ToArray(), run.FillColor.DefaultColorSpaces, run.FillColor.Conversion));
        }

        if (run.Strokes)
        {
            StrokeStyle style = run.StrokeStyle;
            text.Append(" | stroke ")
                .Append(Number(style.LineWidth)).Append(' ').Append(style.LineCap).Append(' ').Append(style.LineJoin).Append(' ')
                .Append(Number(style.MiterLimit)).Append(" [");
            for (int i = 0; i < style.DashArray.Length; i++)
            {
                text.Append(i == 0 ? string.Empty : " ").Append(Number(style.DashArray[i]));
            }

            text.Append("] ").Append(Number(style.DashPhase)).Append(style.StrokeAdjustment ? " adjust " : " ").Append(run.StrokeColor.ColorSpace.Family.ToString());
            foreach (float component in run.StrokeColor.Components)
            {
                text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
            }

            Strokes.Add((run.StrokeColor.ColorSpace, run.StrokeColor.Components.ToArray(), run.StrokeColor.DefaultColorSpaces, run.StrokeColor.Conversion));
        }

        Operations.Add(text.ToString());
    }

    /// <summary>Writes a path as its verbs and points.</summary>
    /// <param name="path">The path.</param>
    /// <param name="text">The text to append to.</param>
    public static void Describe(PathView path, StringBuilder text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int point = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            (string letter, int count) = verb switch
            {
                PathVerb.MoveTo => ("M", 1),
                PathVerb.LineTo => ("L", 1),
                PathVerb.QuadTo => ("Q", 2),
                PathVerb.CubicTo => ("C", 3),
                _ => ("Z", 0),
            };
            text.Append(' ').Append(letter);
            for (int i = 0; i < count; i++, point++)
            {
                text.Append(' ').Append(path.Points[point].X.ToString("R", CultureInfo.InvariantCulture))
                    .Append(' ').Append(path.Points[point].Y.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
