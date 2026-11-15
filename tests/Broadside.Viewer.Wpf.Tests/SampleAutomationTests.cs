using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>
/// #222's acceptance test: the sample app, started as its own process and driven only through UI Automation, opens each document of
/// the pinned subset, exposes page 1's text in reading order through the viewer's automation peers, zooms, scrolls to the end,
/// selects all and searches. The pinned subset is a fixed set of the repository's representative sample documents (text, columns,
/// vertical writing, forms, scans) plus, where the real-world corpus is fetched (<c>BROADSIDE_CORPUS_DIR</c>), the rendering gate's
/// pinned real-world files. Needs an interactive Windows desktop (the Windows CI runner has one).
/// </summary>
[Trait("Category", "UIAutomation")]
public sealed partial class SampleAutomationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly string[] SampleSubset =
    [
        "textbook.pdf", "paper.pdf", "newsletter.pdf", "leaflet-multilingual.pdf", "rashomon-vertical.pdf", "application-form.pdf",
        "invoice-conservative.pdf", "scan-memo-g4.pdf",
    ];

    public static TheoryData<string> PinnedSubset()
    {
        var data = new TheoryData<string>();
        foreach (string name in SampleSubset)
        {
            data.Add(Shell.SampleDocument(name));
        }

        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        string pinned = Path.Combine(Corpus.Directory, "..", "Broadside.Rendering.Tests", "CorpusGate", "pinned-pages.txt");
        if (!string.IsNullOrEmpty(corpus) && File.Exists(pinned))
        {
            foreach (string line in File.ReadLines(pinned))
            {
                if (line.Length > 0 && line[0] != '#' && Path.Combine(corpus, line.Split('\t')[0]) is { } file && File.Exists(file))
                {
                    data.Add(file);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PinnedSubset))]
    public async Task The_sample_opens_reads_zooms_scrolls_selects_and_searches_the_document(string path)
    {
        // What the app must show, worked out here through the same viewer core.
        (int pageCount, string pageText) = await ExpectedAsync(path);

        using Process app = StartSample(path);
        try
        {
            AutomationElement window = WaitFor(() => MainWindow(app), "the main window");
            AutomationElement viewer = Find(window, "Viewer");
            Assert.Equal(ControlType.Document, viewer.Current.ControlType);

            // Opens: the status shows the page count, and page 1's peer carries its text in reading order.
            WaitForText(window, "PageStatus", text => text == $"Page 1 of {pageCount}");
            AutomationElement page = WaitFor(() => viewer.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "Page 1")), "page 1's automation element");
            Assert.Equal(pageText, ((ValuePattern)page.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);

            // Zooms.
            double zoom = Percent(Text(window, "ZoomStatus"));
            Invoke(window, "ZoomIn");
            WaitForText(window, "ZoomStatus", text => Percent(text) > zoom);

            // Scrolls (the scroll pattern of the viewer's peer) to the end of the document.
            var scroll = (ScrollPattern)viewer.GetCurrentPattern(ScrollPattern.Pattern);
            if (scroll.Current.VerticallyScrollable)
            {
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 100);
                WaitFor(() => scroll.Current.VerticalScrollPercent >= 99.9 ? viewer : null, "the scroll to the end");
                WaitForText(window, "PageStatus", text => text == $"Page {pageCount} of {pageCount}");
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
                WaitForText(window, "PageStatus", text => text.StartsWith("Page 1 of", StringComparison.Ordinal));
            }

            // Selects every page's text.
            Invoke(window, "SelectAll");
            if (pageText.Length > 0)
            {
                WaitForText(window, "SelectionStatus", text => SelectedCount().Match(text) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) > 0);
            }

            // Searches for a word of page 1 and finds it.
            if (FirstWord().Match(pageText) is { Success: true } word)
            {
                ((ValuePattern)Find(window, "SearchBox").GetCurrentPattern(ValuePattern.Pattern)).SetValue(word.Value);
                Invoke(window, "Find");
                WaitForText(window, "SearchStatus", text => MatchStatus().IsMatch(text));
            }

            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.WaitForExitAsync(exit.Token);
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<(int PageCount, string PageText)> ExpectedAsync(string path)
    {
        // The sample's options: the bundled Standard 14 fonts and predefined CMaps, the system's fonts for the rest.
        var options = new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps()).WithThreading(ViewerThreading.Cooperative);
        await using DocumentSession session = DocumentSession.Open(path, options);
        var bridge = new AccessibilityBridge(session);
        AccessibleNode page = await bridge.LoadPageAsync(0, TestContext.Current.CancellationToken);
        return (session.PageCount, ReadingOrder.TextOf(page));
    }

    private static Process StartSample(string path)
    {
        string executable = Environment.GetEnvironmentVariable("BROADSIDE_WPF_SAMPLE") is { Length: > 0 } configured
            ? configured
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "Broadside.Viewer.Wpf.Sample", new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name, "Broadside.Viewer.Wpf.Sample.exe"));
        Assert.True(File.Exists(executable), $"The sample app is not built at {executable} (set BROADSIDE_WPF_SAMPLE to its path).");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(path));
        return Process.Start(start) ?? throw new InvalidOperationException("The sample app did not start.");
    }

    private static AutomationElement? MainWindow(Process app)
    {
        Assert.False(app.HasExited, $"The sample app exited with code {(app.HasExited ? app.ExitCode : 0)}.");
        return AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "MainWindow")));
    }

    private static AutomationElement Find(AutomationElement window, string automationId) =>
        WaitFor(() => window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)), automationId);

    private static string Text(AutomationElement window, string automationId) => Find(window, automationId).Current.Name;

    private static void Invoke(AutomationElement window, string automationId) =>
        ((InvokePattern)Find(window, automationId).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static void WaitForText(AutomationElement window, string automationId, Func<string, bool> expected)
    {
        AutomationElement element = Find(window, automationId);
        string last = string.Empty;
        try
        {
            WaitFor(() => expected(last = element.Current.Name) ? element : null, automationId);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{exception.Message} It last read \"{last}\".", exception);
        }
    }

    private static T WaitFor<T>(Func<T?> probe, string what)
        where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Patience)
        {
            if (probe() is { } found)
            {
                return found;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    private static double Percent(string text) => double.Parse(text.TrimEnd('%', ' '), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+) characters selected$")]
    private static partial Regex SelectedCount();

    [GeneratedRegex(@"\b\p{L}{4,}\b")]
    private static partial Regex FirstWord();

    [GeneratedRegex(@"^[1-9]\d* of [1-9]\d*\+?$")]
    private static partial Regex MatchStatus();
}
