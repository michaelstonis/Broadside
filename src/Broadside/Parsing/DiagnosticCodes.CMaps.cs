using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CMaps, Type 0 fonts and CIDFonts (issue #53).</summary>
internal static partial class DiagnosticCodes
{
    // CMap files (§9.7.5; Adobe TN 5014 §7).
    public static string CMapSyntaxInvalid => CMapDiagnostics.CMapSyntaxInvalid.Name;
    public static string CMapEntryInvalid => CMapDiagnostics.CMapEntryInvalid.Name;
    public static string CMapEntryLimitExceeded => CMapDiagnostics.CMapEntryLimitExceeded.Name;
    public static string CMapCodespaceMissing => CMapDiagnostics.CMapCodespaceMissing.Name;
    public static string CMapOperatorNotAllowed => CMapDiagnostics.CMapOperatorNotAllowed.Name;
    public static string CMapUseCMapInvalid => CMapDiagnostics.CMapUseCMapInvalid.Name;
    public static string CMapUseCMapCycle => CMapDiagnostics.CMapUseCMapCycle.Name;
    public static string CMapUseCMapTooDeep => CMapDiagnostics.CMapUseCMapTooDeep.Name;
    public static string CMapWritingModeMismatch => CMapDiagnostics.CMapWritingModeMismatch.Name;
    public static string CMapUnavailable => CMapDiagnostics.CMapUnavailable.Name;

    // Showing text in a composite font (§9.7.6.2, §9.7.6.3).
    public static string CMapCodeInvalid => CMapDiagnostics.CMapCodeInvalid.Name;

    // Type 0 font and CIDFont dictionaries (§9.7.3, §9.7.4, §9.7.6.1).
    public static string Type0EncodingMissing => FontDiagnostics.Type0EncodingMissing.Name;
    public static string Type0DescendantFontsInvalid => FontDiagnostics.Type0DescendantFontsInvalid.Name;
    public static string Type0IdentityNotEmbedded => FontDiagnostics.Type0IdentityNotEmbedded.Name;
    public static string CidSystemInfoInvalid => FontDiagnostics.CidSystemInfoInvalid.Name;
    public static string CidSystemInfoMismatch => FontDiagnostics.CidSystemInfoMismatch.Name;
    public static string CidFontSubtypeInvalid => FontDiagnostics.CidFontSubtypeInvalid.Name;
    public static string CidFontWidthsInvalid => FontDiagnostics.CidFontWidthsInvalid.Name;
    public static string CidFontVerticalMetricsInvalid => FontDiagnostics.CidFontVerticalMetricsInvalid.Name;
    public static string CidToGidMapMissing => FontDiagnostics.CidToGidMapMissing.Name;
    public static string CidToGidMapInvalid => FontDiagnostics.CidToGidMapInvalid.Name;
}
