namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the CCITTFaxDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.6. Each code is reported under <see cref="DiagnosticArea.Ccitt"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CcittDiagnostics
{
    /// <summary>EndOfLine is true but a line has no EOL before it.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittMissingEol { get; } = new(nameof(CcittMissingEol), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6");

    /// <summary>A run or vertical position goes past the last column and is cut there.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittRunTooLong { get; } = new(nameof(CcittRunTooLong), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6");

    /// <summary>A damaged row is replaced by the previous row or a white row, as DamagedRowsBeforeError allows.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittDamagedRowReplaced { get; } = new(nameof(CcittDamagedRowReplaced), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6");

    /// <summary>A row has an invalid code: kept as far as it decodes, white after; decoding resumes at the next EOL or stops.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittDataInvalid { get; } = new(nameof(CcittDataInvalid), DiagnosticArea.Ccitt, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6");

    /// <summary>The image's Width differs from Columns: rows are decoded at Columns and cut or padded with white to Width.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittWidthMismatch { get; } = new(nameof(CcittWidthMismatch), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        CcittMissingEol,
        CcittRunTooLong,
        CcittDamagedRowReplaced,
        CcittDataInvalid,
        CcittWidthMismatch,
    ];
}
