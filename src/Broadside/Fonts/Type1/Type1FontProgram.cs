using Broadside.Graphics;

namespace Broadside.Fonts.Type1;

/// <summary>
/// A parsed Type 1 program: glyph names, the built-in encoding, the font matrix and bounding box, and every charstring and
/// subroutine decrypted into one buffer at parse time, so glyph outlines are interpreted without allocating.
/// </summary>
/// <remarks>
/// Adobe Type 1 Font Format; TN 5015 (multiple master blends, seac errata). ISO 32000-2 §9.6.2, §9.6.5.2, §9.9. Glyph ids are
/// positions in <c>CharStrings</c>, <c>.notdef</c> first. Immutable and safe for concurrent use.
/// </remarks>
internal sealed class Type1FontProgram : FontProgram
{
    private static readonly Matrix DefaultFontMatrix = new(0.001, 0, 0, 0.001, 0, 0);

    private readonly string[] _names;
    private readonly Dictionary<string, int> _ids;
    private readonly string[] _encoding;
    private readonly int[] _standardGlyphs;

    private Type1FontProgram(Type1ProgramReader reader, string[] names, (int Start, int Length)[] glyphs, (int Start, int Length)[] subrs, byte[] pool, FontProgramContext context)
    {
        _names = names;
        Glyphs = glyphs;
        Subrs = subrs;
        Pool = pool;
        Context = context;
        _encoding = reader.Encoding;
        WeightVector = reader.WeightVector;
        PostScriptName = reader.FontName;
        FontMatrix = reader.FontMatrix is [var a, var b, var c, var d, var e, var f] && IsUsable(a, b, c, d, e, f)
            ? new Matrix(a, b, c, d, e, f)
            : DefaultFontMatrix;
        FontBBox = reader.FontBBox is [var x1, var y1, var x2, var y2] ? new PdfRectangle(x1, y1, x2, y2) : default;

        _ids = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int glyph = 0; glyph < names.Length; glyph++)
        {
            _ids.TryAdd(names[glyph], glyph);
        }

        // seac names its components by StandardEncoding codes (Type 1 Font Format §6.4, Appendix 3), whatever the font's encoding.
        _standardGlyphs = new int[256];
        ReadOnlySpan<short> standard = GlyphNameTable.Table(Fonts.BuiltInEncoding.Standard);
        for (int code = 0; code < 256; code++)
        {
            _standardGlyphs[code] = standard[code] >= 0 && _ids.TryGetValue(GlyphNameTable.Names[standard[code]], out int glyph) ? glyph : -1;
        }
    }

    /// <inheritdoc/>
    public override FontProgramFormat Format => FontProgramFormat.Type1;

    /// <inheritdoc/>
    public override int GlyphCount => _names.Length;

    /// <inheritdoc/>
    /// <remarks>Type 1 Font Format §2.3, <c>/FontMatrix</c>; <c>[0.001 0 0 0.001 0 0]</c> when absent or unusable.</remarks>
    public override Matrix FontMatrix { get; }

    /// <inheritdoc/>
    /// <remarks>Type 1 Font Format §2.3, <c>/FontName</c>.</remarks>
    public override string? PostScriptName { get; }

    /// <inheritdoc/>
    /// <remarks>Type 1 Font Format §2.3, <c>/FontBBox</c>.</remarks>
    public override PdfRectangle FontBBox { get; }

    /// <inheritdoc/>
    /// <remarks>Type 1 Font Format §2.3, <c>/Encoding</c>; ISO 32000-2 §9.6.5.2.</remarks>
    public override IReadOnlyList<string> BuiltInEncoding => _encoding;

    /// <summary>Gets the decrypted charstrings and subroutines.</summary>
    internal byte[] Pool { get; }

    /// <summary>Gets each glyph's charstring in <see cref="Pool"/>.</summary>
    internal (int Start, int Length)[] Glyphs { get; }

    /// <summary>Gets each subroutine's charstring in <see cref="Pool"/>; a length of −1 for a missing one.</summary>
    internal (int Start, int Length)[] Subrs { get; }

    /// <summary>Gets the multiple master weight vector, or <see langword="null"/>.</summary>
    internal double[]? WeightVector { get; }

    /// <summary>Gets the context diagnostics are reported to.</summary>
    internal FontProgramContext Context { get; }

    /// <summary>Builds a program from a reader, or returns <see langword="null"/> when it found no glyphs.</summary>
    internal static Type1FontProgram? Create(Type1ProgramReader reader, ReadOnlySpan<byte> plain, FontProgramContext context) =>
        reader.TryBuild(plain, out string[] names, out (int, int)[] glyphs, out (int, int)[] subrs, out byte[] pool)
            ? new Type1FontProgram(reader, names, glyphs, subrs, pool, context)
            : null;

    /// <summary>The glyph a StandardEncoding code names, or −1.</summary>
    internal int StandardGlyph(int code) => code is >= 0 and < 256 ? _standardGlyphs[code] : -1;

    /// <inheritdoc/>
    /// <remarks>
    /// Type 1 Font Format chapters 6 and 8: the charstring is interpreted with its subroutines, flex and hint replacement through
    /// OtherSubrs 0-3, multiple master blends through OtherSubrs 14-18 (TN 5015 §3.13), and <c>seac</c> accented characters. Hints
    /// are ignored. A charstring that cannot be finished (truncated, a missing subroutine, too deep or too large) is dropped with a
    /// diagnostic; smaller deviations are repaired with one.
    /// </remarks>
    public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        outline.Clear();
        if ((uint)glyphId >= (uint)_names.Length)
        {
            return GlyphOutlineStatus.Invalid;
        }

        var interpreter = new Type1CharstringInterpreter(this, outline, metricsOnly: false);
        if (!interpreter.Run(glyphId))
        {
            outline.Clear();
            return GlyphOutlineStatus.Invalid;
        }

        return outline.IsEmpty ? GlyphOutlineStatus.Empty : GlyphOutlineStatus.Complete;
    }

    /// <inheritdoc/>
    /// <remarks>Type 1 Font Format §6.4: <c>hsbw</c> or <c>sbw</c>, the first command of every charstring.</remarks>
    public override GlyphMetrics GetMetrics(int glyphId)
    {
        if ((uint)glyphId >= (uint)_names.Length)
        {
            return default;
        }

        var interpreter = new Type1CharstringInterpreter(this, null, metricsOnly: true);
        return interpreter.Run(glyphId) ? interpreter.Metrics : default;
    }

    /// <inheritdoc/>
    public override bool TryGetGlyphId(string glyphName, out int glyphId)
    {
        ArgumentNullException.ThrowIfNull(glyphName);
        return _ids.TryGetValue(glyphName, out glyphId) || (glyphId = 0) != 0;
    }

    /// <inheritdoc/>
    public override string? GetGlyphName(int glyphId) => (uint)glyphId < (uint)_names.Length ? _names[glyphId] : null;

    private static bool IsUsable(double a, double b, double c, double d, double e, double f) =>
        double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c) && double.IsFinite(d) && double.IsFinite(e) && double.IsFinite(f)
        && (a * d) - (b * c) != 0;
}
