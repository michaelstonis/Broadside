using System.Windows.Input;

namespace Broadside.Viewer;

/// <summary>A <see cref="ViewerCommand"/> that runs an action when pressed.</summary>
/// <remarks>Not a spec feature. A shell shows it as a button; it is an <see cref="ICommand"/>, so a XAML template binds a button's <c>Command</c> to it.</remarks>
public sealed class ViewerButtonCommand : ViewerCommand, ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Creates a command for an app's own toolbar item.</summary>
    /// <param name="id">The identifier, unique in the toolbar.</param>
    /// <param name="label">The name shown and announced.</param>
    /// <param name="execute">What pressing it does (on the UI thread).</param>
    /// <param name="canExecute">Whether it can run now, or <see langword="null"/> for always; asked again whenever the viewer changes.</param>
    public ViewerButtonCommand(string id, string label, Action execute, Func<bool>? canExecute = null)
        : base(id, label)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        SetEnabled(canExecute?.Invoke() ?? true);
        Changed += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when <see cref="ViewerCommand.IsEnabled"/> may have changed.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Runs the command; does nothing while it is disabled.</summary>
    public void Execute()
    {
        Refresh();
        if (IsEnabled)
        {
            _execute();
            AfterRun();
        }
    }

    /// <inheritdoc/>
    bool ICommand.CanExecute(object? parameter) => IsEnabled;

    /// <inheritdoc/>
    void ICommand.Execute(object? parameter) => Execute();

    private protected override bool Update() => SetEnabled(_canExecute?.Invoke() ?? true);
}
