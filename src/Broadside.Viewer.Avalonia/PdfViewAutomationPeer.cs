using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// Exposes a <see cref="PdfView"/> to UI Automation, AT-SPI and the macOS accessibility API as a document whose children are
/// its pages, each holding the <see cref="AccessibilityBridge"/>'s nodes of that page in reading order: headings with their level,
/// paragraphs, lists, tables, figures with their alternate descriptions, links and form fields with their values.
/// </summary>
/// <remarks>
/// <para>
/// The tree is virtual: no control stands behind a node. A page's nodes exist once the viewer loaded the page (it loads the pages it
/// shows, and a page an assistive technology asks for); <see cref="AutomationPeer.ChildrenChanged"/> is raised when they arrive. Bounds
/// follow the viewport, so a screen reader's highlight sits on the text it reads, and focusing a node scrolls the viewer to it.
/// </para>
/// <para>ISO 32000-2 §14.8 (tagged PDF: logical structure, reading order and standard structure types), §14.9.3 (alternate descriptions).</para>
/// </remarks>
public sealed class PdfViewAutomationPeer : ControlAutomationPeer
{
    private readonly Dictionary<AccessibleNode, NodePeer> _peers = new(ReferenceEqualityComparer.Instance);
    private AccessibilityBridge? _bridge;

    /// <summary>Creates the peer of a viewer.</summary>
    /// <param name="owner">The viewer.</param>
    public PdfViewAutomationPeer(PdfView owner)
        : base(owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Viewer = owner;
    }

    /// <summary>Gets the viewer.</summary>
    public PdfView Viewer { get; }

    internal ViewerController Controller => Viewer.Controller;

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(PdfView);

    /// <inheritdoc/>
    protected override string? GetNameCore() =>
        base.GetNameCore() is { Length: > 0 } name ? name : Controller.Bridge?.Root.Name is { Length: > 0 } title ? title : "Document";

    /// <inheritdoc/>
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        AccessibilityBridge? bridge = Controller.Bridge;
        if (!ReferenceEquals(bridge, _bridge))
        {
            _peers.Clear();
            _bridge = bridge;
        }

        if (bridge is null)
        {
            return null;
        }

        IReadOnlyList<AccessibleNode> pages = bridge.Root.Children;
        var children = new AutomationPeer[pages.Count];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = GetPeer(pages[i], this);
        }

        return children;
    }

    /// <summary>Gets the peer of a node of the open document (created on first use).</summary>
    internal NodePeer GetPeer(AccessibleNode node, AutomationPeer parent)
    {
        if (!_peers.TryGetValue(node, out NodePeer? peer))
        {
            peer = new NodePeer(this, node, parent);
            _peers.Add(node, peer);
        }

        return peer;
    }

    /// <summary>The bridge's tree changed: a page's nodes arrived or were rebuilt, or the document changed (<paramref name="node"/> null).</summary>
    internal void OnChildrenChanged(AccessibleNode? node)
    {
        if (node is not null && _peers.TryGetValue(node, out NodePeer? peer))
        {
            peer.NotifyChildrenChanged();
        }
        else
        {
            InvalidateChildren();
        }
    }

    /// <summary>The bridge's focus moved to <paramref name="node"/>: the platform hears it as a focus change on its peer.</summary>
    internal void OnFocusChanged(AccessibleNode? node)
    {
        if (node is not null && _peers.TryGetValue(node, out NodePeer? peer))
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.BoundingRectangleProperty, null, peer.GetBoundingRectangle());
        }
    }

    /// <summary>The peer of one <see cref="AccessibleNode"/>: a page, or an element of a page's logical structure.</summary>
    /// <remarks>ISO 32000-2 §14.8.4 (standard structure types), §14.9.3 (alternate descriptions).</remarks>
    internal sealed class NodePeer : AutomationPeer, IValueProvider
    {
        private readonly PdfViewAutomationPeer _owner;
        private AutomationPeer _parent;

        public NodePeer(PdfViewAutomationPeer owner, AccessibleNode node, AutomationPeer parent)
        {
            _owner = owner;
            _parent = parent;
            Node = node;
        }

        public AccessibleNode Node { get; }

        public void NotifyChildrenChanged() => RaiseChildrenChangedEvent();

        string? IValueProvider.Value => Node.Value;

        bool IValueProvider.IsReadOnly => true;

        void IValueProvider.SetValue(string? value) => throw new InvalidOperationException("Form field values are set through the viewer's annotation layer.");

        private AccessibilityBridge? Bridge => _owner.Controller.Bridge is { } bridge && ReferenceEquals(bridge, _owner._bridge) ? bridge : null;

        private AccessibleStates States => Bridge?.GetStates(Node) ?? AccessibleStates.None;

        protected override void BringIntoViewCore() => _owner.Controller.BringIntoView(Node);

        protected override string? GetAcceleratorKeyCore() => null;

        protected override string? GetAccessKeyCore() => null;

        protected override AutomationControlType GetAutomationControlTypeCore() => Node.Role switch
        {
            AccessibleRole.Document => AutomationControlType.Document,
            AccessibleRole.Page or AccessibleRole.Section or AccessibleRole.Group or AccessibleRole.Note or AccessibleRole.Annotation => AutomationControlType.Group,
            AccessibleRole.Heading or AccessibleRole.Paragraph or AccessibleRole.Text or AccessibleRole.Caption or AccessibleRole.ListMarker => AutomationControlType.Text,
            AccessibleRole.List => AutomationControlType.List,
            AccessibleRole.ListItem => AutomationControlType.ListItem,
            AccessibleRole.Table => AutomationControlType.Table,
            AccessibleRole.Row => AutomationControlType.DataItem,
            AccessibleRole.ColumnHeader or AccessibleRole.RowHeader => AutomationControlType.HeaderItem,
            AccessibleRole.Cell => AutomationControlType.DataItem,
            AccessibleRole.Figure => AutomationControlType.Image,
            AccessibleRole.Link => AutomationControlType.Hyperlink,
            AccessibleRole.Button => AutomationControlType.Button,
            AccessibleRole.CheckBox => AutomationControlType.CheckBox,
            AccessibleRole.RadioButton => AutomationControlType.RadioButton,
            AccessibleRole.TextBox => AutomationControlType.Edit,
            AccessibleRole.ComboBox => AutomationControlType.ComboBox,
            AccessibleRole.ListBox => AutomationControlType.List,
            _ => AutomationControlType.Custom,
        };

        protected override string? GetAutomationIdCore() => null;

        protected override Rect GetBoundingRectangleCore()
        {
            // Relative to the visual root, as Avalonia's own peers report it.
            if (!_owner.Controller.TryGetNodeBounds(Node, out LayoutRectangle bounds) || TopLevel.GetTopLevel(_owner.Viewer) is not { } root)
            {
                return default;
            }

            Point? origin = _owner.Viewer.TranslatePoint(new Point(bounds.X, bounds.Y), root);
            return origin is { } o ? new Rect(o.X, o.Y, Math.Max(bounds.Width, 0), Math.Max(bounds.Height, 0)) : default;
        }

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
        {
            if (Bridge is null)
            {
                return [];
            }

            if (Node.Role == AccessibleRole.Page && !Bridge.IsPageLoaded(Node.PageIndex))
            {
                // Asked for a page the viewer has not loaded: load it; ChildrenChanged follows.
                _owner.Controller.RequestAccessibilityPage(Node.PageIndex);
                return [];
            }

            IReadOnlyList<AccessibleNode> nodes = Node.Children;
            var children = new AutomationPeer[nodes.Count];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = _owner.GetPeer(nodes[i], this);
            }

            return children;
        }

        protected override string GetClassNameCore() => "PdfStructureElement";

        protected override AutomationPeer? GetLabeledByCore() => null;

        // A page is named by its number; a node by its name (alternate description first, else the text of name-from-content roles),
        // and a leaf without one by its text, so a screen reader reads every word once and in reading order.
        protected override string? GetNameCore() => Node.Role == AccessibleRole.Page
            ? "Page " + (Node.PageIndex + 1).ToString(CultureInfo.InvariantCulture)
            : Node.Name is { Length: > 0 } name ? name : Node.Children.Count == 0 ? Node.Text : null;

        protected override string GetLocalizedControlTypeCore() => Node.Role switch
        {
            AccessibleRole.Heading => "heading",
            AccessibleRole.Paragraph => "paragraph",
            AccessibleRole.Page => "page",
            AccessibleRole.Section => "section",
            AccessibleRole.Annotation => "comment",
            AccessibleRole.Note => "note",
            _ => base.GetLocalizedControlTypeCore(),
        };

        protected override int GetHeadingLevelCore() => Node.Role == AccessibleRole.Heading ? Node.Level : 0;

        protected override AutomationLandmarkType? GetLandmarkTypeCore() =>
            Node.Role == AccessibleRole.Section ? AutomationLandmarkType.Region : null;

        protected override string? GetItemStatusCore() => (States & AccessibleStates.Checked) != 0 ? "checked" : null;

        protected override AutomationPeer? GetParentCore() => _parent;

        protected override bool HasKeyboardFocusCore() => Bridge?.Focus is { } focus && ReferenceEquals(focus, Node);

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsEnabledCore() => (States & AccessibleStates.Disabled) == 0;

        protected override bool IsKeyboardFocusableCore() => true;

        protected override bool IsOffscreenCore() => (States & AccessibleStates.Offscreen) != 0;

        protected override void SetFocusCore() => Bridge?.SetFocus(Node);

        protected override bool ShowContextMenuCore() => false;

        // Form fields and comments report their value; other nodes have none.
        protected override object? GetProviderCore(Type providerType) =>
            providerType == typeof(IValueProvider) ? (Node.Value is not null ? this : null) : base.GetProviderCore(providerType);

        protected override bool TrySetParent(AutomationPeer? parent)
        {
            if (parent is null)
            {
                return false;
            }

            _parent = parent;
            return true;
        }
    }
}
