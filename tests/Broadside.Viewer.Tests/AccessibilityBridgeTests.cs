using System.Globalization;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class AccessibilityBridgeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tagged_file_exposes_its_nodes_in_reading_order_with_heading_levels_and_table_structure()
    {
        await using DocumentSession session = Open("tagged-structure.pdf");
        var bridge = new AccessibilityBridge(session);
        await bridge.LoadPageAsync(0, Token);

        var read = new List<string>();
        while (bridge.MoveFocus(AccessibleNavigation.Element))
        {
            read.Add(Describe(bridge.Focus!));
        }

        Assert.Equal(
            [
                "Section", "Heading 1: Broadside", "Paragraph: A paragraph with a link.",
                "List", "ListItem: 1. First item", "ListMarker: 1.", "Group", "ListItem: 2. Second item", "ListMarker: 2.", "Group",
                "Table 2x2", "Row", "ColumnHeader (0,0): Name", "ColumnHeader (0,1): Value",
                "Row", "Cell (1,0) headers Name: Alpha", "Cell (1,1): 1",
            ],
            read);

        AccessibleNode page = bridge.GetPage(0);
        Assert.Same(bridge.Root, page.Parent);
        Assert.Equal(AccessibleRole.Section, Assert.Single(page.Children).Role);
        Assert.Equal("en-US", page.Children[0].Language);
        Assert.Equal("First item", bridge.FindNext(null, AccessibleNavigation.List)!.Children[0].Children[1].Text);
        Assert.False(bridge.MoveFocus(AccessibleNavigation.Element));
        Assert.Equal("1", bridge.FindNext(null, AccessibleNavigation.Element, backward: true)!.Name);
    }

    [Fact]
    public async Task Heading_landmark_and_table_navigation_skip_to_their_roles_both_ways()
    {
        await using DocumentSession session = Open("tagged-structure.pdf");
        var bridge = new AccessibilityBridge(session);

        Assert.True(bridge.MoveFocus(AccessibleNavigation.Table));
        Assert.Equal(AccessibleRole.Table, bridge.Focus!.Role);
        Assert.True(bridge.MoveFocus(AccessibleNavigation.Heading, backward: true));
        Assert.Equal("Broadside", bridge.Focus!.Name);
        Assert.False(bridge.MoveFocus(AccessibleNavigation.Heading));
        Assert.Null(bridge.FindNext(null, AccessibleNavigation.Heading, level: 2));
        Assert.Equal(AccessibleRole.Section, bridge.FindNext(null, AccessibleNavigation.Landmark)!.Role);
        Assert.Equal(AccessibleRole.List, bridge.FindNext(null, AccessibleNavigation.List, backward: true)!.Role);
        Assert.Null(bridge.FindNext(null, AccessibleNavigation.Figure));
        Assert.Null(bridge.FindNext(null, AccessibleNavigation.Control));
    }

    [Fact]
    public async Task Navigating_pages_moves_the_focus_to_the_new_pages_first_node()
    {
        await using Viewer viewer = await Viewer.OpenAsync("text-tagged-spanning.pdf");
        var bridge = new AccessibilityBridge(viewer.Session) { Viewer = viewer.Zoom };
        var focusChanges = new List<AccessibleNode?>();
        bridge.Observer = change =>
        {
            if (change.Kind == AccessibilityChangeKind.FocusChanged)
            {
                focusChanges.Add(change.Node);
            }
        };
        bridge.Update();
        Assert.Null(bridge.Focus);

        viewer.Input.GoToPage(1);
        viewer.Settle();
        bridge.Update();

        AccessibleNode first = Assert.Single(focusChanges)!;
        Assert.Same(first, bridge.Focus);
        Assert.Equal(1, first.PageIndex);
        Assert.Equal(0, first.Index);
        Assert.Same(bridge.GetPage(1).Children[0], first);
        Assert.Equal(AccessibleStates.Focusable | AccessibleStates.Focused, bridge.GetStates(first));

        viewer.Input.GoToPage(0);
        viewer.Settle();
        bridge.Update();
        Assert.Equal("Before", bridge.Focus!.Name);
        Assert.Equal(AccessibleStates.Focusable | AccessibleStates.Offscreen, bridge.GetStates(first));
    }

    [Fact]
    public async Task Moving_the_focus_to_another_page_scrolls_the_viewer_to_it()
    {
        await using Viewer viewer = await Viewer.OpenAsync("text-tagged-spanning.pdf");
        var bridge = new AccessibilityBridge(viewer.Session) { Viewer = viewer.Zoom };
        bridge.Update();

        Assert.True(bridge.MoveFocus(AccessibleNavigation.Page));
        Assert.Equal(0, bridge.Focus!.PageIndex);
        Assert.True(bridge.MoveFocus(AccessibleNavigation.Page));
        AccessibleNode after = bridge.Focus!;
        Assert.Equal(1, after.PageIndex);
        Assert.Equal(1, viewer.Zoom.Layout.GetCurrentPage(viewer.Zoom.Viewport));
        Assert.True(bridge.TryGetBounds(after, out LayoutRectangle bounds));
        Assert.True(bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= viewer.Zoom.Viewport.Width && bounds.Bottom <= viewer.Zoom.Viewport.Height);
        Assert.False(bridge.TryGetViewportShowing(after, out _));

        bridge.Update();
        Assert.Same(after, bridge.Focus);
        Assert.False(bridge.MoveFocus(AccessibleNavigation.Page));
        Assert.True(bridge.MoveFocus(AccessibleNavigation.Element, backward: true));
        Assert.Equal((0, AccessibleRole.Figure), (bridge.Focus!.PageIndex, bridge.Focus.Role));
        Assert.Equal(0, viewer.Zoom.Layout.GetCurrentPage(viewer.Zoom.Viewport));
    }

    [Fact]
    public async Task Bounds_follow_the_page_layout_and_a_point_finds_the_deepest_node()
    {
        await using DocumentSession session = Open("tagged-structure.pdf");
        var bridge = new AccessibilityBridge(session);
        AccessibleNode heading = bridge.FindNext(null, AccessibleNavigation.Heading)!;
        Assert.False(bridge.TryGetBounds(heading, out _));
        Assert.Equal(AccessibleStates.Focusable | AccessibleStates.Offscreen, bridge.GetStates(heading));

        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        var viewport = new Viewport(0, 0, 800, 1200, 1.5);
        int changes = 0;
        bridge.Observer = change => changes += change.Kind == AccessibilityChangeKind.BoundsChanged ? 1 : 0;
        bridge.Update(layout, viewport);
        bridge.Update(layout, viewport);
        Assert.Equal(1, changes);

        Assert.True(bridge.TryGetBounds(heading, out LayoutRectangle bounds));
        LayoutRectangle expected = layout.PageToViewport(viewport, 0, new PdfRectangle(72, 715, 180, 737.2));
        Assert.Equal(expected.X, bounds.X, 0.2);
        Assert.Equal(expected.Y, bounds.Y, 0.2);
        Assert.Equal(108 * 1.5, bounds.Width, 0.2);
        Assert.Equal(AccessibleStates.Focusable, bridge.GetStates(heading));

        Assert.Same(heading, bridge.GetNodeAt(new PagePoint(0, 100, 725)));
        Assert.Equal("Alpha", bridge.GetNodeAt(new PagePoint(0, 80, 598)).Name);
        Assert.Same(bridge.GetPage(0), bridge.GetNodeAt(new PagePoint(0, 400, 100)));
    }

    [Fact]
    public async Task Provider_elements_follow_the_content_in_focus_order_and_rebuild_on_invalidation()
    {
        await using DocumentSession session = Open("tagged-structure.pdf");
        var field = new object();
        var provider = new FieldProvider(field);
        var bridge = new AccessibilityBridge(session, provider);
        var rebuilt = new List<AccessibleNode?>();
        bridge.Observer = change =>
        {
            if (change.Kind == AccessibilityChangeKind.ChildrenChanged)
            {
                rebuilt.Add(change.Node);
            }
        };

        Assert.True(bridge.MoveFocus(AccessibleNavigation.Control));
        AccessibleNode box = bridge.Focus!;
        Assert.Equal(AccessibleRole.CheckBox, box.Role);
        Assert.Equal("Agree", box.Name);
        Assert.Equal("Yes", box.Value);
        Assert.Same(box, bridge.GetPage(0).Children[^1]);
        Assert.Same(box, bridge.FindNode(0, field));
        Assert.Equal(AccessibleStates.Focusable | AccessibleStates.Focused | AccessibleStates.Checked | AccessibleStates.Offscreen, bridge.GetStates(box));
        Assert.Null(bridge.FindNext(box, AccessibleNavigation.Control));

        provider.Value = "Off";
        bridge.InvalidatePage(0);
        Assert.Equal(2, rebuilt.Count);
        Assert.NotSame(box, bridge.Focus);
        Assert.Equal("Off", bridge.Focus!.Value);
        Assert.Same(bridge.FindNode(0, field), bridge.Focus);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        await using DocumentSession session = Open("tagged-structure.pdf");
        await using DocumentSession other = Open("tagged-structure.pdf");
        var bridge = new AccessibilityBridge(session);
        AccessibleNode foreign = new AccessibilityBridge(other).GetPage(0);

        Assert.Throws<ArgumentNullException>(() => new AccessibilityBridge(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => bridge.GetPage(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bridge.LoadPage(-1));
        Assert.Throws<ArgumentException>(() => bridge.SetFocus(foreign));
        Assert.Throws<ArgumentException>(() => bridge.FindNext(foreign, AccessibleNavigation.Element));
        Assert.Throws<ArgumentOutOfRangeException>(() => bridge.FindNext(null, AccessibleNavigation.Heading, level: -1));
        Assert.Throws<InvalidOperationException>(() => bridge.Update());
        Assert.Throws<ArgumentNullException>(() => bridge.FindNode(0, null!));
        Assert.Equal(AccessibleRole.Document, bridge.Root.Role);
        Assert.Equal(-1, bridge.Root.PageIndex);
    }

    internal static DocumentSession Open(string fileName) =>
        DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background));

    private static string Describe(AccessibleNode node)
    {
        string text = node.Role switch
        {
            AccessibleRole.Heading => string.Create(CultureInfo.InvariantCulture, $"Heading {node.Level}"),
            AccessibleRole.Table => string.Create(CultureInfo.InvariantCulture, $"Table {node.RowCount}x{node.ColumnCount}"),
            AccessibleRole.Cell or AccessibleRole.ColumnHeader or AccessibleRole.RowHeader =>
                string.Create(CultureInfo.InvariantCulture, $"{node.Role} ({node.Row},{node.Column})")
                + (node.Headers.Count > 0 ? " headers " + string.Join(",", node.Headers.Select(h => h.Name)) : string.Empty),
            _ => node.Role.ToString(),
        };
        return node.Name is { } name ? $"{text}: {name.Replace('\n', ' ')}" : text;
    }

    private sealed class FieldProvider(object field) : IAccessibleElementProvider
    {
        public string Value { get; set; } = "Yes";

        public void GetElements(PdfPage page, int pageIndex, ICollection<AccessibleElement> destination) =>
            destination.Add(new AccessibleElement(AccessibleRole.CheckBox, new PdfRectangle(72, 500, 84, 512))
            {
                Name = "Agree",
                Value = Value,
                States = Value == "Yes" ? AccessibleStates.Checked : AccessibleStates.None,
                Source = field,
            });
    }

    /// <summary>A single-page-mode viewer over a corpus file, fit to an 600x800 viewport, with zero-length animations.</summary>
    internal sealed class Viewer : IAsyncDisposable
    {
        private Viewer(DocumentSession session, PageLayout layout)
        {
            Session = session;
            var viewport = new Viewport(0, 0, 600, 800, layout.GetZoom(ZoomMode.FitPage, 600, 800, 0));
            Scheduler = new TileScheduler(session, layout, new NullSurface());
            Zoom = new ProgressiveZoom(Scheduler, viewport);
            Input = new InputModel(Zoom, new InputModelOptions().WithAnimationDuration(TimeSpan.Zero));
        }

        public DocumentSession Session { get; }

        public TileScheduler Scheduler { get; }

        public ProgressiveZoom Zoom { get; }

        public InputModel Input { get; }

        public static async Task<Viewer> OpenAsync(string fileName)
        {
            DocumentSession session = Open(fileName);
            PageLayout layout = await PageLayout.CreateAsync(session, new PageLayoutOptions().WithMode(PageLayoutMode.SinglePage), TestContext.Current.CancellationToken);
            return new Viewer(session, layout);
        }

        public void Settle()
        {
            for (int i = 0; i < 4 && Input.IsAnimating; i++)
            {
                Input.RunFrame(TimeSpan.FromMilliseconds(16));
            }
        }

        public async ValueTask DisposeAsync()
        {
            Scheduler.Dispose();
            await Session.DisposeAsync();
        }
    }
}
