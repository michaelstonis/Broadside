using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Broadside.Rendering;

namespace Broadside.Viewer.Avalonia.Sample;

/// <summary>The sample's window: a toolbar over a <see cref="PdfView"/>, built in code.</summary>
internal sealed class MainWindow : Window, IDisposable
{
    private readonly PdfView _viewer = new();
    private readonly SidePanel _side;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly TextBox _query = new() { PlaceholderText = "Search", Width = 180 };
    private readonly TextBlock _matches = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };

    public MainWindow(string source)
    {
        Title = "Broadside viewer";
        Width = 1000;
        Height = 800;

        // Standard 14 fonts and the predefined CMaps are bundled, so text draws the same on every OS; tiles render on worker threads.
        _viewer.Options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
        _side = new SidePanel(_viewer);
        _viewer.StateChanged += (_, e) =>
        {
            ShowState(e.State);
            _side.Update(e.State);
        };
        _viewer.ActionRequested += (_, e) =>
        {
            // Links to the web are never followed by the viewer; the sample shows where they go.
            if (e.Uri is { } uri)
            {
                _status.Text = "Link: " + uri;
            }
        };
        _viewer.Error += (_, e) => _status.Text = "Error: " + e.Exception.Message;

        var layout = new ComboBox { ItemsSource = Enum.GetNames<PageLayoutMode>(), SelectedIndex = (int)PageLayoutMode.Continuous };
        layout.SelectionChanged += (_, _) => _viewer.LayoutOptions = new PageLayoutOptions().WithMode((PageLayoutMode)layout.SelectedIndex);
        var dark = new ToggleButton { Content = "Dark" };
        dark.IsCheckedChanged += (_, _) => _viewer.ColorFilter = dark.IsChecked == true ? ColorFilter.DarkMode : ColorFilter.None;
        var annotations = new ToggleButton { Content = "Annotations", IsChecked = true };
        annotations.IsCheckedChanged += (_, _) => _viewer.ShowAnnotations = annotations.IsChecked == true;
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (_viewer.Search?.Query == _query.Text)
                {
                    _viewer.FindNext();
                }
                else
                {
                    _viewer.Find(_query.Text ?? string.Empty);
                }

                e.Handled = true;
            }
        };

        var toolbar = new WrapPanel
        {
            Margin = new Thickness(4),
            Children =
            {
                Button("Open", Open),
                Button("◀", _viewer.PreviousPage),
                Button("▶", _viewer.NextPage),
                Button("−", () => _viewer.ZoomBy(1 / 1.25)),
                Button("+", () => _viewer.ZoomBy(1.25)),
                Button("Fit width", () => _viewer.ZoomTo(ZoomMode.FitWidth)),
                Button("Fit page", () => _viewer.ZoomTo(ZoomMode.FitPage)),
                layout,
                dark,
                annotations,
                _query,
                Button("Next", () => _viewer.FindNext()),
                Button("Previous", () => _viewer.FindPrevious()),
                _matches,
                _status,
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_side, Dock.Left);
        root.Children.Add(_side);
        root.Children.Add(_viewer);
        Content = root;
        _viewer.Source = source;

        // Search progress arrives on the indexing threads; the sample polls it on a UI timer instead.
        ShowMatches();
    }

    /// <summary>Closes the document when the window closes.</summary>
    public void Dispose()
    {
        _side.Dispose();
        _viewer.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    private void ShowMatches()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            DocumentSearch? search = _viewer.Search;
            _matches.Text = search is null || search.Query.Length == 0
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1}/{search.MatchCount}{(search.IsSearchComplete ? "" : "…")}");
        };
        timer.Start();
    }

    private void ShowState(PdfViewState state) =>
        Title = state.PageCount == 0
            ? "Broadside viewer"
            : string.Create(CultureInfo.InvariantCulture, $"Broadside viewer: page {state.PageIndex + 1} of {state.PageCount}, {state.Zoom:P0}{(state.IsRendering ? " (drawing)" : "")}");

    private async void Open()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a PDF",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _viewer.Source = path;
        }
    }

    private static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(2, 0) };
        button.Click += (_, _) => click();
        return button;
    }
}
