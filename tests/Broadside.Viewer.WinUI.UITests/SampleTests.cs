using System.Drawing;
using Broadside.Fonts.Standard14;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Broadside.Viewer.WinUI.UITests;

/// <summary>
/// The WinUI sample on the pinned subset (representative sample documents, all tagged and multi-page): it opens, scrolls, zooms,
/// selects and searches through real input, and its automation peers expose page text in reading order.
/// The tests stay in this one class: xUnit runs a class's tests one at a time, and they share the mouse, the keyboard and the desktop.
/// </summary>
public sealed class SampleTests
{
    /// <summary>The pinned subset: sample documents the sample app carries.</summary>
    public static TheoryData<string> PinnedSubset => ["textbook.pdf", "newsletter.pdf", "paper.pdf", "novel-hardback.pdf"];

    [Theory]
    [MemberData(nameof(PinnedSubset))]
    public void The_sample_opens_scrolls_zooms_selects_and_searches(string document)
    {
        using SampleApp app = SampleApp.Open(document);
        SampleApp.Status opened = app.ReadStatus();
        Assert.Equal(1, opened.Page);
        Assert.True(opened.PageCount > 1, opened.Text);

        // Scroll: wheel notches over the page move to a later page; End and Home jump to the last and first.
        app.Viewer.Focus();
        Rectangle viewer = app.Viewer.BoundingRectangle;
        Mouse.MoveTo(new Point(viewer.X + (viewer.Width / 2), viewer.Y + (viewer.Height / 2)));
        Assert.True(SampleApp.Poll(() =>
        {
            Mouse.Scroll(-5);
            return app.ReadStatus().Page > 1;
        }), $"wheel scrolling did not leave page 1: {app.ReadStatus().Text}");
        Keyboard.Type(VirtualKeyShort.END);
        app.WaitForState(s => s.Page == s.PageCount, "the last page");
        Keyboard.Type(VirtualKeyShort.HOME);
        app.WaitForState(s => s.Page == 1 && s.Activity == "Ready", "back on page 1");

        // Zoom: the toolbar's + zooms by a quarter; Ctrl+wheel zooms too; Fit width goes back.
        int zoom = app.ReadStatus().Zoom;
        app.Click("ZoomIn");
        app.WaitForState(s => s.Zoom >= zoom * 1.2, "zoom in");
        int zoomed = app.ReadStatus().Zoom;
        Mouse.MoveTo(new Point(viewer.X + (viewer.Width / 2), viewer.Y + (viewer.Height / 2)));
        Keyboard.Press(VirtualKeyShort.CONTROL);
        try
        {
            Mouse.Scroll(2);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.CONTROL);
        }

        app.WaitForState(s => s.Zoom > zoomed, "ctrl+wheel zoom");
        app.Click("FitWidth");
        app.WaitForState(s => Math.Abs(s.Zoom - zoom) <= 1 && s.Activity == "Ready", "fit width");

        // Select: a mouse drag across the first line of text the peers place on screen, then Ctrl+A; Copy reports what it copied.
        AutomationElement text = FirstText(app);
        Rectangle line = text.BoundingRectangle;
        Assert.False(line.IsEmpty, "the first text node has no bounds");
        int y = line.Y + (line.Height / 2);
        Mouse.Drag(new Point(line.X + 2, y), new Point(line.Right - 2, y));
        app.Click("Copy");
        app.WaitForText("Copied", s => s.StartsWith("Copied ", StringComparison.Ordinal) && !s.StartsWith("Copied 0 ", StringComparison.Ordinal), "the dragged selection copied");
        app.Viewer.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        app.Click("Copy");
        string all = app.WaitForText("Copied", s => s.StartsWith("Copied ", StringComparison.Ordinal) && Characters(s) > line.Width / 20, "select all copied");
        Assert.True(Characters(all) > 20, all);

        // Search: a word of page 1's text, found and revealed with every match counted once the index is complete.
        string word = text.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new string([.. w.Where(char.IsLetter)])).OrderByDescending(w => w.Length).First();
        Assert.True(word.Length >= 3, $"no word in '{text.Name}'");
        app.Find("SearchBox").AsTextBox().Text = word;
        app.Click("Find");
        string matches = app.WaitForText("MatchCount", s => s.EndsWith("complete", StringComparison.Ordinal), $"search for '{word}'");
        Assert.False(matches.StartsWith("0 ", StringComparison.Ordinal), matches);
        app.Click("FindNext");

        // Dark mode draws the pages again without an error.
        app.Click("DarkMode");
        app.WaitForState(s => s.Activity == "Ready", "dark mode drawn");
    }

    [Theory]
    [InlineData("tagged-structure.pdf")]
    [InlineData("novel-hardback.pdf")]
    [InlineData("textbook.pdf")]
    public async Task The_automation_peers_expose_page_one_text_in_reading_order(string document)
    {
        string[] expected = await ReadingOrderAsync(SampleApp.DocumentPath(document));
        Assert.NotEmpty(expected);

        using SampleApp app = SampleApp.Open(document);
        AutomationElement page = SampleApp.WaitFor(() => app.Viewer.FindFirstDescendant(cf => cf.ByAutomationId("page-0")), "page 1's peer");
        Assert.Equal("Page 1", page.Name);
        string[] names = [];
        Assert.True(SampleApp.Poll(() =>
        {
            names = [.. page.FindAllDescendants().Where(e => e.ControlType == ControlType.Text).Select(e => e.Name.Trim()).Where(n => n.Length > 0)];
            return names.Length >= expected.Length;
        }), $"page 1 exposed {names.Length} of {expected.Length} text nodes");
        Assert.Equal(expected, names);

        // Headings carry their level.
        AutomationElement? heading = page.FindAllDescendants().FirstOrDefault(e => e.Properties.HeadingLevel.TryGetValue(out HeadingLevel level) && level != HeadingLevel.None);
        Assert.NotNull(heading);
    }

    private static int Characters(string copied) =>
        int.TryParse(copied.Split(' ')[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static AutomationElement FirstText(SampleApp app)
    {
        AutomationElement page = SampleApp.WaitFor(() => app.Viewer.FindFirstDescendant(cf => cf.ByAutomationId("page-0")), "page 1's peer");
        return SampleApp.WaitFor(
            () => page.FindAllDescendants().FirstOrDefault(e => e.ControlType == ControlType.Text && e.Name.Trim().Length > 12 && !e.BoundingRectangle.IsEmpty),
            "a text node on page 1");
    }

    // What the viewer core says page 1 reads, in order: the text-like nodes, named as the peers name them.
    private static async Task<string[]> ReadingOrderAsync(string path)
    {
        ViewerOptions options = new ViewerOptions()
            .WithDocumentOptions(new PdfOptions().UseStandard14Fonts())
            .WithThreading(ViewerThreading.Cooperative);
        await using DocumentSession session = DocumentSession.Open(path, options);
        var bridge = new AccessibilityBridge(session);
        AccessibleNode page = await bridge.LoadPageAsync(0);
        return [.. Flatten(page)
            .Where(n => n.Role is AccessibleRole.Heading or AccessibleRole.Paragraph or AccessibleRole.Text or AccessibleRole.Caption or AccessibleRole.ListMarker)
            .Select(n => (n.Name ?? n.Text).Trim())
            .Where(n => n.Length > 0)];

        static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(child => (IEnumerable<AccessibleNode>)[child, .. Flatten(child)]);
    }
}
