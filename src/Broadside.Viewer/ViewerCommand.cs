using System.ComponentModel;

namespace Broadside.Viewer;

/// <summary>
/// One item of a <see cref="ViewerToolbar"/>: what a shell shows (a label, a glyph, a tool tip, a shortcut), whether it can run now,
/// and where it goes (<see cref="Placement"/>). The kinds are <see cref="ViewerButtonCommand"/>, <see cref="ViewerToggleCommand"/>,
/// <see cref="ViewerChoiceCommand"/>, <see cref="ViewerPageCommand"/> and <see cref="ViewerSearchCommand"/>; a shell picks a native
/// control for each.
/// </summary>
/// <remarks>
/// Not a spec feature. A command reads the viewer it is bound to and never the file; a command whose state changed raises
/// <see cref="Changed"/> and <see cref="PropertyChanged"/> (on the UI thread), so a XAML template binds to its properties. Labels and glyphs are English text until the viewer ships localization tables; the
/// <see cref="Id"/> is the stable key those tables will use.
/// </remarks>
public abstract class ViewerCommand : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs AllProperties = new(string.Empty);

    private ViewerCommandPlacement _placement = ViewerCommandPlacement.Primary;
    private bool _isEnabled = true;
    private string? _error;

    private protected ViewerCommand(string id, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Label = label;
    }

    /// <summary>Raised when the command's state (enablement, placement, value, text or error) changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with an empty property name (every property may have changed) whenever <see cref="Changed"/> is.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the command's identifier, unique in its toolbar (<see cref="ViewerCommandIds"/> for the built-in ones).</summary>
    public string Id { get; }

    /// <summary>Gets the command's name, what an accessible name or an overflow menu item shows.</summary>
    public string Label { get; }

    /// <summary>Gets a short symbol a compact button shows instead of <see cref="Label"/>, or <see langword="null"/> to show the label.</summary>
    public string? Glyph { get; init; }

    /// <summary>Gets the tool tip, or <see langword="null"/>.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Gets the keyboard shortcut written as <c>Ctrl+F</c> (a shell shows Ctrl as ⌘ where that is the platform's key), or <see langword="null"/>.</summary>
    public string? Shortcut { get; init; }

    /// <summary>Gets a value indicating whether the command can run now.</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Gets the reason the last input was refused (a page that does not exist), or <see langword="null"/>; it clears with the next
    /// accepted input.
    /// </summary>
    public string? Error => _error;

    /// <summary>Gets or sets where a shell shows the command: in the bar, in its overflow menu, or not at all.</summary>
    public ViewerCommandPlacement Placement
    {
        get => _placement;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a placement.");
            }

            if (_placement != value)
            {
                _placement = value;
                OnChanged();
            }
        }
    }

    /// <summary>Gets the toolbar the command is in, or <see langword="null"/>.</summary>
    internal ViewerToolbar? Owner { get; set; }

    /// <summary>Works out the command's state again from its viewer; returns whether anything changed (and raises <see cref="Changed"/> then).</summary>
    internal bool Refresh()
    {
        bool changed = Update();
        if (changed)
        {
            OnChanged();
        }

        return changed;
    }

    /// <summary>Updates the state the kind adds, and the enablement; returns whether anything changed.</summary>
    private protected abstract bool Update();

    /// <summary>Sets <see cref="IsEnabled"/>; returns whether it changed.</summary>
    private protected bool SetEnabled(bool enabled)
    {
        bool changed = _isEnabled != enabled;
        _isEnabled = enabled;
        return changed;
    }

    /// <summary>Sets <see cref="Error"/>; returns whether it changed.</summary>
    private protected bool SetError(string? error)
    {
        bool changed = !string.Equals(_error, error, StringComparison.Ordinal);
        _error = error;
        return changed;
    }

    /// <summary>Raises <see cref="Changed"/>.</summary>
    private protected void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, AllProperties);
    }

    /// <summary>After the command ran: the toolbar works out every command's state again (a custom command's may depend on it).</summary>
    private protected void AfterRun()
    {
        if (Owner is { } owner)
        {
            owner.Refresh();
        }
        else
        {
            Refresh();
        }
    }
}
