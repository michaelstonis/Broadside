using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CID-keyed CFF programs (issue #54).</summary>
internal static partial class DiagnosticCodes
{
    // CID-keyed CFF fonts (Adobe Technical Note #5176 §18-19).
    public static string FontCffFdArrayMissing => CffDiagnostics.FontCffFdArrayMissing.Name;
    public static string FontCffFdSelectInvalid => CffDiagnostics.FontCffFdSelectInvalid.Name;
}
