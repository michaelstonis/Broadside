using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A TrueType or OpenType font: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.3. The dictionary has the entries of a Type 1 font dictionary (Table 109). Glyph names from the encoding are
/// what §9.6.5.4 maps through the font program's "cmap" table for a nonsymbolic font; a non-embedded TrueType font named after a
/// Standard 14 font (such as <c>Arial</c>) takes that font's metrics.
/// </remarks>
public sealed class PdfTrueTypeFont : PdfSimpleFont
{
    internal PdfTrueTypeFont(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.TrueType)
    {
    }
}
