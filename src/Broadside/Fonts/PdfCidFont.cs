using Broadside.Diagnostics;
using Broadside.Fonts.Cff;
using Broadside.Fonts.Resolution;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A CIDFont, the descendant of a Type 0 font whose glyphs are selected by CID: a live view over its dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4. A CIDFont is never used directly as a font: it is reached through <see cref="PdfType0Font.DescendantFont"/>.
/// Its glyph metrics are in glyph space units, thousandths of text space (§9.7.4.3), and are always looked up by CID, even when the
/// glyph itself is selected another way (§9.7.4.2).
/// </para>
/// <para>
/// The metrics (<c>DW</c>, <c>W</c>, <c>DW2</c>, <c>W2</c>), the <c>CIDToGIDMap</c> and the program are read on first use and kept
/// until the dictionary, one of its arrays, its descriptor or the map stream changes; a lookup then allocates nothing. Deviations
/// found while reading them are recorded once, on the CIDFont's object (the Type 0 font's when the CIDFont is direct).
/// </para>
/// </remarks>
public abstract class PdfCidFont
{
    private volatile CidFontMetrics? _metrics;
    private volatile SubstituteState? _substitute;
    private PdfFontDescriptor? _descriptor;

    private protected PdfCidFont(PdfType0Font parent, CosDictionary dictionary, CosReference? reference, PdfCidFontType cidFontType)
    {
        Parent = parent;
        Dictionary = dictionary;
        Reference = reference;
        CidFontType = cidFontType;
    }

    /// <summary>Gets the CIDFont dictionary.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.1, Table 115.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the CIDFont dictionary, or <see langword="null"/> when the Type 0 font holds it directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the type of the CIDFont, from <c>Subtype</c>.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.1, Table 115.</remarks>
    public PdfCidFontType CidFontType { get; }

    /// <summary>Gets the PostScript name of the CIDFont (<c>BaseFont</c>), or <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.1, Table 115.</remarks>
    public string? BaseFont => (Get(FontNames.BaseFont) as CosName)?.Value;

    /// <summary>Gets the character collection of the CIDFont (<c>CIDSystemInfo</c>), or <see langword="null"/> when absent or malformed.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, Table 114, and §9.7.4.1, Table 115. Read on every call.</remarks>
    public CidSystemInfo? SystemInfo => ReadSystemInfo(Document, Get(CompositeFontNames.CidSystemInfo));

    /// <summary>Gets the font descriptor, or <see langword="null"/> when absent or not a dictionary.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.1, Table 115 (required), §9.8.1 and §9.8.3.</remarks>
    public PdfFontDescriptor? Descriptor
    {
        get
        {
            if (DescriptorDictionary is not { } dictionary)
            {
                return null;
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

    /// <summary>Gets a value indicating whether the font program is embedded in the descriptor.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120, and §9.9.</remarks>
    public bool IsEmbedded => Metrics.IsEmbedded;

    /// <summary>
    /// Gets the CIDFont's embedded font program, parsed by the engine's font program parsers; <see langword="null"/> when not embedded
    /// or not read (each recorded as a diagnostic).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9 and Table 124: a TrueType program (<c>FontFile2</c>) for a CIDFontType2; it needs no "cmap" table.</remarks>
    /// <exception cref="DiagnosticException">In strict mode, when the program deviates from its format.</exception>
    public FontProgram? Program => Metrics.Program;

    /// <summary>
    /// Gets the font program the CIDFont is drawn with when it has no usable embedded program, found by the engine's font resolvers
    /// (<see cref="PdfOptions.UseFontResolver"/>, the operating system's fonts); <see langword="null"/> when it has a usable embedded
    /// program or no resolver has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.7.4 and ADR 0009, as <see cref="PdfSimpleFont.Substitute"/>: the resolvers are asked for the CIDFont by name,
    /// with its CIDFont type, character collection and descriptor facts (<see cref="FontQuery"/>), then for the most similar Standard
    /// 14 font. A substitute standing in for another font records an information diagnostic naming both; when nothing is found, an
    /// information diagnostic says so (never thrown in strict mode: which fonts a machine has is not a deviation of the file).
    /// </para>
    /// <para>
    /// <see cref="GetGlyphId"/> then selects the substitute's glyph: a CID-keyed CFF substitute by its charset; otherwise the CID's
    /// Unicode value from the collection's CID-to-Unicode table (§9.10.2, <c>Adobe-Japan1-UCS2</c> and the like, through
    /// <see cref="FontResourceKind.CidToUnicode"/>) looked up in the substitute's Unicode "cmap"; and for an
    /// <see cref="FontMatchKind.Exact"/> substitute of a CIDFontType2 without such a table (Adobe-Identity), the CID as the glyph id,
    /// since an Identity CIDFont's CIDs are the glyph ids of the font it was made from. Text keeps the CIDFont's own widths.
    /// </para>
    /// </remarks>
    public FontSubstitute? Substitute => GetSubstituteState()?.Substitute;

    /// <summary>Gets the default width of the CIDFont's glyphs (<c>DW</c>), in glyph space units; 1000 when absent.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.1, Table 115, and §9.7.4.3.</remarks>
    public double DefaultWidth => Metrics.DefaultWidth;

    /// <summary>Gets the PDF document the CIDFont belongs to.</summary>
    internal PdfDocument Document => Parent.Document;

    /// <summary>Gets the Type 0 font the CIDFont descends from.</summary>
    internal PdfType0Font Parent { get; }

    /// <summary>Gets the metrics, rebuilt when an object they come from has changed.</summary>
    internal CidFontMetrics Metrics
    {
        get
        {
            CidFontMetrics? metrics = _metrics;
            if (metrics is null || !metrics.IsCurrent)
            {
                metrics = CidFontMetrics.Build(this);
                _metrics = metrics;
            }

            return metrics;
        }
    }

    /// <summary>Gets the font descriptor dictionary, resolved, or <see langword="null"/>.</summary>
    internal CosDictionary? DescriptorDictionary => Get(FontNames.FontDescriptor) as CosDictionary;

    /// <summary>Gets the width w0 of a CID's glyph, in glyph space units: its <c>W</c> entry, else <c>DW</c>.</summary>
    /// <param name="cid">The CID.</param>
    /// <returns>The width.</returns>
    /// <remarks>ISO 32000-2 §9.7.4.3. Where <c>W</c> gives a CID twice, the first specification is used.</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found in the CIDFont's metrics.</exception>
    public double GetWidth(int cid) => Metrics.GetWidth(cid);

    /// <summary>Gets the vertical metrics of a CID's glyph: its <c>W2</c> entry, else <c>DW2</c> with v.x half the glyph's width.</summary>
    /// <param name="cid">The CID.</param>
    /// <returns>The vertical displacement w1.y and the position vector v, in glyph space units.</returns>
    /// <remarks>ISO 32000-2 §9.7.4.3; §9.9.1: vertical metrics come only from these entries, never from the program's "vhea"/"vmtx".</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found in the CIDFont's metrics.</exception>
    public CidVerticalMetrics GetVerticalMetrics(int cid) => Metrics.GetVerticalMetrics(cid);

    /// <summary>Gets the glyph id a CID selects in <see cref="Program"/>; 0, the CID 0 glyph, when the program has no glyph for it.</summary>
    /// <param name="cid">The CID.</param>
    /// <returns>The glyph id.</returns>
    /// <remarks>ISO 32000-2 §9.7.4.2 and §9.7.6.3.</remarks>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation found in the CIDFont or its program.</exception>
    public int GetGlyphId(int cid) => TryGetGlyphId(cid, out int glyphId) ? glyphId : 0;

    /// <summary>Returns whether the CIDFont has a glyph for a CID, and its glyph id.</summary>
    internal abstract bool TryGetGlyphId(int cid, out int glyphId);

    /// <summary>Returns whether the CIDFont's substitute has a glyph for a CID, and its glyph id there (see <see cref="Substitute"/>).</summary>
    internal bool TryGetSubstituteGlyphId(int cid, out int glyphId)
    {
        glyphId = 0;
        if (GetSubstituteState() is not { Substitute: { } substitute } state)
        {
            return false;
        }

        FontProgram program = substitute.Program;
        if (program is CffFontProgram { Font.IsCidKeyed: true } cff)
        {
            return cff.Font.TryGetGlyphForCid(cid, out glyphId) && glyphId < program.GlyphCount;
        }

        if (state.Ucs2 is { } ucs2)
        {
            Span<char> text = stackalloc char[16];
            if (cid != 0 && ucs2.TryMap((uint)cid, 2, text, out int written, out _) && written > 0
                && System.Text.Rune.DecodeFromUtf16(text[..written], out System.Text.Rune rune, out _) == System.Buffers.OperationStatus.Done
                && rune.Value != FontUnicode.Replacement)
            {
                glyphId = program.CharacterMapSelection.GetGlyphIdForUnicode(rune.Value, out _);
                return glyphId != 0;
            }

            return false;
        }

        if (CidFontType == PdfCidFontType.CidFontType2 && substitute.MatchKind == FontMatchKind.Exact && (uint)cid < (uint)program.GlyphCount)
        {
            glyphId = cid;
            return cid != 0 || program.GlyphCount > 0;
        }

        return false;
    }

    /// <summary>The program of the descriptor's font file (§9.9), parsed once per stream, as for a CIDFont.</summary>
    internal FontProgram? GetProgram(CosDictionary descriptor)
    {
        foreach ((CosName key, FontProgramSource source) in ProgramEntries)
        {
            if (descriptor.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is CosStream stream)
            {
                return Document.GetFontProgram(stream, value as CosReference, source, FaceName);
            }
        }

        return null;
    }

    /// <summary>Gets a value indicating whether a descriptor holds a program.</summary>
    internal bool IsEmbeddedIn(CosDictionary descriptor) => Parent.IsEmbeddedIn(descriptor);

    /// <summary>The CIDFont dictionary's entry, resolved; <see langword="null"/> when absent.</summary>
    internal CosObject? Get(CosName key) => Parent.GetFrom(Dictionary, key);

    /// <summary>Records a deviation found in this CIDFont.</summary>
    internal void Report(string code, DiagnosticSeverity severity, string message) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference ?? Parent.Reference);

    /// <summary>Reads a <c>CIDSystemInfo</c> dictionary (Table 114); <see langword="null"/> unless Registry and Ordering are strings.</summary>
    internal static CidSystemInfo? ReadSystemInfo(PdfDocument document, CosObject? value)
    {
        if (value is not CosDictionary dictionary)
        {
            return null;
        }

        CosObject? registry = dictionary.TryGetValue(CompositeFontNames.Registry, out CosObject? r) ? document.Resolve(r) : null;
        CosObject? ordering = dictionary.TryGetValue(CompositeFontNames.Ordering, out CosObject? o) ? document.Resolve(o) : null;
        CosObject? supplement = dictionary.TryGetValue(CompositeFontNames.Supplement, out CosObject? s) ? document.Resolve(s) : null;
        return registry is CosString registryText && ordering is CosString orderingText
            ? new CidSystemInfo(registryText.DecodeText(), orderingText.DecodeText(), supplement is CosInteger number ? (int)Math.Clamp(number.Value, int.MinValue, int.MaxValue) : 0)
            : null;
    }

    /// <summary>The substitute and the table its CIDs reach Unicode through, rebuilt when the metrics change; <see langword="null"/> with a usable program.</summary>
    private SubstituteState? GetSubstituteState()
    {
        CidFontMetrics metrics = Metrics;
        if (metrics.Program is not null)
        {
            return null;
        }

        SubstituteState? state = _substitute;
        if (state is null || state.Metrics != metrics)
        {
            FontSubstitute? substitute = FontSubstitution.Resolve(this);
            ToUnicodeMap? ucs2 = substitute is not null && Type0FontUnicode.Ucs2Collection(Parent, Parent.State) is { } collection
                ? Document.FindCidToUnicode(Type0FontUnicode.Ucs2TableName(collection))
                : null;
            state = new SubstituteState(metrics, substitute, ucs2);
            _substitute = state;
        }

        return state;
    }

    /// <summary>The font descriptor entries that hold a program, and what each holds (Table 120).</summary>
    private static readonly (CosName Key, FontProgramSource Source)[] ProgramEntries =
    [
        (FontNames.FontFile, FontProgramSource.FontFile),
        (FontNames.FontFile2, FontProgramSource.FontFile2),
        (FontNames.FontFile3, FontProgramSource.FontFile3),
    ];

    /// <summary>A resolved substitute, with the metrics it was resolved for and the CID-to-Unicode table of the font's collection.</summary>
    private sealed record SubstituteState(CidFontMetrics Metrics, FontSubstitute? Substitute, ToUnicodeMap? Ucs2);

    /// <summary>The <c>BaseFont</c> without a subset tag, which picks the font of a font collection.</summary>
    private string? FaceName =>
        BaseFont is { Length: > 7 } name && name[6] == '+' && name.AsSpan(0, 6).ContainsAnyExceptInRange('A', 'Z') is false ? name[7..] : BaseFont;
}
