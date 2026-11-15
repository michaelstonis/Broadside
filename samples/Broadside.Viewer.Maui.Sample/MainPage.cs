using System.Globalization;
using Broadside.Viewer.Maui;
using SkiaSharp;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// The sample's shell around a <see cref="PdfView"/>, at parity with the Blazor demo (#358). On a phone: a compact top bar (documents,
/// title, pager, share), the tools in a bottom bar, and each tool's panel in a bottom sheet. On a tablet or desktop (720 units and
/// wider): a top bar with zoom, a rail of tools on the left and the panel in a sidebar beside the pages.
/// </summary>
public sealed class MainPage : ContentPage
{
    /// <summary>The width from which the page uses the tablet and desktop layout.</summary>
    public const double WideWidth = 720;

    private static readonly SamplePanel[] Tools =
        [SamplePanel.Search, SamplePanel.Outline, SamplePanel.Pages, SamplePanel.Annotate, SamplePanel.Form, SamplePanel.Info, SamplePanel.Signatures, SamplePanel.View, SamplePanel.Create];

    private readonly SampleDocument _document = new();
    private readonly PdfView _view = new() { ShowAnnotations = true, AutomationId = "viewer" };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center, AutomationId = "document-title" };
    private readonly Label _edited = new() { Text = "edited", FontSize = 11, TextColor = Colors.White, BackgroundColor = Color.FromRgb(0xB0, 0x6D, 0x00), Padding = new Thickness(6, 1), VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center, IsVisible = false, AutomationId = "document-edited" };
    private readonly Label _page = new() { VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center, MinimumWidthRequest = 56, AutomationId = "page-indicator" };
    private readonly Label _zoom = new() { VerticalTextAlignment = TextAlignment.Center, MinimumWidthRequest = 48, HorizontalTextAlignment = TextAlignment.Center, AutomationId = "zoom-indicator" };
    private readonly HorizontalStackLayout _zoomGroup;
    private readonly Grid _topBar;
    private readonly Grid _message = new() { Padding = new Thickness(12, 6), BackgroundColor = Color.FromRgb(0xFF, 0xF4, 0xCE), IsVisible = false, AutomationId = "message" };
    private readonly Label _messageText = new() { TextColor = Colors.Black, FontSize = 13, VerticalTextAlignment = TextAlignment.Center };
    private readonly Dictionary<SamplePanel, Button> _toolButtons = [];
    private readonly Dictionary<SamplePanel, View> _panels = [];
    private readonly Border _sheet;
    private readonly Label _sheetTitle = new() { FontAttributes = FontAttributes.Bold, FontSize = 17, VerticalTextAlignment = TextAlignment.Center, AutomationId = "sheet-title" };
    private readonly ContentView _sheetBody = new();
    private readonly Grid _root = new();
    private Microsoft.Maui.Controls.Layout _tools = new Grid();
    private bool? _wide;
    private SamplePanel _panel;
    private bool _smokeDone;
    private bool _benchStarted;
    private bool _uiTestStarted;
    private double _sheetDrag;

    public MainPage()
    {
        Title = "Broadside Viewer";
        NavigationPage.SetHasNavigationBar(this, false);
        SemanticProperties.SetDescription(_view, "Document");

        PdfSearchPanel search = new() { Viewer = _view };
        PdfAnnotationsPanel annotate = new() { Viewer = _view };
        annotate.Edited += (_, _) => _document.MarkEdited();
        PdfDocumentInfoPanel info = new() { Viewer = _view };
        var view = new VerticalStackLayout { Spacing = 6, Children = { new PdfViewSettingsPanel { Viewer = _view }, RendererSection() } };
        _panels[SamplePanel.Open] = new OpenPanel(_document);
        _panels[SamplePanel.Search] = search;
        _panels[SamplePanel.Outline] = new PdfOutlinePanel { Viewer = _view };
        _panels[SamplePanel.Pages] = new PdfThumbnailsPanel { Viewer = _view };
        _panels[SamplePanel.Annotate] = annotate;
        PdfFormPanel form = new() { Viewer = _view };
        form.Edited += (_, _) => _document.MarkEdited();
        _panels[SamplePanel.Form] = form;
        _panels[SamplePanel.Info] = info;
        _panels[SamplePanel.Signatures] = new PdfSignaturesPanel { Viewer = _view };
        _panels[SamplePanel.View] = new ScrollView { Content = view };
        _panels[SamplePanel.Create] = new CreatePanel(_document);

        _zoomGroup = new HorizontalStackLayout
        {
            Spacing = 2,
            Children =
            {
                IconButton("−", "zoom-out-top", "Zoom out", () => _view.ZoomBy(1 / 1.25)),
                _zoom,
                IconButton("+", "zoom-in-top", "Zoom in", () => _view.ZoomBy(1.25)),
                IconButton("↔", "fit-width-top", "Fit width", () => _view.ZoomTo(ZoomMode.FitWidth)),
            },
        };
        _topBar = new Grid
        {
            Padding = new Thickness(4, 2),
            ColumnSpacing = 2,
            MinimumHeightRequest = 48,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _topBar.Add(IconButton("☰", "tool-open", "Documents", () => Toggle(SamplePanel.Open)));
        _topBar.Add(new HorizontalStackLayout { Spacing = 6, Children = { _title, _edited } }, 1, 0);
        _topBar.Add(new HorizontalStackLayout
        {
            Children =
            {
                IconButton("‹", "page-previous", "Previous page", () => _view.PreviousPage()),
                _page,
                IconButton("›", "page-next", "Next page", () => _view.NextPage()),
            },
        }, 2, 0);
        _topBar.Add(_zoomGroup, 3, 0);
        _topBar.Add(IconButton("⤴", "share", "Save or share the document with your edits", () => _ = _document.ShareAsync()), 4, 0);
        _topBar.SetAppThemeColor(BackgroundColorProperty, Color.FromRgb(0xF6, 0xF6, 0xF8), Color.FromRgb(0x1E, 0x1E, 0x22));

        _message.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _message.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _message.Add(_messageText);
        _message.Add(IconButton("×", "message-dismiss", "Dismiss", () => _document.Report(null)), 1, 0);

        foreach (SamplePanel tool in Tools)
        {
            var button = new Button
            {
                Text = Glyph(tool) + "\n" + TitleOf(tool),
                FontSize = 11,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(0, 4),
                CornerRadius = 0,
                BackgroundColor = Colors.Transparent,
                AutomationId = "tool-" + tool.ToString().ToLowerInvariant(),
            };
            button.SetAppThemeColor(Button.TextColorProperty, Color.FromRgb(0x30, 0x30, 0x36), Color.FromRgb(0xE0, 0xE0, 0xE6));
            SemanticProperties.SetDescription(button, TitleOf(tool));
            button.Clicked += (_, _) => Toggle(tool);
            _toolButtons[tool] = button;
        }

        var sheetHeader = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        sheetHeader.Add(_sheetTitle);
        sheetHeader.Add(IconButton("×", "sheet-close", "Close panel", () => Toggle(SamplePanel.None)), 1, 0);
        var handle = new BoxView { HeightRequest = 5, WidthRequest = 40, CornerRadius = 3, Color = Colors.Gray, HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center, Margin = new Thickness(0, 6, 0, 2) };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnSheetPan;
        var grip = new VerticalStackLayout { Children = { handle, sheetHeader } };
        grip.GestureRecognizers.Add(pan);
        var sheetGrid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, Padding = new Thickness(12, 0, 12, 8) };
        sheetGrid.Add(grip);
        sheetGrid.Add(_sheetBody, 0, 1);
        _sheet = new Border { Content = sheetGrid, StrokeThickness = 0, AutomationId = "sheet" };
        _sheet.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromRgb(0x24, 0x24, 0x28));

        _document.Changed += OnDocumentChanged;
        _view.StateChanged += OnStateChanged;
        _view.Error += (_, e) => _document.Report(e.Exception.Message);
        _view.ActionRequested += OnActionRequested;
        _root.SetAppThemeColor(BackgroundColorProperty, Color.FromRgb(0xF6, 0xF6, 0xF8), Color.FromRgb(0x1E, 0x1E, 0x22));
        Content = _root;
        Arrange(false);
        UpdateState(_view.State);
        _ = _document.OpenGalleryAsync(SampleDocument.Gallery[0]);
    }

    /// <summary>Gets the open document (the UI tests drive it).</summary>
    public SampleDocument Document => _document;

    /// <summary>Gets the viewer.</summary>
    public PdfView Viewer => _view;

    /// <summary>Gets the panel shown in the sheet or sidebar.</summary>
    public SamplePanel CurrentPanel => _panel;

    /// <summary>Gets whether the page uses the tablet and desktop layout.</summary>
    public bool IsWide => _wide == true;

    /// <summary>Gets the content of a panel (created once, kept while the app runs).</summary>
    public View PanelView(SamplePanel panel) => _panels[panel];

    /// <summary>Shows a panel, or hides the shown one when it is asked for again (<see cref="SamplePanel.None"/> hides any).</summary>
    public void Toggle(SamplePanel panel) => Show(_panel == panel ? SamplePanel.None : panel);

    /// <summary>Shows a panel in the sheet (phone) or sidebar (tablet, desktop), or none.</summary>
    public void Show(SamplePanel panel)
    {
        if (_panel == SamplePanel.Search && panel != SamplePanel.Search && _panels[SamplePanel.Search] is PdfSearchPanel search)
        {
            search.Clear(); // the highlights go with the panel, as in the Blazor demo
        }

        _panel = panel;
        _sheetTitle.Text = TitleOf(panel);
        _sheetBody.Content = panel == SamplePanel.None ? null : _panels[panel];
        if (panel == SamplePanel.Info && _panels[panel] is PdfDocumentInfoPanel info)
        {
            info.Refresh(); // diagnostics grow as pages are read
        }

        foreach ((SamplePanel tool, Button button) in _toolButtons)
        {
            button.FontAttributes = tool == panel ? FontAttributes.Bold : FontAttributes.None;
            button.SetAppThemeColor(BackgroundColorProperty, tool == panel ? Color.FromRgb(0xDD, 0xE6, 0xFA) : Colors.Transparent, tool == panel ? Color.FromRgb(0x2C, 0x3A, 0x55) : Colors.Transparent);
        }

        SizePanel();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            bool wide = width >= WideWidth;
            if (wide != _wide)
            {
                Arrange(wide);
            }
            else
            {
                SizePanel();
            }
        }
    }

    // Opening or closing a panel only resizes its row or column: the viewer is never taken out of the page (its canvas stays).
    private void SizePanel()
    {
        bool open = _panel != SamplePanel.None;
        _sheet.IsVisible = open;
        if (_wide == true && _root.ColumnDefinitions.Count == 3)
        {
            _root.ColumnDefinitions[1].Width = open ? new GridLength(360) : new GridLength(0);
        }
        else if (_wide == false && _root.RowDefinitions.Count == 5)
        {
            _root.RowDefinitions[3].Height = open ? new GridLength(SheetHeight()) : new GridLength(0);
        }
    }

    private double SheetHeight() => Math.Max(240, (Height > 0 ? Height : 800) * 0.5);

    // One page, two arrangements of the same views: the views move between containers, nothing is created again.
    private void Arrange(bool wide)
    {
        _wide = wide;
        foreach (View child in new View[] { _topBar, _message, _view, _sheet, _tools })
        {
            (child.Parent as Microsoft.Maui.Controls.Layout)?.Children.Remove(child);
        }

        foreach (Button button in _toolButtons.Values)
        {
            (button.Parent as Microsoft.Maui.Controls.Layout)?.Children.Remove(button);
        }

        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        _zoomGroup.IsVisible = wide;
        if (wide)
        {
            // Top bar / message / [rail | sidebar | pages].
            var rail = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(2, 6), WidthRequest = 76 };
            foreach (SamplePanel tool in Tools)
            {
                Button button = _toolButtons[tool];
                button.HeightRequest = 56;
                rail.Children.Add(button);
            }

            _tools = rail;
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            _root.ColumnDefinitions.Add(new ColumnDefinition(_panel == SamplePanel.None ? new GridLength(0) : new GridLength(360)));
            _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _root.Add(_topBar, 0, 0);
            Grid.SetColumnSpan(_topBar, 3);
            _root.Add(_message, 0, 1);
            Grid.SetColumnSpan(_message, 3);
            _root.Add(new ScrollView { Content = rail }, 0, 2);
            _root.Add(_sheet, 1, 2);
            _root.Add(_view, 2, 2);
            _sheet.IsVisible = _panel != SamplePanel.None;
        }
        else
        {
            // Top bar / message / pages / sheet / tool bar: nine tools share the phone's width, never past its edge.
            var bar = new Grid { ColumnSpacing = 0, Padding = new Thickness(0, 2) };
            for (int i = 0; i < Tools.Length; i++)
            {
                Button button = _toolButtons[Tools[i]];
                button.HeightRequest = 54;
                bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                bar.Add(button, i, 0);
            }

            bar.SetAppThemeColor(BackgroundColorProperty, Color.FromRgb(0xF6, 0xF6, 0xF8), Color.FromRgb(0x1E, 0x1E, 0x22));
            _tools = bar;
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _root.RowDefinitions.Add(new RowDefinition(_panel == SamplePanel.None ? new GridLength(0) : new GridLength(SheetHeight())));
            _root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _root.Add(_topBar, 0, 0);
            _root.Add(_message, 0, 1);
            _root.Add(_view, 0, 2);
            _root.Add(_sheet, 0, 3);
            _root.Add(bar, 0, 4);
            _sheet.IsVisible = _panel != SamplePanel.None;
        }
    }

    private void OnSheetPan(object? sender, PanUpdatedEventArgs e)
    {
        // Dragging the sheet's handle down far enough closes it, as a native bottom sheet does.
        if (_wide == true)
        {
            return;
        }

        if (e.StatusType == GestureStatus.Running)
        {
            _sheetDrag = e.TotalY;
            _sheet.TranslationY = Math.Max(0, e.TotalY);
        }
        else if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _sheet.TranslationY = 0;
            if (_sheetDrag > 80)
            {
                Show(SamplePanel.None);
            }

            _sheetDrag = 0;
        }
    }

    private VerticalStackLayout RendererSection()
    {
        var skia = new RadioButton { Content = "SkiaSharp", IsChecked = SampleDocument.UseSkia, GroupName = "renderer", AutomationId = "renderer-skia" };
        var managed = new RadioButton { Content = "Managed rasterizer", IsChecked = !SampleDocument.UseSkia, GroupName = "renderer", AutomationId = "renderer-managed" };
        skia.CheckedChanged += (_, e) =>
        {
            if (SampleDocument.UseSkia != skia.IsChecked)
            {
                SampleDocument.UseSkia = skia.IsChecked;
                _view.Redraw();
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "Tile renderer", FontAttributes = FontAttributes.Bold, FontSize = 15, Margin = new Thickness(0, 8, 0, 2) },
                skia,
                managed,
                new Label { Text = "Content tiles draw with SkiaSharp or with Broadside's own managed rasterizer (the one the browser demo uses); switching redraws every tile.", FontSize = 12, TextColor = Colors.Gray },
            },
        };
    }

    private static Button IconButton(string text, string automationId, string description, Action action)
    {
        var button = new Button { Text = text, FontSize = 20, Padding = new Thickness(0), WidthRequest = 44, HeightRequest = 44, BackgroundColor = Colors.Transparent, AutomationId = automationId };
        button.SetAppThemeColor(Button.TextColorProperty, Color.FromRgb(0x30, 0x30, 0x36), Color.FromRgb(0xE0, 0xE0, 0xE6));
        SemanticProperties.SetDescription(button, description);
        button.Clicked += (_, _) => action();
        return button;
    }

    private static string TitleOf(SamplePanel panel) => panel switch
    {
        SamplePanel.Open => "Documents",
        SamplePanel.Search => "Search",
        SamplePanel.Outline => "Outline",
        SamplePanel.Pages => "Pages",
        SamplePanel.Annotate => "Annotate",
        SamplePanel.Form => "Form",
        SamplePanel.Info => "Info",
        SamplePanel.Signatures => "Signed",
        SamplePanel.View => "View",
        SamplePanel.Create => "Create",
        _ => string.Empty,
    };

    private static string Glyph(SamplePanel panel) => panel switch
    {
        SamplePanel.Search => "⌕",
        SamplePanel.Outline => "≡",
        SamplePanel.Pages => "▦",
        SamplePanel.Annotate => "✎",
        SamplePanel.Form => "☐",
        SamplePanel.Info => "ⓘ",
        SamplePanel.Signatures => "✓",
        SamplePanel.View => "◐",
        SamplePanel.Create => "+",
        _ => string.Empty,
    };

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_view.Session, _document.Session))
        {
            _view.Session = _document.Session;
        }

        if (_panels[SamplePanel.Info] is PdfDocumentInfoPanel info)
        {
            info.FileName = _document.Name;
        }

        _title.Text = _document.IsOpening ? "Opening…" : _document.Name.Length == 0 ? "Broadside" : _document.Name;
        _edited.IsVisible = _document.IsEdited;
        _message.IsVisible = _document.Message is not null;
        _messageText.Text = _document.Message;
        if (_document.SuggestedPanel is { } suggested)
        {
            _document.SuggestedPanel = null;
            Show(suggested);
        }
        else if (_panel == SamplePanel.Open && _document.Session is not null && !_document.IsOpening && sender is SampleDocument && !_wide.GetValueOrDefault())
        {
            Show(SamplePanel.None); // a phone shows the document it just opened
        }
    }

    private void OnStateChanged(object? sender, PdfViewStateChangedEventArgs e)
    {
        PdfViewState state = e.State;
        UpdateState(state);
        if (PhotoBench.Requested && !_benchStarted && state is { PageCount: > 0, IsLoading: false })
        {
            _benchStarted = true;
            _ = PhotoBench.RunAsync(_view, session => _view.Session = session);
        }

        if (FrameBench.Requested && !_benchStarted && state is { PageCount: > 0, IsLoading: false })
        {
            _benchStarted = true;
            _ = FrameBench.RunAsync(_view, OpenAssetAsync);
        }

        if (SmokeTest.Requested && !_smokeDone && state is { PageCount: > 0, IsLoading: false, IsRendering: false, Zoom: > 0 })
        {
            _smokeDone = true;
            _ = RunSmokeAsync();
        }

        if (UiTests.Requested && !_uiTestStarted && state is { PageCount: > 0, IsLoading: false })
        {
            _uiTestStarted = true;
            _ = UiTests.RunAsync(this);
        }
    }

    // Only page and zoom changes reach the labels: a text change lays the bar out again, which a scroll must not pay for each
    // time the view starts or finishes drawing (#357).
    private void UpdateState(PdfViewState state)
    {
        string page = state.PageCount == 0 ? "–" : string.Create(CultureInfo.InvariantCulture, $"{state.PageIndex + 1} / {state.PageCount}");
        if (_page.Text != page)
        {
            _page.Text = page;
        }

        string zoom = state.PageCount == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{state.Zoom * 100:0}%");
        if (_zoom.Text != zoom)
        {
            _zoom.Text = zoom;
        }
    }

    private async Task<bool> OpenAssetAsync(string file)
    {
        GalleryEntry entry = Array.Find(SampleDocument.Gallery, g => g.File == file) ?? new GalleryEntry(file, file);
        if (!file.StartsWith("https://", StringComparison.Ordinal) && !await FileSystem.AppPackageFileExistsAsync(file))
        {
            return false;
        }

        await _document.OpenGalleryAsync(entry);
        return _document.Session is not null && _document.Message is null;
    }

    private async void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        // The view never opens anything itself: links inside the document already navigated; a web link asks first.
        if (e.Uri is { } uri)
        {
            if (!UiTests.Requested && await DisplayAlertAsync("This link leaves the document", uri.ToString(), "Open", "Cancel"))
            {
                await Launcher.Default.OpenAsync(uri);
            }
        }
        else if (e.Destination is null && e.PageIndex is null)
        {
            _document.Report("The document asked for an action: " + e.Kind);
        }
    }

    private async Task RunSmokeAsync()
    {
        try
        {
            byte[] png = await _view.CaptureFrameAsync();
            using SKBitmap bitmap = SKBitmap.Decode(png);
            int dark = 0;
            int white = 0;
            int samples = 0;
            for (int y = 0; y < bitmap.Height / 2; y += 4)
            {
                for (int x = 0; x < bitmap.Width; x += 4)
                {
                    SKColor c = bitmap.GetPixel(x, y);
                    dark += c.Red < 96 && c.Green < 96 && c.Blue < 96 ? 1 : 0;
                    white += c.Red > 250 && c.Green > 250 && c.Blue > 250 ? 1 : 0;
                    samples++;
                }
            }

            string path = Path.Combine(Environment.GetEnvironmentVariable("BROADSIDE_SMOKE_OUT") ?? FileSystem.CacheDirectory, "smoke.png");
            await File.WriteAllBytesAsync(path, png);
            bool ok = dark > samples / 1000 && white > samples / 4 && _view.State.PageIndex == 0;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BROADSIDE_SMOKE_{(ok ? "OK" : "FAILED")} pages={_view.State.PageCount} page={_view.State.PageIndex + 1} size={bitmap.Width}x{bitmap.Height} dark={dark} white={white} samples={samples} gpu={_view.UseHardwareAcceleration} path={path}"));
        }
        catch (Exception exception)
        {
            Console.WriteLine("BROADSIDE_SMOKE_FAILED " + exception);
        }

        Application.Current?.Quit();
    }
}

/// <summary>The smoke run's switch: BROADSIDE_SMOKE=1 (BROADSIDE_SMOKE_OUT: the folder for smoke.png), or the Android intent extra "smoke".</summary>
public static class SmokeTest
{
    public static bool Requested { get; set; } = Environment.GetEnvironmentVariable("BROADSIDE_SMOKE") == "1";
}
