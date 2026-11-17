using System.Globalization;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Broadside.Viewer.Uno.Sample;

/// <summary>The viewer page: a toolbar over a <see cref="PdfView"/>.</summary>
public sealed partial class MainPage : Page
{
    /// <summary>The documents the sample carries (embedded resources under "documents/").</summary>
    public static readonly string[] Documents = ["textbook.pdf", "paper.pdf", "application-form.pdf", "tagged-structure.pdf"];

    private readonly ComboBox _documents = new() { ItemsSource = Documents, MinWidth = 180 };
    private readonly TextBlock _page = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
    private readonly TextBox _find = new() { PlaceholderText = "Find (Ctrl+F)", Width = 180 };
    private readonly TextBlock _matches = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly ToggleButton _dark = new() { Content = "Dark" };
    private DocumentSession? _session;
    private bool _control;

    public MainPage()
    {
        AutomationProperties.SetAutomationId(_find, "find");
        AutomationProperties.SetName(_find, "Find");
        Viewer = new PdfView();
        Viewer.StateChanged += (_, e) => ShowState(e.State);
        Viewer.Error += (_, e) => System.Diagnostics.Debug.WriteLine(e.Exception);
        Viewer.ActionRequested += (_, e) => System.Diagnostics.Debug.WriteLine($"Action: {e.Kind} {e.Uri}");

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8) };
        toolbar.Children.Add(_documents);
        toolbar.Children.Add(Button("Previous", (_, _) => Viewer.PreviousPage()));
        toolbar.Children.Add(Button("Next", (_, _) => Viewer.NextPage()));
        toolbar.Children.Add(_page);
        toolbar.Children.Add(Button("-", (_, _) => Viewer.ZoomBy(1 / 1.25)));
        toolbar.Children.Add(Button("+", (_, _) => Viewer.ZoomBy(1.25)));
        toolbar.Children.Add(Button("Fit width", (_, _) => Viewer.ZoomTo(ZoomMode.FitWidth)));
        toolbar.Children.Add(_dark);
        toolbar.Children.Add(_find);
        toolbar.Children.Add(Button("Find next", (_, _) => Viewer.FindNext()));
        toolbar.Children.Add(_matches);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(toolbar);
        Grid.SetRow(Viewer, 1);
        root.Children.Add(Viewer);
        Content = root;

        _documents.SelectionChanged += (_, _) =>
        {
            if (_documents.SelectedItem is string name)
            {
                Open(name);
            }
        };
        _dark.Click += (_, _) => Viewer.ColorFilter = _dark.IsChecked == true ? ColorFilter.DarkMode : ColorFilter.None;
        _find.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                if (Viewer.Search?.Query == _find.Text && _find.Text.Length > 0)
                {
                    Viewer.FindNext();
                }
                else
                {
                    Viewer.Find(_find.Text);
                }
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                _find.Text = string.Empty;
                Viewer.ClearFind();
                Viewer.Focus(FocusState.Keyboard);
            }
        };
        KeyDown += OnKeyDown;
        KeyUp += (_, e) => _control &= e.Key is not (VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftWindows or VirtualKey.RightWindows);
        Loaded += (_, _) =>
        {
            _documents.SelectedIndex = 0;
            Automation.Start(this);
        };

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => ShowMatches();
        timer.Start();
    }

    /// <summary>Gets the viewer.</summary>
    public PdfView Viewer { get; }

    /// <summary>Gets the name of the open document.</summary>
    public string? DocumentName { get; private set; }

    /// <summary>Opens one of the sample's documents.</summary>
    public void Open(string name)
    {
        if (name == DocumentName && _session is not null)
        {
            return;
        }

        using Stream stream = typeof(MainPage).Assembly.GetManifestResourceStream("documents/" + name)
            ?? throw new FileNotFoundException("The sample does not carry this document.", name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        DocumentSession previous = _session!;
        _session = DocumentSession.Open(copy.ToArray(), Options());
        DocumentName = name;
        Viewer.Session = _session;
        previous?.Dispose();
        if (!ReferenceEquals(_documents.SelectedItem, name))
        {
            _documents.SelectedItem = name;
        }
    }

    /// <summary>The options the sample opens documents with: the standard 14 fonts and the predefined CMaps bundled; threading automatic.</summary>
    public static ViewerOptions Options() => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            _control = true;
        }
        else if (_control && e.Key == VirtualKey.F)
        {
            e.Handled = true;
            _find.Focus(FocusState.Keyboard);
            _find.SelectAll();
        }
    }

    private void ShowState(PdfViewState state) =>
        _page.Text = state.PageCount == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{state.PageIndex + 1} / {state.PageCount}  {state.Zoom:P0}");

    private void ShowMatches()
    {
        DocumentSearch? search = Viewer.Search;
        _matches.Text = search is null || search.Query.Length == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1} of {search.MatchCount}{(search.IsSearchComplete ? string.Empty : "...")}");
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text };
        button.Click += click;
        return button;
    }
}
