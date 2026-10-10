using Broadside.Fonts.Cff;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A CIDFontType0: a CIDFont whose glyphs are CFF glyph descriptions, selected by CID.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4.1 and §9.7.4.2 (p.346-347); Adobe Technical Note #5176 §18-19. The metrics are read as for every CIDFont. The
/// program is a <c>FontFile3</c> of subtype <c>/CIDFontType0C</c> (a bare CFF program) or <c>/OpenType</c> (a "CFF " table, whose
/// "cmap" is not used to select glyphs; §9.9, Table 124). Whether the program is CID-keyed is read from its bytes (a Top DICT that
/// begins with ROS), not from the subtype: a CID-keyed program maps a CID to the glyph whose charset entry is that CID, and draws it
/// with the Private DICT and FontMatrix of the Font DICT that FDSelect gives the glyph; any other program uses the CID as the glyph
/// id. A CID the program has no glyph for has "no glyph", and the font shows the glyph of the CMap's notdef mapping, else of CID 0
/// (§9.7.6.3).
/// </para>
/// <para>
/// <c>CIDToGIDMap</c> is defined for CIDFontType2 only (Table 115) and is ignored here. A font without a usable embedded program is
/// drawn with its <see cref="PdfCidFont.Substitute"/>; without one it has no glyphs: <see cref="PdfCidFont.GetGlyphId"/> returns 0.
/// </para>
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
        if (Metrics.Program is not { } program)
        {
            return TryGetSubstituteGlyphId(cid, out glyphId);
        }

        if (program is CffFontProgram { Font.IsCidKeyed: true } cff)
        {
            return cff.Font.TryGetGlyphForCid(cid, out glyphId) && glyphId < program.GlyphCount;
        }

        glyphId = (uint)cid < (uint)program.GlyphCount ? cid : 0;
        return (uint)cid < (uint)program.GlyphCount;
    }
}
