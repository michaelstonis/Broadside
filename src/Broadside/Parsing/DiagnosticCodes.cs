using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>
/// The codes of the diagnostics the file reader and the document model record, grouped by area. Codes are stable: tests, logs and users match on them.
/// </summary>
/// <remarks>
/// Every ticket adds its own group here. The catalogue is internal while parallel tickets add codes; the codes reach users as
/// <see cref="Diagnostics.Diagnostic.Code"/> strings.
/// </remarks>
internal static partial class DiagnosticCodes
{
    // Stream filters (§7.3.8.2 Table 5, §7.4).
    public static string FilterInvalid => FilterDiagnostics.FilterInvalid.Name;
    public static string FilterUnsupported => FilterDiagnostics.FilterUnsupported.Name;
    public static string FilterAbbreviationNotAllowed => FilterDiagnostics.FilterAbbreviationNotAllowed.Name;
    public static string FilterDataInvalid => FilterDiagnostics.FilterDataInvalid.Name;
    public static string FilterDataTruncated => FilterDiagnostics.FilterDataTruncated.Name;
    public static string FilterFailed => FilterDiagnostics.FilterFailed.Name;
    public static string DecodeParmsInvalid => FilterDiagnostics.DecodeParmsInvalid.Name;
    public static string PredictorInvalid => FilterDiagnostics.PredictorInvalid.Name;
    public static string CryptFilterNotFirst => FilterDiagnostics.CryptFilterNotFirst.Name;
    public static string CryptFilterUnsupported => FilterDiagnostics.CryptFilterUnsupported.Name;
    public static string StreamExternalFileUnsupported => FilterDiagnostics.StreamExternalFileUnsupported.Name;
    public static string StreamDecodedLengthExceeded => FilterDiagnostics.StreamDecodedLengthExceeded.Name;
    public static string StreamDecodingTooDeep => FilterDiagnostics.StreamDecodingTooDeep.Name;
}
