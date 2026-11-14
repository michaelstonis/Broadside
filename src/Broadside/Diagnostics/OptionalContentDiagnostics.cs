namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about optional content.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.11. Each code is reported under <see cref="DiagnosticArea.OptionalContent"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class OptionalContentDiagnostics
{
    /// <summary>Gets the <c>OptionalContentConfigMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentConfigMissing { get; } = new(nameof(OptionalContentConfigMissing), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentGroupsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupsMissing { get; } = new(nameof(OptionalContentGroupsMissing), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentGroupNotListed</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupNotListed { get; } = new(nameof(OptionalContentGroupNotListed), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentGroupInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupInvalid { get; } = new(nameof(OptionalContentGroupInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentBaseStateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentBaseStateInvalid { get; } = new(nameof(OptionalContentBaseStateInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentIntentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentIntentInvalid { get; } = new(nameof(OptionalContentIntentInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentGroupOnAndOff</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentGroupOnAndOff { get; } = new(nameof(OptionalContentGroupOnAndOff), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>VisibilityExpressionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode VisibilityExpressionInvalid { get; } = new(nameof(VisibilityExpressionInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>VisibilityPolicyInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode VisibilityPolicyInvalid { get; } = new(nameof(VisibilityPolicyInvalid), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

    /// <summary>Gets the <c>OptionalContentInlineProperties</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.11</remarks>
    public static DiagnosticCode OptionalContentInlineProperties { get; } = new(nameof(OptionalContentInlineProperties), DiagnosticArea.OptionalContent, DiagnosticSeverity.Warning, "ISO 32000-2 §8.11");

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
