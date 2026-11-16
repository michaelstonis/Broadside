using System.Globalization;
using Broadside.Fonts.Standard14;
using Broadside.Rendering;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>
/// A toolbar over a <see cref="PdfView"/>: page and zoom buttons, search, copy, dark mode, and status lines the UI tests read (every
/// control has an automation id).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The view is disposed when the window closes (Closed), the window's own end of life.")]
public sealed partial class MainWindow : Window
{
    private readonly PdfView _view = new()
    {
        Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts()),
    };

    private readonly TextBlock _status = Label("Status", "Opening");
    private readonly TextBlock _matches = Label("MatchCount", string.Empty);
    private readonly TextBlock _copied = Label("Copied", string.Empty);
    private readonly TextBox _query = new() { Width = 180, PlaceholderText = "Find" };
    private readonly DispatcherQueue _dispatcher;
    private DocumentSearch? _search;
    private bool _revealFirst;
    private int _searchUpdatePosted;

    public MainWindow(string path)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Title = "Broadside WinUI Viewer";
        AutomationProperties.SetAutomationId(_view, "Viewer");
        AutomationProperties.SetAutomationId(_query, "SearchBox");
        AutomationProperties.SetName(_query, "Find");

        var dark = new ToggleButton { Content = "Dark" };
        AutomationProperties.SetAutomationId(dark, "DarkMode");
        dark.Click += (_, _) => _view.ColorFilter = dark.IsChecked == true ? ColorFilter.DarkMode : ColorFilter.None;
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Find();
            }
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8, 4, 8, 4) };
        toolbar.Children.Add(Button("Previous", "Previous", () => _view.PreviousPage()));
        toolbar.Children.Add(Button("Next", "Next", () => _view.NextPage()));
        toolbar.Children.Add(Button("ZoomOut", "-", () => _view.ZoomBy(1 / 1.25)));
        toolbar.Children.Add(Button("ZoomIn", "+", () => _view.ZoomBy(1.25)));
        toolbar.Children.Add(Button("FitWidth", "Fit width", () => _view.ZoomTo(ZoomMode.FitWidth)));
        toolbar.Children.Add(Button("Copy", "Copy", CopyAsync));
        toolbar.Children.Add(dark);
        toolbar.Children.Add(_query);
        toolbar.Children.Add(Button("Find", "Find", Find));
        toolbar.Children.Add(Button("FindNext", "Next match", () => Reveal(_search?.Next())));
        toolbar.Children.Add(Button("FindPrevious", "Previous match", () => Reveal(_search?.Previous())));

        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(8, 2, 8, 4) };
        status.Children.Add(_status);
        status.Children.Add(_matches);
        status.Children.Add(_copied);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(status, 1);
        grid.Children.Add(status);
        Grid.SetRow(_view, 2);
        grid.Children.Add(_view);
        Content = grid;

        _view.StateChanged += (_, e) => ShowState(e.State);
        if (Environment.GetEnvironmentVariable("BROADSIDE_WINUI_TRACE") == "1")
        {
            Trace(grid);
        }

        _view.Error += (_, e) => _status.Text = "Error: " + e.Exception.Message;
        _view.ActionRequested += (_, e) => _copied.Text = e.Uri is { } uri ? "Link: " + uri : _copied.Text;
        Closed += (_, _) => _view.Dispose();
        _view.Source = path;
    }

    private static TextBlock Label(string id, string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(label, id);
        return label;
    }

    private static Button Button(string id, string text, Action click)
    {
        var button = new Button { Content = text };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    // Input diagnostics for the UI tests (BROADSIDE_WINUI_TRACE=1): every wheel turn and modifier key the view handles, what it did,
    // every wheel turn that reaches the window's content (handled or not, wherever it lands) and every status line, in StartupLog.
    private void Trace(Grid root)
    {
        _view.InputTrace = line => StartupLog.Write("trace view " + line);
        root.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
            {
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(root);
                StartupLog.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"trace window wheel: delta {point.Properties.MouseWheelDelta}, at ({point.Position.X:0}, {point.Position.Y:0}), source {e.OriginalSource?.GetType().Name ?? "null"}, handled {e.Handled}, event modifiers {e.KeyModifiers}"));
            }),
            handledEventsToo: true);
        string last = string.Empty;
        _view.StateChanged += (_, _) =>
        {
            if (_status.Text != last)
            {
                last = _status.Text;
                StartupLog.Write("trace status " + last);
            }
        };
    }

    // "Page 3 of 8 | Zoom 125% | Ready": the UI tests parse this line.
    private void ShowState(PdfViewState state)
    {
        string activity = state.IsLoading ? "Loading" : state.IsRendering ? "Rendering" : "Ready";
        _status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Page {state.PageIndex + 1} of {state.PageCount} | Zoom {Math.Round(state.Zoom * 100)}% | {activity}");
        if (!ReferenceEquals(_search, _view.Search))
        {
            _search?.Changed -= OnSearchChanged;
            _search = _view.Search;
            _search?.Changed += OnSearchChanged;
        }
    }

    private void Find()
    {
        if (_view.Search is not { } search)
        {
            return;
        }

        _revealFirst = true;
        _matches.Text = "Searching";
        search.Search(_query.Text, null, _view.State.PageIndex);
    }

    // Raised on the indexing thread: one update at a time goes to the UI thread.
    private void OnSearchChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _searchUpdatePosted, 1) == 0)
        {
            _dispatcher.TryEnqueue(ShowSearch);
        }
    }

    // "3 matches | complete": the UI tests parse this line.
    private void ShowSearch()
    {
        Volatile.Write(ref _searchUpdatePosted, 0);
        if (_search is not { } search)
        {
            return;
        }

        if (search.Query.Length == 0)
        {
            _matches.Text = string.Empty;
            return;
        }

        _matches.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{search.MatchCount} matches | {(search.IsSearchComplete ? "complete" : "searching")}");
        if (_revealFirst && search.Current is { } first)
        {
            _revealFirst = false;
            Reveal(first);
        }
    }

    private void Reveal(SearchMatch? match)
    {
        if (match is not null)
        {
            _view.Reveal(match.PageIndex, match.Bounds);
        }
    }

    private async void CopyAsync()
    {
        string? text = await _view.CopySelectionAsync();
        _copied.Text = text is null ? "Nothing selected" : string.Create(CultureInfo.InvariantCulture, $"Copied {text.Length} characters");
    }
}
