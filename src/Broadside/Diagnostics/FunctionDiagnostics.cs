namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about functions.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.10. Each code is reported under <see cref="DiagnosticArea.Function"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FunctionDiagnostics
{
    /// <summary>Gets the <c>FunctionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionInvalid { get; } = new(nameof(FunctionInvalid), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionEntryInvalid { get; } = new(nameof(FunctionEntryInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionSampleDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionSampleDataTruncated { get; } = new(nameof(FunctionSampleDataTruncated), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionProgramSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionProgramSyntaxInvalid { get; } = new(nameof(FunctionProgramSyntaxInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionProgramStackInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionProgramStackInvalid { get; } = new(nameof(FunctionProgramStackInvalid), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionCycle { get; } = new(nameof(FunctionCycle), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionTooDeep { get; } = new(nameof(FunctionTooDeep), DiagnosticArea.Function, DiagnosticSeverity.Error, "ISO 32000-2 §7.10");

    /// <summary>Gets the <c>FunctionEvaluationRepaired</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.10</remarks>
    public static DiagnosticCode FunctionEvaluationRepaired { get; } = new(nameof(FunctionEvaluationRepaired), DiagnosticArea.Function, DiagnosticSeverity.Warning, "ISO 32000-2 §7.10");

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
