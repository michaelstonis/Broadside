using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Keyboard and screen-reader navigation reach page text in reading order (#221 acceptance 3). The expected order comes from the
/// viewer core's <see cref="AccessibilityBridge"/> over the same file (structure order on tagged pages, column detection on untagged
/// ones); the browser must expose that order both as the DOM a screen reader browses and as the keyboard's reading focus.
/// </summary>
public sealed partial class AccessibilityTests(BrowserHost host)
{
    [Theory]
    [InlineData("layout-two-column-untagged.pdf", "reader goes down one column first", "The second column is read after")]
    [InlineData("layout-two-column-tagged.pdf", null, null)]
    [InlineData("tagged-structure.pdf", null, null)]
    public async Task Keyboard_and_screen_reader_navigation_reach_page_text_in_reading_order(string file, string? first, string? second)
    {
        BrowserHost.RequireApp();
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Corpus", file), TestContext.Current.CancellationToken);
        List<(string Text, bool Leaf)> order = ReadingOrder(bytes);
        List<string> expected = [.. order.Select(static node => node.Text)];
        Assert.NotEmpty(expected);
        host.Serve(file, bytes);

        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, "?file=../files/" + file).ToString());
        await page.WaitForSelectorAsync(".bs-a11y section[data-bs-page='0'] [data-bs-node]", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });

        // Screen reader: the accessibility tree of the text layer holds the page's text in reading order.
        string snapshot = await page.Locator(".bs-a11y").AriaSnapshotAsync();
        string flat = Normalize(snapshot);
        int at = 0;
        foreach (string text in order.Where(static node => node.Leaf && node.Text.Length > 0).Select(static node => node.Text))
        {
            int found = flat.IndexOf(text, at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"'{text}' after position {at} of the accessibility tree:\n{snapshot}");
            at = found;
        }

        if (first is not null)
        {
            Assert.True(flat.IndexOf(Normalize(first), StringComparison.Ordinal) < flat.IndexOf(Normalize(second!), StringComparison.Ordinal));
        }

        // Keyboard: N moves the reading focus element by element through the same order.
        await page.Locator(".bs-viewer").FocusAsync();
        string previous = "";
        foreach (string text in expected.Take(12))
        {
            await page.Keyboard.PressAsync("n");
            JsonHandle handle = await WaitForFocusChangeAsync(page, previous);
            previous = handle.Id;
            Assert.Equal(text, Normalize(handle.Text));
        }

        await Viewer.LogFramesAsync(page, file);
    }

    private static async Task<JsonHandle> WaitForFocusChangeAsync(IPage page, string previous)
    {
        await page.WaitForFunctionAsync(
            "previous => { const e = document.activeElement; return e && e.hasAttribute('data-bs-node') && e.id !== previous; }",
            previous,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        return await page.EvaluateAsync<JsonHandle>(
            "() => { const e = document.activeElement; return { id: e.id, text: e.textContent || e.getAttribute('aria-label') || '' }; }");
    }

    /// <summary>The text of page 1's accessible nodes in the order the bridge's element navigation visits them.</summary>
    private static List<(string Text, bool Leaf)> ReadingOrder(byte[] file)
    {
        using DocumentSession session = DocumentSession.Open(file);
        var bridge = new AccessibilityBridge(session);
        bridge.LoadPage(0);
        var texts = new List<(string Text, bool Leaf)>();
        var seen = new HashSet<AccessibleNode>(ReferenceEqualityComparer.Instance);
        for (AccessibleNode? node = bridge.FindNext(null, AccessibleNavigation.Element); node is not null && node.PageIndex == 0 && seen.Add(node); node = bridge.FindNext(node, AccessibleNavigation.Element))
        {
            texts.Add((Normalize(node.Role == AccessibleRole.Figure ? node.Name ?? "" : node.Children.Count == 0 ? node.Value ?? node.Text : node.Text), node.Children.Count == 0));
        }

        return texts;
    }

    private static string Normalize(string text) => Whitespace().Replace(text, "");

    [GeneratedRegex(@"\s+|[""']|- (paragraph|text|heading|group|section|region|document|list|listitem|table|row|cell|rowheader|columnheader|img|figure|link|note)\b|:", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    private sealed class JsonHandle
    {
        public string Id { get; set; } = "";

        public string Text { get; set; } = "";
    }
}
