namespace Broadside.Parsing;

/// <summary>Diagnostic codes for the CCITTFaxDecode filter (issue #63; ISO 32000-2 §7.4.6). Parameters use <c>DecodeParmsInvalid</c>, short data <c>FilterDataTruncated</c>.</summary>
internal static partial class DiagnosticCodes
{
    /// <summary>EndOfLine is true but a line has no EOL before it (Warning).</summary>
    public const string CcittMissingEol = nameof(CcittMissingEol);

    /// <summary>A run or vertical position goes past the last column and is cut there (Warning).</summary>
    public const string CcittRunTooLong = nameof(CcittRunTooLong);

    /// <summary>A damaged row is replaced by the previous row or a white row, as DamagedRowsBeforeError allows (Warning).</summary>
    public const string CcittDamagedRowReplaced = nameof(CcittDamagedRowReplaced);

    /// <summary>A row has an invalid code: kept as far as it decodes, white after; decoding resumes at the next EOL or stops (Error).</summary>
    public const string CcittDataInvalid = nameof(CcittDataInvalid);


    /// <summary>The image's Width differs from Columns: rows are decoded at Columns and cut or padded with white to Width (Warning).</summary>
    public const string CcittWidthMismatch = nameof(CcittWidthMismatch);
}
