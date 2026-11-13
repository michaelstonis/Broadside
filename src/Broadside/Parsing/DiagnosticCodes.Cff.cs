using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CFF font programs and charstrings (issue #51).</summary>
internal static partial class DiagnosticCodes
{
    // The CFF container (Adobe Technical Note #5176).
    public static string FontCffHeaderInvalid => CffDiagnostics.FontCffHeaderInvalid.Name;
    public static string FontCffIndexInvalid => CffDiagnostics.FontCffIndexInvalid.Name;
    public static string FontCffDictInvalid => CffDiagnostics.FontCffDictInvalid.Name;
    public static string FontCffPrivateMissing => CffDiagnostics.FontCffPrivateMissing.Name;
    public static string FontCffMultipleFonts => CffDiagnostics.FontCffMultipleFonts.Name;
    public static string FontCffCharsetInvalid => CffDiagnostics.FontCffCharsetInvalid.Name;
    public static string FontCffEncodingInvalid => CffDiagnostics.FontCffEncodingInvalid.Name;
    public static string FontCff2Unsupported => CffDiagnostics.FontCff2Unsupported.Name;
}
