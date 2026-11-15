using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Rendering;
using Microsoft.Win32;

namespace Broadside.Viewer.Wpf.Sample;

/// <summary>
/// A window around <see cref="PdfView"/>: a toolbar to open a file or a sample document, move between pages, zoom, select and copy,
/// search and switch to dark mode, and a status bar with what the view shows. Every control carries an automation ID, which the UI
/// Automation test drives.
/// </summary>
#pragma warning disable CA1001 // a window's lifetime ends with Closed, where the view is disposed
internal sealed class MainWindow : Window
#pragma warning restore CA1001
{
    private readonly PdfView _view = new();
    private readonly ComboBox _documents = new() { MinWidth = 200, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _pageStatus = Status("PageStatus");
    private readonly TextBlock _zoomStatus = Status("ZoomStatus");
    private readonly TextBlock _selectionStatus = Status("SelectionStatus");
    private readonly TextBlock _searchStatus = Status("SearchStatus");
    private readonly TextBlock _message = Status("Message");
    private readonly TextBox _query = new() { MinWidth = 160, Margin = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private DocumentSearch? _search;
    private TextSelection? _selection;
    private bool _revealFirst;

    public MainWindow(string? path)
    {
        Title = "Broadside Viewer (WPF)";
        Width = 1000;
        Height = 800;
        AutomationProperties.SetAutomationId(this, "MainWindow");

        _view.Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
        AutomationProperties.SetAutomationId(_view, "Viewer");
        AutomationProperties.SetName(_view, "Document");
        _view.StateChanged += (_, e) => ShowState(e.State);
        _view.Error += (_, e) => _message.Text = "Error: " + e.Exception.Message;
        _view.ActionRequested += (_, e) =>
        {
            // A link to a web page or another file is shown, never followed by the view; the user can open it from here.
            if (e.Uri is { } uri)
            {
                _message.Text = "Link: " + uri;
            }
        };

        AutomationProperties.SetAutomationId(_query, "SearchBox");
        AutomationProperties.SetName(_query, "Search");
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Find();
                e.Handled = true;
            }
        };
        AutomationProperties.SetAutomationId(_documents, "Documents");
        AutomationProperties.SetName(_documents, "Sample documents");
        foreach (string document in SampleDocuments())
        {
            _documents.Items.Add(new ComboBoxItem { Content = Path.GetFileName(document), Tag = document });
        }

        _documents.SelectionChanged += (_, _) =>
        {
            if (_documents.SelectedItem is ComboBoxItem { Tag: string file })
            {
                Open(file);
            }
        };

        var darkMode = new CheckBox { Content = "Dark", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
        AutomationProperties.SetAutomationId(darkMode, "DarkMode");
        darkMode.Checked += (_, _) => _view.ColorFilter = ColorFilter.DarkMode;
        darkMode.Unchecked += (_, _) => _view.ColorFilter = ColorFilter.None;

        var toolbar = new WrapPanel { Margin = new Thickness(4) };
        toolbar.Children.Add(Button("Open…", "OpenFile", OpenFile));
        toolbar.Children.Add(_documents);
        toolbar.Children.Add(Button("◀", "PreviousPage", _view.PreviousPage, "Previous page"));
        toolbar.Children.Add(Button("▶", "NextPage", _view.NextPage, "Next page"));
        toolbar.Children.Add(Button("−", "ZoomOut", () => _view.ZoomBy(1 / 1.25), "Zoom out"));
        toolbar.Children.Add(Button("+", "ZoomIn", () => _view.ZoomBy(1.25), "Zoom in"));
        toolbar.Children.Add(Button("Fit width", "FitWidth", () => _view.ZoomTo(ZoomMode.FitWidth)));
        toolbar.Children.Add(Button("Select all", "SelectAll", () => _view.Selection?.SelectAll()));
        toolbar.Children.Add(Button("Copy", "Copy", () => _ = _view.CopySelectionAsync()));
        toolbar.Children.Add(_query);
        toolbar.Children.Add(Button("Find", "Find", Find));
        toolbar.Children.Add(Button("↑", "PreviousMatch", () => Step(-1), "Previous match"));
        toolbar.Children.Add(Button("↓", "NextMatch", () => Step(1), "Next match"));
        toolbar.Children.Add(darkMode);

        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        foreach (TextBlock block in new[] { _pageStatus, _zoomStatus, _selectionStatus, _searchStatus, _message })
        {
            status.Children.Add(block);
        }

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(status);
        root.Children.Add(_view);
        Content = root;

        Loaded += (_, _) =>
        {
            if (path is not null)
            {
                Open(path);
            }
            else if (_documents.Items.Count > 0)
            {
                _documents.SelectedIndex = 0;
            }

            _view.Focus();
        };
        Closed += (_, _) => _view.Dispose();
    }

    private void Open(string file)
    {
        _message.Text = string.Empty;
        _selectionStatus.Text = string.Empty;
        _searchStatus.Text = string.Empty;
        _view.Source = file;
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            Open(dialog.FileName);
        }
    }

    private void ShowState(PdfViewState state)
    {
        _pageStatus.Text = state.IsLoading ? "Opening…" : string.Create(CultureInfo.InvariantCulture, $"Page {state.PageIndex + 1} of {state.PageCount}");
        _zoomStatus.Text = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(state.Zoom * 100)} %");

        // The view's selection and search are per document: follow them when a document opens.
        if (_view.Selection is { } selection && !ReferenceEquals(selection, _selection))
        {
            _selection = selection;
            selection.Changed += async (_, _) =>
            {
                string? text = selection.IsEmpty ? null : await selection.GetTextAsync().ConfigureAwait(true);
                _selectionStatus.Text = text is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{text.Length} characters selected");
            };
        }

        if (!ReferenceEquals(_view.Search, _search))
        {
            _search = _view.Search;
            if (_search is { } search)
            {
                // Raised on the indexing thread: the status follows on the UI thread.
                search.Changed += (_, _) => Dispatcher.InvokeAsync(() => ShowSearch(search));
            }
        }
    }

    private void Find()
    {
        if (_search is not { } search)
        {
            return;
        }

        _revealFirst = true;
        search.Search(_query.Text, null, _view.State.PageIndex);
        ShowSearch(search);
    }

    private void Step(int direction)
    {
        if (_search?.MatchCount > 0)
        {
            SearchMatch? match = direction < 0 ? _search.Previous() : _search.Next();
            if (match is not null)
            {
                _view.Reveal(match.PageIndex, match.Bounds);
            }

            ShowSearch(_search);
        }
    }

    private void ShowSearch(DocumentSearch search)
    {
        if (!ReferenceEquals(search, _search))
        {
            return;
        }

        if (_revealFirst && search.Current is { } first)
        {
            _revealFirst = false;
            _view.Reveal(first.PageIndex, first.Bounds);
        }

        _searchStatus.Text = search.Query.Length == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1} of {search.MatchCount}{(search.IsSearchComplete ? string.Empty : "+")}");
    }

    private static Button Button(string text, string automationId, Action click, string? name = null)
    {
        var button = new Button { Content = text, MinWidth = 32, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(6, 2, 6, 2), Focusable = false };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name ?? text);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Status(string automationId)
    {
        var block = new TextBlock { Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(block, automationId);
        return block;
    }

    // The repository's representative documents (samples/documents/pdf), found by walking up from the executable.
    private static IEnumerable<string> SampleDocuments()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "samples", "documents", "pdf");
            if (Directory.Exists(candidate))
            {
                return Directory.GetFiles(candidate, "*.pdf").Order(StringComparer.Ordinal);
            }
        }

        return [];
    }
}
