namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about content streams: operators, operands, resources, text, XObjects, inline images and marked content.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.8.2, §8.2 to §8.10, §9.3, §14.6. Each code is reported under <see cref="DiagnosticArea.Content"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ContentDiagnostics
{
    /// <summary>Gets the <c>ContentFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.3, §9.4</remarks>
    public static DiagnosticCode ContentFontMissing { get; } = new(nameof(ContentFontMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §9.3, §9.4",
        "Text is shown with a font that is not in the resources Font dictionary, or with no font selected at all; Helvetica metrics are used instead.");

    /// <summary>Gets the <c>ContentExtGStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.4.5 Table 57</remarks>
    public static DiagnosticCode ContentExtGStateMissing { get; } = new(nameof(ContentExtGStateMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.5 Table 57",
        "A gs operator names a graphics state parameter dictionary that is not in the resources ExtGState dictionary; the operator is ignored.");

    /// <summary>Gets the <c>ContentExtGStateInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.4.5 Table 57</remarks>
    public static DiagnosticCode ContentExtGStateInvalid { get; } = new(nameof(ContentExtGStateInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.4.5 Table 57",
        "A graphics state parameter dictionary has an entry that does not match its defined type or range; the entry is repaired or ignored and the rest is applied.");

    /// <summary>Gets the <c>ContentXObjectMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentXObjectMissing { get; } = new(nameof(ContentXObjectMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10",
        "A Do operator names an XObject that is not in the resources XObject dictionary; the operator is ignored and nothing is painted.");

    /// <summary>Gets the <c>ContentXObjectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentXObjectInvalid { get; } = new(nameof(ContentXObjectInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10",
        "A Do operator names an XObject that is not a stream or has a Subtype other than Form, Image or PS; the operator is ignored.");

    /// <summary>Gets the <c>ContentPostScriptXObject</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentPostScriptXObject { get; } = new(nameof(ContentPostScriptXObject), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §8.8, §8.10",
        "A Do operator names a deprecated PostScript XObject. The object is recorded and preserved but not interpreted, so nothing is painted for it.");

    /// <summary>Gets the <c>ContentFormInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentFormInvalid { get; } = new(nameof(ContentFormInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10",
        "A form XObject has a Matrix that is not six numbers or no usable BBox; the identity matrix is used and the content is not clipped.");

    /// <summary>Gets the <c>ContentFormCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.8, §8.10</remarks>
    public static DiagnosticCode ContentFormCycle { get; } = new(nameof(ContentFormCycle), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §8.8, §8.10",
        "A form XObject, glyph description or soft-mask group would run inside itself; it is not run again, which breaks the cycle.");

    /// <summary>Gets the <c>ContentInlineImageLengthMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.9.7</remarks>
    public static DiagnosticCode ContentInlineImageLengthMissing { get; } = new(nameof(ContentInlineImageLengthMissing), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §8.9.7",
        "An inline image in a PDF 2.0 file has no L (length) entry; the end of its data is found by scanning for EI instead.");

    /// <summary>Gets the <c>ContentPropertiesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6</remarks>
    public static DiagnosticCode ContentPropertiesMissing { get; } = new(nameof(ContentPropertiesMissing), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6",
        "A marked-content operator names a property list that is not in the resources Properties dictionary; the sequence is processed without properties.");

    /// <summary>Gets the <c>ContentMarkedContentUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §14.6</remarks>
    public static DiagnosticCode ContentMarkedContentUnbalanced { get; } = new(nameof(ContentMarkedContentUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §14.6",
        "Marked-content sequences are unbalanced: an EMC has no open sequence, or the stream ends with BMC or BDC still open. The stray operator is ignored or the open sequences are closed.");

    /// <summary>Gets the <c>ContentStreamInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStreamInvalid { get; } = new(nameof(ContentStreamInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "The page Contents entry is empty or is neither a stream nor an array of streams, or a stream cannot be read; the page is treated as having no content.");

    /// <summary>Gets the <c>ContentUnknownOperator</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentUnknownOperator { get; } = new(nameof(ContentUnknownOperator), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "A keyword that is not a known operator appears outside a BX/EX compatibility section; it and its operands are ignored.");

    /// <summary>Gets the <c>ContentOperandCount</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandCount { get; } = new(nameof(ContentOperandCount), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "An operator has fewer or more operands than it takes, or operands are left over at the end of the stream. Operators with too few operands are skipped; extra operands are dropped.");

    /// <summary>Gets the <c>ContentOperandType</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandType { get; } = new(nameof(ContentOperandType), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "An operator has an operand of the wrong type, such as a non-number in a dash array or a TJ array; the operator or the element is skipped.");

    /// <summary>Gets the <c>ContentOperandOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperandOverflow { get; } = new(nameof(ContentOperandOverflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "More operands are written before an operator than the interpreter limit allows; the oldest operands are dropped.");

    /// <summary>Gets the <c>ContentSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentSyntaxInvalid { get; } = new(nameof(ContentSyntaxInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "The content stream has a malformed token, such as a stray delimiter, a bad number, string or name, or an unclosed array or dictionary; the token is skipped or repaired.");

    /// <summary>Gets the <c>ContentGluedTokens</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentGluedTokens { get; } = new(nameof(ContentGluedTokens), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "Operators and operands are written without white space between them, such as q1 or 0cm. This is recorded only; they are read as separate tokens.");

    /// <summary>Gets the <c>ContentOperatorOutOfContext</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentOperatorOutOfContext { get; } = new(nameof(ContentOperatorOutOfContext), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "An operator appears where the content stream object rules do not allow it, such as a graphics state change inside a path or text outside a text object. It is applied anyway.");

    /// <summary>Gets the <c>ContentStackUnderflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStackUnderflow { get; } = new(nameof(ContentStackUnderflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "A Q operator has no matching q, so there is no saved graphics state to restore; the operator is ignored.");

    /// <summary>Gets the <c>ContentStackOverflow</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentStackOverflow { get; } = new(nameof(ContentStackOverflow), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "The graphics state stack is deeper than the interpreter limit; the offending q and its matching Q are ignored.");

    /// <summary>Gets the <c>ContentUnbalancedSave</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentUnbalancedSave { get; } = new(nameof(ContentUnbalancedSave), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "The content stream ends with graphics states saved by q that were never restored by Q; they are restored at the end of the stream.");

    /// <summary>Gets the <c>ContentNoCurrentPoint</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentNoCurrentPoint { get; } = new(nameof(ContentNoCurrentPoint), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "A line, curve or closepath operator is used without a current point; a line or curve is read as a moveto to its end point and h is ignored.");

    /// <summary>Gets the <c>ContentNoCurrentPath</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentNoCurrentPath { get; } = new(nameof(ContentNoCurrentPath), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "A clipping or path-painting operator is used without a current path. This is recorded only; nothing is painted, and a pending clip applies to the next painted path.");

    /// <summary>Gets the <c>ContentPathNotPainted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentPathNotPainted { get; } = new(nameof(ContentPathNotPainted), DiagnosticArea.Content, DiagnosticSeverity.Information, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "The content stream ends inside a path object that was never painted. This is recorded only; the unpainted path is discarded.");

    /// <summary>Gets the <c>ContentGraphicsStateRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentGraphicsStateRange { get; } = new(nameof(ContentGraphicsStateRange), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "A graphics state value is outside its allowed range, such as a negative line width, a miter limit below 1, a bad flatness, dash array or text rendering mode. It is clamped or replaced by a default.");

    /// <summary>Gets the <c>ContentCompatibilityUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentCompatibilityUnbalanced { get; } = new(nameof(ContentCompatibilityUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "BX and EX are unbalanced: an EX has no matching BX, or the stream ends inside a compatibility section; the stray EX is ignored.");

    /// <summary>Gets the <c>ContentTextObjectUnbalanced</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentTextObjectUnbalanced { get; } = new(nameof(ContentTextObjectUnbalanced), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "BT and ET are unbalanced: BT appears inside a text object, ET appears without one, or the stream ends inside a text object. The stray operator is ignored or the object is ended there.");

    /// <summary>Gets the <c>ContentInlineImageInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5</remarks>
    public static DiagnosticCode ContentInlineImageInvalid { get; } = new(nameof(ContentInlineImageInvalid), DiagnosticArea.Content, DiagnosticSeverity.Warning, "ISO 32000-2 §7.8.2, §8.2, §8.4, §8.5",
        "An inline image is malformed: ID or EI is missing or out of place, its dictionary holds invalid values, or its data does not end where L says. The image ends at the end that is found.");

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
