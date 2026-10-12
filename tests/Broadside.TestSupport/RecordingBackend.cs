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
/// Lines: <c>save</c>, <c>restore</c>, <c>transform [a b c d e f]</c>, and
/// <c>fill nonzero|evenodd M x y L x y Q … C … Z | Family c0 c1 …</c>, and <c>image WxH Family bpc N[ interpolate]</c>. Numbers are written round-trippable and culture-invariant.
/// </remarks>
public sealed class RecordingBackend : IRenderingBackend
{
    /// <summary>Gets the calls, in replay order.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>Gets the colours of the fills, in replay order: the space as the list hands it over and a copy of the components.</summary>
    public List<(PdfColorSpace ColorSpace, float[] Components, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Fills { get; } = [];

    /// <summary>Gets the image paints, in replay order: the interned image and what the paint converts it with.</summary>
    public List<(DisplayImage Image, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Images { get; } = [];

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
    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(image);
        Operations.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"image {image.Width}x{image.Height} {image.ColorSpace.Family} bpc {image.BitsPerComponent}{(image.Interpolate ? " interpolate" : string.Empty)}"));
        Images.Add((image, defaultColorSpaces, conversion));
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
}
