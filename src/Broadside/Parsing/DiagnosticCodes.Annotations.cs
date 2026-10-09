namespace Broadside.Parsing;

/// <summary>Diagnostic codes for annotations and their appearances (issue #71).</summary>
internal static partial class DiagnosticCodes
{
    // The page's Annots array (§7.7.3.3, Table 31; §12.5.2).
    public const string AnnotsInvalid = nameof(AnnotsInvalid);
    public const string AnnotsInherited = nameof(AnnotsInherited);
    public const string AnnotationEntryInvalid = nameof(AnnotationEntryInvalid);
    public const string AnnotationNotIndirect = nameof(AnnotationNotIndirect);
    public const string AnnotationDuplicate = nameof(AnnotationDuplicate);
    public const string AnnotationSharedAcrossPages = nameof(AnnotationSharedAcrossPages);
    public const string AnnotationPageMismatch = nameof(AnnotationPageMismatch);

    // Annotation dictionaries (§12.5.2 to §12.5.4, Tables 166 to 169).
    public const string AnnotationTypeInvalid = nameof(AnnotationTypeInvalid);
    public const string AnnotationSubtypeMissing = nameof(AnnotationSubtypeMissing);
    public const string AnnotationSubtypeNotName = nameof(AnnotationSubtypeNotName);
    public const string AnnotationRectInvalid = nameof(AnnotationRectInvalid);
    public const string AnnotationFlagsInvalid = nameof(AnnotationFlagsInvalid);
    public const string AnnotationValueInvalid = nameof(AnnotationValueInvalid);
    public const string AnnotationRequiredEntryMissing = nameof(AnnotationRequiredEntryMissing);
    public const string BorderArrayInvalid = nameof(BorderArrayInvalid);
    public const string BorderStyleTypeInvalid = nameof(BorderStyleTypeInvalid);
    public const string ColorArrayInvalid = nameof(ColorArrayInvalid);
    public const string QuadPointsInvalid = nameof(QuadPointsInvalid);

    // Appearance streams (§12.5.5, Table 170).
    public const string AppearanceMissing = nameof(AppearanceMissing);
    public const string AppearanceStateMissing = nameof(AppearanceStateMissing);
    public const string AppearanceEntryInvalid = nameof(AppearanceEntryInvalid);
    public const string AppearanceBBoxMissing = nameof(AppearanceBBoxMissing);
    public const string AppearanceNotForm = nameof(AppearanceNotForm);

    // Relations between annotations (§12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21).
    public const string PopupLinkMismatch = nameof(PopupLinkMismatch);
    public const string PopupParentInvalid = nameof(PopupParentInvalid);
    public const string InReplyToInvalid = nameof(InReplyToInvalid);
    public const string InReplyToOtherPage = nameof(InReplyToOtherPage);
    public const string ReplyTypeWithoutInReplyTo = nameof(ReplyTypeWithoutInReplyTo);
    public const string StateModelMissing = nameof(StateModelMissing);
    public const string LinkActionAndDest = nameof(LinkActionAndDest);
    public const string TrapNetPlacementInvalid = nameof(TrapNetPlacementInvalid);
}
