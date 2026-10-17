using Broadside.Annotations;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// The normal appearances generated for the widgets of a check box or radio button field: an on and an <c>Off</c> form XObject each,
/// the on one showing the widget's caption in ZapfDingbats, built before anything in the document changes.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.2.3 (example 1: <c>/ZaDb 12 Tf</c> ... <c>(4) Tj</c>), §12.7.5.2.4 and §12.5.6.19, Table 192 (<c>CA</c>: the
/// normal caption, which "may be used with any type of button field"). The caption's characters are ZapfDingbats character codes
/// (Annex D.6); the background, border and rotation are drawn as for variable text (<see cref="TextFieldAppearance.BuildStream"/>).
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
        PdfSimpleFont font;
        CosName fontName;
        if (appearance is { Font: PdfSimpleFont { Standard14: Standard14Font.ZapfDingbats } daFont, FontName: { } daName } && fonts.ContainsKey(daName))
        {
            (font, fontName) = (daFont, daName);
        }
        else
        {
            var dictionary = new CosDictionary
            {
                [KnownNames.Type] = FontNames.Font,
                [Subtype] = FontNames.Type1,
                [FontNames.BaseFont] = ZapfDingbats,
            };
            font = (PdfSimpleFont)document.GetFont(dictionary)!;
            fontName = TextFieldAppearance.UniqueName(fonts, "ZaDb");
            fonts[fontName] = dictionary;
        }

        double fontSize = Math.Abs(appearance?.FontSize ?? 0);
        PdfColor? color = appearance?.Color;
        CosName value = field.Value;
        IReadOnlyList<PdfWidgetAnnotation> selected = field.SelectedWidgets;
        var appearances = new List<(PdfWidgetAnnotation, CosName, CosName, CosStream, CosStream)>();
        foreach (PdfWidgetAnnotation widget in field.Widgets)
        {
            CosName? existing = field.GetOnState(widget);
            int index = field.IndexOfWidget(widget.Dictionary);
            CosName on = existing ?? (onStates is not null && index >= 0 && index < onStates.Count ? onStates[index] : field.DefaultOnState(index));
            bool isOn = existing is not null ? selected.Any(other => ReferenceEquals(other.Dictionary, widget.Dictionary)) : on.Equals(value);
            byte[] codes = Codes(widget.AppearanceCharacteristics?.NormalCaption is { Length: > 0 } caption ? caption : field.DefaultCaption);
            CosStream onStream = TextFieldAppearance.BuildStream(document, widget, appearance?.FontResources, fonts, (content, box, _) =>
                WriteCaption(content, font, fontName, fontSize, color, codes, box));
            CosStream offStream = TextFieldAppearance.BuildStream(document, widget, appearance?.FontResources, fonts, (_, _, _) => { });
            appearances.Add((widget, on, isOn ? on : FormNames.Off, onStream, offStream));
        }

        return new ToggleButtonAppearance(document, appearances);
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
}
