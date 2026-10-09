using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A CIDFontType2: a CIDFont whose glyphs are TrueType glyph descriptions, selected through <c>CIDToGIDMap</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4.1 and §9.7.4.2. With an embedded program, a CID selects the glyph id its <c>CIDToGIDMap</c> gives: the CID
/// itself for <c>/Identity</c>, else bytes 2c and 2c+1 of the map stream (high byte first). A missing map is read as
/// <c>/Identity</c> with a diagnostic. A CID past the end of the map, a glyph id the program does not have, or glyph 0 for a CID
/// other than 0 mean "no glyph", and the font shows the glyph of CID 0 (§9.7.6.3).
/// </para>
/// <para>
/// A program that is not embedded is selected by the predefined CMap's encoding and the font's "cmap" table, which needs the font
/// resolver and the predefined CMaps; until then the CID is used as the glyph id. <c>CIDToGIDMap</c> is ignored then (§9.7.4.2).
/// </para>
/// </remarks>
public sealed class PdfCidFontType2 : PdfCidFont
{
    internal PdfCidFontType2(PdfType0Font parent, CosDictionary dictionary, CosReference? reference)
        : base(parent, dictionary, reference, PdfCidFontType.CidFontType2)
    {
    }

    /// <inheritdoc/>
    internal override bool TryGetGlyphId(int cid, out int glyphId)
    {
        CidFontMetrics metrics = Metrics;
        if (!metrics.IsEmbedded)
        {
            glyphId = cid;
            return true;
        }

        if (metrics.Program is not { } program || !metrics.TryMapGlyph(cid, out glyphId) || glyphId >= program.GlyphCount || (glyphId == 0 && cid != 0))
        {
            glyphId = 0;
            return false;
        }

        return true;
    }
}
