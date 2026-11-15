using System.Diagnostics;
using System.Globalization;
using Broadside.Annotations;
using Broadside.Rendering;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// The sample's UI tests (#358): with BROADSIDE_UITEST=1 (Android: the intent extra "uitest"; iOS simulator: SIMCTL_CHILD_BROADSIDE_UITEST=1)
/// the app walks every panel through its real controls, found by automation ID (buttons clicked, entries filled, switches flipped on the
/// platform's own handlers), checks what the viewer and the document did, logs one <c>BROADSIDE_UITEST_PASS</c> or
/// <c>BROADSIDE_UITEST_FAIL</c> line per step and a final <c>BROADSIDE_UITEST_OK</c> or <c>BROADSIDE_UITEST_FAILED</c>, then quits.
/// CI runs it on Mac Catalyst, the iOS simulator and an Android emulator (.github/workflows/maui-ui.yml, uitest.sh).
/// </summary>
public static class UiTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Gets or sets whether the run was asked for.</summary>
    public static bool Requested { get; set; } = Environment.GetEnvironmentVariable("BROADSIDE_UITEST") == "1";

    /// <summary>Runs every step on the UI thread, then quits the app.</summary>
    public static async Task RunAsync(MainPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var steps = new (string Name, Func<MainPage, Task> Run)[]
        {
            ("layout-fits-the-window", LayoutFitsAsync),
            ("search-highlights-matches", SearchAsync),
            ("annotate-adds-a-note-and-save-keeps-it", AnnotateAndSaveAsync),
            ("view-settings-apply", ViewSettingsAsync),
            ("info-lists-facts-diagnostics-and-tags", InfoAsync),
            ("signatures-show-status", SignaturesAsync),
            ("outline-navigates", OutlineAsync),
            ("thumbnails-render-and-navigate", ThumbnailsAsync),
            ("create-pdfa-pdfua-opens", CreateAsync),
        };

        int passed = 0;
        var failed = new List<string>();
        foreach ((string name, Func<MainPage, Task> run) in steps)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await run(page);
                passed++;
                Log(string.Create(CultureInfo.InvariantCulture, $"BROADSIDE_UITEST_PASS {name} ({watch.ElapsedMilliseconds} ms)"));
            }
            catch (Exception exception)
            {
                failed.Add(name);
                Log($"BROADSIDE_UITEST_FAIL {name}: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                page.Show(SamplePanel.None);
            }
        }

        Log(failed.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"BROADSIDE_UITEST_OK passed={passed} wide={page.IsWide} width={page.Width:0}")
            : string.Create(CultureInfo.InvariantCulture, $"BROADSIDE_UITEST_FAILED passed={passed} failed={string.Join(',', failed)}"));
        await Task.Delay(500);
        Application.Current?.Quit();
    }

    // ---- Steps ----

    // The bars never run past the window's edge (the phone toolbar overflow in #358), and each layout has its tools.
    private static async Task LayoutFitsAsync(MainPage page)
    {
        await WaitAsync(() => page.Width > 0 && page.Viewer.State.PageCount > 0, "the first document");
        foreach (string id in new[] { "tool-open", "page-previous", "page-next", "share", "tool-search", "tool-create", "tool-view" })
        {
            var button = Find<VisualElement>(page, id);
            await WaitAsync(() => button.Width > 0, id + " laid out");
            Rect bounds = BoundsInPage(button);
            Check(bounds.Right <= page.Width + 0.5 && bounds.Left >= -0.5, $"{id} at {bounds} inside the page's width {page.Width}");
        }

        Check(page.IsWide == page.Width >= MainPage.WideWidth, "the layout matches the width");
        page.Show(SamplePanel.Search);
        await WaitAsync(() => Find<VisualElement>(page, "sheet").Height > 100, "the sheet or sidebar opens");
        Rect sheet = BoundsInPage(Find<VisualElement>(page, "sheet"));
        Check(sheet.Right <= page.Width + 0.5 && sheet.Bottom <= page.Height + 0.5, $"the panel {sheet} inside the page");
        Click(page, "sheet-close");
        Check(page.CurrentPanel == SamplePanel.None, "the close button hides the panel");
    }

    private static async Task SearchAsync(MainPage page)
    {
        await OpenAsync(page, "documents/textbook.pdf");
        Click(page, "tool-search");
        Find<Entry>(page, "search-query").Text = "the";
        Click(page, "search-find");
        DocumentSearch search = page.Viewer.Search!;
        await WaitAsync(() => search.IsSearchComplete && search.MatchCount > 0, "matches for \"the\"");
        await WaitAsync(() => Find<Label>(page, "search-status").Text?.Contains(" of ", StringComparison.Ordinal) == true, "the match count");
        int before = search.CurrentIndex;
        Click(page, "search-next");
        Check(search.CurrentIndex == before + 1, "next moves to the next match");
        Click(page, "sheet-close");
        Check(search.MatchCount == 0, "closing the panel clears the highlights");
    }

    private static async Task AnnotateAndSaveAsync(MainPage page)
    {
        await OpenAsync(page, "documents/textbook.pdf");
        Click(page, "tool-annotate");
        AnnotationLayer layer = page.Viewer.Annotations!;
        int before = await layer.LoadPageAsync(0);
        Click(page, "annotate-note");
        await WaitAsync(() => page.Document.IsEdited, "the document is marked edited");
        Check(layer.GetCount(0) == before + 1, "a note was added to page 1");
        await WaitAsync(() => Find<Label>(page, "annotation-count").Text?.Contains(string.Create(CultureInfo.InvariantCulture, $"{before + 1} on this page"), StringComparison.Ordinal) == true, "the list shows it");
        Check(Find<VisualElement>(page, "document-edited").IsVisible, "the top bar shows the edit");

        byte[] saved = await page.Document.SaveAsync() ?? throw new InvalidOperationException("The document did not save.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Check(reopened.Pages[0].Annotations.Any(a => a is PdfTextAnnotation { Contents: "A note added in the Broadside viewer." }), "the saved file has the note");
    }

    private static async Task ViewSettingsAsync(MainPage page)
    {
        await OpenAsync(page, "documents/textbook.pdf");
        Click(page, "tool-view");
        Click(page, "layout-two-up");
        Check(page.Viewer.LayoutOptions?.Mode == PageLayoutMode.TwoUp, "two-up layout");
        Click(page, "rotate-right");
        Check(page.Viewer.LayoutOptions?.Rotation == 90, "rotated right");
        Click(page, "rotate-left");
        Click(page, "layout-continuous");
        Find<Switch>(page, "view-dark-mode").IsToggled = true;
        Check(page.Viewer.ColorFilter == ColorFilter.DarkMode && page.Viewer.CurrentSession?.ColorFilter == ColorFilter.DarkMode, "dark mode on the view and its session");
        await WaitAsync(() => !page.Viewer.State.IsRendering, "the dark tiles");
        Find<Switch>(page, "view-dark-mode").IsToggled = false;
        Check(page.Viewer.CurrentSession?.ColorFilter == ColorFilter.None, "dark mode off");
        double zoom = page.Viewer.State.Zoom;
        Click(page, "zoom-in");
        await WaitAsync(() => page.Viewer.State.Zoom > zoom * 1.1, "zoomed in");
        Click(page, "zoom-fit-width");
        Find<RadioButton>(page, "renderer-managed").IsChecked = true;
        Check(!SampleDocument.UseSkia, "the managed rasterizer draws");
        await WaitAsync(() => !page.Viewer.State.IsRendering, "the managed tiles");
        Find<RadioButton>(page, "renderer-skia").IsChecked = true;
        Check(SampleDocument.UseSkia, "SkiaSharp draws again");
    }

    private static async Task InfoAsync(MainPage page)
    {
        await OpenAsync(page, "documents/tagged-structure.pdf");
        page.Show(SamplePanel.Info);
        Click(page, "info-tab-document");
        Check(Labels(Find<Grid>(page, "info-facts")).Contains("Tagged") && Labels(Find<Grid>(page, "info-facts")).Contains("yes"), "the facts say tagged");
        Click(page, "info-tab-diagnostics");
        Find<Label>(page, "info-diagnostic-count");
        Click(page, "info-tab-tags");
        var tags = Find<CollectionView>(page, "info-tags");
        Check(tags.ItemsSource is System.Collections.ICollection { Count: > 0 }, "the tag tree has roots");
    }

    private static async Task SignaturesAsync(MainPage page)
    {
        await OpenAsync(page, "documents/signed.pdf");
        await WaitAsync(() => page.CurrentPanel == SamplePanel.Signatures, "the gallery opens the signatures panel");
        await WaitAsync(() => TryFind<Label>(page, "signature-badge") is { Text.Length: > 0 }, "a signature status");
    }

    private static async Task OutlineAsync(MainPage page)
    {
        await OpenAsync(page, "documents/novel-large-print.pdf");
        page.Show(SamplePanel.Outline);
        var outline = (PdfOutlinePanel)page.PanelView(SamplePanel.Outline);
        await WaitAsync(() => outline.VisibleCount > 1, "outline entries");
        int target = outline.VisibleCount - 1;
        Check(outline.Activate(target), "the last entry has a destination");
        await WaitAsync(() => page.Viewer.State.PageIndex > 0, "the view moved to the entry's page");
    }

    private static async Task ThumbnailsAsync(MainPage page)
    {
        await OpenAsync(page, "documents/textbook.pdf");
        Click(page, "tool-pages");
        var thumbnails = (PdfThumbnailsPanel)page.PanelView(SamplePanel.Pages);
        await WaitAsync(() => thumbnails.PageCount == 8 && thumbnails.RenderedCount >= 2, "thumbnails");
        thumbnails.Activate(3);
        await WaitAsync(() => page.Viewer.State.PageIndex == 3, "page 4 shown");
    }

    private static async Task CreateAsync(MainPage page)
    {
        Click(page, "tool-create");
        var create = (CreatePanel)page.PanelView(SamplePanel.Create);
        create.SelectConformance("PDF/A-2u", "PDF/UA-1");
        Click(page, "create-run");
        await WaitAsync(() => page.Document.Name == "created.pdf" && page.Viewer.State.PageCount > 0, "the created document opens");
        Check(create.IsConformant, "identified as PDF/A-2u and PDF/UA-1: " + Find<Label>(page, "create-result").Text);
    }

    // ---- Helpers ----

    private static async Task OpenAsync(MainPage page, string file)
    {
        GalleryEntry entry = Array.Find(SampleDocument.Gallery, g => g.File == file) ?? throw new InvalidOperationException(file + " is not in the gallery");
        await page.Document.OpenGalleryAsync(entry);
        Check(page.Document.Message is null || entry.Note is not null, "opened " + file + ": " + page.Document.Message);
        await WaitAsync(() => ReferenceEquals(page.Viewer.CurrentSession, page.Document.Session) && page.Viewer.State is { PageCount: > 0, IsLoading: false }, file + " shown");
    }

    private static T Find<T>(Element root, string automationId)
        where T : Element =>
        TryFind<T>(root, automationId) ?? throw new InvalidOperationException($"No {typeof(T).Name} \"{automationId}\" on screen.");

    private static T? TryFind<T>(Element root, string automationId)
        where T : Element =>
        ((IVisualTreeElement)root).GetVisualTreeDescendants().OfType<T>().FirstOrDefault(e => e is VisualElement { AutomationId: var id } && id == automationId);

    private static void Click(Element root, string automationId)
    {
        var button = Find<Button>(root, automationId);
        Check(button.IsEnabled, automationId + " is enabled");
        ((IButtonController)button).SendClicked();
    }

    private static string Labels(Layout layout) => string.Join("|", layout.Children.OfType<Label>().Select(l => l.Text));

    private static Rect BoundsInPage(VisualElement element)
    {
        double x = element.X;
        double y = element.Y;
        for (Element? parent = element.Parent; parent is VisualElement visual and not Page; parent = parent.Parent)
        {
            x += visual.X + visual.TranslationX;
            y += visual.Y + visual.TranslationY;
            if (visual is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }
        }

        return new Rect(x, y, element.Width, element.Height);
    }

    private static async Task WaitAsync(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > StepTimeout)
            {
                throw new TimeoutException("Timed out waiting for " + what + ".");
            }

            await Task.Delay(100);
        }
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected " + what + ".");
        }
    }

    private static void Log(string line) => Console.WriteLine(line);
}
