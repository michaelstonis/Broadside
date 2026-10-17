using Microsoft.AspNetCore.Components;

namespace Broadside.Viewer.Blazor.Sample.Panels;

/// <summary>
/// A panel of the demo: it follows <see cref="Sample.Demo"/> itself (Blazor does not re-render a child whose parameters did not change),
/// so <see cref="OnDemoChangedAsync"/> runs when it opens and after every change of the demo (another document, an edit, the viewer's
/// page or zoom), followed by a render.
/// </summary>
public abstract class DemoPanel : ComponentBase, IDisposable
{
    private bool _disposed;

    /// <summary>Gets the demo's state.</summary>
    [Inject]
    protected Demo Demo { get; private set; } = default!;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops following the demo and runs <see cref="OnClosed"/>.</summary>
    /// <param name="disposing">Whether called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Demo.Changed -= OnChanged;
            OnClosed();
        }
    }

    /// <inheritdoc/>
    protected override Task OnInitializedAsync()
    {
        Demo.Changed += OnChanged;
        return OnDemoChangedAsync();
    }

    /// <summary>Runs when the panel opens and after every change of the demo; compare <see cref="Sample.Demo.Version"/> to skip work.</summary>
    protected virtual Task OnDemoChangedAsync() => Task.CompletedTask;

    /// <summary>Runs when the panel closes (cancel background work here).</summary>
    protected virtual void OnClosed()
    {
    }

    private void OnChanged(object? sender, EventArgs e) => _ = InvokeAsync(async () =>
    {
        if (!_disposed)
        {
            await OnDemoChangedAsync().ConfigureAwait(true);
            StateHasChanged();
        }
    });
}
