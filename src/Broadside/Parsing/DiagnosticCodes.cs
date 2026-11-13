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
    // Content streams (§7.8.2, §8.2, §8.4, §8.5; issue #55). Reported once per code per content stream (DiagnosticSink.ReportOnce),
    // on the stream's object (or the page's when the stream is direct), with the part and decoded offset in the message.
    public static string ContentStreamInvalid => ContentDiagnostics.ContentStreamInvalid.Name;
    public static string ContentUnknownOperator => ContentDiagnostics.ContentUnknownOperator.Name;
    public static string ContentOperandCount => ContentDiagnostics.ContentOperandCount.Name;
    public static string ContentOperandType => ContentDiagnostics.ContentOperandType.Name;
    public static string ContentOperandOverflow => ContentDiagnostics.ContentOperandOverflow.Name;
    public static string ContentSyntaxInvalid => ContentDiagnostics.ContentSyntaxInvalid.Name;
    public static string ContentGluedTokens => ContentDiagnostics.ContentGluedTokens.Name;
    public static string ContentOperatorOutOfContext => ContentDiagnostics.ContentOperatorOutOfContext.Name;
    public static string ContentStackUnderflow => ContentDiagnostics.ContentStackUnderflow.Name;
    public static string ContentStackOverflow => ContentDiagnostics.ContentStackOverflow.Name;
    public static string ContentUnbalancedSave => ContentDiagnostics.ContentUnbalancedSave.Name;
    public static string ContentNoCurrentPoint => ContentDiagnostics.ContentNoCurrentPoint.Name;
    public static string ContentNoCurrentPath => ContentDiagnostics.ContentNoCurrentPath.Name;
    public static string ContentPathNotPainted => ContentDiagnostics.ContentPathNotPainted.Name;
    public static string ContentGraphicsStateRange => ContentDiagnostics.ContentGraphicsStateRange.Name;
    public static string ContentCompatibilityUnbalanced => ContentDiagnostics.ContentCompatibilityUnbalanced.Name;
    public static string ContentTextObjectUnbalanced => ContentDiagnostics.ContentTextObjectUnbalanced.Name;
    public static string ContentInlineImageInvalid => ContentDiagnostics.ContentInlineImageInvalid.Name;

    // Functions (§7.10, issue #78).
    public static string FunctionInvalid => FunctionDiagnostics.FunctionInvalid.Name;
    public static string FunctionEntryInvalid => FunctionDiagnostics.FunctionEntryInvalid.Name;
    public static string FunctionSampleDataTruncated => FunctionDiagnostics.FunctionSampleDataTruncated.Name;
    public static string FunctionProgramSyntaxInvalid => FunctionDiagnostics.FunctionProgramSyntaxInvalid.Name;
    public static string FunctionProgramStackInvalid => FunctionDiagnostics.FunctionProgramStackInvalid.Name;
    public static string FunctionCycle => FunctionDiagnostics.FunctionCycle.Name;
    public static string FunctionTooDeep => FunctionDiagnostics.FunctionTooDeep.Name;
    public static string FunctionEvaluationRepaired => FunctionDiagnostics.FunctionEvaluationRepaired.Name;
}
