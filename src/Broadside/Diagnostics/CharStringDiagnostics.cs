namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 1 and Type 2 charstrings.</summary>
/// <remarks>
/// <para>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8. Each code is reported under <see cref="DiagnosticArea.CharString"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CharStringDiagnostics
{
    /// <summary>Gets the <c>FontCharstringStackOverflow</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringStackOverflow { get; } = new(nameof(FontCharstringStackOverflow), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring pushes more operands than the argument stack limit allows. The glyph is dropped.");

    /// <summary>Gets the <c>FontCharstringArgumentCount</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringArgumentCount { get; } = new(nameof(FontCharstringArgumentCount), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring operator has too few or too many arguments. Whole argument groups are used and the rest are dropped.");

    /// <summary>Gets the <c>FontCharstringSubrOutOfRange</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSubrOutOfRange { get; } = new(nameof(FontCharstringSubrOutOfRange), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring subroutine call names a subroutine that does not exist. The call is skipped.");

    /// <summary>Gets the <c>FontCharstringSubrDepth</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSubrDepth { get; } = new(nameof(FontCharstringSubrDepth), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "Charstring subroutines nest deeper than the allowed limit. The glyph is dropped.");

    /// <summary>Gets the <c>FontCharstringBudgetExceeded</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringBudgetExceeded { get; } = new(nameof(FontCharstringBudgetExceeded), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring executes more operators than the configured limit. The glyph is dropped.");

    /// <summary>Gets the <c>FontCharstringUnknownOperator</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringUnknownOperator { get; } = new(nameof(FontCharstringUnknownOperator), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring uses a reserved operator, or a return outside a subroutine. The operator is ignored and the argument stack is cleared.");

    /// <summary>Gets the <c>FontCharstringSeacComponentMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringSeacComponentMissing { get; } = new(nameof(FontCharstringSeacComponentMissing), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A component of an accented character built with seac or a four-argument endchar is missing from the font, or is itself accented. The component is skipped.");

    /// <summary>Gets the <c>FontCharstringNoEndchar</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringNoEndchar { get; } = new(nameof(FontCharstringNoEndchar), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring ends without endchar, or a subroutine ends without return. It is treated as ended at that point.");

    /// <summary>Gets the <c>FontCharstringMovetoMissing</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringMovetoMissing { get; } = new(nameof(FontCharstringMovetoMissing), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring draws a path segment before any moveto. The contour starts at the current point.");

    /// <summary>Gets the <c>FontCharstringOperandInvalid</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringOperandInvalid { get; } = new(nameof(FontCharstringOperandInvalid), DiagnosticArea.CharString, DiagnosticSeverity.Warning, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "An arithmetic or storage operator in a charstring gets an operand it cannot use. A value of 0 is used.");

    /// <summary>Gets the <c>FontCharstringTruncated</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringTruncated { get; } = new(nameof(FontCharstringTruncated), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "A charstring ends in the middle of an operand or a hint mask. The glyph is dropped.");

    /// <summary>Gets the <c>FontCharstringCoordinateOverflow</c> code.</summary>
    /// <remarks>Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8</remarks>
    public static DiagnosticCode FontCharstringCoordinateOverflow { get; } = new(nameof(FontCharstringCoordinateOverflow), DiagnosticArea.CharString, DiagnosticSeverity.Error, "Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8",
        "The arithmetic of a glyph path overflows, so a coordinate is not a finite number. The glyph is dropped.");

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
        FontCharstringCoordinateOverflow,
    ];
}
