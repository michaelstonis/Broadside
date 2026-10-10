namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CFF font programs and charstrings (issue #51).</summary>
internal static partial class DiagnosticCodes
{
    // The CFF container (Adobe Technical Note #5176).
    public const string FontCffHeaderInvalid = nameof(FontCffHeaderInvalid);
    public const string FontCffIndexInvalid = nameof(FontCffIndexInvalid);
    public const string FontCffDictInvalid = nameof(FontCffDictInvalid);
    public const string FontCffPrivateMissing = nameof(FontCffPrivateMissing);
    public const string FontCffMultipleFonts = nameof(FontCffMultipleFonts);
    public const string FontCffCharsetInvalid = nameof(FontCffCharsetInvalid);
    public const string FontCffEncodingInvalid = nameof(FontCffEncodingInvalid);
    public const string FontCff2Unsupported = nameof(FontCff2Unsupported);

    // Charstrings (Adobe Technical Note #5177; shared with Type 1 charstrings).
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
