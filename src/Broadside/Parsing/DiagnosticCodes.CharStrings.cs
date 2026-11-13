using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for charstrings, Type 1 and Type 2 alike (issues #51 and #52), reported through <c>CharStringReporter</c>.</summary>
internal static partial class DiagnosticCodes
{
    // Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8.
    public static string FontCharstringStackOverflow => CharStringDiagnostics.FontCharstringStackOverflow.Name;
    public static string FontCharstringArgumentCount => CharStringDiagnostics.FontCharstringArgumentCount.Name;
    public static string FontCharstringSubrOutOfRange => CharStringDiagnostics.FontCharstringSubrOutOfRange.Name;
    public static string FontCharstringSubrDepth => CharStringDiagnostics.FontCharstringSubrDepth.Name;
    public static string FontCharstringBudgetExceeded => CharStringDiagnostics.FontCharstringBudgetExceeded.Name;
    public static string FontCharstringUnknownOperator => CharStringDiagnostics.FontCharstringUnknownOperator.Name;
    public static string FontCharstringSeacComponentMissing => CharStringDiagnostics.FontCharstringSeacComponentMissing.Name;
    public static string FontCharstringNoEndchar => CharStringDiagnostics.FontCharstringNoEndchar.Name;
    public static string FontCharstringMovetoMissing => CharStringDiagnostics.FontCharstringMovetoMissing.Name;
    public static string FontCharstringOperandInvalid => CharStringDiagnostics.FontCharstringOperandInvalid.Name;
    public static string FontCharstringTruncated => CharStringDiagnostics.FontCharstringTruncated.Name;
}
