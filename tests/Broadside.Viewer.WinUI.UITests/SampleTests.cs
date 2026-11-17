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
        // Ctrl stays down until the zoom shows: WinUI handles injected key events before an injected wheel event sent with them, so a
        // release sent right after the wheel would arrive first and the wheel would scroll.
        Keyboard.Press(VirtualKeyShort.CONTROL);
        try
        {
            Mouse.Scroll(2);
            app.WaitForState(s => s.Zoom > zoomed, "ctrl+wheel zoom");
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.CONTROL);
        }

        app.Click("FitWidth");
        app.WaitForState(s => Math.Abs(s.Zoom - zoom) <= 1 && s.Activity == "Ready", "fit width");

        // The zoom kept the point under the mouse in place, so page 1's first lines may now be above the viewport: back to the top.
        // Home scrolls there in an animation, and page 1 is already the current page, so the status reads "page 1, ready" before the
        // scroll ends: wait for page 1 to stop moving too. A line read mid-scroll (the newsletter's photo caption, passing through the
        // viewport on its way below it) is gone from where the drag then presses, and off screen for every retry (#563).
        app.Viewer.Focus();
        Keyboard.Type(VirtualKeyShort.HOME);
        app.WaitForState(s => s.Page == 1 && s.Activity == "Ready", "back on page 1 after zooming");
        WaitUntilStill(app);

        // Select: a mouse drag across the first line of text the peers place on screen (the selection line shows it), then Ctrl+A;
        // Copy reports what it copied.
        AutomationElement text = FirstText(app);
        Rectangle line = text.BoundingRectangle;
        Assert.False(line.IsEmpty, "the first text node has no bounds");

        // A node's bounds cover its line boxes, not its glyphs: a drag along the middle of a heading's box can pass between its glyphs
        // or above a line clipped at the viewport's top. Try a few heights across the box, reading the bounds again each time.
        foreach (double at in (double[])[0.5, 0.35, 0.65, 0.2, 0.8])
        {
            line = text.BoundingRectangle;
            if (line.IsEmpty)
            {
                // The line left the viewport (its peer reports no bounds): drag across the first line that shows now.
                WaitUntilStill(app);
                text = FirstText(app);
                line = text.BoundingRectangle;
            }

            int y = line.Y + (int)(line.Height * at);
            DragSlowly(new Point(line.X + 2, y), new Point(line.Right - 2, y));
            if (Selected(app, TimeSpan.FromSeconds(3)))
            {
                break;
            }
        }

        app.WaitForText("Selection", s => s.StartsWith("Selected ", StringComparison.Ordinal), "the drag's selection");
        app.Click("Copy");
        app.WaitForText("Copied", s => s.StartsWith("Copied ", StringComparison.Ordinal) && !s.StartsWith("Copied 0 ", StringComparison.Ordinal), "the dragged selection copied");
        app.Viewer.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        app.Click("Copy");
        string all = app.WaitForText("Copied", s => s.StartsWith("Copied ", StringComparison.Ordinal) && Characters(s) > line.Width / 20, "select all copied");
        Assert.True(Characters(all) > 20, all);

        // Search: a word of page 1's text, found and revealed with every match counted once the index is complete.
        // A whole word of the page's text: surrounding punctuation trimmed, and only words that are letters throughout (dropping the
        // inner hyphen of "two-term" would search for "twoterm", which the page does not have).
        string word = text.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TrimToLetters)
            .Where(w => w.Length > 0 && w.All(char.IsLetter))
            .OrderByDescending(w => w.Length)
            .First();
        Assert.True(word.Length >= 3, $"no word in '{text.Name}'");
        app.Find("SearchBox").AsTextBox().Text = word;
        app.Click("Find");
        string matches = app.WaitForText("MatchCount", s => s.EndsWith("complete", StringComparison.Ordinal), $"search for '{word}'");
        Assert.False(matches.StartsWith("0 ", StringComparison.Ordinal), matches);
        app.Click("FindNext");

        // Dark mode draws the pages again without an error.
        app.Toggle("DarkMode");
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

    [Fact]
    public void A_multi_select_list_box_opens_a_list_whose_mouse_clicks_toggle_options()
    {
        // #560: a click on the multi-select list box of the shared field set (acroform-filling.pdf, `colors`, Opt Red Green Blue, V Red,
        // its widget at [50 370 250 460] on a Letter page) opens a native multi-select list. Each mouse click toggles an option and
        // the list stays open; a click elsewhere writes the picks, and the list opened again shows what the field holds.
        using SampleApp app = SampleApp.Open(SampleApp.CorpusFile("acroform-filling.pdf"));
        WaitUntilStill(app);
        AutomationElement page = SampleApp.WaitFor(() => app.Viewer.FindFirstDescendant(cf => cf.ByAutomationId("page-0")), "page 1's peer");
        Point At(double x, double y)
        {
            Rectangle box = page.BoundingRectangle;
            return new Point(box.X + (int)(box.Width * x / 612), box.Y + (int)(box.Height * (792 - y) / 792));
        }

        AutomationElement[] OpenList()
        {
            Click(At(150, 415));
            AutomationElement list = SampleApp.WaitFor(
                () => app.Viewer.FindFirstDescendant(cf => cf.ByControlType(ControlType.List).And(cf.ByName("Form field"))), "the multi-select list");
            AutomationElement[] items = [];
            Assert.True(SampleApp.Poll(() => (items = list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))).Length == 3), "the list's three options");
            Assert.Equal(["Red", "Green", "Blue"], items.Select(item => item.Name));
            return items;
        }

        static bool[] Selected(AutomationElement[] items) => [.. items.Select(item => item.AsListBoxItem().IsSelected)];

        AutomationElement[] options = OpenList();
        Assert.Equal([true, false, false], Selected(options));

        // Blue on (Red and Blue picked), then Red off: a pick that replaced the selection would end on Red.
        Click(options[2].GetClickablePoint());
        Assert.True(SampleApp.Poll(() => Selected(options) is [true, false, true]), "Blue toggled on beside Red");
        Click(options[0].GetClickablePoint());
        Assert.True(SampleApp.Poll(() => Selected(options) is [false, false, true]), "Red toggled off");

        // A click on the page away from the fields ends the edit; the list opened again shows the field's value.
        Click(At(450, 720));
        Assert.True(SampleApp.Poll(() => app.Viewer.FindFirstDescendant(cf => cf.ByControlType(ControlType.List).And(cf.ByName("Form field"))) is null), "the list closed");
        options = OpenList();
        Assert.True(SampleApp.Poll(() => Selected(options) is [false, false, true]), "the field holds Blue");
    }

    // A click the way a hand makes one: move, press, release, with pauses between (see DragSlowly).
    private static void Click(Point at)
    {
        Send(at, MouseEventFlags.MOUSEEVENTF_MOVE);
        Thread.Sleep(100);
        Send(at, MouseEventFlags.MOUSEEVENTF_MOVE | MouseEventFlags.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(60);
        Send(at, MouseEventFlags.MOUSEEVENTF_MOVE | MouseEventFlags.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(200);
    }

    private static int Characters(string copied) =>
        int.TryParse(copied.Split(' ')[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static bool Selected(SampleApp app, TimeSpan within)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < within)
        {
            if (app.Find("Selection").Name.StartsWith("Selected ", StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    // Page 1's peer at the same bounds over a few reads in a row, with the status ready: no scroll or zoom animation is running.
    private static void WaitUntilStill(SampleApp app)
    {
        AutomationElement page = SampleApp.WaitFor(() => app.Viewer.FindFirstDescendant(cf => cf.ByAutomationId("page-0")), "page 1's peer");
        Rectangle last = Rectangle.Empty;
        int still = 0;
        Assert.True(
            SampleApp.Poll(() =>
            {
                Rectangle now = page.BoundingRectangle;
                still = !now.IsEmpty && now == last && app.ReadStatus().Activity == "Ready" ? still + 1 : 0;
                last = now;
                return still >= 3;
            }),
            $"page 1 did not stop moving: {last}, {app.ReadStatus().Text}");
    }

    private static string TrimToLetters(string word)
    {
        int start = 0, end = word.Length;
        while (start < end && !char.IsLetter(word[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetter(word[end - 1]))
        {
            end--;
        }

        return word[start..end];
    }

    private static AutomationElement FirstText(SampleApp app)
    {
        AutomationElement page = SampleApp.WaitFor(() => app.Viewer.FindFirstDescendant(cf => cf.ByAutomationId("page-0")), "page 1's peer");
        AutomationElement? found = null;
        // The first line on screen in reading order with some length; in a short window a page's paragraphs can sit below the
        // viewport (their peers report no bounds), leaving only a short heading such as the newsletter's masthead in view.
        if (SampleApp.Poll(() =>
        {
            AutomationElement[] shown = [.. page.FindAllDescendants().Where(e => e.ControlType == ControlType.Text && !e.BoundingRectangle.IsEmpty)];
            found = shown.FirstOrDefault(e => e.Name.Trim().Length > 12) ?? shown.FirstOrDefault(e => e.Name.Trim().Length >= 5);
            return found is not null;
        }))
        {
            return found!;
        }

        // Say what page 1 exposed instead: each node's type, name and bounds, against the viewer's bounds.
        var seen = new System.Text.StringBuilder();
        seen.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"viewer {app.Viewer.BoundingRectangle}, page 1 {page.BoundingRectangle}; status '{app.ReadStatus().Text}'");
        foreach (AutomationElement node in page.FindAllDescendants().Take(40))
        {
            string name = node.Name.Length > 60 ? node.Name[..60] : node.Name;
            seen.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {node.ControlType} '{name}' {node.BoundingRectangle}");
        }

        Assert.Fail("Timed out waiting for a text node on page 1." + Environment.NewLine + seen);
        return null!;
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

    // A drag the way a hand makes one: press, a few moves, release, with pauses between. Injected input sent all at once can reach
    // WinUI out of order (as Ctrl+wheel showed), so a one-shot drag may arrive as a click. Every step is a SendInput mouse event at
    // an absolute position, as a real mouse sends: FlaUI's Mouse.MoveTo only repositions the cursor (SetCursorPos), and nothing
    // promises that WinUI's pointer input reports that as a move with the button down.
    private static void DragSlowly(Point from, Point to)
    {
        Send(from, MouseEventFlags.MOUSEEVENTF_MOVE);
        Thread.Sleep(100);
        Send(from, MouseEventFlags.MOUSEEVENTF_MOVE | MouseEventFlags.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(100);
        const int steps = 12;
        for (int step = 1; step <= steps; step++)
        {
            Send(new Point(from.X + ((to.X - from.X) * step / steps), from.Y + ((to.Y - from.Y) * step / steps)), MouseEventFlags.MOUSEEVENTF_MOVE);
            Thread.Sleep(30);
        }

        Thread.Sleep(100);
        Send(to, MouseEventFlags.MOUSEEVENTF_MOVE | MouseEventFlags.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(100);
    }

    // One mouse event at a screen point (physical pixels), in the 0..65535 coordinates SendInput takes across the virtual desktop.
    private static void Send(Point point, MouseEventFlags flags)
    {
        int left = User32.GetSystemMetrics(SystemMetric.SM_XVIRTUALSCREEN);
        int top = User32.GetSystemMetrics(SystemMetric.SM_YVIRTUALSCREEN);
        int width = Math.Max(User32.GetSystemMetrics(SystemMetric.SM_CXVIRTUALSCREEN) - 1, 1);
        int height = Math.Max(User32.GetSystemMetrics(SystemMetric.SM_CYVIRTUALSCREEN) - 1, 1);
        INPUT[] input =
        [
            new INPUT
            {
                type = InputType.INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = (int)Math.Round((point.X - left) * 65535.0 / width),
                        dy = (int)Math.Round((point.Y - top) * 65535.0 / height),
                        dwFlags = flags | MouseEventFlags.MOUSEEVENTF_ABSOLUTE | MouseEventFlags.MOUSEEVENTF_VIRTUALDESK,
                    },
                },
            },
        ];
        Assert.Equal(1u, User32.SendInput(1, input, INPUT.Size));
    }
}
