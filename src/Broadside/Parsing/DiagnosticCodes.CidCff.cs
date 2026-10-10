namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CID-keyed CFF programs (issue #54).</summary>
internal static partial class DiagnosticCodes
{
    // CID-keyed CFF fonts (Adobe Technical Note #5176 §18-19).
    public const string FontCffFdArrayMissing = nameof(FontCffFdArrayMissing);
    public const string FontCffFdSelectInvalid = nameof(FontCffFdSelectInvalid);
}
