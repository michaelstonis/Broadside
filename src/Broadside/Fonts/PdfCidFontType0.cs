using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A CIDFontType0: a CIDFont whose glyphs are CFF glyph descriptions, selected by CID.</summary>
/// <remarks>
/// ISO 32000-2 §9.7.4.1 and §9.7.4.2. The metrics are read as for every CIDFont. Selecting glyphs needs a CID-keyed CFF program
/// (<c>FontFile3</c> <c>/CIDFontType0C</c>), whose charset maps CIDs to glyph ids; until a parser reads one, every CID has no glyph
/// in the program and <see cref="PdfCidFont.GetGlyphId"/> returns 0.
/// </remarks>
public sealed class PdfCidFontType0 : PdfCidFont
{
    internal PdfCidFontType0(PdfType0Font parent, CosDictionary dictionary, CosReference? reference)
        : base(parent, dictionary, reference, PdfCidFontType.CidFontType0)
    {
    }

    /// <inheritdoc/>
    internal override bool TryGetGlyphId(int cid, out int glyphId)
    {
        glyphId = 0;
        return false;
    }
}
