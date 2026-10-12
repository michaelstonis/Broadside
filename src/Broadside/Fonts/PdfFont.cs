using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>A font: a live view over a font dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.5. The type of the view follows the dictionary's <c>Subtype</c> (Table 108): <see cref="PdfType1Font"/> for
/// <c>Type1</c> and <c>MMType1</c>, <see cref="PdfTrueTypeFont"/>, <see cref="PdfType3Font"/> and <see cref="PdfType0Font"/>. A
/// dictionary whose <c>Subtype</c> is missing or not a font type is read as a Type 1 font, with a diagnostic.
/// </para>
/// <para>
/// Views are obtained from <see cref="PdfDocument.GetFont(CosObject)"/> or <see cref="PdfPage.GetFont(string)"/>, which return the
/// same instance for the same dictionary. Properties read the COS objects when called; values derived from several entries (the
/// encoding and widths of a simple font) are cached and rebuilt when one of the dictionaries or arrays they come from changes. A
/// font is safe for concurrent reads while nobody mutates the document.
/// </para>
/// </remarks>
public abstract class PdfFont
{
    private PdfFontDescriptor? _descriptor;
    private int _unicodeReports;

    private protected PdfFont(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfFontType fontType)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        FontType = fontType;
    }

    /// <summary>Gets the font dictionary.</summary>
    /// <remarks>ISO 32000-2 §9.5; Table 109 for Type 1 fonts.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the font dictionary, or <see langword="null"/> when the resource dictionary holds it directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the type of the font, from <c>Subtype</c>; <see cref="PdfFontType.Type1"/> when that is missing or not a font type.</summary>
    /// <remarks>ISO 32000-2 §9.5, Table 108.</remarks>
    public PdfFontType FontType { get; }

    /// <summary>Gets the PostScript name of the font, or <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.1, Table 109, <c>BaseFont</c> (required except for Type 3 fonts); §9.6.3 for TrueType fonts.</remarks>
    public string? BaseFont => (Get(FontNames.BaseFont) as CosName)?.Value;

    /// <summary>
    /// Gets the font descriptor: a view over the dictionary's <c>FontDescriptor</c>, or, for a non-embedded Standard 14 font without
    /// one, a descriptor synthesized from the font's metrics; <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8 and §9.6.2.1 (the paragraph after Table 109).</remarks>
    public PdfFontDescriptor? Descriptor
    {
        get
        {
            if (DescriptorDictionary is not { } dictionary)
            {
                return SynthesizedDescriptor;
            }

            PdfFontDescriptor? cached = _descriptor;
            if (cached?.Dictionary != dictionary)
            {
                cached = new PdfFontDescriptor(Document, dictionary, Dictionary.TryGetValue(FontNames.FontDescriptor, out CosObject? value) ? value as CosReference : null);
                _descriptor = cached;
            }

            return cached;
        }
    }

    /// <summary>Gets a value indicating whether the font program is embedded: the font descriptor has a <c>FontFile</c>, <c>FontFile2</c> or <c>FontFile3</c> stream.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, and §9.9.</remarks>
    public bool IsEmbedded => DescriptorDictionary is { } descriptor && IsEmbeddedIn(descriptor);

    /// <summary>
    /// Gets the font's embedded font program, parsed by the engine's font program parsers; <see langword="null"/> when the font is
    /// not embedded, no parser reads the program's format, or the program is unusable (each recorded as a diagnostic).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.9: the program in the font descriptor's <c>FontFile</c>, <c>FontFile2</c> or <c>FontFile3</c> stream, decoded
    /// through its filters. The parser is picked by the program's bytes, then by the format the stream declares (Table 124); see
    /// <see cref="IFontProgramParser"/>. The managed defaults read TrueType and Type 1 programs.
    /// </para>
    /// <para>
    /// The program is parsed once per font file stream and shared by every font and thread that uses it; it is parsed again only
    /// after the stream changes. Selecting a glyph for a character code is the font's job (§9.6.5): for a TrueType font,
    /// <see cref="PdfTrueTypeFont.GetGlyphId"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="DiagnosticException">In strict mode, when the program deviates from its format.</exception>
    public FontProgram? Program => DescriptorDictionary is { } descriptor ? GetProgram(descriptor, isCidFont: false) : null;

    /// <summary>Gets the document the font belongs to.</summary>
    internal PdfDocument Document { get; }

    /// <summary>Gets the font descriptor dictionary, resolved, or <see langword="null"/> when absent or not a dictionary.</summary>
    internal CosDictionary? DescriptorDictionary => Get(FontNames.FontDescriptor) as CosDictionary;

    /// <summary>Gets the descriptor a font without a <c>FontDescriptor</c> entry is given; <see langword="null"/> unless a Standard 14 font.</summary>
    private protected virtual PdfFontDescriptor? SynthesizedDescriptor => null;

    /// <summary>Creates the view for a font dictionary, recording a diagnostic when its <c>Type</c> or <c>Subtype</c> is not a font's.</summary>
    /// <param name="document">The document.</param>
    /// <param name="dictionary">The font dictionary.</param>
    /// <param name="reference">The reference it was reached through, if any.</param>
    /// <returns>The view.</returns>
    internal static PdfFont Create(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        DiagnosticSink diagnostics = document.DiagnosticSink;
        CosObject? type = dictionary.TryGetValue(KnownNames.Type, out CosObject? typeValue) ? document.Resolve(typeValue) : null;
        if (!FontNames.Font.Equals(type))
        {
            diagnostics.Report(
                DiagnosticCodes.FontTypeInvalid,
                DiagnosticSeverity.Warning,
                "A font dictionary's Type shall be Font (ISO 32000-2 §9.6.2.1, Table 109); read as a font.",
                objectReference: reference);
        }

        CosName? subtype = (dictionary.TryGetValue(FontNames.Subtype, out CosObject? subtypeValue) ? document.Resolve(subtypeValue) : null) as CosName;
        if (FontNames.Type1.Equals(subtype))
        {
            return new PdfType1Font(document, dictionary, reference, PdfFontType.Type1);
        }

        if (FontNames.MMType1.Equals(subtype))
        {
            return new PdfType1Font(document, dictionary, reference, PdfFontType.MMType1);
        }

        if (FontNames.TrueType.Equals(subtype))
        {
            return new PdfTrueTypeFont(document, dictionary, reference);
        }

        if (FontNames.Type3.Equals(subtype))
        {
            return new PdfType3Font(document, dictionary, reference);
        }

        if (FontNames.Type0.Equals(subtype))
        {
            return new PdfType0Font(document, dictionary, reference);
        }

        diagnostics.Report(
            DiagnosticCodes.FontSubtypeInvalid,
            DiagnosticSeverity.Warning,
            $"A font dictionary's Subtype shall be one of the font types of ISO 32000-2 §9.5 Table 108, not {(subtype is null ? "absent" : "/" + subtype.Value)}; read as a Type 1 font.",
            objectReference: reference);
        return new PdfType1Font(document, dictionary, reference, PdfFontType.Type1);
    }

    /// <summary>Gets a value indicating whether a font descriptor holds a font program.</summary>
    internal bool IsEmbeddedIn(CosDictionary descriptor) =>
        GetFrom(descriptor, FontNames.FontFile) is CosStream
        || GetFrom(descriptor, FontNames.FontFile2) is CosStream
        || GetFrom(descriptor, FontNames.FontFile3) is CosStream;

    /// <summary>The program of a font descriptor's first font file stream, parsed once per stream (§9.9).</summary>
    /// <param name="descriptor">The font descriptor dictionary.</param>
    /// <param name="isCidFont">Whether the descriptor belongs to a CIDFont.</param>
    /// <returns>The program, or <see langword="null"/>.</returns>
    internal FontProgram? GetProgram(CosDictionary descriptor, bool isCidFont)
    {
        foreach ((CosName key, FontProgramSource source) in ProgramEntries)
        {
            if (descriptor.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is CosStream stream)
            {
                return Document.GetFontProgram(stream, value as CosReference, source, isCidFont, this);
            }
        }

        return null;
    }

    /// <summary>The font descriptor entries that hold a program, and what each holds (Table 120).</summary>
    private static readonly (CosName Key, FontProgramSource Source)[] ProgramEntries =
    [
        (FontNames.FontFile, FontProgramSource.FontFile),
        (FontNames.FontFile2, FontProgramSource.FontFile2),
        (FontNames.FontFile3, FontProgramSource.FontFile3),
    ];

    /// <summary>The <c>BaseFont</c> without a subset tag (six uppercase letters and a plus sign, §9.9.2).</summary>
    internal string? FaceName =>
        BaseFont is { Length: > 7 } name && name[6] == '+' && name.AsSpan(0, 6).ContainsAnyExceptInRange('A', 'Z') is false ? name[7..] : BaseFont;

    /// <summary>The font dictionary's entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    internal CosObject? Get(CosName key) => GetFrom(Dictionary, key);

    /// <summary>A dictionary's entry, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    internal CosObject? GetFrom(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>The font matrix of every font but Type 3: glyph space is a thousandth of text space (§9.2.4).</summary>
    internal static readonly Matrix ThousandthMatrix = new(0.001, 0, 0, 0.001, 0, 0);

    /// <summary>
    /// Gets the matrix from glyph space to text space: a thousandth for every font type but Type 3, whose <c>FontMatrix</c> it is.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.2.4 and §9.6.4 (Table 110, <c>FontMatrix</c>).</remarks>
    internal virtual Matrix GlyphSpaceMatrix => ThousandthMatrix;

    /// <summary>Gets a value indicating whether the font writes vertically (writing mode 1, §9.7.4.3); only composite fonts can.</summary>
    internal virtual bool IsVertical => false;

    /// <summary>
    /// Reads the first character code of <paramref name="text"/> (a non-empty rest of a string a text-showing operator shows) and the
    /// metrics of its glyph: the content interpreter's font contract (issue #56). Allocation-free once the font's tables are built.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.4.3 and §9.2.4: one byte per code for a simple font, whose width maps from glyph space to text space; a
    /// composite font's CMap decides the code length and its CIDFont the metrics (§9.7.4.3, §9.7.6.2).
    /// </remarks>
    internal virtual ShownGlyph ReadShownGlyph(ReadOnlySpan<byte> text)
    {
        byte code = text[0];
        return new ShownGlyph(code, 1, GetHorizontalDisplacement(code), 0, default, code == 32);
    }

    /// <summary>Returns the glyph's horizontal displacement w0 for a one-byte code, in text space units (before the font size).</summary>
    /// <remarks>ISO 32000-2 §9.2.4: the glyph width mapped from glyph space to text space.</remarks>
    internal virtual double GetHorizontalDisplacement(byte code) => 0;

    /// <summary>Records a deviation found in this font.</summary>
    internal void Report(string code, DiagnosticSeverity severity, string message) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference);

    /// <summary>Maps a character code to the Unicode text it stands for.</summary>
    /// <param name="code">
    /// The code: for a simple font one byte (<see cref="CharacterCode.Length"/> 1); for a Type 0 font as its CMap reads it
    /// (<see cref="PdfType0Font.ReadGlyph"/>, <see cref="CMap.ReadCode"/>), an invalid code selecting CID 0.
    /// </param>
    /// <param name="destination">
    /// Where the UTF-16 text goes. One code can stand for several characters (a ligature, a surrogate pair, a decomposed sequence);
    /// 512 units always suffice, and text that does not fit is cut at a character boundary.
    /// </param>
    /// <param name="source">Which method mapped the code; <see cref="UnicodeSource.Unmapped"/> when none did.</param>
    /// <returns>The number of UTF-16 units written: 0 for a code a ToUnicode CMap maps to an empty string.</returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.10.2, in its priority order, per code (a ToUnicode CMap that lacks a code falls through to the next method):
    /// the font's ToUnicode CMap (§9.10.3; a code is looked up under its own length, then by value under the other lengths); for a
    /// simple font, the glyph name its encoding gives the code (§9.6.5) through the Adobe Glyph List specification's algorithm; for
    /// a Type 0 font whose CMap is a predefined CMap other than Identity-H/V, or whose CIDFont uses the Adobe-GB1, -CNS1,
    /// -Japan1, -Korea1 or -KR collection, the code's CID through the collection's Registry-Ordering-UCS2 table (from the engine's
    /// font resolvers: the Broadside.Fonts.Cmaps package). When all fail, the glyph's code point in a Unicode "cmap" subtable of
    /// the embedded TrueType program, as PDFBox does. Otherwise the text is U+FFFD, never the code itself.
    /// </para>
    /// <para>
    /// An unmapped code is recorded once per font as an information diagnostic (§9.10.2 allows it; strict mode does not throw), or,
    /// when the UCS2 table is missing, that is recorded instead. A malformed ToUnicode CMap is recorded when first used and throws in
    /// strict mode. The tables are built on first use and kept until the font's dictionaries or ToUnicode stream change; a lookup
    /// then allocates nothing. Safe for concurrent use.
    /// </para>
    /// </remarks>
    /// <exception cref="DiagnosticException">In strict mode, for a deviation in the font, its encoding or its ToUnicode CMap.</exception>
    public int GetUnicode(CharacterCode code, Span<char> destination, out UnicodeSource source) => MapUnicode(code, destination, out source);

    /// <summary>Maps a character code to the Unicode text it stands for, as a new string.</summary>
    /// <param name="code">The code; see <see cref="GetUnicode(CharacterCode, Span{char}, out UnicodeSource)"/>.</param>
    /// <returns>The text; U+FFFD when nothing maps the code.</returns>
    /// <remarks>ISO 32000-2 §9.10.2; see <see cref="GetUnicode(CharacterCode, Span{char}, out UnicodeSource)"/>, which allocates nothing.</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for a deviation in the font, its encoding or its ToUnicode CMap.</exception>
    public string GetUnicode(CharacterCode code)
    {
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        int written = MapUnicode(code, buffer, out _);
        return new string(buffer[..written]);
    }

    /// <summary>Maps a code to Unicode (§9.10.2): the font kind's part of <see cref="GetUnicode(CharacterCode, Span{char}, out UnicodeSource)"/>.</summary>
    internal abstract int MapUnicode(CharacterCode code, Span<char> destination, out UnicodeSource source);

    /// <summary>Reads the first character code of a non-empty shown string, as <see cref="ReadShownGlyph"/> does, and maps it to Unicode.</summary>
    internal virtual int ReadUnicode(ReadOnlySpan<byte> text, Span<char> destination, out UnicodeSource source) =>
        MapUnicode(new CharacterCode(text[0], 1, IsValid: true), destination, out source);

    /// <summary>Whether a once-per-font Unicode diagnostic is still to be recorded (strict mode records Warnings every time, so they throw).</summary>
    internal bool NeedsUnicodeReport(int report, DiagnosticSeverity severity) =>
        (Volatile.Read(ref _unicodeReports) & report) == 0 || (severity != DiagnosticSeverity.Information && Document.DiagnosticSink.IsStrict);

    /// <summary>Records a once-per-font Unicode diagnostic (<see cref="FontUnicodeReports"/>).</summary>
    internal void ReportUnicode(int report, string code, DiagnosticSeverity severity, string message)
    {
        Interlocked.Or(ref _unicodeReports, report);
        Report(code, severity, message);
    }

    /// <summary>Records, once per font, that a shown code maps to no Unicode value (§9.10.2, last paragraph).</summary>
    internal void ReportUnmapped(uint code, int length)
    {
        if (NeedsUnicodeReport(FontUnicodeReports.Unmapped, DiagnosticSeverity.Information))
        {
            ReportUnicode(
                FontUnicodeReports.Unmapped,
                DiagnosticCodes.TextUnicodeUnmapped,
                DiagnosticSeverity.Information,
                $"No method of ISO 32000-2 §9.10.2 maps code 0x{code.ToString(length == 1 ? "X2" : "X" + (2 * Math.Clamp(length, 1, 4)), System.Globalization.CultureInfo.InvariantCulture)} of the font to Unicode (no ToUnicode entry for it, no usable glyph name or character collection); its text is U+FFFD. Further codes of this font are not reported.");
        }
    }
}
