using System.Globalization;
using Broadside.Viewer.Maui;
using SkiaSharp;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>One gallery entry: an app asset (or an https address, downloaded when opened) and what the gallery calls it.</summary>
public sealed record GalleryEntry(string File, string Title);

/// <summary>The sample's page: a toolbar over a <see cref="PdfView"/>.</summary>
public sealed class MainPage : ContentPage
{
    /// <summary>
    /// pdf.js's copy of the TraceMonkey paper: its ACM copyright notice does not allow redistribution, so the app does not bundle it
    /// and downloads it when opened.
    /// </summary>
    public const string TraceMonkey = "https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf";

    /// <summary>The Blazor demo's gallery: the sample documents (#363) first, then TraceMonkey (downloaded), then the corpus files.</summary>
    public static readonly GalleryEntry[] Gallery =
    [
        new("documents/textbook.pdf", "Textbook chapter (WeasyPrint)"),
        new("documents/catalogue.pdf", "Product catalogue (WeasyPrint, CMYK and bleed)"),
        new("documents/newsletter.pdf", "Newsletter (Chrome)"),
        new("documents/paper.pdf", "Scientific paper with equations (Chrome)"),
        new("documents/invoice-colourful.pdf", "Invoice, colourful (WeasyPrint)"),
        new("documents/invoice-conservative-filled.pdf", "Invoice with a filled form (Broadside)"),
        new("documents/brochure.pdf", "Brochure, print-ready (Broadside)"),
        new("documents/dictionary.pdf", "Dictionary (WeasyPrint)"),
        new("documents/showcase.pdf", "Feature showcase with layers"),
        new("documents/novel-pocket.pdf", "Novel, pocket edition (170 pages)"),
        new("documents/novel-hardback.pdf", "Novel, hardback edition (tagged)"),
        new("documents/novel-large-print.pdf", "Novel, large print (Broadside, PDF/UA)"),
        new(TraceMonkey, "TraceMonkey paper (real world, downloaded)"),
        new("documents/layout-two-column-tagged.pdf", "Two columns (tagged)"),
        new("documents/outline-full.pdf", "Outline"),
        new("documents/acroform-fields.pdf", "Form fields"),
        new("documents/tagged-structure.pdf", "Tagged structure"),
        new("documents/layout-tables.pdf", "Tables"),
        new("documents/signed.pdf", "Signed document"),
        new("documents/info-dictionary.pdf", "Document information"),
        new("documents/annotations-link.pdf", "Links"),
        new("documents/shading-radial-cases.pdf", "Shadings"),
        new("documents/transparency-blend-modes.pdf", "Transparency"),
        new("documents/page-rotation.pdf", "Rotated pages"),
        new("documents/linearized.pdf", "Linearized"),
    ];

    private static readonly ViewerOptions Options = new ViewerOptions()
        .UseEngine(new PdfEngine(new PdfOptions().UseStandard14Fonts()))
        .UseBackend(() => new SkiaTileBackend());

    private readonly PdfView _view = new() { ShowAnnotations = true };
    private readonly Picker _gallery = new() { Title = "Document", ItemsSource = Gallery.Select(g => g.Title).ToList(), WidthRequest = 260 };
    private readonly Label _status = new() { VerticalTextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0) };
    private static readonly HttpClient Http = new();
    private DocumentSession? _session;
    private bool _smokeDone;
    private bool _benchStarted;

    public MainPage()
    {
        Title = "Broadside Viewer";
        var annotations = new Switch { IsToggled = true };
        annotations.Toggled += (_, e) => _view.ShowAnnotations = e.Value;
        var toolbar = new HorizontalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(8, 4),
            Children =
            {
                _gallery,
                Button("Prev", () => _view.PreviousPage()),
                Button("Next", () => _view.NextPage()),
                Button("-", () => _view.ZoomBy(1 / 1.25)),
                Button("+", () => _view.ZoomBy(1.25)),
                Button("Fit width", () => _view.ZoomTo(ZoomMode.FitWidth)),
                Button("Fit page", () => _view.ZoomTo(ZoomMode.FitPage)),
                Button("Copy", async () => _status.Text = await _view.CopySelectionAsync() is { } text ? $"Copied {text.Length} characters" : "Nothing selected"),
                new Label { Text = "Annotations", VerticalTextAlignment = TextAlignment.Center },
                annotations,
                _status,
            },
        };

        _gallery.SelectedIndexChanged += async (_, _) => await OpenAsync(Gallery[_gallery.SelectedIndex]);
        _view.StateChanged += OnStateChanged;
        _view.Error += (_, e) => _status.Text = e.Exception.Message;
        _view.ActionRequested += OnActionRequested;

        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        grid.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = toolbar });
        grid.Add(_view, 0, 1);
        Content = grid;
        _gallery.SelectedIndex = 0;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, Padding = new Thickness(10, 2) };
        button.Clicked += (_, _) => action();
        return button;
    }

    private Task<bool> OpenAsync(GalleryEntry entry) => OpenFileAsync(entry.File);

    private async Task<bool> OpenFileAsync(string file)
    {
        try
        {
            byte[] bytes;
            if (file.StartsWith("https://", StringComparison.Ordinal))
            {
                _status.Text = "Downloading…";
                bytes = await Http.GetByteArrayAsync(new Uri(file));
            }
            else
            {
                if (!await FileSystem.AppPackageFileExistsAsync(file))
                {
                    return false;
                }

                await using Stream asset = await FileSystem.OpenAppPackageFileAsync(file);
                using var buffer = new MemoryStream();
                await asset.CopyToAsync(buffer);
                bytes = buffer.ToArray();
            }

            DocumentSession session = DocumentSession.Open(bytes, Options);
            DocumentSession? old = _session;
            _session = session;
            _view.Session = session;
            old?.Dispose();
            return true;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            return false;
        }
    }

    private void Show(DocumentSession session)
    {
        DocumentSession? old = _session;
        _session = session;
        _view.Session = session;
        old?.Dispose();
    }

    private void OnStateChanged(object? sender, PdfViewStateChangedEventArgs e)
    {
        PdfViewState state = e.State;

        // Only page and zoom changes reach the label: a text change lays the page out again, which a scroll must not pay for each time
        // the view starts or finishes drawing (#357).
        string status = state.PageCount == 0
            ? (state.IsLoading ? "Opening…" : string.Empty)
            : string.Create(CultureInfo.InvariantCulture, $"Page {state.PageIndex + 1} of {state.PageCount}  {state.Zoom * 100:0}%");
        if (_status.Text != status)
        {
            _status.Text = status;
        }
        if (PhotoBench.Requested && !_benchStarted && state is { PageCount: > 0, IsLoading: false })
        {
            _benchStarted = true;
            _ = PhotoBench.RunAsync(_view, Show);
        }

        if (FrameBench.Requested && !_benchStarted && state is { PageCount: > 0, IsLoading: false })
        {
            _benchStarted = true;
            _ = FrameBench.RunAsync(_view, OpenFileAsync);
        }

        if (SmokeTest.Requested && !_smokeDone && state is { PageCount: > 0, IsLoading: false, IsRendering: false, Zoom: > 0 })
        {
            _smokeDone = true;
            _ = RunSmokeAsync();
        }
    }

    private async void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        // The view never opens anything itself: links inside the document already navigated; a web link asks first.
        if (e.Uri is { } uri && await DisplayAlertAsync("Open link?", uri.ToString(), "Open", "Cancel"))
        {
            await Launcher.Default.OpenAsync(uri);
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
            bool ok = dark > samples / 200 && white > samples / 4 && _view.State.PageIndex == 0;
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
