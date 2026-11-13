using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for interactive forms (issue #74).</summary>
internal static partial class DiagnosticCodes
{
    // The interactive form dictionary (§12.7.3, Table 224) and XFA (Annex K).
    public static string AcroFormInvalid => FormDiagnostics.AcroFormInvalid.Name;
    public static string AcroFormFieldsMissing => FormDiagnostics.AcroFormFieldsMissing.Name;
    public static string AcroFormEntryInvalid => FormDiagnostics.AcroFormEntryInvalid.Name;
    public static string XfaFormPresent => FormDiagnostics.XfaFormPresent.Name;
    public static string XfaFormDynamic => FormDiagnostics.XfaFormDynamic.Name;
    public static string XfaPacketsInvalid => FormDiagnostics.XfaPacketsInvalid.Name;
    public static string XfaDataNotUpdated => FormDiagnostics.XfaDataNotUpdated.Name;

    // Flattening (§12.5.5, issue #170).
    public static string FlattenInvalidatesSignatures => FormDiagnostics.FlattenInvalidatesSignatures.Name;

    // The field tree (§12.7.2, §12.7.4.1, §12.7.4.2): reported when the tree is built.
    public static string FieldReferenceInvalid => FormDiagnostics.FieldReferenceInvalid.Name;
    public static string FieldNotIndirect => FormDiagnostics.FieldNotIndirect.Name;
    public static string FieldTreeCycle => FormDiagnostics.FieldTreeCycle.Name;
    public static string FieldTreeTooDeep => FormDiagnostics.FieldTreeTooDeep.Name;
    public static string FieldNotRoot => FormDiagnostics.FieldNotRoot.Name;
    public static string FieldParentMissing => FormDiagnostics.FieldParentMissing.Name;
    public static string FieldParentMismatch => FormDiagnostics.FieldParentMismatch.Name;
    public static string FieldKidsMixed => FormDiagnostics.FieldKidsMixed.Name;
    public static string FieldNameMissing => FormDiagnostics.FieldNameMissing.Name;
    public static string FieldNameHasPeriod => FormDiagnostics.FieldNameHasPeriod.Name;
    public static string FieldNameDuplicateInconsistent => FormDiagnostics.FieldNameDuplicateInconsistent.Name;
    public static string FieldTypeMissing => FormDiagnostics.FieldTypeMissing.Name;
    public static string ButtonFlagsConflict => FormDiagnostics.ButtonFlagsConflict.Name;
    public static string SignatureFieldMultipleWidgets => FormDiagnostics.SignatureFieldMultipleWidgets.Name;
    public static string WidgetNotInFieldTree => FormDiagnostics.WidgetNotInFieldTree.Name;

    // Field entries and values (§12.7.4, §12.7.5): reported when read.
    public static string FieldEntryInvalid => FormDiagnostics.FieldEntryInvalid.Name;
    public static string FieldFlagsInvalid => FormDiagnostics.FieldFlagsInvalid.Name;
    public static string FieldValueTypeInvalid => FormDiagnostics.FieldValueTypeInvalid.Name;
    public static string DefaultAppearanceMissing => FormDiagnostics.DefaultAppearanceMissing.Name;
    public static string DefaultAppearanceInvalid => FormDiagnostics.DefaultAppearanceInvalid.Name;
    public static string DefaultAppearanceFontMissing => FormDiagnostics.DefaultAppearanceFontMissing.Name;
    public static string TextCombInvalid => FormDiagnostics.TextCombInvalid.Name;
    public static string ButtonValueStateMismatch => FormDiagnostics.ButtonValueStateMismatch.Name;
    public static string ButtonValueUnknownState => FormDiagnostics.ButtonValueUnknownState.Name;
    public static string ButtonStatesAmbiguous => FormDiagnostics.ButtonStatesAmbiguous.Name;
    public static string ButtonOnStateMissing => FormDiagnostics.ButtonOnStateMissing.Name;
    public static string ButtonOptLengthMismatch => FormDiagnostics.ButtonOptLengthMismatch.Name;
    public static string ChoiceOptionInvalid => FormDiagnostics.ChoiceOptionInvalid.Name;
    public static string ChoiceOptionsInherited => FormDiagnostics.ChoiceOptionsInherited.Name;
    public static string ChoiceValueNotInOptions => FormDiagnostics.ChoiceValueNotInOptions.Name;
    public static string ChoiceIndicesInvalid => FormDiagnostics.ChoiceIndicesInvalid.Name;
    public static string SignatureFieldDictNotIndirect => FormDiagnostics.SignatureFieldDictNotIndirect.Name;
}
