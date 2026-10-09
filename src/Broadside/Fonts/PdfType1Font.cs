using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A Type 1 font, or an instance of a multiple master font: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.2 (Table 109) and §9.6.2.3. A non-embedded Type 1 font named after one of the Standard 14 fonts (§9.6.2.2) may
/// omit <c>FirstChar</c>, <c>LastChar</c>, <c>Widths</c> and <c>FontDescriptor</c>; its widths and descriptor data then come from
/// the Standard 14 metrics. The encoding of a Type 1 font overrides the font program's built-in encoding (§9.6.5.2).
/// </remarks>
public sealed class PdfType1Font : PdfSimpleFont
{
    internal PdfType1Font(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfFontType fontType)
        : base(document, dictionary, reference, fontType)
    {
    }

    /// <summary>Gets a value indicating whether the font is an instance of a multiple master font (<c>Subtype</c> <c>MMType1</c>).</summary>
    /// <remarks>ISO 32000-2 §9.6.2.3. An embedded program of such an instance is an ordinary Type 1 program, a snapshot of the instance.</remarks>
    public bool IsMultipleMaster => FontType == PdfFontType.MMType1;
}
