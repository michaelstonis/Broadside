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
/// <c>clip nonzero|evenodd M x y L … Z</c>,
/// <c>image WxH Family bpc N[ interpolate][ mask WxH[ inverted]][ smask WxH Family bpc N][ matte m…][ colorkey min max …]</c>,
/// <c>imagemask WxH[ inverted][ interpolate] | Family c0 c1 …</c>,
/// <c>annotation Kind [left bottom right top][ nozoom][ norotate][ opacity a][ blend Mode][ isolated][ knockout]</c>, <c>end annotation</c>, <c>shading Type [matrix][ background]</c>,
/// <c>glyphs Mode Format gid [text matrix][ xScale] … [| fill Family c0 …][| stroke width Cap Join miterLimit [dash …] phase[ adjust] Family c0 …]</c>
/// (Format is the program's format, <c>none</c> without a program), and fills and strokes in a shading pattern ending
/// <c>| shading Type [matrix] background</c> instead of a colour, and in a tiling pattern ending
/// <c>| tiling PaintType TilingType [matrix] bbox [l b r t] step x y</c>; <c>compositing CA ca BlendMode</c>;
/// <c>group Kind[ isolated][ knockout][ Family][ opacity a][ BlendMode][ bbox [l b r t] [matrix]]</c>, then the group's content, then
/// <c>end group</c> (a group with a soft mask adds <c> softmask …</c> as below before its content);
/// <c>softmask None</c> or <c>softmask Alpha|Luminosity [matrix][ Family bc …][ tr FunctionType][ shape][ bbox [l b r t]]</c>. Numbers are written round-trippable and culture-invariant.
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

    /// <summary>Gets the shading paints (<c>sh</c> and shading-pattern fills and strokes), in replay order.</summary>
    public List<(PdfShading Shading, Matrix Matrix, bool PaintsBackground, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Shadings { get; } = [];

    /// <summary>Gets the tiling-pattern paints (fills and strokes), in replay order: the pattern, its pattern space and its cell list.</summary>
    public List<(PdfTilingPattern Pattern, Matrix Matrix, DisplayList Cell)> Tilings { get; } = [];

    /// <summary>Gets the stencil mask paints, in replay order: the interned mask and a copy of its colour, like <see cref="Fills"/>.</summary>
    public List<(DisplayImageMask Mask, PdfColorSpace ColorSpace, float[] Components, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> ImageMasks { get; } = [];

    /// <summary>Gets the groups, in replay order: their kind, colour space, default colour spaces and conversion.</summary>
    public List<(DisplayGroupKind Kind, PdfColorSpace? ColorSpace, PdfDefaultColorSpaces Defaults, ColorConversion Conversion)> Groups { get; } = [];

    /// <summary>Gets the soft masks set (none included) and those of groups, in replay order.</summary>
    public List<(bool IsNone, PdfSoftMaskType Subtype, DisplayList? Content, Matrix Matrix, PdfFunction? TransferFunction)> SoftMasks { get; } = [];

    /// <summary>Gets the annotations begun, in replay order.</summary>
    public List<DisplayAnnotation> Annotations { get; } = [];

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
        var text = new StringBuilder(string.Create(
            CultureInfo.InvariantCulture,
            $"image {image.Width}x{image.Height} {image.ColorSpace.Family} bpc {image.BitsPerComponent}{(image.Interpolate ? " interpolate" : string.Empty)}"));
        if (image.Mask is { } mask)
        {
            text.Append(CultureInfo.InvariantCulture, $" mask {mask.Width}x{mask.Height}{(mask.IsInverted ? " inverted" : string.Empty)}");
        }

        if (image.SoftMask is { } soft)
        {
            text.Append(CultureInfo.InvariantCulture, $" smask {soft.Width}x{soft.Height} {soft.ColorSpace.Family} bpc {soft.BitsPerComponent}");
        }

        if (!image.Matte.IsEmpty)
        {
            text.Append(" matte");
            foreach (float component in image.Matte)
            {
                text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        if (!image.ColorKey.IsEmpty)
        {
            text.Append(" colorkey");
            foreach (double value in image.ColorKey)
            {
                text.Append(' ').Append(Number(value));
            }
        }

        Operations.Add(text.ToString());
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

    /// <inheritdoc/>
    public void DrawShading(in DisplayShading shading) => Operations.Add(Shading(shading));

    /// <inheritdoc/>
    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
    {
        var text = new StringBuilder("fill ").Append(rule == FillRule.EvenOdd ? "evenodd" : "nonzero");
        Describe(path, text);
        Operations.Add(text.Append(" | ").Append(Shading(shading)).ToString());
    }

    /// <inheritdoc/>
    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
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
        Operations.Add(text.Append(" | ").Append(Shading(shading)).ToString());
    }

    /// <inheritdoc/>
    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
    {
        var text = new StringBuilder("fill ").Append(rule == FillRule.EvenOdd ? "evenodd" : "nonzero");
        Describe(path, text);
        Operations.Add(text.Append(" | ").Append(Tiling(pattern)).ToString());
    }

    /// <inheritdoc/>
    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
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
        Operations.Add(text.Append(" | ").Append(Tiling(pattern)).ToString());
    }

    /// <inheritdoc/>
    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var text = new StringBuilder(string.Create(
            CultureInfo.InvariantCulture,
            $"imagemask {mask.Width}x{mask.Height}{(mask.IsInverted ? " inverted" : string.Empty)}{(mask.Interpolate ? " interpolate" : string.Empty)}"));
        text.Append(" | ").Append(color.ColorSpace.Family.ToString());
        foreach (float component in color.Components)
        {
            text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
        }

        Operations.Add(text.ToString());
        ImageMasks.Add((mask, color.ColorSpace, color.Components.ToArray(), color.DefaultColorSpaces, color.Conversion));
    }

    /// <inheritdoc/>
    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) =>
        Operations.Add(string.Create(CultureInfo.InvariantCulture, $"compositing {Number(strokeAlpha)} {Number(fillAlpha)} {blendMode}"));

    /// <inheritdoc/>
    public void DrawGroup(in DisplayGroup group)
    {
        var text = new StringBuilder("group ").Append(group.Kind);
        if (group.IsIsolated)
        {
            text.Append(" isolated");
        }

        if (group.IsKnockout)
        {
            text.Append(" knockout");
        }

        if (group.ColorSpace is { } space)
        {
            text.Append(' ').Append(space.Family);
        }

        if (group.Opacity != 1)
        {
            text.Append(" opacity ").Append(Number(group.Opacity));
        }

        if (group.BlendMode != BlendMode.Normal)
        {
            text.Append(' ').Append(group.BlendMode);
        }

        if (group.HasBoundingBox)
        {
            PdfRectangle box = group.BoundingBox;
            text.Append(CultureInfo.InvariantCulture, $" bbox [{Number(box.Left)} {Number(box.Bottom)} {Number(box.Right)} {Number(box.Top)}] {group.Matrix}");
        }

        if (!group.SoftMask.IsNone)
        {
            text.Append(' ').Append(SoftMask(group.SoftMask));
        }

        Operations.Add(text.ToString());
        Groups.Add((group.Kind, group.ColorSpace, group.DefaultColorSpaces, group.Conversion));
        group.Replay(this);
        Operations.Add("end group");
    }

    /// <inheritdoc/>
    public void SetSoftMask(in DisplaySoftMask mask) => Operations.Add(SoftMask(mask));

    public void BeginAnnotation(in DisplayAnnotation annotation)
    {
        PdfRectangle rect = annotation.Rectangle;
        var text = new StringBuilder("annotation ").Append(annotation.Kind)
            .Append(" [").Append(Number(rect.Left)).Append(' ').Append(Number(rect.Bottom)).Append(' ')
            .Append(Number(rect.Right)).Append(' ').Append(Number(rect.Top)).Append(']');
        text.Append(annotation.NoZoom ? " nozoom" : string.Empty).Append(annotation.NoRotate ? " norotate" : string.Empty);
        if (annotation.Opacity != 1)
        {
            text.Append(" opacity ").Append(Number(annotation.Opacity));
        }

        if (annotation.BlendMode != BlendMode.Normal)
        {
            text.Append(" blend ").Append(annotation.BlendMode);
        }

        text.Append(annotation.Isolated ? " isolated" : string.Empty).Append(annotation.Knockout ? " knockout" : string.Empty);
        Operations.Add(text.ToString());
        Annotations.Add(annotation);
    }

    /// <inheritdoc/>
    public void EndAnnotation() => Operations.Add("end annotation");

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

    private string Shading(in DisplayShading shading)
    {
        Shadings.Add((shading.Shading, shading.Matrix, shading.PaintsBackground, shading.DefaultColorSpaces, shading.Conversion));
        return $"shading {shading.Shading.ShadingType} {shading.Matrix}{(shading.PaintsBackground ? " background" : string.Empty)}";
    }

    private string Tiling(in DisplayTilingPattern tiling)
    {
        PdfTilingPattern pattern = tiling.Pattern;
        Tilings.Add((pattern, tiling.Matrix, tiling.Cell));
        PdfRectangle box = pattern.BoundingBox;
        return $"tiling {pattern.PaintType} {pattern.TilingType} {tiling.Matrix} bbox [{Number(box.Left)} {Number(box.Bottom)} {Number(box.Right)} {Number(box.Top)}] step {Number(pattern.XStep)} {Number(pattern.YStep)}";
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private string SoftMask(in DisplaySoftMask mask)
    {
        SoftMasks.Add((mask.IsNone, mask.Subtype, mask.IsNone ? null : mask.Content, mask.Matrix, mask.TransferFunction));
        if (mask.IsNone)
        {
            return "softmask None";
        }

        var text = new StringBuilder("softmask ").Append(mask.Subtype).Append(' ').Append(mask.Matrix);
        if (mask.ColorSpace is { } space)
        {
            text.Append(' ').Append(space.Family);
            foreach (float component in mask.Backdrop)
            {
                text.Append(' ').Append(component.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        if (mask.TransferFunction is { } function)
        {
            text.Append(" tr ").Append(function.FunctionType);
        }

        if (mask.IsShape)
        {
            text.Append(" shape");
        }

        if (mask.HasBoundingBox)
        {
            PdfRectangle box = mask.BoundingBox;
            text.Append(CultureInfo.InvariantCulture, $" bbox [{Number(box.Left)} {Number(box.Bottom)} {Number(box.Right)} {Number(box.Top)}]");
        }

        return text.ToString();
    }
}
