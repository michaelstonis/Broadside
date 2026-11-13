using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for annotations and their appearances (issue #71).</summary>
internal static partial class DiagnosticCodes
{
    // The page's Annots array (§7.7.3.3, Table 31; §12.5.2).
    public static string AnnotsInvalid => AnnotationDiagnostics.AnnotsInvalid.Name;
    public static string AnnotsInherited => AnnotationDiagnostics.AnnotsInherited.Name;
    public static string AnnotationEntryInvalid => AnnotationDiagnostics.AnnotationEntryInvalid.Name;
    public static string AnnotationNotIndirect => AnnotationDiagnostics.AnnotationNotIndirect.Name;
    public static string AnnotationDuplicate => AnnotationDiagnostics.AnnotationDuplicate.Name;
    public static string AnnotationSharedAcrossPages => AnnotationDiagnostics.AnnotationSharedAcrossPages.Name;
    public static string AnnotationPageMismatch => AnnotationDiagnostics.AnnotationPageMismatch.Name;

    // Annotation dictionaries (§12.5.2 to §12.5.4, Tables 166 to 169).
    public static string AnnotationTypeInvalid => AnnotationDiagnostics.AnnotationTypeInvalid.Name;
    public static string AnnotationSubtypeMissing => AnnotationDiagnostics.AnnotationSubtypeMissing.Name;
    public static string AnnotationSubtypeNotName => AnnotationDiagnostics.AnnotationSubtypeNotName.Name;
    public static string AnnotationRectInvalid => AnnotationDiagnostics.AnnotationRectInvalid.Name;
    public static string AnnotationFlagsInvalid => AnnotationDiagnostics.AnnotationFlagsInvalid.Name;
    public static string AnnotationValueInvalid => AnnotationDiagnostics.AnnotationValueInvalid.Name;
    public static string AnnotationRequiredEntryMissing => AnnotationDiagnostics.AnnotationRequiredEntryMissing.Name;
    public static string BorderArrayInvalid => AnnotationDiagnostics.BorderArrayInvalid.Name;
    public static string BorderStyleTypeInvalid => AnnotationDiagnostics.BorderStyleTypeInvalid.Name;
    public static string ColorArrayInvalid => AnnotationDiagnostics.ColorArrayInvalid.Name;
    public static string QuadPointsInvalid => AnnotationDiagnostics.QuadPointsInvalid.Name;

    // Appearance streams (§12.5.5, Table 170).
    public static string AppearanceMissing => AnnotationDiagnostics.AppearanceMissing.Name;
    public static string AppearanceStateMissing => AnnotationDiagnostics.AppearanceStateMissing.Name;
    public static string AppearanceEntryInvalid => AnnotationDiagnostics.AppearanceEntryInvalid.Name;
    public static string AppearanceBBoxMissing => AnnotationDiagnostics.AppearanceBBoxMissing.Name;
    public static string AppearanceNotForm => AnnotationDiagnostics.AppearanceNotForm.Name;
    public static string AppearanceResourcesMissing => AnnotationDiagnostics.AppearanceResourcesMissing.Name;

    // Rendering annotations (§12.5.5, issue #98).
    public static string AppearanceNotRendered => AnnotationDiagnostics.AppearanceNotRendered.Name;

    // Relations between annotations (§12.5.6.2, §12.5.6.3, §12.5.6.14, §12.5.6.21).
    public static string PopupLinkMismatch => AnnotationDiagnostics.PopupLinkMismatch.Name;
    public static string PopupParentInvalid => AnnotationDiagnostics.PopupParentInvalid.Name;
    public static string InReplyToInvalid => AnnotationDiagnostics.InReplyToInvalid.Name;
    public static string InReplyToOtherPage => AnnotationDiagnostics.InReplyToOtherPage.Name;
    public static string ReplyTypeWithoutInReplyTo => AnnotationDiagnostics.ReplyTypeWithoutInReplyTo.Name;
    public static string StateModelMissing => AnnotationDiagnostics.StateModelMissing.Name;
    public static string LinkActionAndDest => AnnotationDiagnostics.LinkActionAndDest.Name;
    public static string TrapNetPlacementInvalid => AnnotationDiagnostics.TrapNetPlacementInvalid.Name;
}
