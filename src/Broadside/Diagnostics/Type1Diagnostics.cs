namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 1 font programs.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.9; Type 1 Font Format. Each code is reported under <see cref="DiagnosticArea.Type1"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class Type1Diagnostics
{
    /// <summary>Gets the <c>FontType1PfbWrapped</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1PfbWrapped { get; } = new(nameof(FontType1PfbWrapped), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3",
        "The font file stream holds a PFB file instead of the bare Type 1 program. The segment headers are removed.");

    /// <summary>Gets the <c>FontType1Length1Repaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1Length1Repaired { get; } = new(nameof(FontType1Length1Repaired), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3",
        "The font file stream's Length1 does not match where the clear text ends. The clear text is taken up to the eexec keyword instead.");

    /// <summary>Gets the <c>FontType1Length2Repaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1Length2Repaired { get; } = new(nameof(FontType1Length2Repaired), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3",
        "The font file stream's Length2 does not match the amount of encrypted data after the clear text. All of the following bytes are decrypted.");

    /// <summary>Gets the <c>FontType1HexEexec</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1HexEexec { get; } = new(nameof(FontType1HexEexec), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3",
        "The encrypted portion of the Type 1 program is hexadecimal text rather than binary. It is decoded anyway.");

    /// <summary>Gets the <c>FontType1NoEexec</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1NoEexec { get; } = new(nameof(FontType1NoEexec), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3",
        "The Type 1 program has no currentfile eexec marker. The whole program is read as clear text.");

    /// <summary>Gets the <c>FontType1FontMatrixInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1FontMatrixInvalid { get; } = new(nameof(FontType1FontMatrixInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10",
        "The Type 1 program's FontMatrix is not an array of six numbers. The default matrix of 0.001 scale is used.");

    /// <summary>Gets the <c>FontType1EncodingInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1EncodingInvalid { get; } = new(nameof(FontType1EncodingInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10",
        "The Type 1 program's Encoding is a named encoding that is not read, or has an entry with an invalid code. StandardEncoding is used or the entry is ignored.");

    /// <summary>Gets the <c>FontType1PrivateMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1PrivateMissing { get; } = new(nameof(FontType1PrivateMissing), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10",
        "The Type 1 program has no Private dictionary. Its CharStrings are read with the default lenIV of 4.");

    /// <summary>Gets the <c>FontType1EntryInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1EntryInvalid { get; } = new(nameof(FontType1EntryInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10",
        "The private dictionary of the Type 1 program has entries that deviate from the font format. The affected entries are ignored or defaulted.");

    /// <summary>Gets the <c>FontType1CharStringsMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1CharStringsMissing { get; } = new(nameof(FontType1CharStringsMissing), DiagnosticArea.Type1, DiagnosticSeverity.Error, "Type 1 Font Format chapters 2, 5 and 10",
        "The Type 1 program has no CharStrings dictionary. None of its glyphs are available.");

    /// <summary>Gets the <c>FontType1NotdefMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1NotdefMissing { get; } = new(nameof(FontType1NotdefMissing), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10",
        "The Type 1 CharStrings dictionary has no .notdef glyph. An empty glyph stands in for it.");

    /// <summary>Gets the <c>FontType1CharstringTruncated</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1CharstringTruncated { get; } = new(nameof(FontType1CharstringTruncated), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015",
        "A Type 1 charstring is shorter than its lenIV random prefix bytes. It is treated as empty.");

    /// <summary>Gets the <c>FontType1FlexMalformed</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1FlexMalformed { get; } = new(nameof(FontType1FlexMalformed), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015",
        "A Type 1 flex does not record seven points between OtherSubrs 1 and 0. The stray point or the whole flex is dropped.");

    /// <summary>Gets the <c>FontType1BlendUnavailable</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1BlendUnavailable { get; } = new(nameof(FontType1BlendUnavailable), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015",
        "A multiple master blend in a Type 1 charstring has no matching WeightVector. The first master's values are used.");

    /// <summary>Gets the <c>FontType1NoWidth</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1NoWidth { get; } = new(nameof(FontType1NoWidth), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015",
        "A Type 1 charstring does not start with hsbw or sbw. Its side bearing and width are taken as 0.");

    /// <summary>Gets the <c>FontType1GlyphTooComplex</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1GlyphTooComplex { get; } = new(nameof(FontType1GlyphTooComplex), DiagnosticArea.Type1, DiagnosticSeverity.Error, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015",
        "A Type 1 glyph has more points than the configured limit, or its coordinates overflow. The glyph is dropped.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FontType1PfbWrapped,
        FontType1Length1Repaired,
        FontType1Length2Repaired,
        FontType1HexEexec,
        FontType1NoEexec,
        FontType1FontMatrixInvalid,
        FontType1EncodingInvalid,
        FontType1PrivateMissing,
        FontType1EntryInvalid,
        FontType1CharStringsMissing,
        FontType1NotdefMissing,
        FontType1CharstringTruncated,
        FontType1FlexMalformed,
        FontType1BlendUnavailable,
        FontType1NoWidth,
        FontType1GlyphTooComplex,
    ];
}
