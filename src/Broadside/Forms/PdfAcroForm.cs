using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>A document's interactive form (AcroForm): its fields, their widgets, and the form-wide defaults. A live view over the interactive form dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.1 to §12.7.3, Table 224. All fields of a document make up one global form. The field hierarchy is read from
/// <c>Fields</c> through the fields' <c>Kids</c> once, when first needed, and read again when a dictionary or array it read has
/// changed; the field views are the same instances until then, also when read from several threads. The other entries are read
/// on every call.
/// </para>
/// <para>
/// Malformed trees (cycles, kids under two parents, missing or mismatched <c>Parent</c> entries, missing names or types) are read as
/// far as they can be, each deviation recorded as a diagnostic when the tree is read (lenient mode); in strict mode reading the
/// tree throws. Reading writes nothing to the file: no appearance is generated and no missing entry is filled in. The <c>Add</c>
/// methods (<see cref="AddTextField"/> and the others) create fields with their widgets and appearances (§12.7.4, §12.7.5).
/// </para>
/// </remarks>
public sealed class PdfAcroForm
{
    private readonly object _gate = new();
    private volatile FieldTree? _tree;

    internal PdfAcroForm(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the interactive form dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the interactive form dictionary, or <see langword="null"/> when the catalog holds it directly.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the root fields (<c>Fields</c>), in order.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224 (required): "the document's root fields (those with no ancestors in the field hierarchy)".</remarks>
    public IReadOnlyList<PdfField> Fields => Tree.Roots;

    /// <summary>Gets every field of the form, depth first in <c>Kids</c> order: each non-terminal field before its children.</summary>
    /// <remarks>ISO 32000-2 §12.7.2.</remarks>
    public IReadOnlyList<PdfField> AllFields => Tree.All;

    /// <summary>Gets the terminal fields of the form, depth first in <c>Kids</c> order.</summary>
    /// <remarks>ISO 32000-2 §12.7.2.</remarks>
    public IReadOnlyList<PdfTerminalField> TerminalFields => [.. Tree.All.OfType<PdfTerminalField>()];

    /// <summary>Gets a value indicating whether a processor shall construct appearances for all widgets (<c>NeedAppearances</c>, deprecated in PDF 2.0); default false.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224. Reported only: reading never generates appearances.</remarks>
    public bool NeedAppearances
    {
        get
        {
            switch (Get(FormNames.NeedAppearances))
            {
                case null:
                    return false;
                case CosBoolean value:
                    return value.Value;
                default:
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's NeedAppearances entry shall be a boolean; it reads as false.");
                    return false;
            }
        }
    }

    /// <summary>Gets the document-level signature flags (<c>SigFlags</c>, PDF 1.3); none when absent.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Tables 224 and 225. Bits the table does not define are kept, with a diagnostic.</remarks>
    public PdfSignatureFlags SignatureFlags
    {
        get
        {
            switch (Get(FormNames.SigFlags))
            {
                case null:
                    return PdfSignatureFlags.None;
                case CosInteger { Value: >= 0 and <= uint.MaxValue } integer:
                    if ((integer.Value & ~3L) != 0)
                    {
                        Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's SigFlags sets bits Table 225 does not define, which shall be 0; they are kept.");
                    }

                    return (PdfSignatureFlags)(int)(uint)integer.Value;
                default:
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's SigFlags entry shall be an unsigned integer; it reads as 0.");
                    return PdfSignatureFlags.None;
            }
        }
    }

    /// <summary>Gets the fields with calculation actions in the order their values are recalculated (<c>CO</c>, PDF 1.3); empty when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.3, Table 224, and §12.6.3. An element that is not one of the form's fields is skipped with an
    /// <c>AcroFormEntryInvalid</c> diagnostic. Calculations are not run.
    /// </remarks>
    public IReadOnlyList<PdfField> CalculationOrder
    {
        get
        {
            if (Get(FormNames.CO) is not { } value)
            {
                return [];
            }

            if (value is not CosArray array)
            {
                Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's CO entry shall be an array of field references; it is ignored.");
                return [];
            }

            var fields = new List<PdfField>(array.Count);
            foreach (CosObject element in array)
            {
                if (Document.Resolve(element) is CosDictionary dictionary && FindField(dictionary) is { } listed)
                {
                    fields.Add(listed);
                }
                else
                {
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "An element of the interactive form's CO array is not a field of the form; it is skipped.");
                }
            }

            return fields;
        }
    }

    /// <summary>Gets the default resources used by the form's appearance streams (<c>DR</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224, and §7.8.3. Only the form's: fields have no resources of their own.</remarks>
    public CosDictionary? DefaultResources
    {
        get
        {
            switch (Get(FormNames.DR))
            {
                case null:
                    return null;
                case CosDictionary resources:
                    return resources;
                default:
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's DR entry shall be a resource dictionary; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>Gets the document-wide default appearance string (<c>DA</c>), or <see langword="null"/>; read as bytes, one character per byte, not parsed.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224, and §12.7.4.3. Fields without a <c>DA</c> of their own or inherited use it (<see cref="PdfField.DefaultAppearance"/>).</remarks>
    public string? DefaultAppearance
    {
        get
        {
            switch (Get(FormNames.DA))
            {
                case null:
                    return null;
                case CosString value:
                    return System.Text.Encoding.Latin1.GetString(value.Bytes);
                default:
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's DA entry shall be a string; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>Gets the document-wide justification of variable text (<c>Q</c>); default left.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224, and §12.7.4.3, Table 228. A value other than 0, 1 or 2 reads as left with a diagnostic.</remarks>
    public PdfTextJustification Quadding
    {
        get
        {
            switch (Get(FormNames.Q))
            {
                case null:
                    return PdfTextJustification.Left;
                case CosInteger { Value: >= 0 and <= 2 } value:
                    return (PdfTextJustification)(int)value.Value;
                default:
                    Report(DiagnosticCodes.AcroFormEntryInvalid, "The interactive form's Q entry shall be 0, 1 or 2; it reads as 0 (left).");
                    return PdfTextJustification.Left;
            }
        }
    }

    /// <summary>Gets the form's XFA resource (<c>XFA</c>, deprecated in PDF 2.0), or <see langword="null"/> when it has none.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224, and Annex K. Kept as data; never rendered.</remarks>
    public PdfXfaForm? Xfa => Get(FormNames.XFA) is (CosStream or CosArray) and var value ? new PdfXfaForm(this, value) : null;

    /// <summary>Gets the document the form belongs to.</summary>
    internal PdfDocument Document { get; }

    private FieldTree Tree
    {
        get
        {
            FieldTree? tree = _tree;
            if (tree is not null && tree.IsCurrent(Dictionary))
            {
                return tree;
            }

            lock (_gate)
            {
                tree = _tree;
                if (tree is null || !tree.IsCurrent(Dictionary))
                {
                    tree = FieldTree.Build(this);
                    _tree = tree;
                }

                return tree;
            }
        }
    }

    /// <summary>Returns the fields whose fully qualified name is <paramref name="fullyQualifiedName"/>: usually one, several when the file repeats a name.</summary>
    /// <param name="fullyQualifiedName">The name, partial names joined by periods.</param>
    /// <returns>The fields in tree order; empty when there is none.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.2: fields with the same name are the same field and shall have the same type and values.</remarks>
    public IReadOnlyList<PdfField> FindFields(string fullyQualifiedName)
    {
        ArgumentNullException.ThrowIfNull(fullyQualifiedName);
        return Tree.ByName.TryGetValue(fullyQualifiedName, out List<PdfField>? fields) ? fields : [];
    }

    /// <summary>Returns the first field whose fully qualified name is <paramref name="fullyQualifiedName"/>, or <see langword="null"/>.</summary>
    /// <param name="fullyQualifiedName">The name, partial names joined by periods.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.2.</remarks>
    public PdfField? FindField(string fullyQualifiedName) => FindFields(fullyQualifiedName) is [var first, ..] ? first : null;

    /// <summary>Returns the field whose field dictionary is <paramref name="dictionary"/>, or <see langword="null"/> when the form's tree has none.</summary>
    /// <param name="dictionary">A field dictionary.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1.</remarks>
    public PdfField? FindField(CosDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        return Tree.ByDictionary.GetValueOrDefault(dictionary);
    }

    /// <summary>
    /// Returns the fields an action target names: a hide action's <c>T</c> or a submit-form or reset-form action's <c>Fields</c>
    /// element. A name gives every field with that fully qualified name; a dictionary gives the field it is, or the field of a widget.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>The fields; empty when the target names nothing in the form (a hide target may be an annotation that is no widget).</returns>
    /// <remarks>ISO 32000-2 §12.6.4.11, Table 214, and §12.7.6.2-12.7.6.3, Tables 239 and 241; names per §12.7.4.2. Descendants are not added.</remarks>
    public IReadOnlyList<PdfField> FindFields(PdfActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.FieldName is { } name)
        {
            return FindFields(name);
        }

        if (target.Dictionary is not { } dictionary)
        {
            return [];
        }

        FieldTree tree = Tree;
        if (tree.ByDictionary.TryGetValue(dictionary, out PdfField? field))
        {
            return [field];
        }

        return tree.ByWidget.TryGetValue(dictionary, out PdfTerminalField? owner) ? [owner] : [];
    }

    /// <summary>Creates a text field with one widget on <paramref name="page"/>.</summary>
    /// <param name="name">The field's fully qualified name: partial names joined by periods. Ancestors that do not exist are created as non-terminal fields.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="options">The field's flags, text appearance and widget colours; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field, with no value; set one with <see cref="PdfTextField.SetValue"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The name has an empty partial name, the page is not an indirect page of the document, the rectangle is not finite, or an
    /// option does not apply to a text field (a flag of another type, a caption, a comb without <c>MaxLen</c>).
    /// </exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.4 and §12.7.5.3. The field (<c>FT</c> <c>Tx</c>) is merged with its widget annotation (Table 226, <c>Kids</c>)
    /// and added to its parent's <c>Kids</c> or the form's <c>Fields</c>, the widget to the page's <c>Annots</c>, with <c>P</c>,
    /// the Print flag, and <c>MK</c> and <c>BS</c> from the options (§12.5.6.19). The field gets its own default appearance
    /// (<c>DA</c>: the options' size and colour in the form's Helvetica) and the form's default resources get Helvetica and
    /// ZapfDingbats when they lack them (§12.7.3, Table 224, <c>DR</c>, <c>DA</c>).
    /// </para>
    /// <para>
    /// Every widget gets an appearance stream, as <see cref="PdfTextField.GenerateAppearances"/> makes it, so the form needs no
    /// <c>NeedAppearances</c>. Text the form's Helvetica cannot show is drawn in an embedded subset font. When any step fails the
    /// document is left as it was. Creating a form field raises a save of a document below PDF 1.2 to 1.2, and the PDF 1.3 to 1.5
    /// entries and flags raise it further. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public PdfTextField AddTextField(string name, PdfPage page, PdfRectangle rectangle, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfTextField>(this, name, FieldCreation.Kind.Text, [(page, rectangle, null)], [], options ?? new PdfFieldOptions());

    /// <summary>Creates a check box with one widget on <paramref name="page"/>, unchecked.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="options">The field's flags, mark colour and size, widget colours and caption; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; check it with <see cref="PdfCheckBoxField.SetChecked"/>.</returns>
    /// <exception cref="ArgumentException">As for <see cref="AddTextField"/>, for a check box.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.3. A button field (<c>FT</c> <c>Btn</c>, no Radio or Pushbutton flag) merged with its widget, whose
    /// appearance subdictionary has the on state <c>Yes</c> (a check mark in ZapfDingbats, or the <see cref="PdfFieldOptions.Caption"/>
    /// character) and <c>Off</c>; <c>AS</c> is <c>Off</c>. Otherwise as <see cref="AddTextField"/> describes.
    /// </remarks>
    public PdfCheckBoxField AddCheckBox(string name, PdfPage page, PdfRectangle rectangle, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfCheckBoxField>(this, name, FieldCreation.Kind.CheckBox, [(page, rectangle, null)], [], options ?? new PdfFieldOptions());

    /// <summary>Creates a radio button field with one widget per button, none selected.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="buttons">The buttons, at least one: each widget's page, rectangle and on state.</param>
    /// <param name="options">The field's flags (NoToggleToOff, RadiosInUnison), dot colour and size, widget colours and caption; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; select a button with <see cref="PdfToggleButtonField.SetValue"/> or <see cref="PdfToggleButtonField.Select"/>.</returns>
    /// <exception cref="ArgumentException">There is no button, or as for <see cref="AddTextField"/>, for radio buttons.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.4. A button field with the Radio flag whose <c>Kids</c> are the widgets (each with <c>Parent</c>), each
    /// with an appearance subdictionary for its on state (a dot in ZapfDingbats, or the caption character) and <c>Off</c>, <c>AS</c>
    /// <c>Off</c>. RadiosInUnison needs PDF 1.5. Otherwise as <see cref="AddTextField"/> describes.
    /// </remarks>
    public PdfRadioButtonField AddRadioButtons(string name, IEnumerable<PdfRadioButton> buttons, PdfFieldOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        List<(PdfPage, PdfRectangle, CosName?)> widgets = [.. buttons.Select(button =>
        {
            ArgumentNullException.ThrowIfNull(button, nameof(buttons));
            return (button.Page, button.Rectangle, (CosName?)button.StateName);
        })];
        if (widgets.Count == 0)
        {
            throw new ArgumentException("A radio button field needs at least one button.", nameof(buttons));
        }

        return FieldCreation.Add<PdfRadioButtonField>(this, name, FieldCreation.Kind.RadioButton, widgets, [], options ?? new PdfFieldOptions());
    }

    /// <summary>Creates a combo box with one widget on <paramref name="page"/>, nothing selected.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="choices">The options, in the order shown.</param>
    /// <param name="options">The field's flags (Edit, Sort, DoNotSpellCheck, CommitOnSelChange), text appearance and widget colours; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; select an option with <see cref="PdfChoiceField.SetValue"/>.</returns>
    /// <exception cref="ArgumentException">As for <see cref="AddTextField"/>, for a combo box.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.4, Tables 233 and 234. A choice field (<c>FT</c> <c>Ch</c>) with the Combo flag and an <c>Opt</c> array
    /// (a text string per option, or <c>[export display]</c> when they differ). Otherwise as <see cref="AddTextField"/> describes.
    /// </remarks>
    public PdfComboBoxField AddComboBox(string name, PdfPage page, PdfRectangle rectangle, IEnumerable<PdfChoiceOption> choices, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfComboBoxField>(this, name, FieldCreation.Kind.ComboBox, [(page, rectangle, null)], Choices(choices), options ?? new PdfFieldOptions());

    /// <summary>Creates a list box with one widget on <paramref name="page"/>, nothing selected.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="choices">The options, in the order shown.</param>
    /// <param name="options">The field's flags (Sort, MultiSelect, CommitOnSelChange), text appearance and widget colours; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; select options with <see cref="PdfChoiceField.SetValue"/> or <see cref="PdfChoiceField.SetSelectedIndices"/>.</returns>
    /// <exception cref="ArgumentException">As for <see cref="AddTextField"/>, for a list box.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.4, Tables 233 and 234. A choice field without the Combo flag; MultiSelect needs PDF 1.4. Otherwise as
    /// <see cref="AddComboBox"/> describes.
    /// </remarks>
    public PdfListBoxField AddListBox(string name, PdfPage page, PdfRectangle rectangle, IEnumerable<PdfChoiceOption> choices, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfListBoxField>(this, name, FieldCreation.Kind.ListBox, [(page, rectangle, null)], Choices(choices), options ?? new PdfFieldOptions());

    /// <summary>Creates a push button with one widget on <paramref name="page"/>, showing <see cref="PdfFieldOptions.Caption"/>.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="options">The caption, its size and colour, and the widget colours; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; give its widget an action through its dictionary's <c>A</c> entry.</returns>
    /// <exception cref="ArgumentException">As for <see cref="AddTextField"/>, for a push button.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.2. A button field with the Pushbutton flag, merged with its widget, whose appearance shows the caption
    /// (<c>MK</c> <c>CA</c>) centred in the form's Helvetica. Otherwise as <see cref="AddTextField"/> describes.
    /// </remarks>
    public PdfPushButtonField AddPushButton(string name, PdfPage page, PdfRectangle rectangle, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfPushButtonField>(this, name, FieldCreation.Kind.PushButton, [(page, rectangle, null)], [], options ?? new PdfFieldOptions());

    /// <summary>Creates an unsigned signature field with one widget on <paramref name="page"/>.</summary>
    /// <param name="name">The field's fully qualified name, as for <see cref="AddTextField"/>.</param>
    /// <param name="page">The page the widget is on, a page of this form's document.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space; an empty one makes the signature invisible.</param>
    /// <param name="options">The widget colours; <see langword="null"/> for the defaults.</param>
    /// <returns>The new field; sign it by name with <see cref="PdfDocument.Sign(Stream, Signatures.IPdfSigner, Signatures.PdfSignatureOptions?)"/>.</returns>
    /// <exception cref="ArgumentException">As for <see cref="AddTextField"/>, for a signature field.</exception>
    /// <exception cref="InvalidOperationException">The form has a field with that name, or an ancestor named is a terminal field.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.5 (PDF 1.3) and §12.7.3, Table 225. A signature field (<c>FT</c> <c>Sig</c>, no <c>V</c>) merged with its
    /// widget, whose appearance draws the widget's background and border; the form's <c>SigFlags</c> gets SignaturesExist. Otherwise
    /// as <see cref="AddTextField"/> describes.
    /// </remarks>
    public PdfSignatureField AddSignatureField(string name, PdfPage page, PdfRectangle rectangle, PdfFieldOptions? options = null) =>
        FieldCreation.Add<PdfSignatureField>(this, name, FieldCreation.Kind.Signature, [(page, rectangle, null)], [], options ?? new PdfFieldOptions());

    /// <summary>Returns the field <paramref name="widget"/> belongs to, recording a widget that the tree from <c>Fields</c> does not reach.</summary>
    internal PdfTerminalField? FieldOf(PdfWidgetAnnotation widget)
    {
        FieldTree tree = Tree;
        if (tree.ByWidget.TryGetValue(widget.Dictionary, out PdfTerminalField? field))
        {
            return field;
        }

        PdfTerminalField? outside = tree.FindOutside(this, widget.Dictionary, widget.Reference);

        widget.Report(
            DiagnosticCodes.WidgetNotInFieldTree,
            outside is null
                ? "The widget annotation belongs to no terminal field: neither it nor a dictionary up its Parent chain is a terminal field with a partial name (T)."

                : "The widget annotation is not reached from the interactive form's Fields array through Kids (§12.7.2); its field is found through its Parent chain.");
        return outside;
    }

    /// <summary>
    /// Records, when the form has an XFA resource, that a field's value was changed in the AcroForm only: the XFA datasets keep the
    /// old value, so an XFA processor shows that one (Annex K).
    /// </summary>
    /// <param name="fieldName">The fully qualified name of the field that was filled.</param>
    internal void ReportXfaNotUpdated(string fieldName)
    {
        if (Xfa is not null)
        {
            Report(
                DiagnosticCodes.XfaDataNotUpdated,
                $"The field {fieldName} was filled in the AcroForm only: the form's XFA resource (Annex K, deprecated in PDF 2.0) is kept unchanged, so its datasets still hold the old value and an XFA processor shows that one.",
                DiagnosticSeverity.Information);
        }
    }

    /// <summary>Reports a deviation in the interactive form dictionary.</summary>
    internal void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: Reference ?? Document.CatalogReference);

    private static List<PdfChoiceOption> Choices(IEnumerable<PdfChoiceOption> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        List<PdfChoiceOption> list = [.. choices];
        if (list.Any(choice => choice is null))
        {
            throw new ArgumentException("An option is null.", nameof(choices));
        }

        return list;
    }

    private CosObject? Get(CosName key) => FieldTree.Get(Document, Dictionary, key);
}
