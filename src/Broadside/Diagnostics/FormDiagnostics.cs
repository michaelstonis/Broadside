namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about interactive forms.</summary>
/// <remarks>
/// <para>ISO 32000-2 §12.7. Each code is reported under <see cref="DiagnosticArea.Form"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FormDiagnostics
{
    /// <summary>Gets the <c>AcroFormInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormInvalid { get; } = new(nameof(AcroFormInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "The AcroForm entry in the document catalog is not an interactive form dictionary; it is ignored and the document is read without a form.");

    /// <summary>Gets the <c>AcroFormFieldsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormFieldsMissing { get; } = new(nameof(AcroFormFieldsMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "The interactive form dictionary has no Fields array, or its Fields entry is not an array; the form is read as having no fields.");

    /// <summary>Gets the <c>AcroFormEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode AcroFormEntryInvalid { get; } = new(nameof(AcroFormEntryInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "An entry of the interactive form dictionary, such as NeedAppearances, SigFlags or CO, has the wrong type or undefined bits; it is read as a default value, kept as is, or ignored.");

    /// <summary>Gets the <c>XfaFormPresent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaFormPresent { get; } = new(nameof(XfaFormPresent), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "The interactive form has an XFA resource, which is deprecated in PDF 2.0. This is recorded only; the XFA data is preserved but not used and the AcroForm fields are read.");

    /// <summary>Gets the <c>XfaFormDynamic</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaFormDynamic { get; } = new(nameof(XfaFormDynamic), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "The catalog marks the document as a dynamic XFA form whose pages are placeholders; the page content is not rendered.");

    /// <summary>Gets the <c>XfaPacketsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaPacketsInvalid { get; } = new(nameof(XfaPacketsInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "The XFA array does not alternate packet names and streams; the elements that do not fit that pattern are skipped.");

    /// <summary>Gets the <c>XfaDataNotUpdated</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.3 Table 224, Annex K</remarks>
    public static DiagnosticCode XfaDataNotUpdated { get; } = new(nameof(XfaDataNotUpdated), DiagnosticArea.Form, DiagnosticSeverity.Information, "ISO 32000-2 §12.7.3 Table 224, Annex K",
        "A field was filled through the AcroForm only. This is recorded only; the XFA resource is kept unchanged, so its datasets still hold the old value.");

    /// <summary>Gets the <c>FlattenInvalidatesSignatures</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.5.5</remarks>
    public static DiagnosticCode FlattenInvalidatesSignatures { get; } = new(nameof(FlattenInvalidatesSignatures), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.5.5",
        "The form is signed and is being flattened; flattening removes the signature fields and changes the signed document, so the signatures no longer verify.");

    /// <summary>Gets the <c>FieldReferenceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldReferenceInvalid { get; } = new(nameof(FieldReferenceInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "An element of the form Fields array or a field Kids array is not a field dictionary; it is skipped.");

    /// <summary>Gets the <c>FieldNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNotIndirect { get; } = new(nameof(FieldNotIndirect), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A field dictionary in the Fields or Kids array is a direct object instead of an indirect reference; it is read anyway.");

    /// <summary>Gets the <c>FieldTreeCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTreeCycle { get; } = new(nameof(FieldTreeCycle), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A field dictionary appears more than once in the field tree, through a cycle or a kid with two parents; only its first occurrence is read.");

    /// <summary>Gets the <c>FieldTreeTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTreeTooDeep { get; } = new(nameof(FieldTreeTooDeep), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "The field tree is nested deeper than the supported limit; fields below the limit are not read.");

    /// <summary>Gets the <c>FieldNotRoot</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNotRoot { get; } = new(nameof(FieldNotRoot), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A field in the form Fields array has a Parent entry although only root fields belong there; it is read as a root field.");

    /// <summary>Gets the <c>FieldParentMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldParentMissing { get; } = new(nameof(FieldParentMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A kid in the field tree has no Parent entry; the dictionary whose Kids array holds it is used as its parent.");

    /// <summary>Gets the <c>FieldParentMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldParentMismatch { get; } = new(nameof(FieldParentMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "The Parent entry of a kid does not name the dictionary whose Kids array holds it; the tree is followed through Kids.");

    /// <summary>Gets the <c>FieldKidsMixed</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldKidsMixed { get; } = new(nameof(FieldKidsMixed), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "The Kids array of a field holds both fields and widget annotations; the widget annotations are ignored.");

    /// <summary>Gets the <c>FieldNameMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameMissing { get; } = new(nameof(FieldNameMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A dictionary in the field tree has no partial name. Its kids are read as kids of the field above, or it is skipped as a widget, or its widgets are ignored when it is an unnamed root.");

    /// <summary>Gets the <c>FieldNameHasPeriod</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameHasPeriod { get; } = new(nameof(FieldNameHasPeriod), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A partial field name contains a period, which is the separator in fully qualified names; the name is kept, so looking the field up by name may be ambiguous.");

    /// <summary>Gets the <c>FieldNameDuplicateInconsistent</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldNameDuplicateInconsistent { get; } = new(nameof(FieldNameDuplicateInconsistent), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "Fields with the same fully qualified name have different field types, values or default values, although they should be identical; each is read as written.");

    /// <summary>Gets the <c>FieldTypeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode FieldTypeMissing { get; } = new(nameof(FieldTypeMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A terminal field has no field type, or its type is not one that is defined, even through inheritance; its type is treated as unknown.");

    /// <summary>Gets the <c>ButtonFlagsConflict</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode ButtonFlagsConflict { get; } = new(nameof(ButtonFlagsConflict), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A button field sets both the Pushbutton and Radio flags, which exclude each other; it is read as a push button.");

    /// <summary>Gets the <c>SignatureFieldMultipleWidgets</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode SignatureFieldMultipleWidgets { get; } = new(nameof(SignatureFieldMultipleWidgets), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A signature field refers to more than one widget annotation, which is not allowed; all its widgets are kept.");

    /// <summary>Gets the <c>WidgetNotInFieldTree</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2</remarks>
    public static DiagnosticCode WidgetNotInFieldTree { get; } = new(nameof(WidgetNotInFieldTree), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.2, §12.7.4.1, §12.7.4.2",
        "A widget annotation belongs to no terminal field, since neither it nor any dictionary up its Parent chain is a terminal field with a name; it is not treated as a form field.");

    /// <summary>Gets the <c>FieldEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldEntryInvalid { get; } = new(nameof(FieldEntryInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A field entry such as Kids, T or Q has the wrong type or value; it is ignored, read in its text form, or read as a default such as an empty name or left alignment.");

    /// <summary>Gets the <c>FieldFlagsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldFlagsInvalid { get; } = new(nameof(FieldFlagsInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The Ff flags entry of a field is negative, a real number, not a number, or sets undefined bits; it is read as its bit pattern, truncated, read as 0, or kept as is.");

    /// <summary>Gets the <c>FieldValueTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode FieldValueTypeInvalid { get; } = new(nameof(FieldValueTypeInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A field entry such as V, DV or MaxLen does not have the type required for its field type; the value is read as far as it can be.");

    /// <summary>Gets the <c>DefaultAppearanceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceMissing { get; } = new(nameof(DefaultAppearanceMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A field with variable text has no default appearance string in itself, its ancestors or the interactive form; defaults are used when drawing its text.");

    /// <summary>Gets the <c>DefaultAppearanceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceInvalid { get; } = new(nameof(DefaultAppearanceInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The default appearance string of a field is malformed; the parts that can be read are used.");

    /// <summary>Gets the <c>DefaultAppearanceFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode DefaultAppearanceFontMissing { get; } = new(nameof(DefaultAppearanceFontMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The font named in the default appearance string of a field is not in the default resources of the form; the font cannot be resolved from them.");

    /// <summary>Gets the <c>TextCombInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode TextCombInvalid { get; } = new(nameof(TextCombInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A text field sets the Comb flag without a MaxLen entry, or together with the Multiline, Password or FileSelect flag; the combination is invalid and recorded.");

    /// <summary>Gets the <c>ButtonValueStateMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonValueStateMismatch { get; } = new(nameof(ButtonValueStateMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The appearance state of a widget differs from the value of its button field, which should match; the appearance state decides what is shown.");

    /// <summary>Gets the <c>ButtonValueUnknownState</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonValueUnknownState { get; } = new(nameof(ButtonValueUnknownState), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The value of a button field is not the on state of any of its widgets; nothing is shown as selected.");

    /// <summary>Gets the <c>ButtonStatesAmbiguous</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonStatesAmbiguous { get; } = new(nameof(ButtonStatesAmbiguous), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A widget of a button field has more than one on state in its normal appearance; the one equal to the field value, otherwise the first, is used.");

    /// <summary>Gets the <c>ButtonOnStateMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonOnStateMissing { get; } = new(nameof(ButtonOnStateMissing), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A widget of a button field has appearance states but none other than Off, so it has no on state and cannot be shown as selected.");

    /// <summary>Gets the <c>ButtonOptLengthMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ButtonOptLengthMismatch { get; } = new(nameof(ButtonOptLengthMismatch), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The Opt array of a button field does not have one entry per widget in Kids; the entries are read as far as they match.");

    /// <summary>Gets the <c>ChoiceOptionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceOptionInvalid { get; } = new(nameof(ChoiceOptionInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "An element of the Opt array of a choice field is neither a text string nor a pair of them; it is read as an empty option.");

    /// <summary>Gets the <c>ChoiceOptionsInherited</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceOptionsInherited { get; } = new(nameof(ChoiceOptionsInherited), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A choice field has no Opt entry of its own, which is not inheritable; the Opt of an ancestor is used.");

    /// <summary>Gets the <c>ChoiceValueNotInOptions</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceValueNotInOptions { get; } = new(nameof(ChoiceValueNotInOptions), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A value of a choice field is none of its options; the value is kept as written.");

    /// <summary>Gets the <c>ChoiceIndicesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode ChoiceIndicesInvalid { get; } = new(nameof(ChoiceIndicesInvalid), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "The I array of a choice field is not an array of integers or does not hold ascending indices into Opt; the entries that are invalid, or the whole entry, are ignored.");

    /// <summary>Gets the <c>SignatureFieldDictNotIndirect</c> code.</summary>
    /// <remarks>ISO 32000-2 §12.7.4, §12.7.5</remarks>
    public static DiagnosticCode SignatureFieldDictNotIndirect { get; } = new(nameof(SignatureFieldDictNotIndirect), DiagnosticArea.Form, DiagnosticSeverity.Warning, "ISO 32000-2 §12.7.4, §12.7.5",
        "A signature field entry that should be an indirect reference is a direct dictionary; it is read anyway.");

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
