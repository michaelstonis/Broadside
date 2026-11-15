using System.Globalization;
using System.Text;
using Broadside.Annotations;
using Broadside.Forms;

namespace Broadside.Viewer;

/// <summary>
/// The viewer's form filling, independent of any UI framework: keyboard focus through the fields in each page's tab order, an edit of
/// the focused field (text with a caret and a selection, an option being picked) committed through the form-filling API once, when
/// the edit ends, check boxes and radio buttons toggled from the keyboard, reset-form actions performed, and required fields checked.
/// Each commit redraws only the field's overlay tiles (<see cref="AnnotationLayer"/>).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7 (interactive forms): §12.5.1 and Table 31 (<c>Tabs</c>, the tab order), §12.7.4.1 (Table 227: ReadOnly and
/// Required), §12.7.5.2 (check boxes and radio buttons), §12.7.5.3 (text fields: <c>MaxLen</c>, multi-line, comb), §12.7.5.4 (choice
/// fields: combo boxes, editable ones, list boxes, multiple selection) and §12.7.6.3 (reset-form action); spec #33 viewer
/// capability 12.
/// </para>
/// <para>
/// The shell sends its key presses to <see cref="KeyDown"/> and typed text (including what an input method composes) to
/// <see cref="TextInput"/> before its own key handling; a shell that edits in a native text box over the widget sends the box's
/// text through <see cref="SetText"/> instead and calls <see cref="Commit"/> when the box loses focus. Filling through these calls
/// produces the same document, and the same saved file, as calling the form API with the final values in the same order. Like the
/// layer, it is single-threaded: call it from the shell's UI thread.
/// </para>
/// </remarks>
public sealed class FormFilling
{
    private readonly AnnotationLayer _layer;
    private readonly List<int> _selected = [];
    private string _text = string.Empty;
    private string _original = string.Empty;
    private int[] _originalSelection = [];
    private int _caret;
    private int _anchor;
    private bool _syncing;

    /// <summary>Initializes a new instance of the <see cref="FormFilling"/> class over a layer's fields.</summary>
    /// <param name="layer">The annotation layer whose focus and fields the form filling follows.</param>
    /// <remarks>Spec #33 viewer capability 12.</remarks>
    public FormFilling(AnnotationLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layer = layer;
        layer.Changed += (_, _) => Sync();
        layer.ActionRequested += OnActionRequested;
        Sync();
    }

    /// <summary>Raised when the edit changes: the field edited, its text, caret, selection or picked options.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the annotation layer.</summary>
    public AnnotationLayer Layer => _layer;

    /// <summary>Gets the focused widget when it belongs to a form field, or <see langword="null"/>.</summary>
    public AnnotationTarget? Target { get; private set; }

    /// <summary>Gets the field of <see cref="Target"/>, or <see langword="null"/>.</summary>
    public PdfTerminalField? Field { get; private set; }

    /// <summary>Gets how the focused field is filled.</summary>
    public FieldEditKind Kind { get; private set; }

    /// <summary>Gets a value indicating whether the focused field is read-only: it takes focus but no edit (Table 227, ReadOnly).</summary>
    public bool IsReadOnly => Field is { IsReadOnly: true };

    /// <summary>Gets a value indicating whether the focused field is required before the form is submitted (Table 227, Required).</summary>
    public bool IsRequired => Field is { IsRequired: true };

    /// <summary>Gets a value indicating whether the edit differs from the field's value (<see cref="Commit"/> writes it).</summary>
    public bool IsDirty => Kind switch
    {
        FieldEditKind.Text => !string.Equals(_text, _original, StringComparison.Ordinal),
        FieldEditKind.Choice => !_selected.SequenceEqual(_originalSelection) || (IsEditableCombo && !string.Equals(_text, _original, StringComparison.Ordinal)),
        _ => false,
    };

    /// <summary>Gets the text being edited: a text field's, or an editable combo box's typed text; empty otherwise.</summary>
    public string Text => _text;

    /// <summary>Gets the caret's position in <see cref="Text"/>, in UTF-16 code units.</summary>
    public int Caret => _caret;

    /// <summary>Gets where the text selection starts in <see cref="Text"/> (the lower of the anchor and the caret).</summary>
    public int SelectionStart => Math.Min(_anchor, _caret);

    /// <summary>Gets how long the text selection is; 0 when only the caret shows.</summary>
    public int SelectionLength => Math.Abs(_caret - _anchor);

    /// <summary>Gets the options picked in the focused choice field, ascending (committed by <see cref="Commit"/>).</summary>
    public IReadOnlyList<int> SelectedOptions => _selected;

    /// <summary>Gets the option the keyboard is on in a choice field (arrows move it), or -1.</summary>
    public int HighlightedOption { get; private set; } = -1;

    /// <summary>Gets the most characters the focused text field takes (<c>MaxLen</c>), or <see langword="null"/>.</summary>
    public int? MaxLength => Field is PdfTextField text ? text.MaxLength : null;

    private bool IsEditableCombo => Field is PdfComboBoxField { IsEditable: true };

    /// <summary>
    /// A key press. Tab and Shift+Tab end the edit and move focus in tab order; Enter commits a single-line edit (a new line in a
    /// multi-line field) or activates a button; Escape abandons the edit; arrows, Home, End, Backspace and Delete edit text (Shift
    /// extends the selection) or move through a choice field's options; Space toggles a check box, turns on a radio button, or picks
    /// the highlighted option of a list box.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held.</param>
    /// <returns>Whether the form filling handled it (the shell then skips its own handling).</returns>
    public bool KeyDown(InputKey key, InputModifiers modifiers)
    {
        bool shift = (modifiers & InputModifiers.Shift) != 0;
        if (key == InputKey.Tab)
        {
            Commit();
            return shift ? _layer.FocusPrevious() : _layer.FocusNext();
        }

        if (Target is not { } target)
        {
            return false;
        }

        if (IsReadOnly)
        {
            return false;
        }

        switch (Kind)
        {
            case FieldEditKind.Button:
                if (key is InputKey.Space && Field is PdfToggleButtonField)
                {
                    return _layer.ToggleField();
                }

                if (key is InputKey.Enter or InputKey.Space)
                {
                    _layer.Activate(target);
                    return true;
                }

                return false;
            case FieldEditKind.Text:
                return TextKey(key, shift);
            case FieldEditKind.Choice:
                // An editable combo box edits its text; its arrows still move through the options.
                return IsEditableCombo ? TextKey(key, shift) : ChoiceKey(key, shift);
            default:
                return false;
        }
    }

    /// <summary>
    /// Typed text: replaces the selection in a text edit (line breaks dropped in a single-line field, the text cut at <c>MaxLen</c>),
    /// or, in a choice field that takes no text, picks the next option whose text starts with it.
    /// </summary>
    /// <param name="text">The text typed or composed.</param>
    /// <returns>Whether the form filling took it.</returns>
    public bool TextInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Target is null || IsReadOnly || text.Length == 0)
        {
            return false;
        }

        if (Kind == FieldEditKind.Text || (Kind == FieldEditKind.Choice && IsEditableCombo))
        {
            Insert(text);
            return true;
        }

        if (Kind == FieldEditKind.Choice && Field is PdfChoiceField choice)
        {
            IReadOnlyList<PdfChoiceOption> options = choice.Options;
            for (int step = 1; step <= options.Count; step++)
            {
                int index = (Math.Max(HighlightedOption, -1) + step) % options.Count;
                if (options[index].DisplayText.StartsWith(text, ignoreCase: true, CultureInfo.InvariantCulture))
                {
                    Pick(index, toggle: false);
                    return true;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the whole edit text, from a shell's native text box over the widget: cut at <c>MaxLen</c> and to one line as typed text
    /// is. In an editable combo box, text equal to an option's picks that option.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="caret">The caret position, or <see langword="null"/> for the end.</param>
    /// <returns>Whether a text edit is open (a writable text field or editable combo box is focused).</returns>
    public bool SetText(string text, int? caret = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Target is null || IsReadOnly || !(Kind == FieldEditKind.Text || IsEditableCombo))
        {
            return false;
        }

        _text = Limit(Clean(text), int.MaxValue);
        _caret = _anchor = Math.Clamp(caret ?? _text.Length, 0, _text.Length);
        if (IsEditableCombo)
        {
            MatchOption();
        }

        Raise();
        return true;
    }

    /// <summary>Picks options of the focused choice field, from a shell's native picker: one for a combo box or single-select list box.</summary>
    /// <param name="indices">The option indices.</param>
    /// <returns>Whether a writable choice field is focused.</returns>
    public bool SetSelectedOptions(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        if (Kind != FieldEditKind.Choice || IsReadOnly || Field is not PdfChoiceField choice)
        {
            return false;
        }

        int count = choice.Options.Count;
        _selected.Clear();
        _selected.AddRange(indices.Where(i => i >= 0 && i < count).Distinct().Order());
        if (!choice.IsMultiSelect && _selected.Count > 1)
        {
            _selected.RemoveRange(1, _selected.Count - 1);
        }

        HighlightedOption = _selected.Count > 0 ? _selected[0] : HighlightedOption;
        if (IsEditableCombo && _selected.Count > 0)
        {
            _text = choice.Options[_selected[0]].DisplayText;
            _caret = _anchor = _text.Length;
        }

        Raise();
        return true;
    }

    /// <summary>Selects all of the edit text.</summary>
    /// <returns>Whether a text edit is open.</returns>
    public bool SelectAll()
    {
        if (!(Kind == FieldEditKind.Text || IsEditableCombo))
        {
            return false;
        }

        _anchor = 0;
        _caret = _text.Length;
        Raise();
        return true;
    }

    /// <summary>
    /// Writes the edit to the field through the form-filling API when it differs from the field's value (one
    /// <see cref="PdfTextField.SetValue"/>, <see cref="PdfChoiceField.SetValue"/> or <see cref="PdfChoiceField.SetSelectedIndices"/>),
    /// which regenerates the field's appearances and redraws only its overlay tiles. Called for the shell when focus leaves the field.
    /// </summary>
    /// <returns>Whether the field changed.</returns>
    public bool Commit()
    {
        if (Target is not { } target || IsReadOnly || !IsDirty)
        {
            return false;
        }

        bool changed;
        if (Kind == FieldEditKind.Text)
        {
            changed = _layer.SetFieldText(target, _text);
        }
        else if (IsEditableCombo && _selected.Count == 0)
        {
            changed = _layer.SetFieldChoice(target, _text.Length == 0 ? null : _text);
        }
        else
        {
            changed = _layer.SetFieldSelection(target, _selected);
        }

        Begin(target, keepCaret: true);
        Raise();
        return changed;
    }

    /// <summary>Abandons the edit: the text and picked options go back to the field's value.</summary>
    /// <returns>Whether there was an edit to abandon.</returns>
    public bool Cancel()
    {
        if (Target is not { } target || !IsDirty)
        {
            return false;
        }

        Begin(target, keepCaret: false);
        Raise();
        return true;
    }

    /// <summary>
    /// Lists the required fields (Table 227, Required) that have no value: an empty text, no option, a check box or radio buttons off.
    /// </summary>
    /// <param name="fields">The fields to check, such as a submit-form action's <see cref="PdfSubmitFormAction.SelectFields"/>; <see langword="null"/> for every field of the form.</param>
    /// <returns>The fields, in the order given.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227: a required field "shall have a value at the time it is exported by a submit-form action".</remarks>
    public IReadOnlyList<PdfTerminalField> GetMissingRequiredFields(IEnumerable<PdfTerminalField>? fields = null)
    {
        fields ??= _layer.Session.Document.AcroForm?.TerminalFields ?? [];
        return [.. fields.Where(field => field.IsRequired && !HasValue(field))];

        static bool HasValue(PdfTerminalField field) => field switch
        {
            PdfTextField text => !string.IsNullOrEmpty(text.Value),
            PdfChoiceField choice => choice.Values.Count > 0,
            PdfToggleButtonField toggle => toggle.SelectedWidgets.Count > 0,
            _ => true,
        };
    }

    // ---- Internals ----

    private void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        // A reset-form action is the form's own: performed here, never a script (§12.7.6.3). Submit and import stay with the shell.
        if (e.Action is PdfResetFormAction reset && _layer.ResetFields(reset.SelectFields()) > 0 && Target is { } target)
        {
            Begin(target, keepCaret: false);
            Raise();
        }
    }

    private void Sync()
    {
        if (_syncing)
        {
            return;
        }

        AnnotationTarget? focused = _layer.Focused;
        if (focused == Target)
        {
            return;
        }

        _syncing = true;
        try
        {
            // Focus moved by a click, the keyboard or the accessibility bridge: the edit of the field it left is kept.
            Commit();
        }
        finally
        {
            _syncing = false;
        }

        if (focused is { } target && _layer.GetAnnotation(target) is PdfWidgetAnnotation { Field: not null })
        {
            Begin(target, keepCaret: false);
        }
        else
        {
            Target = null;
            Field = null;
            Kind = FieldEditKind.None;
            _text = _original = string.Empty;
            _originalSelection = [];
            _selected.Clear();
            _caret = _anchor = 0;
            HighlightedOption = -1;
        }

        Raise();
    }

    /// <summary>Starts (or restarts, after a commit) the edit of a field from its current value.</summary>
    private void Begin(AnnotationTarget target, bool keepCaret)
    {
        var widget = (PdfWidgetAnnotation)_layer.GetAnnotation(target);
        PdfTerminalField field = widget.Field!;
        Target = target;
        Field = field;
        _selected.Clear();
        HighlightedOption = -1;
        switch (field)
        {
            case PdfTextField text:
                Kind = FieldEditKind.Text;
                _text = text.Value ?? string.Empty;
                break;
            case PdfChoiceField choice:
                Kind = FieldEditKind.Choice;
                _selected.AddRange(choice.SelectedIndices);
                HighlightedOption = _selected.Count > 0 ? _selected[0] : -1;
                _text = choice is PdfComboBoxField { IsEditable: true }
                    ? (_selected.Count > 0 ? choice.Options[_selected[0]].DisplayText : choice.Values is [var typed, ..] ? typed : string.Empty)
                    : string.Empty;
                break;
            case PdfButtonField:
                Kind = FieldEditKind.Button;
                _text = string.Empty;
                break;
            default:
                Kind = FieldEditKind.None;
                _text = string.Empty;
                break;
        }

        _original = _text;
        _originalSelection = [.. _selected];
        if (keepCaret)
        {
            _caret = Math.Min(_caret, _text.Length);
            _anchor = _caret;
        }
        else
        {
            // Focus selects the whole value, so typing replaces it, as text boxes do.
            _anchor = 0;
            _caret = _text.Length;
        }
    }

    private bool TextKey(InputKey key, bool shift)
    {
        bool multiline = Field is PdfTextField { IsMultiline: true };
        switch (key)
        {
            case InputKey.ArrowLeft:
                MoveCaret(SelectionLength > 0 && !shift ? SelectionStart : Previous(_caret), shift);
                return true;
            case InputKey.ArrowRight:
                MoveCaret(SelectionLength > 0 && !shift ? SelectionStart + SelectionLength : Next(_caret), shift);
                return true;
            case InputKey.Home:
                MoveCaret(multiline ? LineStart(_caret) : 0, shift);
                return true;
            case InputKey.End:
                MoveCaret(multiline ? LineEnd(_caret) : _text.Length, shift);
                return true;
            case InputKey.ArrowUp or InputKey.ArrowDown when IsEditableCombo:
                return ChoiceKey(key, shift);
            case InputKey.Backspace:
                if (SelectionLength == 0)
                {
                    _anchor = Previous(_caret);
                }

                Insert(string.Empty);
                return true;
            case InputKey.Delete:
                if (SelectionLength == 0)
                {
                    _anchor = Next(_caret);
                }

                Insert(string.Empty);
                return true;
            case InputKey.Space:
                Insert(" ");
                return true;
            case InputKey.Enter when multiline:
                Insert("\n");
                return true;
            case InputKey.Enter:
                Commit();
                return true;
            case InputKey.Escape:
                return Cancel();
            default:
                return false;
        }
    }

    private bool ChoiceKey(InputKey key, bool shift)
    {
        var choice = (PdfChoiceField)Field!;
        int count = choice.Options.Count;
        bool multi = choice.IsMultiSelect;
        switch (key)
        {
            case InputKey.ArrowDown or InputKey.ArrowUp or InputKey.Home or InputKey.End when count > 0:
                int index = key switch
                {
                    InputKey.ArrowDown => Math.Min(count - 1, HighlightedOption + 1),
                    InputKey.ArrowUp => Math.Max(0, HighlightedOption < 0 ? 0 : HighlightedOption - 1),
                    InputKey.Home => 0,
                    _ => count - 1,
                };
                if (multi && !shift)
                {
                    // In a multi-select list box arrows only move the highlight; Space picks.
                    HighlightedOption = index;
                    Raise();
                }
                else
                {
                    Pick(index, toggle: false);
                }

                return true;
            case InputKey.Space when HighlightedOption >= 0:
                Pick(HighlightedOption, toggle: multi);
                return true;
            case InputKey.Enter:
                Commit();
                return true;
            case InputKey.Escape:
                return Cancel();
            default:
                return false;
        }
    }

    /// <summary>Picks an option: the only one, or toggled in a multi-select list box.</summary>
    private void Pick(int index, bool toggle)
    {
        var choice = (PdfChoiceField)Field!;
        HighlightedOption = index;
        if (toggle)
        {
            if (!_selected.Remove(index))
            {
                _selected.Add(index);
                _selected.Sort();
            }
        }
        else
        {
            _selected.Clear();
            _selected.Add(index);
        }

        if (IsEditableCombo)
        {
            _text = choice.Options[index].DisplayText;
            _caret = _anchor = _text.Length;
        }

        Raise();
    }

    /// <summary>Replaces the selection with text, as typed: one line unless multi-line, cut at <c>MaxLen</c>.</summary>
    private void Insert(string text)
    {
        int start = SelectionStart;
        int length = SelectionLength;
        string rest = _text.Remove(start, length);
        int room = MaxLength is int max and > 0 ? Math.Max(0, max - RuneCount(rest)) : int.MaxValue;
        string inserted = Limit(Clean(text), room);
        _text = rest.Insert(start, inserted);
        _caret = _anchor = start + inserted.Length;
        if (IsEditableCombo)
        {
            MatchOption();
        }

        Raise();
    }

    /// <summary>In an editable combo box, picks the option the text is, or none (the text is then the value).</summary>
    private void MatchOption()
    {
        var choice = (PdfChoiceField)Field!;
        _selected.Clear();
        IReadOnlyList<PdfChoiceOption> options = choice.Options;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].DisplayText, _text, StringComparison.Ordinal))
            {
                _selected.Add(i);
                break;
            }
        }

        HighlightedOption = _selected.Count > 0 ? _selected[0] : -1;
    }

    private string Clean(string text) =>
        Field is PdfTextField { IsMultiline: true }
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    private string Limit(string text, int room)
    {
        if (MaxLength is int max and > 0)
        {
            room = Math.Min(room, max);
        }

        if (room == int.MaxValue || RuneCount(text) <= room)
        {
            return text;
        }

        var kept = new StringBuilder();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (room-- <= 0)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString();
    }

    private static int RuneCount(string text)
    {
        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private void MoveCaret(int position, bool extend)
    {
        _caret = Math.Clamp(position, 0, _text.Length);
        if (!extend)
        {
            _anchor = _caret;
        }

        Raise();
    }

    private int Previous(int index) =>
        index <= 0 ? 0 : index >= 2 && char.IsLowSurrogate(_text[index - 1]) && char.IsHighSurrogate(_text[index - 2]) ? index - 2 : index - 1;

    private int Next(int index) =>
        index >= _text.Length ? _text.Length : index + 1 < _text.Length && char.IsHighSurrogate(_text[index]) && char.IsLowSurrogate(_text[index + 1]) ? index + 2 : index + 1;

    private int LineStart(int index) => index == 0 ? 0 : _text.LastIndexOf('\n', index - 1) + 1;

    private int LineEnd(int index) => _text.IndexOf('\n', index) is int end and >= 0 ? end : _text.Length;

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>How the focused form field is filled (<see cref="FormFilling.Kind"/>).</summary>
/// <remarks>ISO 32000-2 §12.7.5 (field types); spec #33 viewer capability 12.</remarks>
public enum FieldEditKind
{
    /// <summary>No form field is focused, or it is one the viewer does not fill (a signature field).</summary>
    None,

    /// <summary>A text field: typed text with a caret (§12.7.5.3).</summary>
    Text,

    /// <summary>A combo box or list box: options picked, or text typed in an editable combo box (§12.7.5.4).</summary>
    Choice,

    /// <summary>A check box, radio button or push button: toggled or activated (§12.7.5.2).</summary>
    Button,
}
