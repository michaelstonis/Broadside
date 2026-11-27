namespace Broadside.Viewer;

/// <summary>A <see cref="ViewerCommand"/> that picks one of a list of choices, such as a zoom or a page layout.</summary>
/// <remarks>Not a spec feature. A shell shows it as a combo box or a menu.</remarks>
public sealed class ViewerChoiceCommand : ViewerCommand
{
    private readonly Func<int> _selected;
    private readonly Action<int> _select;
    private readonly Func<bool>? _canExecute;
    private readonly Func<int, string>? _text;
    private int _index = -1;
    private string _current = string.Empty;

    /// <summary>Creates a choice for an app's own toolbar item.</summary>
    /// <param name="id">The identifier, unique in the toolbar.</param>
    /// <param name="label">The name shown and announced.</param>
    /// <param name="choices">The choices, in the order shown.</param>
    /// <param name="selected">Reads the selected choice's index, or -1 for none; asked again whenever the viewer changes.</param>
    /// <param name="select">Picks a choice by its index (on the UI thread).</param>
    /// <param name="canExecute">Whether a choice can be picked now, or <see langword="null"/> for always.</param>
    public ViewerChoiceCommand(string id, string label, IReadOnlyList<string> choices, Func<int> selected, Action<int> select, Func<bool>? canExecute = null)
        : this(id, label, choices, selected, select, canExecute, text: null)
    {
    }

    internal ViewerChoiceCommand(
        string id, string label, IReadOnlyList<string> choices, Func<int> selected, Action<int> select, Func<bool>? canExecute, Func<int, string>? text)
        : base(id, label)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(select);
        Choices = [.. choices];
        _selected = selected;
        _select = select;
        _canExecute = canExecute;
        _text = text;
        Update();
    }

    /// <summary>Gets the choices, in the order shown.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>
    /// Gets or sets the selected choice's index, or -1 when none matches the view (a zoom between the presets). Setting it picks that
    /// choice; setting it while the command is disabled, or to -1, does nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not -1 or a choice's.</exception>
    public int Selected
    {
        get => _index;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, Choices.Count);
            Refresh();
            if (IsEnabled && value >= 0)
            {
                _select(value);
                AfterRun();
            }
        }
    }

    /// <summary>
    /// Gets the text that stands for the current value: the selected choice, or what the view shows when no choice matches it (the
    /// zoom as a percentage, for the built-in zoom).
    /// </summary>
    public string Text => _current;

    private protected override bool Update()
    {
        bool enabled = SetEnabled(_canExecute?.Invoke() ?? true);
        int index = _selected();
        if (index < -1 || index >= Choices.Count)
        {
            index = -1;
        }

        string text = _text?.Invoke(index) ?? (index >= 0 ? Choices[index] : string.Empty);
        bool changed = index != _index || !string.Equals(text, _current, StringComparison.Ordinal);
        _index = index;
        _current = text;
        return enabled | changed;
    }
}
