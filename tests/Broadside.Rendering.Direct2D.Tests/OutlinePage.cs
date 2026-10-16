using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Writes a page whose text is replaced by its glyph outlines as ordinary path objects: each glyph's outline mapped to user space by
/// its glyph transform (ISO 32000-2 §9.2.4, §9.4.4), under the transform of its run, filled by the non-zero winding rule and stroked
/// with the run's line parameters (§9.3.6), in the run's colours converted to DeviceRGB. Rendering it shows what a backend's path
/// rendering makes of the same outlines its forced-outline text rendering fills.
/// </summary>
/// <remarks>Only for pages whose display lists hold glyph runs, transforms and saved states; anything else throws.</remarks>
internal sealed class OutlinePage : IRenderingBackend
{
    private readonly StringBuilder _content = new();
    private readonly PdfDocument _document;

    private OutlinePage(PdfDocument document) => _document = document;

    /// <summary>Returns a one-page file of the page's glyphs as paths, with the page's media box, crop box and rotation.</summary>
    public static byte[] Write(PdfPage page)
    {
        DisplayList list = page.CreateDisplayList();
        var writer = new OutlinePage(list.Document);
        list.Replay(writer);
        PdfRectangle media = page.MediaBox;
        PdfRectangle crop = page.CropBox;
        string boxes = Invariant($"/MediaBox [{media.Left} {media.Bottom} {media.Right} {media.Top}] /CropBox [{crop.Left} {crop.Bottom} {crop.Right} {crop.Top}] /Rotate {page.Rotation}");
        return ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R {boxes} /Resources << >> /Contents 4 0 R >>",
            ContentSamples.Stream(writer._content.ToString()),
        ]);
    }

    private Matrix Transform { get; set; } = Matrix.Identity;

    public void SaveState()
    {
    }

    public void RestoreState()
    {
    }

    public void SetTransform(in Matrix transform) => Transform = transform;

    public void DrawGlyphs(in GlyphRun run)
    {
        for (int index = 0; index < run.Glyphs.Length; index++)
        {
            PathView outline = run.GetOutline(index);
            if (outline.IsEmpty)
            {
                continue;
            }

            Matrix glyph = run.GetGlyphTransform(index);
            Matrix ctm = Transform;
            _content.Append(Invariant($"q {ctm.A} {ctm.B} {ctm.C} {ctm.D} {ctm.E} {ctm.F} cm\n"));
            if (run.Fills)
            {
                Color(run.FillColor, "rg");
                Path(outline, glyph);
                _content.Append("f\n");
            }

            if (run.Strokes)
            {
                StrokeStyle style = run.StrokeStyle;
                Assert.False(style.StrokeAdjustment);
                Color(run.StrokeColor, "RG");
                _content.Append(Invariant($"{style.LineWidth} w {(int)style.LineCap} J {(int)style.LineJoin} j {style.MiterLimit} M ["));
                foreach (double dash in style.DashArray)
                {
                    _content.Append(Invariant($"{dash} "));
                }

                _content.Append(Invariant($"] {style.DashPhase} d\n"));
                Path(outline, glyph);
                _content.Append("S\n");
            }

            _content.Append("Q\n");
        }
    }

    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) =>
        Assert.True(strokeAlpha == 1 && fillAlpha == 1 && blendMode == BlendMode.Normal);

    public void FillPath(PathView path, FillRule rule, in DisplayColor color) => throw Unexpected();

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => throw Unexpected();

    public void IntersectClip(PathView path, FillRule rule) => throw Unexpected();

    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => throw Unexpected();

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => throw Unexpected();

    public void BeginAnnotation(in DisplayAnnotation annotation) => throw Unexpected();

    public void EndAnnotation() => throw Unexpected();

    public void DrawShading(in DisplayShading shading) => throw Unexpected();

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => throw Unexpected();

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => throw Unexpected();

    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => throw Unexpected();

    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => throw Unexpected();

    public void DrawGroup(in DisplayGroup group)
    {
        if (group.IsKnockout || group.Opacity < 1 || group.BlendMode != BlendMode.Normal || !group.SoftMask.IsNone)
        {
            throw Unexpected();
        }

        group.Replay(this);
    }

    public void SetSoftMask(in DisplaySoftMask mask)
    {
        if (!mask.IsNone)
        {
            throw Unexpected();
        }
    }

    public void SetDeviceParameters(in DisplayDeviceParameters parameters)
    {
    }

    private static NotSupportedException Unexpected() => new("The outline page writer handles pages of text only.");

    private static string Invariant(FormattableString text) => text.ToString(new NumberFormat());

    private void Color(in DisplayColor color, string operatorName)
    {
        PdfColorConverter converter = _document.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
        Span<float> rgb = stackalloc float[4];
        converter.Convert(color.Components, rgb);
        _content.Append(Invariant($"{Channel(rgb[0])} {Channel(rgb[1])} {Channel(rgb[2])} {operatorName}\n"));
    }

    private static double Channel(float value) => (int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f) / 255.0;

    private void Path(PathView outline, in Matrix glyph)
    {
        ReadOnlySpan<PathPoint> points = outline.Points;
        int p = 0;
        PathPoint current = default;
        foreach (PathVerb verb in outline.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    current = points[p++];
                    Point(glyph, current, "m");
                    break;
                case PathVerb.LineTo:
                    current = points[p++];
                    Point(glyph, current, "l");
                    break;
                case PathVerb.QuadTo:
                    {
                        // A quadratic Bézier curve as the cubic it is (PDF has no quadratic curve operator).
                        PathPoint control = points[p];
                        PathPoint end = points[p + 1];
                        PathPoint first = new(current.X + (2.0 / 3 * (control.X - current.X)), current.Y + (2.0 / 3 * (control.Y - current.Y)));
                        PathPoint second = new(end.X + (2.0 / 3 * (control.X - end.X)), end.Y + (2.0 / 3 * (control.Y - end.Y)));
                        Curve(glyph, first, second, end);
                        current = end;
                        p += 2;
                        break;
                    }

                case PathVerb.CubicTo:
                    Curve(glyph, points[p], points[p + 1], points[p + 2]);
                    current = points[p + 2];
                    p += 3;
                    break;
                case PathVerb.Close:
                    _content.Append("h\n");
                    break;
            }
        }
    }

    private void Point(in Matrix glyph, PathPoint point, string operatorName)
    {
        PathPoint user = glyph.Transform(point);
        _content.Append(Invariant($"{user.X} {user.Y} {operatorName}\n"));
    }

    private void Curve(in Matrix glyph, PathPoint first, PathPoint second, PathPoint end)
    {
        PathPoint a = glyph.Transform(first);
        PathPoint b = glyph.Transform(second);
        PathPoint c = glyph.Transform(end);
        _content.Append(Invariant($"{a.X} {a.Y} {b.X} {b.Y} {c.X} {c.Y} c\n"));
    }

    /// <summary>Writes doubles as PDF real numbers: no exponent, enough digits to round-trip a float coordinate.</summary>
    private sealed class NumberFormat : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;

        public string Format(string? format, object? arg, IFormatProvider? formatProvider) => arg switch
        {
            double value => value.ToString("0.##########", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
            _ => Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }
}
