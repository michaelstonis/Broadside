namespace Broadside.Fonts;

/// <summary>The characteristics a font descriptor's <c>Flags</c> entry records.</summary>
/// <remarks>
/// ISO 32000-2 §9.8.2, Table 121. Bit positions are numbered from 1 (low-order); each value here is the bit's value. Bits not listed
/// are reserved. <see cref="Symbolic"/> and <see cref="Nonsymbolic"/> are meant to be exclusive; when both or neither are set, a
/// processor checks <see cref="Symbolic"/> (§9.8.2).
/// </remarks>
[Flags]
public enum PdfFontFlags
{
    /// <summary>No flag is set.</summary>
    None = 0,

    /// <summary>Bit 1: all glyphs have the same width.</summary>
    FixedPitch = 1 << 0,

    /// <summary>Bit 2: glyphs have serifs.</summary>
    Serif = 1 << 1,

    /// <summary>Bit 3: the font contains glyphs outside the Standard Latin character set (Annex D.2).</summary>
    Symbolic = 1 << 2,

    /// <summary>Bit 4: glyphs resemble cursive handwriting.</summary>
    Script = 1 << 3,

    /// <summary>Bit 6: the font uses the Standard Latin character set or a subset of it.</summary>
    Nonsymbolic = 1 << 5,

    /// <summary>Bit 7: glyphs have dominant vertical strokes that are slanted.</summary>
    Italic = 1 << 6,

    /// <summary>Bit 17: the font contains no lowercase letters.</summary>
    AllCap = 1 << 16,

    /// <summary>Bit 18: lowercase letters are small capitals.</summary>
    SmallCap = 1 << 17,

    /// <summary>Bit 19: bold glyphs are thickened at small text sizes.</summary>
    ForceBold = 1 << 18,
}
