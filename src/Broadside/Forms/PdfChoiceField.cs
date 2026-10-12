using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>One option of a choice field: the value exported for it and the text displayed for it.</summary>
/// <remarks>ISO 32000-2 §12.7.5.4, Table 234 (<c>Opt</c>): a text string is both; a two-element array is <c>[export display]</c>.</remarks>
public sealed class PdfChoiceOption
{
    internal PdfChoiceOption(string exportValue, string displayText)
    {
        ExportValue = exportValue;
        DisplayText = displayText;
    }

    /// <summary>Gets the value exported when the option is selected.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 234 (<c>Opt</c>): the first element of a two-element array, or the text string itself.</remarks>
    public string ExportValue { get; }

    /// <summary>Gets the text shown for the option.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 234 (<c>Opt</c>): the second element of a two-element array, or the text string itself.</remarks>
    public string DisplayText { get; }

    /// <inheritdoc/>
    public override string ToString() => ExportValue == DisplayText ? ExportValue : $"{ExportValue} ({DisplayText})";
}

/// <summary>A choice field: a list of text options, of which one or (for a multi-select list box) several are selected.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.4, Tables 233 and 234; field type <c>Ch</c>. The Combo flag (bit 18) selects a <see cref="PdfComboBoxField"/>,
/// else a <see cref="PdfListBoxField"/>. Its text is variable text (§12.7.4.3).
/// </remarks>
public abstract class PdfChoiceField : PdfTerminalField
{
    private protected PdfChoiceField(FieldInfo info, PdfFieldKind kind, CosObject[] widgets)
        : base(info, kind, widgets)
    {
    }

    /// <summary>Gets the options presented to the user, in <c>Opt</c> order (never sorted); empty when there is no <c>Opt</c>.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.4, Table 234: <c>Opt</c> is not inheritable, but an ancestor's is used when the field has none, as viewers
    /// do, with a <c>ChoiceOptionsInherited</c> diagnostic. An element that is neither a text string nor a pair of them reads as an
    /// empty option, so indices stay aligned, with a <c>ChoiceOptionInvalid</c> diagnostic.
    /// </remarks>
    public IReadOnlyList<PdfChoiceOption> Options
    {
        get
        {
            CosObject? value = Own(FormNames.Opt);
            if (value is null && Inherited(FormNames.Opt) is { } inherited)
            {
                Report(DiagnosticCodes.ChoiceOptionsInherited, "The choice field has no Opt entry, which is not inheritable (Table 234); its ancestor's is used.");
                value = inherited;
            }

            switch (value)
            {
                case null:
                    return [];
                case CosArray array:
                    var options = new PdfChoiceOption[array.Count];
                    for (int index = 0; index < array.Count; index++)
                    {
                        options[index] = ReadOption(Document.Resolve(array[index]), index);
                    }

                    return options;
                default:
                    return InvalidEntry<PdfChoiceOption[]>(FormNames.Opt, "an array") ?? [];
            }
        }
    }

    /// <summary>Gets the selected items as stored (<c>V</c>, inheritable): one text string, or an array of them; empty when nothing is selected.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.4. Writers store the export value, though the clause names the displayed text for an
    /// <c>[export display]</c> option; <see cref="SelectedIndices"/> matches either. An empty string, which some writers store for
    /// "nothing selected", is not an item.
    /// </remarks>

    public IReadOnlyList<string> Values => ReadValues(ValueObject, FormNames.V);

    /// <summary>Gets the items selected after a reset-form action (<c>DV</c>, inheritable); empty when there are none.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226, and §12.7.5.4.</remarks>
    public IReadOnlyList<string> DefaultValues => ReadValues(DefaultValueObject, FormNames.DV);

    /// <summary>
    /// Gets the indices in <see cref="Options"/> of the selected items, ascending: each value matched against the options' export
    /// values, then their displayed text. <c>I</c> decides between options with the same text when it agrees with the value.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.4, Table 234: when <c>I</c> differs from <c>V</c>, "the V entry shall be used". A value no option has records
    /// <c>ChoiceValueNotInOptions</c>, except in an editable combo box, where any text is legal.
    /// </remarks>
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            IReadOnlyList<PdfChoiceOption> options = Options;
            IReadOnlyList<string> values = Values;
            IReadOnlyList<int> stored = StoredIndices;
            if (stored.Count == values.Count && stored.Count > 0 && Matches(stored, options, values))
            {
                return stored;
            }

            var selected = new SortedSet<int>();
            foreach (string value in values)
            {
                int index = Find(options, value, selected, export: true);
                index = index >= 0 ? index : Find(options, value, selected, export: false);
                if (index >= 0)
                {
                    selected.Add(index);
                }
                else if (!AllowsFreeText)
                {
                    Report(DiagnosticCodes.ChoiceValueNotInOptions, "A value of the choice field is none of its options.");
                }
            }

            return [.. selected];
        }
    }

    /// <summary>Gets the indices of the selected options as stored in <c>I</c> (PDF 1.4); empty when absent. Entries that are not ascending in-range integers are left out.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 234; not inheritable. A left-out entry records <c>ChoiceIndicesInvalid</c>.</remarks>
    public IReadOnlyList<int> StoredIndices
    {
        get
        {
            switch (Own(FormNames.I))
            {
                case null:
                    return [];
                case CosArray array:
                    int count = Options.Count;
                    var indices = new List<int>(array.Count);
                    bool invalid = false;
                    foreach (CosObject element in array)
                    {
                        if (Document.Resolve(element) is CosInteger { Value: >= 0 } integer && integer.Value < count && (indices.Count == 0 || integer.Value > indices[^1]))
                        {
                            indices.Add((int)integer.Value);
                        }
                        else
                        {
                            invalid = true;
                        }
                    }

                    if (invalid)
                    {
                        Report(DiagnosticCodes.ChoiceIndicesInvalid, "The choice field's I array shall hold ascending indices into Opt; the entries that do not are ignored.");
                    }

                    return indices;
                default:
                    Report(DiagnosticCodes.ChoiceIndicesInvalid, "The choice field's I entry shall be an array of integers; it is ignored.");
                    return [];
            }
        }
    }

    /// <summary>Gets the index in <see cref="Options"/> of the first option visible in a scrolling list (<c>TI</c>); default 0.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 234; not inheritable.</remarks>
    public int TopIndex => ReadInteger(FormNames.TI, inheritable: false) ?? 0;

    /// <summary>Gets a value indicating whether the writer is asked to sort the options (<c>Ff</c> bit 20, Sort); readers keep <c>Opt</c> order.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 233.</remarks>
    public bool IsSort => HasFlag(PdfFieldFlags.Sort);

    /// <summary>Gets a value indicating whether several options may be selected at once (<c>Ff</c> bit 22, MultiSelect, PDF 1.4).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 233.</remarks>
    public bool IsMultiSelect => HasFlag(PdfFieldFlags.MultiSelect);

    /// <summary>Gets a value indicating whether a new selection is committed as soon as it is made (<c>Ff</c> bit 27, CommitOnSelChange, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 233.</remarks>
    public bool IsCommitOnSelectionChange => HasFlag(PdfFieldFlags.CommitOnSelChange);

    /// <summary>Gets a value indicating whether a value need not be one of the options.</summary>
    private protected virtual bool AllowsFreeText => false;

    private static bool Matches(IReadOnlyList<int> indices, IReadOnlyList<PdfChoiceOption> options, IReadOnlyList<string> values)
    {
        var remaining = values.ToList();
        foreach (int index in indices)
        {
            PdfChoiceOption option = options[index];
            int at = remaining.IndexOf(option.ExportValue);
            at = at >= 0 ? at : remaining.IndexOf(option.DisplayText);
            if (at < 0)
            {
                return false;
            }

            remaining.RemoveAt(at);
        }

        return true;
    }

    private static int Find(IReadOnlyList<PdfChoiceOption> options, string value, SortedSet<int> taken, bool export)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (!taken.Contains(index) && string.Equals(export ? options[index].ExportValue : options[index].DisplayText, value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private PdfChoiceOption ReadOption(CosObject element, int index)
    {
        switch (element)
        {
            case CosString text:
                string both = text.DecodeText();
                return new PdfChoiceOption(both, both);
            case CosArray { Count: 2 } pair when Document.Resolve(pair[0]) is CosString export && Document.Resolve(pair[1]) is CosString display:
                return new PdfChoiceOption(export.DecodeText(), display.DecodeText());
            default:
                Report(DiagnosticCodes.ChoiceOptionInvalid, $"Element {index} of the choice field's Opt array is neither a text string nor a pair of them; it reads as an empty option.");
                return new PdfChoiceOption(string.Empty, string.Empty);
        }
    }

    private string[] ReadValues(CosObject? value, CosName key)
    {
        switch (value)
        {
            case null:
                return [];
            case CosArray array:
                var values = new List<string>(array.Count);
                foreach (CosObject element in array)
                {
                    if (Document.Resolve(element) is CosString text)
                    {
                        if (text.DecodeText() is { Length: > 0 } item)
                        {
                            values.Add(item);
                        }
                    }
                    else
                    {
                        ReportValueType(key, "a text string or an array of them; an element that is not a string is ignored");
                    }
                }

                return [.. values];
            default:
                return ReadTextValue(value, key) is { Length: > 0 } single ? [single] : [];
        }
    }
}

/// <summary>A combo box: a drop-down list, optionally with a text box for a value that is not one of the options.</summary>
/// <remarks>ISO 32000-2 §12.7.5.4, Table 233; Combo flag set.</remarks>
public sealed class PdfComboBoxField : PdfChoiceField
{
    internal PdfComboBoxField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.ComboBox, widgets)
    {
    }

    /// <summary>Gets a value indicating whether the combo box includes an editable text box (<c>Ff</c> bit 19, Edit).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 233.</remarks>
    public bool IsEditable => HasFlag(PdfFieldFlags.Edit);

    /// <summary>Gets a value indicating whether text typed into the editable box shall not be spell-checked (<c>Ff</c> bit 23, DoNotSpellCheck, PDF 1.4).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.4, Table 233: meaningful only with Combo and Edit set.</remarks>
    public bool IsDoNotSpellCheck => HasFlag(PdfFieldFlags.DoNotSpellCheck);

    /// <inheritdoc/>
    private protected override bool AllowsFreeText => IsEditable;
}

/// <summary>A scrollable list box.</summary>
/// <remarks>ISO 32000-2 §12.7.5.4, Table 233; Combo flag clear.</remarks>
public sealed class PdfListBoxField : PdfChoiceField
{
    internal PdfListBoxField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.ListBox, widgets)
    {
    }
}
