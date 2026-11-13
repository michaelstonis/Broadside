namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about content streams: operators, operands, resources, text, XObjects, inline images and marked content.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.8.2, §8.2 to §8.10, §9.3, §14.6. Each code is reported under <see cref="DiagnosticArea.Content"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ContentDiagnostics
{
    /// <summary>Gets the <c>ContentFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.3, §9.4</remarks>
    public static DiagnosticCode ContentFontMissing { get; } = new(nameof(ContentFontMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §9.3, §9.4");

    /// <summary>Gets the <c>ContentExtGStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.4.5 Table 57</remarks>
    public static DiagnosticCode ContentExtGStateMissing { get; } = new(nameof(ContentExtGStateMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.5 Table 57");

    /// <summary>Gets the <c>ContentExtGStateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.4.5 Table 57</remarks>
    public static DiagnosticCode ContentExtGStateInvalid { get; } = new(nameof(ContentExtGStateInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.5 Table 57");

    /// <summary>Gets the <c>ContentXObjectMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentXObjectMissing { get; } = new(nameof(ContentXObjectMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10");

    /// <summary>Gets the <c>ContentXObjectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentXObjectInvalid { get; } = new(nameof(ContentXObjectInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10");

    /// <summary>Gets the <c>ContentPostScriptXObject</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentPostScriptXObject { get; } = new(nameof(ContentPostScriptXObject), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §8.8, §8.10");

    /// <summary>Gets the <c>ContentFormInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentFormInvalid { get; } = new(nameof(ContentFormInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10");

    /// <summary>Gets the <c>ContentFormCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentFormCycle { get; } = new(nameof(ContentFormCycle), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10");

    /// <summary>Gets the <c>ContentInlineImageLengthMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.7</remarks>
    public static DiagnosticCode ContentInlineImageLengthMissing { get; } = new(nameof(ContentInlineImageLengthMissing), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §8.9.7");

    /// <summary>Gets the <c>ContentPropertiesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6</remarks>
    public static DiagnosticCode ContentPropertiesMissing { get; } = new(nameof(ContentPropertiesMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6");

    /// <summary>Gets the <c>ContentMarkedContentUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6</remarks>
    public static DiagnosticCode ContentMarkedContentUnbalanced { get; } = new(nameof(ContentMarkedContentUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6");

    /// <summary>Gets the <c>ContentStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStreamInvalid { get; } = new(nameof(ContentStreamInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentUnknownOperator</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentUnknownOperator { get; } = new(nameof(ContentUnknownOperator), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentOperandCount</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandCount { get; } = new(nameof(ContentOperandCount), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentOperandType</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandType { get; } = new(nameof(ContentOperandType), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentOperandOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandOverflow { get; } = new(nameof(ContentOperandOverflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentSyntaxInvalid { get; } = new(nameof(ContentSyntaxInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentGluedTokens</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentGluedTokens { get; } = new(nameof(ContentGluedTokens), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentOperatorOutOfContext</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperatorOutOfContext { get; } = new(nameof(ContentOperatorOutOfContext), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentStackUnderflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStackUnderflow { get; } = new(nameof(ContentStackUnderflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentStackOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStackOverflow { get; } = new(nameof(ContentStackOverflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentUnbalancedSave</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentUnbalancedSave { get; } = new(nameof(ContentUnbalancedSave), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentNoCurrentPoint</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentNoCurrentPoint { get; } = new(nameof(ContentNoCurrentPoint), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentNoCurrentPath</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentNoCurrentPath { get; } = new(nameof(ContentNoCurrentPath), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentPathNotPainted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentPathNotPainted { get; } = new(nameof(ContentPathNotPainted), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentGraphicsStateRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentGraphicsStateRange { get; } = new(nameof(ContentGraphicsStateRange), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentCompatibilityUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentCompatibilityUnbalanced { get; } = new(nameof(ContentCompatibilityUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentTextObjectUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentTextObjectUnbalanced { get; } = new(nameof(ContentTextObjectUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets the <c>ContentInlineImageInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentInlineImageInvalid { get; } = new(nameof(ContentInlineImageInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ContentFontMissing,
        ContentExtGStateMissing,
        ContentExtGStateInvalid,
        ContentXObjectMissing,
        ContentXObjectInvalid,
        ContentPostScriptXObject,
        ContentFormInvalid,
        ContentFormCycle,
        ContentInlineImageLengthMissing,
        ContentPropertiesMissing,
        ContentMarkedContentUnbalanced,
        ContentStreamInvalid,
        ContentUnknownOperator,
        ContentOperandCount,
        ContentOperandType,
        ContentOperandOverflow,
        ContentSyntaxInvalid,
        ContentGluedTokens,
        ContentOperatorOutOfContext,
        ContentStackUnderflow,
        ContentStackOverflow,
        ContentUnbalancedSave,
        ContentNoCurrentPoint,
        ContentNoCurrentPath,
        ContentPathNotPainted,
        ContentGraphicsStateRange,
        ContentCompatibilityUnbalanced,
        ContentTextObjectUnbalanced,
        ContentInlineImageInvalid,
    ];
}
