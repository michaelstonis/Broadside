using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>The font-wide metrics and attributes of a simple font or CIDFont: a live view over a font descriptor dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.8.1, Table 120, and §9.8.2. Every property reads the dictionary when called, so a change made through
/// <see cref="Dictionary"/> is visible at once. An absent or malformed entry reads as the default Table 120 gives (0 for the optional
/// numbers), or as <see langword="null"/> for entries without one; the font that uses the descriptor records a diagnostic for a
/// malformed or missing required entry when it is first used. Dimensions are in glyph space units: thousandths of text space for
/// every font type except Type 3 (§9.2.4).
/// </para>
/// <para>
/// A non-embedded Standard 14 font may have no font descriptor (§9.6.2.1, the paragraph after Table 109); a processor shall then
/// provide the descriptor data itself. <see cref="PdfFont.Descriptor"/> returns a synthesized descriptor for such a font, with
/// <see cref="Dictionary"/> <see langword="null"/> and values from the font's AFM file: FontBBox, ItalicAngle, CapHeight, XHeight,
/// Ascender (Ascent), Descender (Descent), StdVW (StemV) and StdHW (StemH). Symbol and ZapfDingbats have no CapHeight, XHeight,
/// Ascender or Descender in their AFM files; their font box supplies Ascent, Descent and CapHeight, and XHeight is 0.
/// </para>
/// </remarks>
public sealed class PdfFontDescriptor
{
    private readonly PdfDocument? _document;
    private readonly Standard14Font _standard14;

    internal PdfFontDescriptor(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    internal PdfFontDescriptor(Standard14Font standard14) => _standard14 = standard14;

    /// <summary>Gets the font descriptor dictionary, or <see langword="null"/> for the descriptor of a Standard 14 font that has none.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public CosDictionary? Dictionary { get; }

    /// <summary>Gets the indirect reference to the font descriptor, or <see langword="null"/> when it is a direct object or synthesized.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets a value indicating whether the descriptor is synthesized from Standard 14 metrics instead of read from the file.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.1 (the paragraph after Table 109) and §9.6.2.2.</remarks>
    public bool IsSynthesized => Dictionary is null;

    /// <summary>Gets the PostScript name of the font, which shall equal the font's <c>BaseFont</c>; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontName</c> (required).</remarks>
    public string? FontName => Dictionary is null ? Standard14Data.PostScriptName(_standard14) : (Get(FontNames.FontName) as CosName)?.Value;

    /// <summary>Gets the preferred font family name, such as <c>Times</c> for Times Bold Italic; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontFamily</c> (PDF 1.5), a byte string.</remarks>
    public string? FontFamily => (Get(FontNames.FontFamily) as CosString)?.DecodeText();

    /// <summary>Gets the font stretch; <see langword="null"/> when absent or not one of the nine names.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontStretch</c> (PDF 1.5).</remarks>
    public PdfFontStretch? FontStretch => (Get(FontNames.FontStretch) as CosName)?.Value switch
    {
        "UltraCondensed" => PdfFontStretch.UltraCondensed,
        "ExtraCondensed" => PdfFontStretch.ExtraCondensed,
        "Condensed" => PdfFontStretch.Condensed,
        "SemiCondensed" => PdfFontStretch.SemiCondensed,
        "Normal" => PdfFontStretch.Normal,
        "SemiExpanded" => PdfFontStretch.SemiExpanded,
        "Expanded" => PdfFontStretch.Expanded,
        "ExtraExpanded" => PdfFontStretch.ExtraExpanded,
        "UltraExpanded" => PdfFontStretch.UltraExpanded,
        _ => null,
    };

    /// <summary>Gets the weight: 100 to 900 in steps of 100, 400 normal and 700 bold; <see langword="null"/> when absent or another value.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontWeight</c> (PDF 1.5).</remarks>
    public int? FontWeight => Get(FontNames.FontWeight) is CosNumber number && number.ToDouble() is var weight
        && weight is >= 100 and <= 900 && weight % 100 == 0 ? (int)weight : null;

    /// <summary>Gets the font flags; none when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>Flags</c> (required), and §9.8.2, Table 121.</remarks>
    public PdfFontFlags Flags => Dictionary is null ? Standard14Data.Flags(_standard14) : ReadFlags(Dictionary, _document!);

    /// <summary>Gets the font bounding box in glyph space; empty at the origin when absent or malformed.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontBBox</c> (required except for Type 3 fonts), and §7.9.5.</remarks>
    public PdfRectangle FontBBox => Dictionary is null ? Standard14Data.FontBBox(_standard14) : ReadRectangle(Get(FontNames.FontBBox));

    /// <summary>Gets the angle of the dominant vertical strokes, in degrees counterclockwise from the vertical (negative for right-slanting italics).</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>ItalicAngle</c> (required). 0 when absent.</remarks>
    public double ItalicAngle => Read(FontNames.ItalicAngle, Standard14Metric.ItalicAngle);

    /// <summary>Gets the maximum height above the baseline reached by glyphs, excluding accents.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>Ascent</c> (required except for Type 3 fonts). 0 when absent.</remarks>
    public double Ascent => Read(FontNames.Ascent, Standard14Metric.Ascent);

    /// <summary>Gets the maximum depth below the baseline reached by glyphs; a negative number.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>Descent</c> (required except for Type 3 fonts). 0 when absent.</remarks>
    public double Descent => Read(FontNames.Descent, Standard14Metric.Descent);

    /// <summary>Gets the spacing between baselines of consecutive lines.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>Leading</c>. Default 0.</remarks>
    public double Leading => Read(FontNames.Leading, metric: null);

    /// <summary>Gets the height of flat capital letters above the baseline.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>CapHeight</c> (required for fonts with Latin characters, except Type 3). 0 when absent.</remarks>
    public double CapHeight => Read(FontNames.CapHeight, Standard14Metric.CapHeight);

    /// <summary>Gets the height of flat non-ascending lowercase letters above the baseline.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>XHeight</c>. Default 0.</remarks>
    public double XHeight => Read(FontNames.XHeight, Standard14Metric.XHeight);

    /// <summary>Gets the horizontal thickness of the dominant vertical stems; 0 when unknown.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>StemV</c> (required except for Type 3 fonts). 0 when absent.</remarks>
    public double StemV => Read(FontNames.StemV, Standard14Metric.StemV);

    /// <summary>Gets the vertical thickness of the dominant horizontal stems; 0 when unknown.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>StemH</c>. Default 0.</remarks>
    public double StemH => Read(FontNames.StemH, Standard14Metric.StemH);

    /// <summary>Gets the average glyph width.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>AvgWidth</c>. Default 0.</remarks>
    public double AvgWidth => Read(FontNames.AvgWidth, metric: null);

    /// <summary>Gets the maximum glyph width.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>MaxWidth</c>. Default 0.</remarks>
    public double MaxWidth => Read(FontNames.MaxWidth, metric: null);

    /// <summary>Gets the width of character codes outside the font's <c>FirstChar</c> to <c>LastChar</c> range.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>MissingWidth</c>, and §9.6.2.1, Table 109, <c>Widths</c>. Default 0.</remarks>
    public double MissingWidth => Read(FontNames.MissingWidth, metric: null);

    /// <summary>Gets the embedded Type 1 font program, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontFile</c>, and §9.9.</remarks>
    public CosStream? FontFile => Get(FontNames.FontFile) as CosStream;

    /// <summary>Gets the embedded TrueType font program, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontFile2</c> (PDF 1.1), and §9.9.</remarks>
    public CosStream? FontFile2 => Get(FontNames.FontFile2) as CosStream;

    /// <summary>Gets the embedded font program whose format the stream's <c>Subtype</c> names, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontFile3</c> (PDF 1.2), and §9.9, Table 124.</remarks>
    public CosStream? FontFile3 => Get(FontNames.FontFile3) as CosStream;

    /// <summary>Gets the names of the glyphs of a Type 1 font subset, each preceded by a slash; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>CharSet</c> (PDF 1.1; deprecated in PDF 2.0).</remarks>
    public string? CharSet => (Get(FontNames.CharSet) as CosString)?.DecodeText();

    /// <summary>
    /// Gets the 12-byte Panose classification of a CIDFont's glyphs, from the <c>Panose</c> entry of its <c>Style</c> dictionary: the
    /// font family class and subclass of the "OS/2" table, then the ten PANOSE bytes; <see langword="null"/> when absent or not 12 bytes.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8.3.1, Table 122 (<c>Style</c>), and §9.8.3.2.</remarks>
    public ReadOnlyMemory<byte>? Panose =>
        Get(FontNames.Style) is CosDictionary style && Resolve(style, FontNames.Panose) is CosString { Bytes.Length: 12 } panose
            ? new ReadOnlyMemory<byte>(panose.Bytes.ToArray())
            : default(ReadOnlyMemory<byte>?);

    /// <summary>Gets the language of a CIDFont, a BCP 47 language tag such as <c>ja</c>; <see langword="null"/> when absent or not a name.</summary>
    /// <remarks>
    /// ISO 32000-2 §9.8.3.1, Table 122, <c>Lang</c> (PDF 1.5). Its absence says nothing about the language of the document.
    /// </remarks>
    public string? Language => (Get(FontNames.Lang) as CosName)?.Value;

    /// <summary>
    /// Gets the font descriptors that override this one's metrics for classes of a CIDFont's glyphs, by class name (such as
    /// <c>Proportional</c> or <c>HKana</c>, Table 123); empty when there are none. Entries that are not dictionaries, or that name a
    /// font file, are left out.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.8.3.1, Table 122 (<c>FD</c>), and §9.8.3.3: each value holds metric entries only, overriding this descriptor's for
    /// that class. A new dictionary of live views is built on each call.
    /// </remarks>
    public IReadOnlyDictionary<string, PdfFontDescriptor> ClassDescriptors
    {
        get
        {
            var classes = new Dictionary<string, PdfFontDescriptor>(StringComparer.Ordinal);
            if (Get(FontNames.FD) is CosDictionary fd)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in fd)
                {
                    if (_document!.Resolve(entry.Value) is CosDictionary dictionary && !NamesFontFile(dictionary))
                    {
                        classes[entry.Key.Value] = new PdfFontDescriptor(_document, dictionary, entry.Value as CosReference);
                    }
                }
            }

            return classes;
        }
    }

    /// <summary>Gets the stream that lists which CIDs a CIDFont subset has, or <see langword="null"/> when absent or not a stream.</summary>
    /// <remarks>ISO 32000-2 §9.8.3.1, Table 122, <c>CIDSet</c> (deprecated in PDF 2.0); <see cref="ContainsCid"/> reads it.</remarks>
    public CosStream? CidSet => Get(FontNames.CidSet) as CosStream;

    /// <summary>Returns whether a CIDFont subset's <c>CIDSet</c> lists a CID; <see langword="null"/> when there is no <c>CIDSet</c>.</summary>
    /// <param name="cid">The CID.</param>
    /// <returns>Whether the CID's bit is set; <see langword="false"/> for a CID past the end of the table.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.8.3.1, Table 122: a table of bits indexed by CID, high-order bit first, the most significant bit of the first
    /// byte for CID 0. The stream is decoded on each call.
    /// </remarks>
    public bool? ContainsCid(int cid)
    {
        if (CidSet is not { } stream)
        {
            return null;
        }

        ReadOnlySpan<byte> bits = _document!.DecodeStream(stream).Span;
        return cid >= 0 && cid / 8 < bits.Length && (bits[cid / 8] & (0x80 >> (cid % 8))) != 0;
    }

    /// <summary>Whether a descriptor names a font file (FontFile, FontFile2 or FontFile3).</summary>
    internal static bool NamesFontFile(CosDictionary descriptor) =>
        descriptor.ContainsKey(FontNames.FontFile) || descriptor.ContainsKey(FontNames.FontFile2) || descriptor.ContainsKey(FontNames.FontFile3);

    /// <summary>Reads <c>Flags</c> from a descriptor dictionary: the low 32 bits of an integer, none otherwise.</summary>
    internal static PdfFontFlags ReadFlags(CosDictionary dictionary, PdfDocument document) =>
        dictionary.TryGetValue(FontNames.Flags, out CosObject? value) && document.Resolve(value) is CosInteger flags
            ? (PdfFontFlags)unchecked((int)flags.Value)
            : PdfFontFlags.None;

    /// <summary>Reads a rectangle: an array of four numbers, normalized; empty at the origin otherwise.</summary>
    private PdfRectangle ReadRectangle(CosObject? value)
    {
        if (value is not CosArray { Count: 4 } array)
        {
            return default;
        }

        Span<double> corners = stackalloc double[4];
        for (int index = 0; index < 4; index++)
        {
            if (_document!.Resolve(array[index]) is not CosNumber number)
            {
                return default;
            }

            corners[index] = number.ToDouble();
        }

        return new PdfRectangle(corners[0], corners[1], corners[2], corners[3]);
    }

    private double Read(CosName key, Standard14Metric? metric)
    {
        if (Dictionary is null)
        {
            return metric is { } value ? Standard14Data.Metric(_standard14, value) : 0;
        }

        return Get(key) is CosNumber number ? number.ToDouble() : 0;
    }

    private CosObject? Get(CosName key) => Dictionary is null ? null : Resolve(Dictionary, key);

    private CosObject? Resolve(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && _document!.Resolve(value) is not CosNull and var resolved ? resolved : null;
}
