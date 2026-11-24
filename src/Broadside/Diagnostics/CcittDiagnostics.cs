namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about the CCITTFaxDecode filter.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.4.6. Each code is reported under <see cref="DiagnosticArea.Ccitt"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CcittDiagnostics
{
    /// <summary>EndOfLine is true but a line has no EOL before it.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittMissingEol { get; } = new(nameof(CcittMissingEol), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6",
        "CCITTFaxDecode is set to expect end-of-line codes but the lines have none. The lines are decoded without them.");

    /// <summary>A run or vertical position goes past the last column and is cut there.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittRunTooLong { get; } = new(nameof(CcittRunTooLong), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6",
        "A CCITTFaxDecode run extends past the last column. The run is cut at the column limit.");

    /// <summary>A damaged row is replaced by the previous row or a white row, as DamagedRowsBeforeError allows.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittDamagedRowReplaced { get; } = new(nameof(CcittDamagedRowReplaced), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6",
        "CCITTFaxDecode data has damaged rows within the allowed DamagedRowsBeforeError count. Each damaged row is replaced by the previous row or a white row.");

    /// <summary>A row has an invalid code: kept as far as it decodes, white after; decoding resumes at the next EOL or stops.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittDataInvalid { get; } = new(nameof(CcittDataInvalid), DiagnosticArea.Ccitt, DiagnosticSeverity.Error, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6",
        "CCITTFaxDecode data has an invalid code. The row is kept as far as it decodes and filled with white, and decoding resumes at the next EOL or stops if there is none.");

    /// <summary>The image's Width differs from Columns: rows are decoded at Columns and cut or padded with white to Width.</summary>
    /// <remarks>ISO 32000-2 §7.4.6; ITU-T T.4, T.6</remarks>
    public static DiagnosticCode CcittWidthMismatch { get; } = new(nameof(CcittWidthMismatch), DiagnosticArea.Ccitt, DiagnosticSeverity.Warning, "ISO 32000-2 §7.4.6; ITU-T T.4, T.6",
        "The image width differs from the CCITTFaxDecode Columns value. Rows are decoded at Columns and then padded or cropped to the image width.");

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
