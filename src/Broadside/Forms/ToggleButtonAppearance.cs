using Broadside.Annotations;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// The normal appearances generated for the widgets of a check box or radio button field: an on and an <c>Off</c> form XObject each,
/// the on one showing the widget's caption in ZapfDingbats (or, when the default appearance names an embedded font, the caption's mark
/// drawn as a path), built before anything in the document changes.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.2.3 (example 1: <c>/ZaDb 12 Tf</c> ... <c>(4) Tj</c>), §12.7.5.2.4 and §12.5.6.19, Table 192 (<c>CA</c>: the
/// normal caption, which "may be used with any type of button field"). The caption's characters are ZapfDingbats character codes
/// (Annex D.6); the background, border and rotation are drawn as for variable text (<see cref="TextFieldAppearance.BuildStream"/>).
/// <para>ISO 32000-2 §12.7.4.3.</para>
/// <para>ISO 14289-1 §7.21: with an embedded default appearance font, the marks are paths and no font goes unembedded.</para>
/// </remarks>
internal sealed class ToggleButtonAppearance
{
    /// <summary>The height of a dingbat's centre above the baseline, in ems: the glyphs mostly span -0.015 to 0.705.</summary>
    private const double DingbatCentre = 0.345;

    /// <summary>The share of the box an auto-sized caption fills.</summary>
    private const double AutoFill = 0.8;

    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName ZapfDingbats = new("ZapfDingbats");

    private readonly PdfDocument _document;
    private readonly List<(PdfWidgetAnnotation Widget, CosName On, CosName State, CosStream OnStream, CosStream OffStream)> _appearances;

    private ToggleButtonAppearance(PdfDocument document, List<(PdfWidgetAnnotation, CosName, CosName, CosStream, CosStream)> appearances)
    {
        _document = document;
        _appearances = appearances;
    }

    /// <summary>Builds the on and off appearances of every widget of <paramref name="field"/>.</summary>
    /// <param name="field">The field.</param>
    /// <param name="onStates">
    /// The on state of each widget in <c>Kids</c> order for widgets that have none yet, or <see langword="null"/> for the defaults
    /// (<c>Yes</c> for a check box, the position in <c>Kids</c> for a radio button).
    /// </param>
    /// <returns>The appearances, not yet placed on the widgets (<see cref="Apply"/>).</returns>
    public static ToggleButtonAppearance Build(PdfToggleButtonField field, IReadOnlyList<CosName>? onStates = null)
    {
        PdfDocument document = field.Document;
        PdfDefaultAppearance? appearance = field.ParsedDefaultAppearance;
        CosDictionary fonts = TextFieldAppearance.CopyFontResources(document, appearance?.FontResources);

        // A default appearance naming an embedded font other than ZapfDingbats (PdfFieldOptions.Font): the mark is drawn as a path, so
        // the appearance uses no font that is not embedded (ISO 14289-1 §7.21.4.1, ISO 19005-2 §6.2.11.4.1).
        bool drawn = appearance?.Font is { } daFont && daFont is not PdfSimpleFont { Standard14: Standard14Font.ZapfDingbats } && IsEmbedded(daFont);
        (PdfSimpleFont Font, CosName Name)? zapfDingbats = null;
        double fontSize = Math.Abs(appearance?.FontSize ?? 0);
        PdfColor? color = appearance?.Color;
        // A field being created has widgets without appearances yet: reading its value through the checks would report the
        // appearances it is about to get as missing.
        bool creating = field.Widgets.Any(static widget => !widget.Dictionary.ContainsKey(AnnotationNames.AP));
        CosName value = creating ? field.StoredValue : field.Value;
        IReadOnlyList<PdfWidgetAnnotation> selected = creating && FormNames.Off.Equals(value) ? [] : field.SelectedWidgets;
        var appearances = new List<(PdfWidgetAnnotation, CosName, CosName, CosStream, CosStream)>();
        foreach (PdfWidgetAnnotation widget in field.Widgets)
        {
            CosName? existing = widget.Dictionary.ContainsKey(AnnotationNames.AP) ? field.GetOnState(widget) : null;
            int index = field.IndexOfWidget(widget.Dictionary);
            CosName on = existing ?? (onStates is not null && index >= 0 && index < onStates.Count ? onStates[index] : field.DefaultOnState(index));
            bool isOn = existing is not null ? selected.Any(other => ReferenceEquals(other.Dictionary, widget.Dictionary)) : on.Equals(value);
            string caption = widget.AppearanceCharacteristics?.NormalCaption is { Length: > 0 } normal ? normal : field.DefaultCaption;
            Mark? mark = drawn ? MarkOf(caption) : null;
            CosStream onStream;
            if (mark is { } shape)
            {
                onStream = TextFieldAppearance.BuildStream(document, widget, appearance?.FontResources, fonts, (content, box, _) =>
                    WriteMark(content, shape, fontSize, color, box));
            }
            else
            {
                byte[] codes = Codes(caption);
                (PdfSimpleFont zapf, CosName zapfName) = zapfDingbats ??= ZapfDingbatsFont(document, appearance, fonts);
                onStream = TextFieldAppearance.BuildStream(document, widget, appearance?.FontResources, fonts, (content, box, _) =>
                    WriteCaption(content, zapf, zapfName, fontSize, color, codes, box));
            }

            CosStream offStream = TextFieldAppearance.BuildStream(document, widget, appearance?.FontResources, fonts, (_, _, _) => { });
            appearances.Add((widget, on, isOn ? on : FormNames.Off, onStream, offStream));
        }

        return new ToggleButtonAppearance(document, appearances);
    }

    /// <summary>
    /// Returns the appearance <paramref name="widget"/> shows in its current state (the on stream when it is on, else the off one), or
    /// <see langword="null"/> when it is not a widget of the field.
    /// </summary>
    /// <param name="widget">The widget.</param>
    /// <returns>The appearance stream, a direct object.</returns>
    public CosStream? StreamFor(PdfWidgetAnnotation widget)
    {
        foreach ((PdfWidgetAnnotation candidate, CosName _, CosName state, CosStream onStream, CosStream offStream) in _appearances)
        {
            if (ReferenceEquals(candidate.Dictionary, widget.Dictionary))
            {
                return state.Equals(FormNames.Off) ? offStream : onStream;
            }
        }

        return null;
    }

    /// <summary>Makes the streams new indirect objects, each widget's normal appearance subdictionary, and sets its appearance state.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 (appearance subdictionaries keyed by state) and §12.7.5.2.3.</remarks>
    public void Apply()
    {
        foreach ((PdfWidgetAnnotation widget, CosName on, CosName state, CosStream onStream, CosStream offStream) in _appearances)
        {
            var normal = new CosDictionary
            {
                [on] = _document.AddObject(onStream),
                [FormNames.Off] = _document.AddObject(offStream),
            };
            widget.Dictionary[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = normal };
            widget.Dictionary[AnnotationNames.AS] = state;
        }
    }

    /// <summary>The default appearance's ZapfDingbats font and its name, or a ZapfDingbats font added to the appearance's resources.</summary>
    private static (PdfSimpleFont Font, CosName Name) ZapfDingbatsFont(PdfDocument document, PdfDefaultAppearance? appearance, CosDictionary fonts)
    {
        if (appearance is { Font: PdfSimpleFont { Standard14: Standard14Font.ZapfDingbats } zapf, FontName: { } daName } && fonts.ContainsKey(daName))
        {
            return (zapf, daName);
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [Subtype] = FontNames.Type1,
            [FontNames.BaseFont] = ZapfDingbats,
        };
        CosName name = TextFieldAppearance.UniqueName(fonts, "ZaDb");
        fonts[name] = dictionary;
        return ((PdfSimpleFont)document.GetFont(dictionary)!, name);
    }

    /// <summary>The marks a caption of one ZapfDingbats character (Annex D.6) is drawn as when no ZapfDingbats font is used.</summary>
    private enum Mark
    {
        /// <summary><c>4</c> (a20), a check mark.</summary>
        Check,

        /// <summary><c>l</c> (a71), a filled circle.</summary>
        Circle,

        /// <summary><c>8</c> (a24), a cross.</summary>
        Cross,

        /// <summary><c>u</c> (a77), a filled diamond.</summary>
        Diamond,

        /// <summary><c>n</c> (a73), a filled square.</summary>
        Square,

        /// <summary><c>H</c> (a8), a filled star.</summary>
        Star,
    }

    /// <summary>Whether <paramref name="font"/> has its program embedded (a Type 0 font: its descendant's).</summary>
    private static bool IsEmbedded(PdfFont font) => font is PdfType0Font composite ? composite.DescendantFont?.IsEmbedded == true : font.IsEmbedded;

    /// <summary>The mark a caption shows, or <see langword="null"/> for a caption that is not one of the marks.</summary>
    private static Mark? MarkOf(string caption) => caption switch
    {
        "4" => Mark.Check,
        "l" => Mark.Circle,
        "8" => Mark.Cross,
        "u" => Mark.Diamond,
        "n" => Mark.Square,
        "H" => Mark.Star,
        _ => null,
    };

    /// <summary>
    /// Draws <paramref name="mark"/> centred in the box as a path, as large as the glyph would be: in the default appearance's size
    /// (three quarters of it), or for an auto size, 60% of the box's shorter side.
    /// </summary>
    private static void WriteMark(ContentWriter content, Mark mark, double fontSize, PdfColor? color, TextFieldAppearance.Box box)
    {
        double shorter = Math.Min(box.Width, box.Height);
        if (shorter <= 0)
        {
            return;
        }

        var path = new MarkPath(content, box.Left + (box.Width / 2), box.Bottom + (box.Height / 2), Math.Min(shorter, fontSize > 0 ? 0.75 * fontSize : 0.6 * shorter));
        content.Operator("q");
        if (color is not null)
        {
            double[] components = Array.ConvertAll(color.Value.Components.ToArray(), component => (double)component);
            TextFieldAppearance.WriteColor(content, components, stroking: false);
            TextFieldAppearance.WriteColor(content, components, stroking: true);
        }

        switch (mark)
        {
            case Mark.Check:
                content.Number(0.14 * path.Side).Operator("w").Number(1).Operator("J").Number(1).Operator("j");
                path.MoveTo(-0.4, 0.02);
                path.LineTo(-0.12, -0.3);
                path.LineTo(0.42, 0.36);
                content.Operator("S");
                break;
            case Mark.Cross:
                content.Number(0.14 * path.Side).Operator("w").Number(1).Operator("J");
                path.MoveTo(-0.36, -0.36);
                path.LineTo(0.36, 0.36);
                path.MoveTo(-0.36, 0.36);
                path.LineTo(0.36, -0.36);
                content.Operator("S");
                break;
            case Mark.Circle:
                // Four Bézier arcs, each a quarter circle (control points at 0.5523 of the radius of 0.5).
                path.MoveTo(0.5, 0);
                path.CurveTo(0.5, 0.27615, 0.27615, 0.5, 0, 0.5);
                path.CurveTo(-0.27615, 0.5, -0.5, 0.27615, -0.5, 0);
                path.CurveTo(-0.5, -0.27615, -0.27615, -0.5, 0, -0.5);
                path.CurveTo(0.27615, -0.5, 0.5, -0.27615, 0.5, 0);
                content.Operator("f");
                break;
            case Mark.Diamond:
                path.MoveTo(0, 0.5);
                path.LineTo(0.5, 0);
                path.LineTo(0, -0.5);
                path.LineTo(-0.5, 0);
                content.Operator("h").Operator("f");
                break;
            case Mark.Square:
                path.MoveTo(-0.42, -0.42);
                path.LineTo(0.42, -0.42);
                path.LineTo(0.42, 0.42);
                path.LineTo(-0.42, 0.42);
                content.Operator("h").Operator("f");
                break;
            default:
                // A five-pointed star: outer points on a radius of 0.5, inner ones on 0.2.
                for (int point = 0; point < 10; point++)
                {
                    double radius = point % 2 == 0 ? 0.5 : 0.2;
                    double angle = (Math.PI / 2) + (point * Math.PI / 5);
                    if (point == 0)
                    {
                        path.MoveTo(radius * Math.Cos(angle), radius * Math.Sin(angle));
                    }
                    else
                    {
                        path.LineTo(radius * Math.Cos(angle), radius * Math.Sin(angle));
                    }
                }

                content.Operator("h").Operator("f");
                break;
        }

        content.Operator("Q");
    }

    /// <summary>The caption's characters as single-byte codes (§9.6.2.2: a simple font's codes); characters past 255 have none.</summary>
    private static byte[] Codes(string caption) => [.. caption.Where(character => character <= 0xFF).Select(character => (byte)character)];

    /// <summary>Writes the caption centred in the box: <c>q BT /ZaDb size Tf colour x y Td (codes) Tj ET Q</c>.</summary>
    private static void WriteCaption(ContentWriter content, PdfSimpleFont font, CosName fontName, double fontSize, PdfColor? color, byte[] codes, TextFieldAppearance.Box box)
    {
        double units = 0;
        foreach (byte code in codes)
        {
            units += font.GetWidth(code);
        }

        if (codes.Length == 0 || box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        double size = fontSize > 0 ? fontSize : AutoFill * Math.Min(box.Height, units > 0 ? box.Width * 1000 / units : box.Height);
        double x = box.Left + ((box.Width - (units * size / 1000)) / 2);
        double y = box.Bottom + (box.Height / 2) - (DingbatCentre * size);
        content.Operator("q").Operator("BT");
        content.Name(fontName).Number(size).Operator("Tf");
        if (color is not null)
        {
            TextFieldAppearance.WriteColor(content, Array.ConvertAll(color.Value.Components.ToArray(), component => (double)component), stroking: false);
        }

        content.Number(x).Number(y).Operator("Td");
        content.String(codes, hexadecimal: false).Operator("Tj");
        content.Operator("ET").Operator("Q");
    }

    /// <summary>Writes path segments in a square of side <see cref="Side"/> centred on a point, from coordinates in units of the side.</summary>
    private readonly struct MarkPath(ContentWriter content, double centerX, double centerY, double side)
    {
        public double Side => side;

        public void MoveTo(double x, double y) => content.Number(centerX + (x * side)).Number(centerY + (y * side)).Operator("m");

        public void LineTo(double x, double y) => content.Number(centerX + (x * side)).Number(centerY + (y * side)).Operator("l");

        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) =>
            content.Number(centerX + (x1 * side)).Number(centerY + (y1 * side)).Number(centerX + (x2 * side)).Number(centerY + (y2 * side))
                .Number(centerX + (x3 * side)).Number(centerY + (y3 * side)).Operator("c");
    }
}
