namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about optional content.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.11. Each code is reported under <see cref="DiagnosticArea.OptionalContent"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class OptionalContentDiagnostics
{
    /// <summary>Gets the <c>OptionalContentConfigMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentConfigMissing { get; } = new(nameof(OptionalContentConfigMissing), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "The optional content properties have no default configuration D; an empty configuration is used, which leaves every group on.");

    /// <summary>Gets the <c>OptionalContentGroupsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupsMissing { get; } = new(nameof(OptionalContentGroupsMissing), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "The optional content properties have no OCGs array; no group is treated as optional content and all content is visible.");

    /// <summary>Gets the <c>OptionalContentGroupNotListed</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupNotListed { get; } = new(nameof(OptionalContentGroupNotListed), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "An optional content group, or a reference to one, is not listed in the OCGs array or is not a group at all; it is not treated as optional content and is ignored or shown.");

    /// <summary>Gets the <c>OptionalContentGroupInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupInvalid { get; } = new(nameof(OptionalContentGroupInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "An OCGs entry is not a valid optional content group dictionary, or a group has a missing or wrong Type or malformed entries; the entry is ignored or read leniently.");

    /// <summary>Gets the <c>OptionalContentBaseStateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentBaseStateInvalid { get; } = new(nameof(OptionalContentBaseStateInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "The default configuration has a BaseState that is not one of the permitted values; Broadside falls back to a permitted state.");

    /// <summary>Gets the <c>OptionalContentIntentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentIntentInvalid { get; } = new(nameof(OptionalContentIntentInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "The default configuration or a group has an Intent that is not valid for its use; the written intent is honored or a default is used.");

    /// <summary>Gets the <c>OptionalContentGroupOnAndOff</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupOnAndOff { get; } = new(nameof(OptionalContentGroupOnAndOff), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "A group is listed in both the ON and the OFF array of a configuration; the OFF entry is applied last, so the group is off.");

    /// <summary>Gets the <c>VisibilityExpressionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode VisibilityExpressionInvalid { get; } = new(nameof(VisibilityExpressionInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "An optional content visibility expression is malformed; the invalid part is ignored when visibility is evaluated.");

    /// <summary>Gets the <c>VisibilityPolicyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode VisibilityPolicyInvalid { get; } = new(nameof(VisibilityPolicyInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "A membership dictionary has a visibility policy that is not AllOn, AnyOn, AnyOff or AllOff; AnyOn is used.");

    /// <summary>Gets the <c>OptionalContentInlineProperties</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentInlineProperties { get; } = new(nameof(OptionalContentInlineProperties), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11",
        "An OC marked-content section carries an inline property list instead of a named resource; the property list is honored.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        OptionalContentConfigMissing,
        OptionalContentGroupsMissing,
        OptionalContentGroupNotListed,
        OptionalContentGroupInvalid,
        OptionalContentBaseStateInvalid,
        OptionalContentIntentInvalid,
        OptionalContentGroupOnAndOff,
        VisibilityExpressionInvalid,
        VisibilityPolicyInvalid,
        OptionalContentInlineProperties,
    ];
}
