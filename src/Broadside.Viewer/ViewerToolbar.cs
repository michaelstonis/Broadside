using System.Collections.ObjectModel;
using System.Globalization;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The model of a viewer toolbar, bound to one <see cref="IDocumentViewer"/>: the page buttons and box, zoom, layout, rotation, find
/// and dark mode (<see cref="ViewerCommandIds"/>), and any commands an app adds. It holds no UI: each shell's <c>PdfToolbar</c> lays
/// <see cref="Items"/> out with native controls and follows <see cref="Changed"/>, so an app restyles or reorders the bar without
/// rebuilding its logic.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels) and the default visibility (§12.2, Table 147,
/// <c>HideToolbar</c>, read through <see cref="PdfViewerPreferences.HideToolbar"/>).
/// </para>
/// <para>
/// Create it, use it and dispose it on the UI thread. It reads only what the viewer exposes and two things of the open session through
/// its read lease: the viewer preferences and, when the viewer has none, the page labels. Without a session every built-in command is
/// disabled; an app's own commands decide for themselves. Input that names no page leaves the view where it is and sets the page
/// command's <see cref="ViewerCommand.Error"/>; nothing throws.
/// </para>
/// </remarks>
public sealed class ViewerToolbar : IDisposable
{
    private const double ZoomStep = 1.25;
    private const int FitModes = 3;

    private static readonly string[] LayoutNames = ["Single page", "Continuous", "Two-up", "Two-up continuous", "Horizontal"];

    private readonly List<ViewerCommand> _items = [];
    private readonly Lock _gate = new();
    private readonly SynchronizationContext? _context;
    private readonly IReadOnlyList<double> _presets;
    private readonly bool _honorHideToolbar;
    private DocumentSession? _session;
    private DocumentSearch? _search;
    private PageLabelTable? _labels;
    private bool _hiddenByDocument;
    private bool _visible = true;
    private int? _zoomChoice;
    private int _generation;
    private int _refreshing;
    private bool _dirty;
    private bool _searchPosted;
    private bool _disposed;

    /// <summary>Creates a toolbar for a viewer and starts following it.</summary>
    /// <param name="viewer">The viewer the commands drive.</param>
    /// <param name="options">The commands, their order and the zoom presets, or <see langword="null"/> for every built-in command.</param>
    public ViewerToolbar(IDocumentViewer viewer, ViewerToolbarOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        Viewer = viewer;
        options ??= new ViewerToolbarOptions();
        _context = SynchronizationContext.Current;
        _presets = options.ZoomPresets;
        _honorHideToolbar = options.HonorHideToolbar;
        Items = new ReadOnlyCollection<ViewerCommand>(_items);
        foreach (string id in options.Items)
        {
            ViewerCommand command = CreateBuiltIn(id);
            command.Placement = options.Compact && !StaysInCompactBar(id) ? ViewerCommandPlacement.Overflow : ViewerCommandPlacement.Primary;
            Attach(command);
            _items.Add(command);
        }

        viewer.ViewChanged += OnViewChanged;
        Follow();
    }

    /// <summary>Raised on the UI thread when an item's state, the item list or <see cref="IsVisible"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the viewer the commands drive.</summary>
    public IDocumentViewer Viewer { get; }

    /// <summary>Gets the commands, in the order shown.</summary>
    public IReadOnlyList<ViewerCommand> Items { get; }

    /// <summary>
    /// Gets a value indicating whether a shell shows the toolbar: <see langword="false"/> while the open document asks the viewer to hide
    /// its tool bars (<c>HideToolbar</c>) and the options honor that; otherwise <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.2, Table 147.</remarks>
    public bool IsVisible => _visible;

    /// <summary>Gets the page labels the page box uses: the viewer's, or the ones the toolbar read itself.</summary>
    internal PageLabelTable? CurrentLabels => Viewer.PageLabels ?? _labels;

    /// <summary>Gets or sets the match last scrolled into view.</summary>
    internal SearchMatch? Revealed { get; set; }

    /// <summary>Gets the read of the open session's preferences and labels (tests wait for it).</summary>
    internal Task DocumentRead { get; private set; } = Task.CompletedTask;

    /// <summary>Finds a command by its identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The command, or <see langword="null"/>.</returns>
    public ViewerCommand? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _items.Find(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Adds a command (an app's own, or a built-in one removed earlier) at a position.</summary>
    /// <param name="index">The position, from 0 to the item count.</param>
    /// <param name="command">The command.</param>
    /// <exception cref="ArgumentException">A command with the same identifier is in the toolbar, or the command is in another toolbar.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the list.</exception>
    public void Insert(int index, ViewerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _items.Count);
        if (Find(command.Id) is not null)
        {
            throw new ArgumentException($"The toolbar already has a command \"{command.Id}\".", nameof(command));
        }

        if (command.Owner is not null && !ReferenceEquals(command.Owner, this))
        {
            throw new ArgumentException($"The command \"{command.Id}\" belongs to another toolbar.", nameof(command));
        }

        Attach(command);
        _items.Insert(index, command);
        command.Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a command.</summary>
    /// <param name="id">The command's identifier.</param>
    /// <returns>Whether the toolbar had it.</returns>
    public bool Remove(string id)
    {
        if (Find(id) is not { } command)
        {
            return false;
        }

        _items.Remove(command);
        Detach(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Creates a built-in command by its identifier, for <see cref="Insert"/> after it was removed or left out of the options.</summary>
    /// <param name="id">An identifier from <see cref="ViewerCommandIds"/>.</param>
    /// <returns>The command, bound to this toolbar's viewer and not yet in the toolbar.</returns>
    /// <exception cref="ArgumentException">The identifier is not a built-in one.</exception>
    public ViewerCommand CreateBuiltIn(string id) => id switch
    {
        ViewerCommandIds.PagePrevious => new ViewerButtonCommand(id, "Previous page", Viewer.PreviousPage, () => HasPages && Viewer.PageIndex > 0)
        {
            Glyph = "‹",
            ToolTip = "Previous page",
            Shortcut = "PageUp",
        },
        ViewerCommandIds.PageNext => new ViewerButtonCommand(id, "Next page", Viewer.NextPage, () => HasPages && Viewer.PageIndex < Viewer.PageCount - 1)
        {
            Glyph = "›",
            ToolTip = "Next page",
            Shortcut = "PageDown",
        },
        ViewerCommandIds.PageNumber => new ViewerPageCommand(this) { ToolTip = "Go to a page by number or label" },
        ViewerCommandIds.ZoomOut => new ViewerButtonCommand(id, "Zoom out", () => ZoomStepBy(1 / ZoomStep), () => HasPages)
        {
            Glyph = "−",
            ToolTip = "Zoom out",
            Shortcut = "-",
        },
        ViewerCommandIds.ZoomIn => new ViewerButtonCommand(id, "Zoom in", () => ZoomStepBy(ZoomStep), () => HasPages)
        {
            Glyph = "+",
            ToolTip = "Zoom in",
            Shortcut = "+",
        },
        ViewerCommandIds.ZoomMode => new ViewerChoiceCommand(id, "Zoom", ZoomChoices(), SelectedZoom, SelectZoom, () => HasPages, ZoomText)
        {
            ToolTip = "Zoom",
        },
        ViewerCommandIds.LayoutMode => new ViewerChoiceCommand(
            id, "Page layout", LayoutNames, () => (int)(Viewer.LayoutOptions?.Mode ?? PageLayoutMode.Continuous), SelectLayout, () => HasPages, text: null)
        {
            ToolTip = "Page layout",
        },
        ViewerCommandIds.Rotate => new ViewerButtonCommand(id, "Rotate clockwise", RotateView, () => HasPages)
        {
            Glyph = "⟳",
            ToolTip = "Rotate the view clockwise (the file is not changed)",
        },
        ViewerCommandIds.Search => new ViewerSearchCommand(this) { ToolTip = "Find in the document", Shortcut = "F3" },
        ViewerCommandIds.DarkMode => new ViewerToggleCommand(id, "Dark mode", IsDark, SetDark, () => Viewer.Session is not null)
        {
            Glyph = "◐",
            ToolTip = "Smart dark mode",
        },
        _ => throw new ArgumentException($"\"{id}\" is not a built-in toolbar command.", nameof(id)),
    };

    /// <summary>Works out every command's state again (an app's own commands' enablement may depend on something the viewer does not report).</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _refreshing++;
        try
        {
            foreach (ViewerCommand command in _items.ToArray())
            {
                command.Refresh();
            }

            bool visible = !(_honorHideToolbar && _hiddenByDocument);
            if (visible != _visible)
            {
                _visible = visible;
                _dirty = true;
            }
        }
        finally
        {
            _refreshing--;
        }

        if (_refreshing == 0 && _dirty)
        {
            _dirty = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stops following the viewer and its search.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        Viewer.ViewChanged -= OnViewChanged;
        _search?.Changed -= OnSearchChanged;
        _search = null;
        foreach (ViewerCommand command in _items)
        {
            Detach(command);
        }
    }

    /// <summary>Scrolls the search's current match into view, once per match.</summary>
    internal void RevealCurrentMatch()
    {
        if (Viewer.Search?.Current is { } current && !ReferenceEquals(current, Revealed))
        {
            Revealed = current;
            Viewer.Reveal(current.PageIndex, current.Bounds);
        }
    }

    private static bool StaysInCompactBar(string id) =>
        id is ViewerCommandIds.PagePrevious or ViewerCommandIds.PageNext or ViewerCommandIds.PageNumber or ViewerCommandIds.Search;

    private bool HasPages => Viewer.Session is not null && Viewer.PageCount > 0;

    private void Attach(ViewerCommand command)
    {
        command.Owner = this;
        command.Changed += OnCommandChanged;
    }

    private void Detach(ViewerCommand command)
    {
        command.Changed -= OnCommandChanged;
        command.Owner = null;
    }

    private void OnCommandChanged(object? sender, EventArgs e)
    {
        if (_refreshing > 0)
        {
            _dirty = true;
        }
        else if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnViewChanged(object? sender, EventArgs e) => Follow();

    // Picks up a new session or search, then works the state out again.
    private void Follow()
    {
        if (_disposed)
        {
            return;
        }

        DocumentSession? session = Viewer.Session;
        if (!ReferenceEquals(session, _session))
        {
            _session = session;
            int generation = ++_generation;
            _labels = null;
            _hiddenByDocument = false;
            _zoomChoice = null;
            Revealed = null;
            DocumentRead = session is null ? Task.CompletedTask : ReadDocumentAsync(session, generation);
        }

        DocumentSearch? search = Viewer.Search;
        if (!ReferenceEquals(search, _search))
        {
            _search?.Changed -= OnSearchChanged;
            _search = search;
            _search?.Changed += OnSearchChanged;
            Revealed = null;
        }

        Refresh();
    }

    private async Task ReadDocumentAsync(DocumentSession session, int generation)
    {
        bool hide;
        bool readLabels = Viewer.PageLabels is null; // asked on the UI thread, before the reads
        PageLabelTable? labels = null;
        try
        {
            hide = await session.ReadAsync(_ => session.Document.ViewerPreferences?.HideToolbar ?? false, CancellationToken.None).ConfigureAwait(false);
            if (readLabels)
            {
                labels = await PageLabelTable.ReadAsync(session).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
#pragma warning disable CA1031 // the toolbar keeps its defaults (shown, page numbers); the view reports the document's own failures
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        await OnUiThreadAsync(() =>
        {
            if (generation == _generation && !_disposed)
            {
                _hiddenByDocument = hide;
                _labels = labels;
                Refresh();
            }
        }).ConfigureAwait(false);
    }

    // DocumentSearch raises Changed on the worker that indexed a page: the state is worked out again on the UI thread, once per burst.
    private void OnSearchChanged(object? sender, EventArgs e)
    {
        if (_context is null)
        {
            OnSearchUpdate(sender);
            return;
        }

        lock (_gate)
        {
            if (_searchPosted)
            {
                return;
            }

            _searchPosted = true;
        }

        _context.Post(
            state =>
            {
                lock (_gate)
                {
                    _searchPosted = false;
                }

                OnSearchUpdate(state);
            },
            sender);
    }

    private void OnSearchUpdate(object? sender)
    {
        if (_disposed || !ReferenceEquals(sender, _search))
        {
            return;
        }

        if (_context is null)
        {
            // No UI thread to return to (a console host): the workers' updates at least run one at a time.
            lock (_gate)
            {
                RevealCurrentMatch();
                Refresh();
            }

            return;
        }

        RevealCurrentMatch();
        Refresh();
    }

    // Runs an action on the toolbar's thread; the task completes once it ran.
    private Task OnUiThreadAsync(Action action)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(
            _ =>
            {
                action();
                done.SetResult();
            },
            null);
        return done.Task;
    }

    private void ZoomStepBy(double factor)
    {
        _zoomChoice = null;
        Viewer.ZoomBy(factor);
    }

    private string[] ZoomChoices()
    {
        var choices = new string[FitModes + _presets.Count];
        choices[0] = "Fit width";
        choices[1] = "Fit page";
        choices[2] = "Actual size";
        for (int i = 0; i < _presets.Count; i++)
        {
            choices[FitModes + i] = Percent(_presets[i]);
        }

        return choices;
    }

    private int SelectedZoom()
    {
        if (!HasPages)
        {
            return -1;
        }

        if (_zoomChoice is int chosen)
        {
            return chosen;
        }

        double zoom = Viewer.Zoom;
        for (int i = 0; i < _presets.Count; i++)
        {
            if (Math.Abs(zoom - _presets[i]) <= _presets[i] * 0.005)
            {
                return FitModes + i;
            }
        }

        return -1;
    }

    private void SelectZoom(int index)
    {
        switch (index)
        {
            case 0:
                _zoomChoice = 0;
                Viewer.ZoomTo(ZoomMode.FitWidth);
                break;
            case 1:
                _zoomChoice = 1;
                Viewer.ZoomTo(ZoomMode.FitPage);
                break;
            case 2:
                _zoomChoice = 2;
                Viewer.ZoomTo(ZoomMode.ActualSize);
                break;
            default:
                _zoomChoice = null;
                double zoom = Viewer.Zoom;
                if (zoom > 0 && double.IsFinite(zoom))
                {
                    Viewer.ZoomBy(_presets[index - FitModes] / zoom);
                }

                break;
        }
    }

    private string ZoomText(int index) => HasPages ? Percent(Viewer.Zoom) : string.Empty;

    private static string Percent(double zoom) => string.Create(CultureInfo.InvariantCulture, $"{zoom * 100:0.#}%");

    private void SelectLayout(int index)
    {
        _zoomChoice = null;
        Viewer.SetLayout(CopyLayout(Viewer.LayoutOptions).WithMode((PageLayoutMode)index));
    }

    private void RotateView()
    {
        PageLayoutOptions layout = CopyLayout(Viewer.LayoutOptions);
        Viewer.SetLayout(layout.WithRotation(layout.Rotation + 90));
    }

    private bool IsDark() => (Viewer.Session?.ColorFilter ?? Viewer.ColorFilter) == ColorFilter.DarkMode;

    private void SetDark(bool dark) => Viewer.ColorFilter = dark ? ColorFilter.DarkMode : ColorFilter.None;

    private static PageLayoutOptions CopyLayout(PageLayoutOptions? options)
    {
        options ??= new PageLayoutOptions();
        return new PageLayoutOptions()
            .WithMode(options.Mode)
            .WithCover(options.ShowCover)
            .WithGap(options.Gap)
            .WithPadding(options.Padding)
            .WithRotation(options.Rotation);
    }
}
