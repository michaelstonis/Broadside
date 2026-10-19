using Broadside.Layout;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The framework-neutral accessible tree of a document session: a document node whose children are page nodes, each page's
/// content nodes built on demand from its layout tree, reading-order navigation (next and previous element, heading, landmark,
/// table, list, figure, link, control, page), an accessible focus that follows the viewer's page and moves the viewer to what it
/// focuses, and change notifications a shell maps onto ARIA/DOM, UI Automation or NSAccessibility.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8 (tagged PDF), §14.8.2.5 (reading order), §14.9.3 (alternate descriptions), Spec #33 viewer capability 8.
/// The tree is virtualised by page: a page's children exist once it is loaded (<see cref="LoadPage"/>,
/// <see cref="LoadPageAsync"/>), and navigation loads the pages it walks through. Each load raises
/// <see cref="AccessibilityChangeKind.ChildrenChanged"/>.
/// </para>
/// <para>
/// The viewer drives the bridge with <see cref="Update()"/> (or <see cref="Update(PageLayout, in Viewport)"/>) once per frame:
/// a change of the viewer's current page moves the focus to the first node of the new page. With a <see cref="Viewer"/>, moving
/// the focus scrolls it so the focused node is in view. Navigation, focus, state and bounds queries over loaded pages allocate
/// nothing. Not thread-safe: drive it from the UI thread.
/// </para>
/// </remarks>
public sealed class AccessibilityBridge
{
    private readonly AccessibleNode[] _pages;
    private readonly AccessibleNode[]?[] _orders;
    private readonly List<AccessibleElement> _elements = [];
    private PageLayout? _layout;
    private Viewport _viewport;
    private int _followPending = -1;
    private int _currentPage = -1;

    /// <summary>Initializes a bridge over a session's document.</summary>
    /// <param name="session">The document session.</param>
    public AccessibilityBridge(DocumentSession session)
        : this(session, null)
    {
    }

    /// <summary>Initializes a bridge over a session's document with the elements of the viewer's annotation, link and form layers.</summary>
    /// <param name="session">The document session.</param>
    /// <param name="elements">The provider of each page's non-content elements in focus order, or null for none.</param>
    public AccessibilityBridge(DocumentSession session, IAccessibleElementProvider? elements)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Elements = elements;
        Root = new AccessibleNode(this, AccessibleRole.Document, -1, null)
        {
            Document = session.Document,
            Language = session.Document.Language,
        };
        _pages = new AccessibleNode[session.PageCount];
        _orders = new AccessibleNode[]?[session.PageCount];
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i] = new AccessibleNode(this, AccessibleRole.Page, i, Root)
            {
                PageBounds = session.TryGetPageMetrics(i, out PageMetrics metrics) ? metrics.CropBox : null,
                Language = Root.Language,
            };
        }

        Root.ChildArray = _pages;
    }

    /// <summary>
    /// Gets or sets the callback told when the focus moves, a page's children are built, or the viewport changes; the shell raises
    /// the matching automation events from it.
    /// </summary>
    public Action<AccessibilityChange>? Observer { get; set; }

    /// <summary>Gets the document session.</summary>
    public DocumentSession Session { get; }

    /// <summary>Gets the provider of non-content elements, or null.</summary>
    public IAccessibleElementProvider? Elements { get; }

    /// <summary>Gets the document node, whose children are the page nodes.</summary>
    public AccessibleNode Root { get; }

    /// <summary>
    /// Gets or sets the viewer the bridge follows: <see cref="Update()"/> reads its layout and viewport, and moving the focus
    /// scrolls it to the focused node.
    /// </summary>
    public ProgressiveZoom? Viewer { get; set; }

    /// <summary>Gets the node with the accessible focus, or null.</summary>
    public AccessibleNode? Focus { get; private set; }

    /// <summary>Gets a page's node; its children are empty until the page is loaded.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The page node.</returns>
    public AccessibleNode GetPage(int pageIndex) => _pages[CheckPage(pageIndex)];

    /// <summary>Returns whether a page's children are built.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>True when the page is loaded.</returns>
    public bool IsPageLoaded(int pageIndex) => _orders[CheckPage(pageIndex)] is not null;

    /// <summary>Builds a page's children from its layout tree, on the calling thread, if they are not built yet.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The page node.</returns>
    /// <remarks>A remote document's page must be fetched first: use <see cref="LoadPageAsync"/>.</remarks>
    public AccessibleNode LoadPage(int pageIndex)
    {
        LoadOrder(CheckPage(pageIndex));
        if (_followPending == pageIndex)
        {
            _followPending = -1;
            if (_currentPage == pageIndex && Focus?.PageIndex != pageIndex)
            {
                SetFocusCore(FirstNode(pageIndex), scroll: false, follow: true);
            }
        }

        return _pages[pageIndex];
    }

    /// <summary>Fetches a page (remote documents) through the session, then builds its children.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The page node.</returns>
    public async ValueTask<AccessibleNode> LoadPageAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        CheckPage(pageIndex);
        if (_orders[pageIndex] is null)
        {
            // A remote page and everything it uses are fetched first (the layout reads its content and fonts); no display list is built.
            PdfPage page = await Session.LoadPageAsync(pageIndex, cancellationToken).ConfigureAwait(true);
            if (Session.IsRemote)
            {
                await page.PrefetchAsync(cancellationToken).ConfigureAwait(true);
            }

            // The layout (structure tree or content reading order) is extracted off the UI thread in background mode (#357); the
            // nodes are built back on the caller's context.
            LayoutTree tree = await Session.ReadAsync(_ => page.ExtractLayout(), cancellationToken).ConfigureAwait(true);
            if (_orders[pageIndex] is null)
            {
                LoadOrder(pageIndex, tree);
            }
        }

        return LoadPage(pageIndex);
    }

    /// <summary>
    /// Rebuilds a loaded page's children, for when its elements' values or states changed; the focus moves to the rebuilt node
    /// with the same source or position.
    /// </summary>
    /// <param name="pageIndex">The zero-based page.</param>
    public void InvalidatePage(int pageIndex)
    {
        if (_orders[CheckPage(pageIndex)] is not { } old)
        {
            return;
        }

        AccessibleNode? focus = Focus is { } f && f.PageIndex == pageIndex && f.Role != AccessibleRole.Page ? f : null;
        _orders[pageIndex] = null;
        _pages[pageIndex].ChildArray = [];
        AccessibleNode[] order = LoadOrder(pageIndex);
        if (old.Length == 0 || focus is not { } focused)
        {
            return;
        }

        AccessibleNode? replacement = focused.Source is { } source ? FindNode(pageIndex, source) : null;
        replacement ??= order.Length > 0 ? order[Math.Min(focused.Index, order.Length - 1)] : _pages[pageIndex];
        SetFocusCore(replacement, scroll: false);
    }

    /// <summary>Finds a loaded page's node for an element source (or the layout node a content node was built from).</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="source">The element's source or a <see cref="LayoutNode"/>.</param>
    /// <returns>The node, or null.</returns>
    public AccessibleNode? FindNode(int pageIndex, object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_orders[CheckPage(pageIndex)] is { } order)
        {
            foreach (AccessibleNode node in order)
            {
                if (ReferenceEquals(node.Source, source) || ReferenceEquals(node.LayoutNode, source))
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the deepest node whose page bounds contain a point (explore by touch), loading the page if needed.</summary>
    /// <param name="point">The point, in the page's default user space (<see cref="PageLayout.TryGetPageAt"/>).</param>
    /// <returns>The deepest node there, or the page node.</returns>
    public AccessibleNode GetNodeAt(in PagePoint point)
    {
        AccessibleNode best = _pages[CheckPage(point.PageIndex)];
        foreach (AccessibleNode node in LoadOrder(point.PageIndex))
        {
            if (node.PageBounds is { } r && point.X >= r.Left && point.X <= r.Right && point.Y >= r.Bottom && point.Y <= r.Top
                && node.Depth >= best.Depth)
            {
                best = node;
            }
        }

        return best;
    }

    /// <summary>Finds the next node in reading order matching a target, loading pages as it walks.</summary>
    /// <param name="from">The node to start after (before, backwards); null or the document starts at the document's edge.</param>
    /// <param name="target">What to find.</param>
    /// <param name="backward">True to walk backwards.</param>
    /// <param name="level">For headings, the level to find; 0 for any.</param>
    /// <returns>The node found, or null at the end of the document.</returns>
    public AccessibleNode? FindNext(AccessibleNode? from, AccessibleNavigation target, bool backward = false, int level = 0)
    {
        if (from is not null && from.Owner != this)
        {
            throw new ArgumentException("The node belongs to another bridge.", nameof(from));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(level);
        if (_pages.Length == 0)
        {
            return null;
        }

        bool fromEdge = from is null || from.Role == AccessibleRole.Document;
        int page = fromEdge ? (backward ? _pages.Length - 1 : 0) : from!.PageIndex;
        if (target == AccessibleNavigation.Page)
        {
            int next = fromEdge ? page : page + (backward ? -1 : 1);
            return (uint)next < (uint)_pages.Length ? FirstNode(next) : null;
        }

        int index = fromEdge ? (backward ? int.MaxValue : -1) : from!.Index;
        int step = backward ? -1 : 1;
        for (int p = page; (uint)p < (uint)_pages.Length; p += step)
        {
            AccessibleNode[] order = LoadOrder(p);
            int i = p != page ? (backward ? order.Length - 1 : 0) : backward ? Math.Min(index, order.Length) - 1 : index + 1;
            for (; (uint)i < (uint)order.Length; i += step)
            {
                if (Matches(order[i], target, level))
                {
                    return order[i];
                }
            }
        }

        return null;
    }

    /// <summary>Moves the focus to the next node matching a target (<see cref="FindNext"/> from <see cref="Focus"/>).</summary>
    /// <param name="target">What to find.</param>
    /// <param name="backward">True to walk backwards.</param>
    /// <param name="level">For headings, the level to find; 0 for any.</param>
    /// <returns>True when the focus moved; false at the end of the document.</returns>
    public bool MoveFocus(AccessibleNavigation target, bool backward = false, int level = 0)
    {
        if (FindNext(Focus, target, backward, level) is not { } node)
        {
            return false;
        }

        SetFocusCore(node, scroll: true);
        return true;
    }

    /// <summary>Moves the focus to a node (or clears it), scrolling the <see cref="Viewer"/> to it.</summary>
    /// <param name="node">The node, or null.</param>
    public void SetFocus(AccessibleNode? node)
    {
        if (node is not null && node.Owner != this)
        {
            throw new ArgumentException("The node belongs to another bridge.", nameof(node));
        }

        SetFocusCore(node, scroll: true);
    }

    /// <summary>Follows the <see cref="Viewer"/>: <see cref="Update(PageLayout, in Viewport)"/> with its layout and viewport.</summary>
    public void Update()
    {
        ProgressiveZoom viewer = Viewer ?? throw new InvalidOperationException("The bridge has no viewer.");
        Update(viewer.Layout, viewer.Viewport);
    }

    /// <summary>
    /// Tells the bridge the viewer's layout and viewport, once per frame: a change raises
    /// <see cref="AccessibilityChangeKind.BoundsChanged"/>, and a change of the current page moves the focus to the first node of
    /// the new page unless the focus is already there.
    /// </summary>
    /// <param name="layout">The page layout.</param>
    /// <param name="viewport">The viewport.</param>
    public void Update(PageLayout layout, in Viewport viewport) => UpdateCore(layout, viewport, follow: true);

    /// <summary>Returns a node's states: its own (focusable, element states) plus focused and offscreen.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The states.</returns>
    public AccessibleStates GetStates(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        AccessibleStates states = node.StaticStates;
        if (ReferenceEquals(node, Focus))
        {
            states |= AccessibleStates.Focused;
        }

        if (!TryGetBounds(node, out LayoutRectangle bounds) || !IsShown(node, _viewport, bounds))
        {
            states |= AccessibleStates.Offscreen;
        }

        return states;
    }

    /// <summary>Returns a node's bounds in viewport pixels for the last <see cref="Update(PageLayout, in Viewport)"/>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="bounds">The bounds, from the viewport's top-left corner in device pixels.</param>
    /// <returns>False when the bridge has no layout yet or the node has no bounds (the document, content with no geometry).</returns>
    public bool TryGetBounds(AccessibleNode node, out LayoutRectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(node);
        return TryGetBounds(node, _viewport, out bounds);
    }

    /// <summary>Returns the viewport that brings a node into view with the least scrolling from the last viewport.</summary>
    /// <param name="node">The node.</param>
    /// <param name="viewport">The viewport showing the node.</param>
    /// <returns>True when the viewport must move; false when the node is in view, has no bounds, or there is no layout.</returns>
    public bool TryGetViewportShowing(AccessibleNode node, out Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(node);
        viewport = _viewport;
        if (_layout is not { } layout || node.PageIndex < 0 || !TryGetBounds(node, viewport, out LayoutRectangle bounds))
        {
            return false;
        }

        if (!IsShown(node, viewport, bounds))
        {
            viewport = layout.ScrollTo(viewport, node.PageIndex);
            TryGetBounds(node, viewport, out bounds);
        }

        double dx = Scroll(bounds.X, bounds.Width, viewport.Width);
        double dy = Scroll(bounds.Y, bounds.Height, viewport.Height);
        viewport = layout.Clamp(viewport with { X = viewport.X + dx, Y = viewport.Y + dy });
        return viewport != _viewport;
    }

    private static double Scroll(double start, double length, double size) =>
        start < 0 || length > size ? start : start + length > size ? start + length - size : 0;

    private static bool Matches(AccessibleNode node, AccessibleNavigation target, int level) => target switch
    {
        AccessibleNavigation.Element => true,
        AccessibleNavigation.Heading => node.Role == AccessibleRole.Heading && (level == 0 || node.Level == level),
        AccessibleNavigation.Landmark => node.Role is AccessibleRole.Section or AccessibleRole.Note,
        AccessibleNavigation.Table => node.Role == AccessibleRole.Table,
        AccessibleNavigation.List => node.Role == AccessibleRole.List,
        AccessibleNavigation.Figure => node.Role == AccessibleRole.Figure,
        AccessibleNavigation.Link => node.Role == AccessibleRole.Link,
        AccessibleNavigation.Control => node.IsElement,
        _ => false,
    };

    private void UpdateCore(PageLayout layout, in Viewport viewport, bool follow)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (ReferenceEquals(layout, _layout) && viewport == _viewport)
        {
            return;
        }

        _layout = layout;
        _viewport = viewport;
        int previous = _currentPage;
        int current = layout.GetCurrentPage(viewport);
        _currentPage = current;
        Raise(AccessibilityChangeKind.BoundsChanged, null);
        if (follow && previous >= 0 && current != previous && (uint)current < (uint)_pages.Length && Focus?.PageIndex != current)
        {
            if (_orders[current] is not null)
            {
                SetFocusCore(FirstNode(current), scroll: false, follow: true);
            }
            else if (Session.IsCooperative)
            {
                // One thread: extracting the page's layout now would stall the scroll (#357). The focus follows once the shell loads
                // the page (LoadPage or LoadPageAsync), which it does when the view settles.
                _followPending = current;
            }
            else
            {
                // The page's layout is extracted on a worker first (#357): a scroll never waits for it on the UI thread.
                _ = FollowAsync(current);
            }
        }
    }

    private async Task FollowAsync(int pageIndex)
    {
        try
        {
            await LoadPageAsync(pageIndex).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // the focus simply stays where it is; the page's own load reports its failure
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        if (_currentPage == pageIndex && Focus?.PageIndex != pageIndex)
        {
            SetFocusCore(FirstNode(pageIndex), scroll: false, follow: true);
        }
    }

    private void SetFocusCore(AccessibleNode? node, bool scroll, bool follow = false)
    {
        if (ReferenceEquals(node, Focus))
        {
            return;
        }

        Focus = node;
        Observer?.Invoke(new AccessibilityChange(AccessibilityChangeKind.FocusChanged, node) { FollowsViewer = follow });
        if (scroll && node is not null && Viewer is { } viewer)
        {
            UpdateCore(viewer.Layout, viewer.Viewport, follow: false);
            if (TryGetViewportShowing(node, out Viewport showing))
            {
                viewer.SetViewport(showing);
                UpdateCore(viewer.Layout, viewer.Viewport, follow: false);
            }
        }
    }

    private bool TryGetBounds(AccessibleNode node, in Viewport viewport, out LayoutRectangle bounds)
    {
        bounds = default;
        if (_layout is not { } layout || node.PageIndex < 0 || node.PageIndex >= layout.PageCount)
        {
            return false;
        }

        PdfRectangle? rectangle = node.Role == AccessibleRole.Page ? layout.GetPageMetrics(node.PageIndex).CropBox : node.PageBounds;
        if (rectangle is not { } r)
        {
            return false;
        }

        bounds = layout.PageToViewport(viewport, node.PageIndex, r);
        return true;
    }

    private bool IsShown(AccessibleNode node, in Viewport viewport, in LayoutRectangle bounds)
    {
        PageLayout layout = _layout!;
        if (layout.Mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp
            && layout.GetSpread(node.PageIndex) != layout.GetSpread(Math.Max(0, layout.GetCurrentPage(viewport))))
        {
            return false;
        }

        return bounds.Right > 0 && bounds.X < viewport.Width && bounds.Bottom > 0 && bounds.Y < viewport.Height;
    }

    private void Raise(AccessibilityChangeKind kind, AccessibleNode? node) => Observer?.Invoke(new AccessibilityChange(kind, node));

    private AccessibleNode FirstNode(int pageIndex)
    {
        AccessibleNode[] order = LoadOrder(pageIndex);
        return order.Length > 0 ? order[0] : _pages[pageIndex];
    }

    private int CheckPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pages.Length);
        return pageIndex;
    }

    private AccessibleNode[] LoadOrder(int pageIndex, LayoutTree? extracted = null)
    {
        if (_orders[pageIndex] is { } loaded)
        {
            return loaded;
        }

        PdfPage page = Session.Document.Pages[pageIndex];
        AccessibleNode pageNode = _pages[pageIndex];
        LayoutTree tree = extracted ?? page.ExtractLayout();
        var order = new List<AccessibleNode>();
        var map = new Dictionary<LayoutNode, AccessibleNode>(ReferenceEqualityComparer.Instance);
        var children = new List<AccessibleNode>(tree.Root.Children.Count);
        foreach (LayoutNode child in tree.Root.Children)
        {
            children.Add(Build(child, pageNode, order, map));
        }

        foreach (AccessibleNode node in order)
        {
            if (node.LayoutNode is LayoutTableCell { Headers.Count: > 0 } cell)
            {
                var headers = new List<AccessibleNode>(cell.Headers.Count);
                foreach (LayoutTableCell header in cell.Headers)
                {
                    if (map.TryGetValue(header, out AccessibleNode? found))
                    {
                        headers.Add(found);
                    }
                }

                node.HeaderArray = [.. headers];
            }
        }

        if (Elements is { } provider)
        {
            _elements.Clear();
            provider.GetElements(page, pageIndex, _elements);
            foreach (AccessibleElement element in _elements)
            {
                var node = new AccessibleNode(this, element.Role, pageIndex, pageNode)
                {
                    Value = element.Value,
                    PageBounds = element.Bounds,
                    Source = element.Source,
                    StaticStates = element.States | AccessibleStates.Focusable,
                    IsElement = true,
                    Language = pageNode.Language,
                };
                node.SetElementName(element.Name);
                children.Add(node);
                order.Add(node);
            }

            _elements.Clear();
        }

        AccessibleNode[] array = [.. order];
        for (int i = 0; i < array.Length; i++)
        {
            array[i].Index = i;
        }

        pageNode.ChildArray = [.. children];
        _orders[pageIndex] = array;
        Raise(AccessibilityChangeKind.ChildrenChanged, pageNode);
        return array;
    }

    private AccessibleNode Build(LayoutNode layout, AccessibleNode parent, List<AccessibleNode> order, Dictionary<LayoutNode, AccessibleNode> map)
    {
        PdfRectangle? bounds = null;
        foreach (LayoutExtent extent in layout.Extents)
        {
            if (extent.Bounds is { } b)
            {
                bounds = bounds is { } u
                    ? new PdfRectangle(Math.Min(u.Left, b.Left), Math.Min(u.Bottom, b.Bottom), Math.Max(u.Right, b.Right), Math.Max(u.Top, b.Top))
                    : b;
            }
        }

        var cell = layout as LayoutTableCell;
        var table = layout as LayoutTable;
        var node = new AccessibleNode(this, RoleOf(layout, parent), parent.PageIndex, parent)
        {
            LayoutNode = layout,
            PageBounds = bounds,
            Language = layout.Language ?? parent.Language,
            Level = layout is LayoutHeading heading ? heading.Level : 0,
            RowCount = table?.RowCount ?? 0,
            ColumnCount = table?.ColumnCount ?? 0,
            Row = cell?.Row ?? -1,
            Column = cell?.Column ?? -1,
            RowSpan = cell?.RowSpan ?? 0,
            ColumnSpan = cell?.ColumnSpan ?? 0,
            StaticStates = AccessibleStates.Focusable,
        };
        order.Add(node);
        map[layout] = node;
        if (layout.Children.Count > 0)
        {
            var children = new AccessibleNode[layout.Children.Count];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = Build(layout.Children[i], node, order, map);
            }

            node.ChildArray = children;
        }

        return node;
    }

    private static AccessibleRole RoleOf(LayoutNode layout, AccessibleNode parent) => layout.Kind switch
    {
        LayoutNodeKind.Document or LayoutNodeKind.Section => AccessibleRole.Section,
        LayoutNodeKind.Paragraph => AccessibleRole.Paragraph,
        LayoutNodeKind.Heading => AccessibleRole.Heading,
        LayoutNodeKind.List => AccessibleRole.List,
        LayoutNodeKind.ListItem => AccessibleRole.ListItem,
        LayoutNodeKind.Label => parent.Role == AccessibleRole.ListItem ? AccessibleRole.ListMarker : AccessibleRole.Text,
        LayoutNodeKind.Table => AccessibleRole.Table,
        LayoutNodeKind.TableRow => AccessibleRole.Row,
        LayoutNodeKind.TableCell when layout is LayoutTableCell { IsHeader: true } header =>
            header.Column == 0 && header.Row > 0 ? AccessibleRole.RowHeader : AccessibleRole.ColumnHeader,
        LayoutNodeKind.TableCell => AccessibleRole.Cell,
        LayoutNodeKind.Figure => AccessibleRole.Figure,
        LayoutNodeKind.Caption => AccessibleRole.Caption,
        LayoutNodeKind.Link => AccessibleRole.Link,
        LayoutNodeKind.Note => AccessibleRole.Note,
        LayoutNodeKind.ListBody => AccessibleRole.Group,
        _ => AccessibleRole.Text,
    };
}
