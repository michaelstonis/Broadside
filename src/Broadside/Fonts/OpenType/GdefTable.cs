using static Broadside.Fonts.OpenType.LayoutData;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The parts of a font's <c>GDEF</c> table that glyph substitution reads: the glyph class of each glyph (base, ligature, mark,
/// component), the mark attachment classes and the mark glyph sets that a lookup's flags filter marks by.
/// </summary>
/// <remarks>OpenType specification, "GDEF — Glyph Definition Table" (versions 1.0, 1.2 and 1.3).</remarks>
internal sealed class GdefTable
{
    /// <summary>Glyph class 1: a base glyph (single character, spacing glyph).</summary>
    public const int BaseGlyph = 1;

    /// <summary>Glyph class 2: a ligature glyph (multiple characters, spacing glyph).</summary>
    public const int LigatureGlyph = 2;

    /// <summary>Glyph class 3: a mark glyph (non-spacing combining glyph).</summary>
    public const int MarkGlyph = 3;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _glyphClassDef;
    private readonly int _markAttachClassDef;
    private readonly int _markGlyphSets;

    private GdefTable(ReadOnlyMemory<byte> data, int glyphClassDef, int markAttachClassDef, int markGlyphSets)
    {
        _data = data;
        _glyphClassDef = glyphClassDef;
        _markAttachClassDef = markAttachClassDef;
        _markGlyphSets = markGlyphSets;
    }

    /// <summary>Gets a value indicating whether the table classifies glyphs (when it does not, classes come from the characters).</summary>
    public bool HasGlyphClasses => _glyphClassDef >= 0;

    /// <summary>Reads the table, or returns <see langword="null"/> when it is not a GDEF table of a known major version.</summary>
    public static GdefTable? Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (U16(span, 0) != 1 || span.Length < 12)
        {
            return null;
        }

        int minor = U16(span, 2);
        int markGlyphSets = minor >= 2 && span.Length >= 14 ? Offset16(span, 0, 12) : -1;
        return new GdefTable(data, Offset16(span, 0, 4), Offset16(span, 0, 10), markGlyphSets);
    }

    /// <summary>The glyph's class (1 base, 2 ligature, 3 mark, 4 component), or 0 when the table does not classify it.</summary>
    public int GlyphClass(int glyph) => ClassOf(_data.Span, _glyphClassDef, glyph);

    /// <summary>The glyph's mark attachment class, 0 when it has none.</summary>
    public int MarkAttachClass(int glyph) => ClassOf(_data.Span, _markAttachClassDef, glyph);

    /// <summary>Whether <paramref name="glyph"/> is in mark glyph set <paramref name="set"/>; false for a set the table does not have.</summary>
    public bool InMarkGlyphSet(int set, int glyph)
    {
        ReadOnlySpan<byte> span = _data.Span;
        if (_markGlyphSets < 0 || U16(span, _markGlyphSets) != 1 || set >= U16(span, _markGlyphSets + 2))
        {
            return false;
        }

        uint relative = U32(span, _markGlyphSets + 4 + (set * 4));
        long coverage = _markGlyphSets + (long)relative;
        return relative != 0 && coverage < span.Length && CoverageIndex(span, (int)coverage, glyph) >= 0;
    }
}
