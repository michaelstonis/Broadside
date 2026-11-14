namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 1 and Type 2 charstrings.</summary>
/// <remarks>
/// <para>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8. Each code is reported under <see cref="DiagnosticArea.CharString"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CharStringDiagnostics
{
    /// <summary>Gets the <c>FontCharstringStackOverflow</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringStackOverflow { get; } = new(nameof(FontCharstringStackOverflow), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringArgumentCount</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringArgumentCount { get; } = new(nameof(FontCharstringArgumentCount), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringSubrOutOfRange</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSubrOutOfRange { get; } = new(nameof(FontCharstringSubrOutOfRange), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringSubrDepth</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSubrDepth { get; } = new(nameof(FontCharstringSubrDepth), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringBudgetExceeded</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringBudgetExceeded { get; } = new(nameof(FontCharstringBudgetExceeded), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringUnknownOperator</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringUnknownOperator { get; } = new(nameof(FontCharstringUnknownOperator), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringSeacComponentMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSeacComponentMissing { get; } = new(nameof(FontCharstringSeacComponentMissing), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringNoEndchar</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringNoEndchar { get; } = new(nameof(FontCharstringNoEndchar), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringMovetoMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringMovetoMissing { get; } = new(nameof(FontCharstringMovetoMissing), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringOperandInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringOperandInvalid { get; } = new(nameof(FontCharstringOperandInvalid), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets the <c>FontCharstringTruncated</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringTruncated { get; } = new(nameof(FontCharstringTruncated), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FontCharstringStackOverflow,
        FontCharstringArgumentCount,
        FontCharstringSubrOutOfRange,
        FontCharstringSubrDepth,
        FontCharstringBudgetExceeded,
        FontCharstringUnknownOperator,
        FontCharstringSeacComponentMissing,
        FontCharstringNoEndchar,
        FontCharstringMovetoMissing,
        FontCharstringOperandInvalid,
        FontCharstringTruncated,
    ];
}
