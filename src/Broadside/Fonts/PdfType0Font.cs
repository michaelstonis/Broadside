using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A composite (Type 0) font, whose glyphs come from a descendant CIDFont: a live view over its font dictionary.</summary>
/// <remarks>ISO 32000-2 §9.7. A Type 0 font has no font descriptor of its own (§9.8.1); its CIDFont has one.</remarks>
public sealed class PdfType0Font : PdfFont
{
    internal PdfType0Font(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.Type0)
    {
    }
}
