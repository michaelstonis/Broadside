namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about mapping character codes to Unicode.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.10. Each code is reported under <see cref="DiagnosticArea.Text"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class TextDiagnostics
{
    /// <summary>Gets the <c>ToUnicodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeInvalid { get; } = new(nameof(ToUnicodeInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3");

    /// <summary>Gets the <c>ToUnicodeIdentityName</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeIdentityName { get; } = new(nameof(ToUnicodeIdentityName), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3");

    /// <summary>Gets the <c>ToUnicodeDestinationInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeDestinationInvalid { get; } = new(nameof(ToUnicodeDestinationInvalid), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3");

    /// <summary>Gets the <c>ToUnicodeBfRangeOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeBfRangeOverflow { get; } = new(nameof(ToUnicodeBfRangeOverflow), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3");

    /// <summary>Gets the <c>ToUnicodeCidMapping</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode ToUnicodeCidMapping { get; } = new(nameof(ToUnicodeCidMapping), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3");

    /// <summary>Gets the <c>ToUnicodeCodeLengthMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode ToUnicodeCodeLengthMismatch { get; } = new(nameof(ToUnicodeCodeLengthMismatch), DiagnosticArea.Text, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.2");

    /// <summary>Gets the <c>GlyphNameNonStandard</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode GlyphNameNonStandard { get; } = new(nameof(GlyphNameNonStandard), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2");

    /// <summary>Gets the <c>TextUcs2CmapMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode TextUcs2CmapMissing { get; } = new(nameof(TextUcs2CmapMissing), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2");

    /// <summary>Gets the <c>TextUnicodeUnmapped</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode TextUnicodeUnmapped { get; } = new(nameof(TextUnicodeUnmapped), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2");

    /// <summary>Gets the <c>CidToUnicodeResourceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.10.2</remarks>
    public static DiagnosticCode CidToUnicodeResourceInvalid { get; } = new(nameof(CidToUnicodeResourceInvalid), DiagnosticArea.Text, DiagnosticSeverity.Information, "ISO 32000-2 §9.10.2");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ToUnicodeInvalid,
        ToUnicodeIdentityName,
        ToUnicodeDestinationInvalid,
        ToUnicodeBfRangeOverflow,
        ToUnicodeCidMapping,
        ToUnicodeCodeLengthMismatch,
        GlyphNameNonStandard,
        TextUcs2CmapMissing,
        TextUnicodeUnmapped,
        CidToUnicodeResourceInvalid,
    ];
}
