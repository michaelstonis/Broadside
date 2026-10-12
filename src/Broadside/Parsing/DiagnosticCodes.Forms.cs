namespace Broadside.Parsing;

/// <summary>Diagnostic codes for interactive forms (issue #74).</summary>
internal static partial class DiagnosticCodes
{
    // The interactive form dictionary (§12.7.3, Table 224) and XFA (Annex K).
    public const string AcroFormInvalid = nameof(AcroFormInvalid);
    public const string AcroFormFieldsMissing = nameof(AcroFormFieldsMissing);
    public const string AcroFormEntryInvalid = nameof(AcroFormEntryInvalid);
    public const string XfaFormPresent = nameof(XfaFormPresent);
    public const string XfaFormDynamic = nameof(XfaFormDynamic);
    public const string XfaPacketsInvalid = nameof(XfaPacketsInvalid);

    // The field tree (§12.7.2, §12.7.4.1, §12.7.4.2): reported when the tree is built.
    public const string FieldReferenceInvalid = nameof(FieldReferenceInvalid);
    public const string FieldNotIndirect = nameof(FieldNotIndirect);
    public const string FieldTreeCycle = nameof(FieldTreeCycle);
    public const string FieldTreeTooDeep = nameof(FieldTreeTooDeep);
    public const string FieldNotRoot = nameof(FieldNotRoot);
    public const string FieldParentMissing = nameof(FieldParentMissing);
    public const string FieldParentMismatch = nameof(FieldParentMismatch);
    public const string FieldKidsMixed = nameof(FieldKidsMixed);
    public const string FieldNameMissing = nameof(FieldNameMissing);
    public const string FieldNameHasPeriod = nameof(FieldNameHasPeriod);
    public const string FieldNameDuplicateInconsistent = nameof(FieldNameDuplicateInconsistent);
    public const string FieldTypeMissing = nameof(FieldTypeMissing);
    public const string ButtonFlagsConflict = nameof(ButtonFlagsConflict);
    public const string SignatureFieldMultipleWidgets = nameof(SignatureFieldMultipleWidgets);
    public const string WidgetNotInFieldTree = nameof(WidgetNotInFieldTree);

    // Field entries and values (§12.7.4, §12.7.5): reported when read.
    public const string FieldEntryInvalid = nameof(FieldEntryInvalid);
    public const string FieldFlagsInvalid = nameof(FieldFlagsInvalid);
    public const string FieldValueTypeInvalid = nameof(FieldValueTypeInvalid);
    public const string DefaultAppearanceMissing = nameof(DefaultAppearanceMissing);
    public const string TextCombInvalid = nameof(TextCombInvalid);
    public const string ButtonValueStateMismatch = nameof(ButtonValueStateMismatch);
    public const string ButtonValueUnknownState = nameof(ButtonValueUnknownState);
    public const string ButtonStatesAmbiguous = nameof(ButtonStatesAmbiguous);
    public const string ButtonOnStateMissing = nameof(ButtonOnStateMissing);
    public const string ButtonOptLengthMismatch = nameof(ButtonOptLengthMismatch);
    public const string ChoiceOptionInvalid = nameof(ChoiceOptionInvalid);
    public const string ChoiceOptionsInherited = nameof(ChoiceOptionsInherited);
    public const string ChoiceValueNotInOptions = nameof(ChoiceValueNotInOptions);
    public const string ChoiceIndicesInvalid = nameof(ChoiceIndicesInvalid);
    public const string SignatureFieldDictNotIndirect = nameof(SignatureFieldDictNotIndirect);
}
