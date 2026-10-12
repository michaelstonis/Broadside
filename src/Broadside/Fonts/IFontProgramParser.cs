namespace Broadside.Fonts;

/// <summary>
/// A font program parser: turns the decoded bytes of an embedded font program into a <see cref="FontProgram"/>. The font program
/// parser extension point: every engine holds a list of parsers, the managed defaults after any registered with
/// <see cref="PdfOptions.UseFontProgramParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9. An engine picks the parser for a program in this order: the first parser, newest registration first and the
/// defaults last, whose <see cref="CanParse"/> accepts the bytes; else the first whose <see cref="Formats"/> include the format the
/// font file stream declares (its descriptor key and <c>Subtype</c>, Table 124); else none, with a diagnostic, and the font has no
/// program. A parser accepting bytes of another format than declared is recorded as a diagnostic and used.
/// </para>
/// <para>
/// The parser receives the bytes after the stream's filters and decryption: it never sees <c>Filter</c>, <c>DecodeParms</c> or
/// encryption. It is called once per font program per document; the document caches the result.
/// </para>
/// <para>
/// Thread safety: one instance serves every document of every thread of the engines it is registered with, so it keeps no state
/// between calls. Leniency (ADR 0005): on malformed data a parser repairs what it can, reports each repair through
/// <see cref="FontProgramContext.Report"/>, and returns a program, or <see langword="null"/> when nothing is usable; it does not
/// throw except for the <see cref="Diagnostics.DiagnosticException"/> strict mode raises. Any other exception is caught by the
/// engine and recorded as a diagnostic.
/// </para>
/// </remarks>
public interface IFontProgramParser
{
    /// <summary>Gets the formats the parser reads: an engine falls back to it for a program that declares one of them.</summary>
    IReadOnlyList<FontProgramFormat> Formats { get; }

    /// <summary>Tells, cheaply, whether the bytes are a program this parser reads, from their signature (such as <c>00 01 00 00</c>, <c>OTTO</c>, <c>%!</c>).</summary>
    /// <param name="data">The decoded font program.</param>
    /// <returns><see langword="true"/> when the parser recognizes the bytes.</returns>
    bool CanParse(ReadOnlySpan<byte> data);

    /// <summary>Parses a font program.</summary>
    /// <param name="data">The decoded font program; the returned program may keep a reference to it.</param>
    /// <param name="context">Where the program comes from, the limits, and the diagnostics sink.</param>
    /// <returns>The program, or <see langword="null"/> when nothing in the bytes is usable.</returns>
    FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context);
}
