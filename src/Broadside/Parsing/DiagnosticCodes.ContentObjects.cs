namespace Broadside.Parsing;

/// <summary>
/// Diagnostic codes for text, XObjects, inline images, graphics state parameter dictionaries and marked content in content streams
/// (issue #56). Reported once per code per content stream, as the codes of issue #55.
/// </summary>
internal static partial class DiagnosticCodes
{
    // Text (§9.3, §9.4).
    public const string ContentFontMissing = nameof(ContentFontMissing);

    // Graphics state parameter dictionaries (§8.4.5, Table 57).
    public const string ContentExtGStateMissing = nameof(ContentExtGStateMissing);
    public const string ContentExtGStateInvalid = nameof(ContentExtGStateInvalid);

    // XObjects (§8.8, §8.10).
    public const string ContentXObjectMissing = nameof(ContentXObjectMissing);
    public const string ContentXObjectInvalid = nameof(ContentXObjectInvalid);
    public const string ContentPostScriptXObject = nameof(ContentPostScriptXObject);
    public const string ContentFormInvalid = nameof(ContentFormInvalid);
    public const string ContentFormCycle = nameof(ContentFormCycle);

    // Inline images (§8.9.7).
    public const string ContentInlineImageLengthMissing = nameof(ContentInlineImageLengthMissing);

    // Marked content (§14.6).
    public const string ContentPropertiesMissing = nameof(ContentPropertiesMissing);
    public const string ContentMarkedContentUnbalanced = nameof(ContentMarkedContentUnbalanced);
}
