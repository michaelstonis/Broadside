namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about annotations and their appearances.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.5. Each code is reported under <see cref="DiagnosticArea.Annotation"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class AnnotationDiagnostics
{
    /// <summary>Gets the <c>AnnotsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotsInvalid { get; } = new(nameof(AnnotsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotsInherited</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotsInherited { get; } = new(nameof(AnnotsInherited), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationEntryInvalid { get; } = new(nameof(AnnotationEntryInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationNotIndirect { get; } = new(nameof(AnnotationNotIndirect), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationDuplicate { get; } = new(nameof(AnnotationDuplicate), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationSharedAcrossPages</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationSharedAcrossPages { get; } = new(nameof(AnnotationSharedAcrossPages), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationPageMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationPageMismatch { get; } = new(nameof(AnnotationPageMismatch), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2");

    /// <summary>Gets the <c>AnnotationTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationTypeInvalid { get; } = new(nameof(AnnotationTypeInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationSubtypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationSubtypeMissing { get; } = new(nameof(AnnotationSubtypeMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationSubtypeNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationSubtypeNotName { get; } = new(nameof(AnnotationSubtypeNotName), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationRectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationRectInvalid { get; } = new(nameof(AnnotationRectInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationFlagsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationFlagsInvalid { get; } = new(nameof(AnnotationFlagsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationValueInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationValueInvalid { get; } = new(nameof(AnnotationValueInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AnnotationRequiredEntryMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationRequiredEntryMissing { get; } = new(nameof(AnnotationRequiredEntryMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>BorderArrayInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode BorderArrayInvalid { get; } = new(nameof(BorderArrayInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>BorderStyleTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode BorderStyleTypeInvalid { get; } = new(nameof(BorderStyleTypeInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>ColorArrayInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode ColorArrayInvalid { get; } = new(nameof(ColorArrayInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>QuadPointsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode QuadPointsInvalid { get; } = new(nameof(QuadPointsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169");

    /// <summary>Gets the <c>AppearanceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceMissing { get; } = new(nameof(AppearanceMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceStateMissing { get; } = new(nameof(AppearanceStateMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceEntryInvalid { get; } = new(nameof(AppearanceEntryInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceBBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceBBoxMissing { get; } = new(nameof(AppearanceBBoxMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceNotForm</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceNotForm { get; } = new(nameof(AppearanceNotForm), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceResourcesMissing { get; } = new(nameof(AppearanceResourcesMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170");

    /// <summary>Gets the <c>AppearanceNotRendered</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5</remarks>
    public static DiagnosticCode AppearanceNotRendered { get; } = new(nameof(AppearanceNotRendered), DiagnosticArea.Annotation, DiagnosticSeverity.Information, "ISO 32000-2 §12.5.5");

    /// <summary>Gets the <c>PopupLinkMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode PopupLinkMismatch { get; } = new(nameof(PopupLinkMismatch), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>PopupParentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode PopupParentInvalid { get; } = new(nameof(PopupParentInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>InReplyToInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode InReplyToInvalid { get; } = new(nameof(InReplyToInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>InReplyToOtherPage</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode InReplyToOtherPage { get; } = new(nameof(InReplyToOtherPage), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>ReplyTypeWithoutInReplyTo</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode ReplyTypeWithoutInReplyTo { get; } = new(nameof(ReplyTypeWithoutInReplyTo), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>StateModelMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode StateModelMissing { get; } = new(nameof(StateModelMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>LinkActionAndDest</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode LinkActionAndDest { get; } = new(nameof(LinkActionAndDest), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets the <c>TrapNetPlacementInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode TrapNetPlacementInvalid { get; } = new(nameof(TrapNetPlacementInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        AnnotsInvalid,
        AnnotsInherited,
        AnnotationEntryInvalid,
        AnnotationNotIndirect,
        AnnotationDuplicate,
        AnnotationSharedAcrossPages,
        AnnotationPageMismatch,
        AnnotationTypeInvalid,
        AnnotationSubtypeMissing,
        AnnotationSubtypeNotName,
        AnnotationRectInvalid,
        AnnotationFlagsInvalid,
        AnnotationValueInvalid,
        AnnotationRequiredEntryMissing,
        BorderArrayInvalid,
        BorderStyleTypeInvalid,
        ColorArrayInvalid,
        QuadPointsInvalid,
        AppearanceMissing,
        AppearanceStateMissing,
        AppearanceEntryInvalid,
        AppearanceBBoxMissing,
        AppearanceNotForm,
        AppearanceResourcesMissing,
        AppearanceNotRendered,
        PopupLinkMismatch,
        PopupParentInvalid,
        InReplyToInvalid,
        InReplyToOtherPage,
        ReplyTypeWithoutInReplyTo,
        StateModelMissing,
        LinkActionAndDest,
        TrapNetPlacementInvalid,
    ];
}
