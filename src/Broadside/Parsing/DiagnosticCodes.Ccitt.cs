using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for the CCITTFaxDecode filter (issue #63; ISO 32000-2 §7.4.6). Parameters use <c>DecodeParmsInvalid</c>, short data <c>FilterDataTruncated</c>.</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>EndOfLine is true but a line has no EOL before it (Warning).</summary>
    public static string CcittMissingEol => CcittDiagnostics.CcittMissingEol.Name;

    /// <summary>A run or vertical position goes past the last column and is cut there (Warning).</summary>
    public static string CcittRunTooLong => CcittDiagnostics.CcittRunTooLong.Name;

    /// <summary>A damaged row is replaced by the previous row or a white row, as DamagedRowsBeforeError allows (Warning).</summary>
    public static string CcittDamagedRowReplaced => CcittDiagnostics.CcittDamagedRowReplaced.Name;

    /// <summary>A row has an invalid code: kept as far as it decodes, white after; decoding resumes at the next EOL or stops (Error).</summary>
    public static string CcittDataInvalid => CcittDiagnostics.CcittDataInvalid.Name;

    /// <summary>The image's Width differs from Columns: rows are decoded at Columns and cut or padded with white to Width (Warning).</summary>
    public static string CcittWidthMismatch => CcittDiagnostics.CcittWidthMismatch.Name;
}
