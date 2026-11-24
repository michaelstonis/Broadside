namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about functions.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.10. Each code is reported under <see cref="DiagnosticArea.Function"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FunctionDiagnostics
{
    /// <summary>Gets the <c>FunctionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionInvalid { get; } = new(nameof(FunctionInvalid), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10",
        "A function dictionary or stream is unusable, for example it has the wrong type, missing required entries, or is an identity where a real function is required. The function is not used.");

    /// <summary>Gets the <c>FunctionEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionEntryInvalid { get; } = new(nameof(FunctionEntryInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10",
        "An entry of a function dictionary has an invalid value, such as a malformed Domain, Range or Bounds array. The entry is repaired with a usable default.");

    /// <summary>Gets the <c>FunctionSampleDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionSampleDataTruncated { get; } = new(nameof(FunctionSampleDataTruncated), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10",
        "A sampled function has fewer sample bytes than its Size, BitsPerSample and output count require. The missing samples are treated as zero.");

    /// <summary>Gets the <c>FunctionProgramSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionProgramSyntaxInvalid { get; } = new(nameof(FunctionProgramSyntaxInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10",
        "A PostScript calculator function has a syntax problem, such as a missing opening or closing brace, text after the closing brace, or an exponent number. The program is read as if it were well formed.");

    /// <summary>Gets the <c>FunctionProgramStackInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionProgramStackInvalid { get; } = new(nameof(FunctionProgramStackInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10",
        "A PostScript calculator function has a stack problem, such as taking more values than the stack holds, needing more entries than readers provide, or leaving a different number of values than Range has outputs.");

    /// <summary>Gets the <c>FunctionCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionCycle { get; } = new(nameof(FunctionCycle), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10",
        "A stitching function contains itself, directly or through other functions. The cyclic function is not used.");

    /// <summary>Gets the <c>FunctionTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionTooDeep { get; } = new(nameof(FunctionTooDeep), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10",
        "Functions are nested deeper than the supported depth limit. The innermost function is not used.");

    /// <summary>Gets the <c>FunctionEvaluationRepaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionEvaluationRepaired { get; } = new(nameof(FunctionEvaluationRepaired), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10",
        "A function met an error while evaluating, so its outputs were repaired, for example clamped or replaced with defaults. The error is reported once per function.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FunctionInvalid,
        FunctionEntryInvalid,
        FunctionSampleDataTruncated,
        FunctionProgramSyntaxInvalid,
        FunctionProgramStackInvalid,
        FunctionCycle,
        FunctionTooDeep,
        FunctionEvaluationRepaired,
    ];
}
