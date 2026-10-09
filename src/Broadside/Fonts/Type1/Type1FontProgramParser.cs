namespace Broadside.Fonts.Type1;

/// <summary>
/// The managed default parser for Type 1 programs: <c>FontFile</c> streams in the layout ISO 32000-2 describes (clear text, binary
/// eexec portion, fixed portion), and the layouts producers also embed: hexadecimal eexec text (PFA) and whole PFB files.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2 and §9.9 (Tables 124 and 125); Adobe Type 1 Font Format (program layout chapter 2, private dictionary
/// chapter 5, charstrings chapter 6, encryption chapter 7, subroutines and OtherSubrs chapter 8, parsing rules chapter 10); TN 5015
/// (multiple master OtherSubrs, errata); TN 5040 §3.3 (PFB segments). <c>FontFile3</c> streams of subtype <c>Type1C</c> are CFF
/// programs, read by the CFF parser.
/// </para>
/// <para>
/// The program exposes glyph names (<c>CharStrings</c>), the built-in encoding (<c>/Encoding</c>, ISO 32000-2 §9.6.5.2), the font
/// matrix and bounding box, advances from <c>hsbw</c>/<c>sbw</c>, and cubic outlines. Charstrings are decrypted once when the
/// program is parsed; an outline is then interpreted without allocating. Hints are ignored, and <c>PaintType</c> too (ISO 32000-2
/// §9.9: the text rendering mode decides between filling and stroking).
/// </para>
/// <para>
/// Leniency follows ADR 0005: wrong <c>Length1</c>/<c>Length2</c>, hexadecimal eexec, PFB wrapping, malformed entries and
/// charstring errors are repaired with a diagnostic; only a program without <c>CharStrings</c> is unusable.
/// </para>
/// </remarks>
public sealed class Type1FontProgramParser : IFontProgramParser
{
    /// <inheritdoc/>
    public IReadOnlyList<FontProgramFormat> Formats { get; } = [FontProgramFormat.Type1];

    /// <inheritdoc/>
    /// <remarks>Accepts a program starting with <c>%!</c> (Type 1 Font Format §2.4) and a PFB file (<c>0x80 0x01</c>, TN 5040 §3.3).</remarks>
    public bool CanParse(ReadOnlySpan<byte> data) => data.StartsWith("%!"u8) || (data.Length >= 6 && data[0] == 0x80 && data[1] == 1);

    /// <inheritdoc/>
    public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Type1Layout.Split(data, context, out ReadOnlyMemory<byte> clear, out byte[]? plain);
        Type1ProgramReader reader = Type1ProgramReader.Read(clear.Span, plain, context);
        return Type1FontProgram.Create(reader, plain ?? clear.Span, context);
    }
}
