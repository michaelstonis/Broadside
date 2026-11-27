namespace Broadside.Viewer;

/// <summary>A <see cref="ViewerCommand"/> that is on or off.</summary>
/// <remarks>Not a spec feature. A shell shows it as a toggle button or a checked menu item.</remarks>
public sealed class ViewerToggleCommand : ViewerCommand
{
    private readonly Func<bool> _isChecked;
    private readonly Action<bool> _setChecked;
    private readonly Func<bool>? _canExecute;
    private bool _checked;

    /// <summary>Creates a toggle for an app's own toolbar item.</summary>
    /// <param name="id">The identifier, unique in the toolbar.</param>
    /// <param name="label">The name shown and announced.</param>
    /// <param name="isChecked">Reads whether it is on; asked again whenever the viewer changes.</param>
    /// <param name="setChecked">Turns it on or off (on the UI thread).</param>
    /// <param name="canExecute">Whether it can change now, or <see langword="null"/> for always.</param>
    public ViewerToggleCommand(string id, string label, Func<bool> isChecked, Action<bool> setChecked, Func<bool>? canExecute = null)
        : base(id, label)
    {
        ArgumentNullException.ThrowIfNull(isChecked);
        ArgumentNullException.ThrowIfNull(setChecked);
        _isChecked = isChecked;
        _setChecked = setChecked;
        _canExecute = canExecute;
        Update();
    }

    /// <summary>Gets or sets whether the toggle is on; setting it while the command is disabled does nothing.</summary>
    public bool IsChecked
    {
        get => _checked;
        set
        {
            Refresh();
            if (IsEnabled && value != _checked)
            {
                _setChecked(value);
                AfterRun();
            }
        }
    }

    private protected override bool Update()
    {
        bool enabled = SetEnabled(_canExecute?.Invoke() ?? true);
        bool value = _isChecked();
        bool changed = value != _checked;
        _checked = value;
        return enabled | changed;
    }
}
