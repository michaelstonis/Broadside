using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Broadside.Viewer.Wpf;

/// <summary>
/// UI Automation for a <see cref="PdfView"/>: a document whose children are the loaded pages (each named "Page n", its text in reading
/// order as its value) and, under each page, the <see cref="AccessibilityBridge"/>'s nodes in reading order, with the control types,
/// bounds and states screen readers and UI tests read. The document scrolls through the scroll pattern.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8 (tagged PDF: logical structure order is the reading order), §14.9.3 (alternate descriptions). Pages load as
/// they are shown (the bridge is virtualized per page); the tree raises a structure change when one arrives and follows the
/// bridge's focus.
/// </remarks>
internal sealed class PdfViewAutomationPeer : FrameworkElementAutomationPeer, IScrollProvider
{
    private readonly Dictionary<int, NodePeer> _pages = [];
    private readonly ConditionalWeakTable<AccessibleNode, NodePeer> _nodes = [];

    public PdfViewAutomationPeer(PdfView owner)
        : base(owner)
    {
    }

    internal PdfView View => (PdfView)Owner;

    internal ViewerController Controller => View.Controller;

    // ---- The document ----

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(PdfView);

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return name.Length > 0 ? name : "Document";
    }

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Scroll && Controller.Input is not null ? this : base.GetPattern(patternInterface);

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        // The field editor (a text box or combo box over the focused widget) first, then the loaded pages in page order.
        List<AutomationPeer> children = base.GetChildrenCore() ?? [];
        if (Controller.Bridge is { } bridge)
        {
            foreach (int page in Controller.AccessiblePages)
            {
                children.Add(PeerFor(bridge.GetPage(page)));
            }
        }

        return children.Count == 0 ? null : children;
    }

    /// <summary>Follows the bridge: rebuilt pages lose their peers, focus moves raise the focus event on the node's peer.</summary>
    internal void OnAccessibilityChanged(AccessibilityChange change)
    {
        if (change.Kind == AccessibilityChangeKind.FocusChanged)
        {
            // Focus that only follows scrolling does not pull a screen reader away from what it is reading.
            if (!change.FollowsViewer && change.Node is { } node && Controller.Bridge is not null
                && Controller.AccessiblePages.Contains(node.PageIndex) && ListenerExists(AutomationEvents.AutomationFocusChanged))
            {
                PeerFor(node).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
            }

            return;
        }

        // Pages that left (or were rebuilt after an edit) lose their peers; the rest keep theirs, so a client's references stay valid.
        var keep = new HashSet<int>(Controller.AccessiblePages);
        foreach (int page in _pages.Keys.ToList())
        {
            if (!keep.Contains(page) || (change.Node is { Role: AccessibleRole.Page } rebuilt && rebuilt.PageIndex == page)
                || !ReferenceEquals(_pages[page].Node, Controller.Bridge?.GetPage(page)))
            {
                _pages.Remove(page);
            }
        }

        ResetChildrenCache();
        if (ListenerExists(AutomationEvents.StructureChanged))
        {
            RaiseAutomationEvent(AutomationEvents.StructureChanged);
        }
    }

    internal NodePeer PeerFor(AccessibleNode node)
    {
        if (node.Role == AccessibleRole.Page)
        {
            if (!_pages.TryGetValue(node.PageIndex, out NodePeer? page) || !ReferenceEquals(page.Node, node))
            {
                page = new NodePeer(this, node);
                _pages[node.PageIndex] = page;
            }

            return page;
        }

        return _nodes.GetValue(node, key => new NodePeer(this, key));
    }

    /// <summary>A rectangle in DIPs from the view's top-left, in screen pixels (what UI Automation reports), or empty when the view is not on screen.</summary>
    internal Rect ToScreen(in LayoutRectangle bounds)
    {
        if (PresentationSource.FromVisual(View) is null)
        {
            return Rect.Empty;
        }

        Point a = View.PointToScreen(new Point(bounds.X, bounds.Y));
        Point b = View.PointToScreen(new Point(bounds.Right, bounds.Bottom));
        return new Rect(a, b);
    }

    // ---- Scroll pattern ----

    public double HorizontalScrollPercent => Percent(Controller.ScrollPosition().Horizontal);

    public double VerticalScrollPercent => Percent(Controller.ScrollPosition().Vertical);

    public double HorizontalViewSize => Controller.ScrollPosition().HorizontalView * 100;

    public double VerticalViewSize => Controller.ScrollPosition().VerticalView * 100;

    public bool HorizontallyScrollable => Controller.ScrollPosition().Horizontal >= 0;

    public bool VerticallyScrollable => Controller.ScrollPosition().Vertical >= 0;

    public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
    {
        (double dx, WheelDeltaMode xMode) = Step(horizontalAmount);
        (double dy, WheelDeltaMode yMode) = Step(verticalAmount);
        if (dx != 0)
        {
            Controller.Wheel(dx, 0, xMode, InputModifiers.None, Controller.Width / 2, Controller.Height / 2);
        }

        if (dy != 0)
        {
            Controller.Wheel(0, dy, yMode, InputModifiers.None, Controller.Width / 2, Controller.Height / 2);
        }
    }

    public void SetScrollPercent(double horizontalPercent, double verticalPercent) =>
        Controller.ScrollToFraction(
            horizontalPercent == ScrollPatternIdentifiers.NoScroll ? -1 : horizontalPercent / 100,
            verticalPercent == ScrollPatternIdentifiers.NoScroll ? -1 : verticalPercent / 100);

    private static double Percent(double fraction) => fraction < 0 ? ScrollPatternIdentifiers.NoScroll : fraction * 100;

    private static (double Delta, WheelDeltaMode Mode) Step(ScrollAmount amount) => amount switch
    {
        ScrollAmount.LargeDecrement => (-1, WheelDeltaMode.Page),
        ScrollAmount.LargeIncrement => (1, WheelDeltaMode.Page),
        ScrollAmount.SmallDecrement => (-1, WheelDeltaMode.Line),
        ScrollAmount.SmallIncrement => (1, WheelDeltaMode.Line),
        _ => (0, WheelDeltaMode.Pixel),
    };

    /// <summary>
    /// One node of the bridge's tree: a page (its value is its text in reading order) or a node within one (its name is its
    /// alternate description or text). Children come in the bridge's order, which is reading order.
    /// </summary>
    internal sealed class NodePeer : AutomationPeer, IValueProvider, IScrollItemProvider
    {
        private readonly PdfViewAutomationPeer _view;

        public NodePeer(PdfViewAutomationPeer view, AccessibleNode node)
        {
            _view = view;
            Node = node;
        }

        public AccessibleNode Node { get; }

        private AccessibilityBridge? Bridge => _view.Controller.Bridge;

        public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
        {
            PatternInterface.Value when Node.Role is AccessibleRole.Page or AccessibleRole.TextBox or AccessibleRole.ComboBox => this,
            PatternInterface.ScrollItem => this,
            _ => null,
        };

        protected override List<AutomationPeer>? GetChildrenCore()
        {
            if (Node.Children.Count == 0)
            {
                return null;
            }

            var children = new List<AutomationPeer>(Node.Children.Count);
            foreach (AccessibleNode child in Node.Children)
            {
                children.Add(_view.PeerFor(child));
            }

            return children;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => Node.Role switch
        {
            AccessibleRole.Document => AutomationControlType.Document,
            AccessibleRole.Paragraph or AccessibleRole.Heading or AccessibleRole.Text or AccessibleRole.ListMarker
                or AccessibleRole.Caption or AccessibleRole.Cell or AccessibleRole.Note or AccessibleRole.Annotation => AutomationControlType.Text,
            AccessibleRole.List or AccessibleRole.ListBox => AutomationControlType.List,
            AccessibleRole.ListItem => AutomationControlType.ListItem,
            AccessibleRole.Table => AutomationControlType.Table,
            AccessibleRole.Row => AutomationControlType.DataItem,
            AccessibleRole.ColumnHeader or AccessibleRole.RowHeader => AutomationControlType.HeaderItem,
            AccessibleRole.Figure => AutomationControlType.Image,
            AccessibleRole.Link => AutomationControlType.Hyperlink,
            AccessibleRole.Button => AutomationControlType.Button,
            AccessibleRole.CheckBox => AutomationControlType.CheckBox,
            AccessibleRole.RadioButton => AutomationControlType.RadioButton,
            AccessibleRole.TextBox => AutomationControlType.Edit,
            AccessibleRole.ComboBox => AutomationControlType.ComboBox,
            _ => AutomationControlType.Group,
        };

        protected override string GetLocalizedControlTypeCore() => Node.Role switch
        {
            AccessibleRole.Page => "page",
            AccessibleRole.Section => "section",
            AccessibleRole.Heading => Node.Level > 0 ? string.Create(CultureInfo.InvariantCulture, $"heading level {Node.Level}") : "heading",
            AccessibleRole.Paragraph => "paragraph",
            AccessibleRole.Note or AccessibleRole.Annotation => "comment",
            AccessibleRole.Cell => "cell",
            _ => base.GetLocalizedControlTypeCore(),
        };

        protected override string GetNameCore() => ReadingOrder.NameOf(Node);

        protected override string GetAutomationIdCore() => Node.Role == AccessibleRole.Page
            ? string.Create(CultureInfo.InvariantCulture, $"Page{Node.PageIndex + 1}")
            : string.Create(CultureInfo.InvariantCulture, $"Page{Node.PageIndex + 1}.{Node.Index}");

        protected override string GetClassNameCore() => Node.Role.ToString();

        protected override string GetItemStatusCore() => Bridge is { } bridge ? ReadingOrder.StatusOf(bridge.GetStates(Node)) : string.Empty;

        protected override string GetItemTypeCore() => string.Empty;

        protected override string GetHelpTextCore() => string.Empty;

        protected override string GetAcceleratorKeyCore() => string.Empty;

        protected override string GetAccessKeyCore() => string.Empty;

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

        protected override Rect GetBoundingRectangleCore() =>
            _view.Controller.TryGetNodeBounds(Node, out LayoutRectangle bounds) ? _view.ToScreen(bounds) : Rect.Empty;

        protected override Point GetClickablePointCore()
        {
            Rect bounds = GetBoundingRectangleCore();
            return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        }

        protected override bool IsOffscreenCore()
        {
            if (!_view.Controller.TryGetNodeBounds(Node, out LayoutRectangle bounds))
            {
                return true;
            }

            return bounds.Right <= 0 || bounds.Bottom <= 0 || bounds.X >= _view.Controller.Width || bounds.Y >= _view.Controller.Height;
        }

        protected override bool HasKeyboardFocusCore() => _view.View.IsKeyboardFocused && ReferenceEquals(Bridge?.Focus, Node);

        protected override bool IsKeyboardFocusableCore() => Node.Role != AccessibleRole.Page;

        protected override bool IsEnabledCore() => Bridge is not { } bridge || (bridge.GetStates(Node) & AccessibleStates.Disabled) == 0;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsPasswordCore() => false;

        protected override bool IsRequiredForFormCore() => Bridge is { } bridge && (bridge.GetStates(Node) & AccessibleStates.Required) != 0;

        protected override void SetFocusCore()
        {
            _view.View.Focus();
            Bridge?.SetFocus(Node);
        }

        // ---- Value pattern: a page's text in reading order, a field's value ----

        public string Value => Node.Role == AccessibleRole.Page ? ReadingOrder.TextOf(Node) : Node.Value ?? string.Empty;

        public bool IsReadOnly => true;

        public void SetValue(string value) => throw new ElementNotEnabledException();

        // ---- Scroll item pattern ----

        public void ScrollIntoView()
        {
            if (Node.Role == AccessibleRole.Page)
            {
                _view.Controller.GoToPage(Node.PageIndex);
            }
            else
            {
                Bridge?.SetFocus(Node); // moving the bridge's focus scrolls the view to show the node
            }
        }
    }
}
