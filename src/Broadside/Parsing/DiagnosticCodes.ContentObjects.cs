using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>
/// Diagnostic codes for text, XObjects, inline images, graphics state parameter dictionaries and marked content in content streams
/// (issue #56). Reported once per code per content stream, as the codes of issue #55.
/// </summary>
internal static partial class DiagnosticCodes
{
    // Text (§9.3, §9.4).
    public static string ContentFontMissing => ContentDiagnostics.ContentFontMissing.Name;

    // Graphics state parameter dictionaries (§8.4.5, Table 57).
    public static string ContentExtGStateMissing => ContentDiagnostics.ContentExtGStateMissing.Name;
    public static string ContentExtGStateInvalid => ContentDiagnostics.ContentExtGStateInvalid.Name;

    // XObjects (§8.8, §8.10).
    public static string ContentXObjectMissing => ContentDiagnostics.ContentXObjectMissing.Name;
    public static string ContentXObjectInvalid => ContentDiagnostics.ContentXObjectInvalid.Name;
    public static string ContentPostScriptXObject => ContentDiagnostics.ContentPostScriptXObject.Name;
    public static string ContentFormInvalid => ContentDiagnostics.ContentFormInvalid.Name;
    public static string ContentFormCycle => ContentDiagnostics.ContentFormCycle.Name;

    // Inline images (§8.9.7).
    public static string ContentInlineImageLengthMissing => ContentDiagnostics.ContentInlineImageLengthMissing.Name;

    // Marked content (§14.6).
    public static string ContentPropertiesMissing => ContentDiagnostics.ContentPropertiesMissing.Name;
    public static string ContentMarkedContentUnbalanced => ContentDiagnostics.ContentMarkedContentUnbalanced.Name;
}
