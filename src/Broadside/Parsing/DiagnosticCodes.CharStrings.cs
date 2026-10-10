namespace Broadside.Parsing;

/// <summary>Diagnostic codes for charstrings, Type 1 and Type 2 alike (issues #51 and #52), reported through <c>CharStringReporter</c>.</summary>
internal static partial class DiagnosticCodes
{
    // Adobe Technical Note #5177; Type 1 Font Format chapters 6 and 8.
    public const string FontCharstringStackOverflow = nameof(FontCharstringStackOverflow);
    public const string FontCharstringArgumentCount = nameof(FontCharstringArgumentCount);
    public const string FontCharstringSubrOutOfRange = nameof(FontCharstringSubrOutOfRange);
    public const string FontCharstringSubrDepth = nameof(FontCharstringSubrDepth);
    public const string FontCharstringBudgetExceeded = nameof(FontCharstringBudgetExceeded);
    public const string FontCharstringUnknownOperator = nameof(FontCharstringUnknownOperator);
    public const string FontCharstringSeacComponentMissing = nameof(FontCharstringSeacComponentMissing);
    public const string FontCharstringNoEndchar = nameof(FontCharstringNoEndchar);
    public const string FontCharstringMovetoMissing = nameof(FontCharstringMovetoMissing);
    public const string FontCharstringOperandInvalid = nameof(FontCharstringOperandInvalid);
    public const string FontCharstringTruncated = nameof(FontCharstringTruncated);
}
