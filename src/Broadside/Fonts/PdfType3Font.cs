using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A Type 3 font, whose glyphs are content streams: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.4. Its encoding is entirely defined by its <c>Encoding</c> entry (§9.6.5.3): the <c>Differences</c> of the
/// encoding dictionary, over the predefined encoding its <c>BaseEncoding</c> names, if any. Its widths are in its glyph space,
/// which the font matrix maps to text space; it never takes Standard 14 metrics.
/// </remarks>
public sealed class PdfType3Font : PdfSimpleFont
{
    internal PdfType3Font(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.Type3)
    {
    }

    /// <inheritdoc/>
    internal override bool CanUseStandard14 => false;
}
