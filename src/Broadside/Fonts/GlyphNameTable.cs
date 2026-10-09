namespace Broadside.Fonts;

/// <summary>Lookups over the generated glyph name and built-in encoding tables (<c>GlyphNameTable.g.cs</c>).</summary>
internal static partial class GlyphNameTable
{
    /// <summary>The glyph name of a code that has none: every Type 1 font program has a glyph of this name (§9.6.5.2).</summary>
    public const string NotDef = ".notdef";

    /// <summary>Finds the index of <paramref name="name"/> in <see cref="Names"/>.</summary>
    /// <param name="name">A glyph name.</param>
    /// <returns>The index, or -1 when no built-in encoding or Standard 14 font uses the name.</returns>
    public static int IndexOf(string name) => Array.BinarySearch(Names, name, StringComparer.Ordinal) is var index and >= 0 ? index : -1;

    /// <summary>Gets the code to name-index table of a built-in encoding.</summary>
    /// <param name="encoding">The encoding.</param>
    /// <returns>For each code, the index of its name in <see cref="Names"/>, or -1.</returns>
    public static ReadOnlySpan<short> Table(BuiltInEncoding encoding) => encoding switch
    {
        BuiltInEncoding.Standard => StandardEncoding,
        BuiltInEncoding.WinAnsi => WinAnsiEncoding,
        BuiltInEncoding.MacRoman => MacRomanEncoding,
        BuiltInEncoding.MacExpert => MacExpertEncoding,
        BuiltInEncoding.Symbol => SymbolEncoding,
        BuiltInEncoding.ZapfDingbats => ZapfDingbatsEncoding,
        BuiltInEncoding.MacOSRoman => MacOSRomanEncoding,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>Gets the glyph name a built-in encoding gives a code.</summary>
    /// <param name="encoding">The encoding.</param>
    /// <param name="code">The character code.</param>
    /// <returns>The name, or <see langword="null"/> when the encoding leaves the code unused.</returns>
    public static string? NameOf(BuiltInEncoding encoding, byte code) => Table(encoding)[code] is var index and >= 0 ? Names[index] : null;
}

/// <summary>The encodings PDF predefines or uses as a default, and the built-in encodings of the two symbolic Standard 14 fonts.</summary>
/// <remarks>ISO 32000-2 §9.6.5.1 and Annex D.</remarks>
internal enum BuiltInEncoding : byte
{
    /// <summary>StandardEncoding, the Adobe standard Latin encoding (Annex D.2, STD column).</summary>
    Standard,

    /// <summary>WinAnsiEncoding (Annex D.2, WIN column and its notes).</summary>
    WinAnsi,

    /// <summary>MacRomanEncoding (Annex D.2, MAC column and its notes).</summary>
    MacRoman,

    /// <summary>MacExpertEncoding (Annex D.4).</summary>
    MacExpert,

    /// <summary>The built-in encoding of the Symbol font (Annex D.5).</summary>
    Symbol,

    /// <summary>The built-in encoding of the ZapfDingbats font (Annex D.6, plus the codes 0x80 to 0x8D its AFM file encodes).</summary>
    ZapfDingbats,

    /// <summary>Mac OS Roman: MacRomanEncoding with the §9.6.5.4 Table 113 additions, for TrueType "cmap" (1, 0) lookups.</summary>
    MacOSRoman,
}
