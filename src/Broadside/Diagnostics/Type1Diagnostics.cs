namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 1 font programs.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.9; Type 1 Font Format. Each code is reported under <see cref="DiagnosticArea.Type1"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class Type1Diagnostics
{
    /// <summary>Gets the <c>FontType1PfbWrapped</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1PfbWrapped { get; } = new(nameof(FontType1PfbWrapped), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3");

    /// <summary>Gets the <c>FontType1Length1Repaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1Length1Repaired { get; } = new(nameof(FontType1Length1Repaired), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3");

    /// <summary>Gets the <c>FontType1Length2Repaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1Length2Repaired { get; } = new(nameof(FontType1Length2Repaired), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3");

    /// <summary>Gets the <c>FontType1HexEexec</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1HexEexec { get; } = new(nameof(FontType1HexEexec), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3");

    /// <summary>Gets the <c>FontType1NoEexec</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3</remarks>
    public static DiagnosticCode FontType1NoEexec { get; } = new(nameof(FontType1NoEexec), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9 Table 125; Type 1 Font Format §7.2; Adobe Technical Note #5040 §3.3");

    /// <summary>Gets the <c>FontType1FontMatrixInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1FontMatrixInvalid { get; } = new(nameof(FontType1FontMatrixInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1EncodingInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1EncodingInvalid { get; } = new(nameof(FontType1EncodingInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1PrivateMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1PrivateMissing { get; } = new(nameof(FontType1PrivateMissing), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1EntryInvalid</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1EntryInvalid { get; } = new(nameof(FontType1EntryInvalid), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1CharStringsMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1CharStringsMissing { get; } = new(nameof(FontType1CharStringsMissing), DiagnosticArea.Type1, DiagnosticSeverity.Error, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1NotdefMissing</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 2, 5 and 10</remarks>
    public static DiagnosticCode FontType1NotdefMissing { get; } = new(nameof(FontType1NotdefMissing), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 2, 5 and 10");

    /// <summary>Gets the <c>FontType1CharstringTruncated</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1CharstringTruncated { get; } = new(nameof(FontType1CharstringTruncated), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015");

    /// <summary>Gets the <c>FontType1FlexMalformed</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1FlexMalformed { get; } = new(nameof(FontType1FlexMalformed), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015");

    /// <summary>Gets the <c>FontType1BlendUnavailable</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1BlendUnavailable { get; } = new(nameof(FontType1BlendUnavailable), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015");

    /// <summary>Gets the <c>FontType1NoWidth</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1NoWidth { get; } = new(nameof(FontType1NoWidth), DiagnosticArea.Type1, DiagnosticSeverity.Warning, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015");

    /// <summary>Gets the <c>FontType1GlyphTooComplex</c> code.</summary>
    /// <remarks>Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015</remarks>
    public static DiagnosticCode FontType1GlyphTooComplex { get; } = new(nameof(FontType1GlyphTooComplex), DiagnosticArea.Type1, DiagnosticSeverity.Error, "Type 1 Font Format chapters 6 and 8; Adobe Technical Note #5015");

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
