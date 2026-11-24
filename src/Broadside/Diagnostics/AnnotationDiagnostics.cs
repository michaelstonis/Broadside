namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about annotations and their appearances.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.5. Each code is reported under <see cref="DiagnosticArea.Annotation"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class AnnotationDiagnostics
{
    /// <summary>Gets the <c>AnnotsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotsInvalid { get; } = new(nameof(AnnotsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "The page Annots entry is not an array; the page is read as having no annotations.");

    /// <summary>Gets the <c>AnnotsInherited</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotsInherited { get; } = new(nameof(AnnotsInherited), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "A page has no Annots entry but a page tree node above it has one, which is not inheritable; it is used anyway, as viewers do.");

    /// <summary>Gets the <c>AnnotationEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationEntryInvalid { get; } = new(nameof(AnnotationEntryInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "An element of a page Annots array is not an annotation dictionary; the element is skipped.");

    /// <summary>Gets the <c>AnnotationNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationNotIndirect { get; } = new(nameof(AnnotationNotIndirect), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "An element of a page Annots array is a direct dictionary instead of an indirect reference; it is read anyway.");

    /// <summary>Gets the <c>AnnotationDuplicate</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationDuplicate { get; } = new(nameof(AnnotationDuplicate), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "A page Annots array lists the same annotation more than once; each listing is kept.");

    /// <summary>Gets the <c>AnnotationSharedAcrossPages</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationSharedAcrossPages { get; } = new(nameof(AnnotationSharedAcrossPages), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "One annotation is listed by the Annots arrays of more than one page; its page is the first page that lists it.");

    /// <summary>Gets the <c>AnnotationPageMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.7.3.3 Table 31, §12.5.2</remarks>
    public static DiagnosticCode AnnotationPageMismatch { get; } = new(nameof(AnnotationPageMismatch), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §7.7.3.3 Table 31, §12.5.2",
        "The P entry of an annotation does not name the page whose Annots array holds it; the page that holds it is used.");

    /// <summary>Gets the <c>AnnotationTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationTypeInvalid { get; } = new(nameof(AnnotationTypeInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation Type entry is present but is not Annot; the entry is ignored.");

    /// <summary>Gets the <c>AnnotationSubtypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationSubtypeMissing { get; } = new(nameof(AnnotationSubtypeMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation has no Subtype name; it is read as an annotation of unknown type.");

    /// <summary>Gets the <c>AnnotationSubtypeNotName</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationSubtypeNotName { get; } = new(nameof(AnnotationSubtypeNotName), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation Subtype is a string rather than a name; it is read as the name the string spells.");

    /// <summary>Gets the <c>AnnotationRectInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationRectInvalid { get; } = new(nameof(AnnotationRectInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation Rect entry is missing or is not four numbers; it is read as an empty rectangle at the origin.");

    /// <summary>Gets the <c>AnnotationFlagsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationFlagsInvalid { get; } = new(nameof(AnnotationFlagsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation F entry is not an integer or sets undefined bits; a real number is truncated, other values read as 0, and undefined bits are kept.");

    /// <summary>Gets the <c>AnnotationValueInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationValueInvalid { get; } = new(nameof(AnnotationValueInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation or appearance characteristics entry holds an invalid value, such as a rotation that is not a multiple of 90; a default value is used or the entry is ignored.");

    /// <summary>Gets the <c>AnnotationRequiredEntryMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode AnnotationRequiredEntryMissing { get; } = new(nameof(AnnotationRequiredEntryMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation lacks an entry that its annotation type requires; the annotation is read without it.");

    /// <summary>Gets the <c>BorderArrayInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode BorderArrayInvalid { get; } = new(nameof(BorderArrayInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation Border entry is not an array of three numbers and an optional dash array; the default border is used or missing elements read as 0.");

    /// <summary>Gets the <c>BorderStyleTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode BorderStyleTypeInvalid { get; } = new(nameof(BorderStyleTypeInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "A border style dictionary has a Type that is not Border; the dictionary is used anyway.");

    /// <summary>Gets the <c>ColorArrayInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode ColorArrayInvalid { get; } = new(nameof(ColorArrayInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation color entry is not an array of 0, 1, 3 or 4 numbers; it is read as absent.");

    /// <summary>Gets the <c>QuadPointsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169</remarks>
    public static DiagnosticCode QuadPointsInvalid { get; } = new(nameof(QuadPointsInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.2 to §12.5.4, Tables 166 to 169",
        "An annotation QuadPoints entry is not an array of 8n numbers; only its complete quadrilaterals are read.");

    /// <summary>Gets the <c>AppearanceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceMissing { get; } = new(nameof(AppearanceMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An annotation has no appearance dictionary where one is required; it has no appearance.");

    /// <summary>Gets the <c>AppearanceStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceStateMissing { get; } = new(nameof(AppearanceStateMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An annotation appearance has several states but no AS entry selects one; the annotation has no appearance.");

    /// <summary>Gets the <c>AppearanceEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceEntryInvalid { get; } = new(nameof(AppearanceEntryInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An appearance dictionary entry or state is not a stream or dictionary as required; the entry is skipped and gives no appearance.");

    /// <summary>Gets the <c>AppearanceBBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceBBoxMissing { get; } = new(nameof(AppearanceBBoxMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An appearance stream has no BBox of four numbers; the size of the annotation rectangle is used.");

    /// <summary>Gets the <c>AppearanceNotForm</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceNotForm { get; } = new(nameof(AppearanceNotForm), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An appearance stream Subtype is not Form; it is read as a form XObject.");

    /// <summary>Gets the <c>AppearanceResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5 Table 170</remarks>
    public static DiagnosticCode AppearanceResourcesMissing { get; } = new(nameof(AppearanceResourcesMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5 Table 170",
        "An appearance stream refers to a resource that its Resources dictionary does not provide; the content that needs it is drawn without it.");

    /// <summary>Gets the <c>AppearanceNotRendered</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5</remarks>
    public static DiagnosticCode AppearanceNotRendered { get; } = new(nameof(AppearanceNotRendered), DiagnosticArea.Annotation, DiagnosticSeverity.Information, "ISO 32000-2 §12.5.5",
        "An annotation has no normal appearance stream for its current state, so it is not drawn; appearances are not generated during rendering. This records the omission.");

    /// <summary>Gets the <c>PopupLinkMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode PopupLinkMismatch { get; } = new(nameof(PopupLinkMismatch), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "A pop-up annotation and its parent markup annotation do not name each other through Popup and Parent; the entries are read as written.");

    /// <summary>Gets the <c>PopupParentInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode PopupParentInvalid { get; } = new(nameof(PopupParentInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "A pop-up annotation Parent is not a markup annotation; the link is not used.");

    /// <summary>Gets the <c>InReplyToInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode InReplyToInvalid { get; } = new(nameof(InReplyToInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "An annotation IRT entry is not another annotation dictionary; it is ignored.");

    /// <summary>Gets the <c>InReplyToOtherPage</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode InReplyToOtherPage { get; } = new(nameof(InReplyToOtherPage), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "An annotation replies to an annotation that is not on the same page; the reply link is kept as written.");

    /// <summary>Gets the <c>ReplyTypeWithoutInReplyTo</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode ReplyTypeWithoutInReplyTo { get; } = new(nameof(ReplyTypeWithoutInReplyTo), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "An annotation has an RT entry but no IRT entry that it depends on; the RT entry has no effect.");

    /// <summary>Gets the <c>StateModelMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode StateModelMissing { get; } = new(nameof(StateModelMissing), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "A text annotation has a State entry but no StateModel; the entries are read as written.");

    /// <summary>Gets the <c>LinkActionAndDest</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode LinkActionAndDest { get; } = new(nameof(LinkActionAndDest), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "A link annotation has both an A and a Dest entry; the action is the one performed.");

    /// <summary>Gets the <c>TrapNetPlacementInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21</remarks>
    public static DiagnosticCode TrapNetPlacementInvalid { get; } = new(nameof(TrapNetPlacementInvalid), DiagnosticArea.Annotation, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21",
        "A page has more than one trap network annotation or it is not the last element of Annots; the annotations are read as written.");

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
