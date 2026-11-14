namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about interactive forms.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.7. Each code is reported under <see cref="DiagnosticArea.Form"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FormDiagnostics
{
    /// <summary>Gets the <c>AcroFormInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormInvalid { get; } = new(nameof(AcroFormInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>AcroFormFieldsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormFieldsMissing { get; } = new(nameof(AcroFormFieldsMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>AcroFormEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormEntryInvalid { get; } = new(nameof(AcroFormEntryInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>XfaFormPresent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaFormPresent { get; } = new(nameof(XfaFormPresent), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>XfaFormDynamic</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaFormDynamic { get; } = new(nameof(XfaFormDynamic), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>XfaPacketsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaPacketsInvalid { get; } = new(nameof(XfaPacketsInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>XfaDataNotUpdated</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaDataNotUpdated { get; } = new(nameof(XfaDataNotUpdated), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K");

    /// <summary>Gets the <c>FlattenInvalidatesSignatures</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5</remarks>
    public static DiagnosticCode FlattenInvalidatesSignatures { get; } = new(nameof(FlattenInvalidatesSignatures), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5");

    /// <summary>Gets the <c>FieldReferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldReferenceInvalid { get; } = new(nameof(FieldReferenceInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNotIndirect { get; } = new(nameof(FieldNotIndirect), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTreeCycle { get; } = new(nameof(FieldTreeCycle), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTreeTooDeep { get; } = new(nameof(FieldTreeTooDeep), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldNotRoot</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNotRoot { get; } = new(nameof(FieldNotRoot), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldParentMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldParentMissing { get; } = new(nameof(FieldParentMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldParentMismatch { get; } = new(nameof(FieldParentMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldKidsMixed</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldKidsMixed { get; } = new(nameof(FieldKidsMixed), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldNameMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameMissing { get; } = new(nameof(FieldNameMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldNameHasPeriod</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameHasPeriod { get; } = new(nameof(FieldNameHasPeriod), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldNameDuplicateInconsistent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameDuplicateInconsistent { get; } = new(nameof(FieldNameDuplicateInconsistent), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldTypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTypeMissing { get; } = new(nameof(FieldTypeMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>ButtonFlagsConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode ButtonFlagsConflict { get; } = new(nameof(ButtonFlagsConflict), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>SignatureFieldMultipleWidgets</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode SignatureFieldMultipleWidgets { get; } = new(nameof(SignatureFieldMultipleWidgets), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>WidgetNotInFieldTree</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode WidgetNotInFieldTree { get; } = new(nameof(WidgetNotInFieldTree), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2");

    /// <summary>Gets the <c>FieldEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldEntryInvalid { get; } = new(nameof(FieldEntryInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>FieldFlagsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldFlagsInvalid { get; } = new(nameof(FieldFlagsInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>FieldValueTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldValueTypeInvalid { get; } = new(nameof(FieldValueTypeInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>DefaultAppearanceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceMissing { get; } = new(nameof(DefaultAppearanceMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>DefaultAppearanceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceInvalid { get; } = new(nameof(DefaultAppearanceInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>DefaultAppearanceFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceFontMissing { get; } = new(nameof(DefaultAppearanceFontMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>TextCombInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode TextCombInvalid { get; } = new(nameof(TextCombInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ButtonValueStateMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonValueStateMismatch { get; } = new(nameof(ButtonValueStateMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ButtonValueUnknownState</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonValueUnknownState { get; } = new(nameof(ButtonValueUnknownState), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ButtonStatesAmbiguous</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonStatesAmbiguous { get; } = new(nameof(ButtonStatesAmbiguous), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ButtonOnStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonOnStateMissing { get; } = new(nameof(ButtonOnStateMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ButtonOptLengthMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonOptLengthMismatch { get; } = new(nameof(ButtonOptLengthMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ChoiceOptionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceOptionInvalid { get; } = new(nameof(ChoiceOptionInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ChoiceOptionsInherited</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceOptionsInherited { get; } = new(nameof(ChoiceOptionsInherited), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ChoiceValueNotInOptions</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceValueNotInOptions { get; } = new(nameof(ChoiceValueNotInOptions), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>ChoiceIndicesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceIndicesInvalid { get; } = new(nameof(ChoiceIndicesInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets the <c>SignatureFieldDictNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode SignatureFieldDictNotIndirect { get; } = new(nameof(SignatureFieldDictNotIndirect), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        AcroFormInvalid,
        AcroFormFieldsMissing,
        AcroFormEntryInvalid,
        XfaFormPresent,
        XfaFormDynamic,
        XfaPacketsInvalid,
        XfaDataNotUpdated,
        FlattenInvalidatesSignatures,
        FieldReferenceInvalid,
        FieldNotIndirect,
        FieldTreeCycle,
        FieldTreeTooDeep,
        FieldNotRoot,
        FieldParentMissing,
        FieldParentMismatch,
        FieldKidsMixed,
        FieldNameMissing,
        FieldNameHasPeriod,
        FieldNameDuplicateInconsistent,
        FieldTypeMissing,
        ButtonFlagsConflict,
        SignatureFieldMultipleWidgets,
        WidgetNotInFieldTree,
        FieldEntryInvalid,
        FieldFlagsInvalid,
        FieldValueTypeInvalid,
        DefaultAppearanceMissing,
        DefaultAppearanceInvalid,
        DefaultAppearanceFontMissing,
        TextCombInvalid,
        ButtonValueStateMismatch,
        ButtonValueUnknownState,
        ButtonStatesAmbiguous,
        ButtonOnStateMissing,
        ButtonOptLengthMismatch,
        ChoiceOptionInvalid,
        ChoiceOptionsInherited,
        ChoiceValueNotInOptions,
        ChoiceIndicesInvalid,
        SignatureFieldDictNotIndirect,
    ];
}
