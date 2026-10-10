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
}
