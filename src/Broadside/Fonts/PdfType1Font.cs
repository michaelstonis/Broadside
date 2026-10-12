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

    /// <summary>Gets the glyph id that a character code selects in the font's program; 0, the missing glyph (<c>.notdef</c>), when none.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>
    /// The glyph id in <see cref="PdfFont.Program"/>, or, for a font without a usable embedded program, in the program of
    /// <see cref="PdfSimpleFont.Substitute"/>; 0 for every code when the font has neither.
    /// </returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the font or its program.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.6.5.2: the code's glyph name from the font's encoding (<see cref="PdfSimpleFont.GetGlyphName"/>, whose base is
    /// the program's built-in encoding for an embedded font) is looked up among the program's glyph names; a name the program does not
    /// have selects <c>.notdef</c>.
    /// </remarks>
    public int GetGlyphId(byte code)
    {
        if (Program is not { } program)
        {
            return GetSubstituteGlyphId(code);
        }

        string name = GetGlyphName(code);
        return program.TryGetGlyphId(name, out int glyphId) ? glyphId : 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The advance of the glyph of that name in the embedded program (Type 1 Font Format §6.4: <c>hsbw</c> or <c>sbw</c>; a CFF
    /// program's width), mapped to thousandths of text space through the program's font matrix. None for a font without an embedded
    /// program or a name the program lacks.
    /// </remarks>
    internal override double? GetGlyphWidth(string glyphName, List<CosObject> sources)
    {
        if (Program is not { } program || !program.TryGetGlyphId(glyphName, out int glyph) || glyph == 0)
        {
            return null;
        }

        return program.FontMatrix.TransformVector(program.GetMetrics(glyph).AdvanceWidth, 0).X * 1000;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §9.6.5.2: the embedded program's <c>/Encoding</c> (<see cref="FontProgram.BuiltInEncoding"/>).</remarks>
    internal override string?[]? GetProgramEncoding() =>
        Program?.BuiltInEncoding is { Count: 256 } encoding ? [.. encoding] : null;
}
