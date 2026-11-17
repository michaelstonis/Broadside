using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Foundation;

namespace Broadside.Viewer.Uno;

/// <summary>
/// The <see cref="PdfView"/>'s automation peer: a document whose children are the loaded pages, each page's children its readable
/// nodes in reading order, mirrored from the <see cref="AccessibilityBridge"/> (headings with their level, lists, tables and cells,
/// figures with their alternate description, links, form fields with their values).
/// </summary>
/// <remarks>ISO 32000-2 §14.8.2.5 (reading order), §14.8.4 (standard structure types), §14.9.3 (alternate descriptions).</remarks>
public sealed partial class PdfViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly ViewerController _controller;
    private readonly Dictionary<AccessibleNode, AccessibleNodeAutomationPeer> _peers = new(ReferenceEqualityComparer.Instance);

    internal PdfViewAutomationPeer(PdfView owner, ViewerController controller)
        : base(owner)
    {
        _controller = controller;
    }

    /// <summary>Gets the text of a loaded page's nodes in reading order, as the peers read it.</summary>
    /// <param name="pageIndex">The page (zero-based); pages load as they become visible.</param>
    /// <returns>The nodes' spoken names in reading order, or an empty list when the page is not loaded.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.5.</remarks>
    public IReadOnlyList<string> GetPageText(int pageIndex) =>
        _controller.Bridge is { } bridge && bridge.IsPageLoaded(pageIndex) ? AccessibleText.ReadingOrder(bridge, pageIndex) : [];

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => nameof(PdfView);

    /// <inheritdoc/>
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return _controller.Bridge?.Root.Name is { Length: > 0 } title ? title : name;
    }

    /// <inheritdoc/>
    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        if (_controller.Bridge is not { } bridge)
        {
            return children;
        }

        foreach (int page in _controller.AccessiblePages)
        {
            children.Add(PeerFor(bridge.GetPage(page)));
        }

        return children;
    }

    internal AccessibleNodeAutomationPeer PeerFor(AccessibleNode node)
    {
        if (!_peers.TryGetValue(node, out AccessibleNodeAutomationPeer? peer))
        {
            peer = new AccessibleNodeAutomationPeer(this, node);
            _peers.Add(node, peer);
        }

        return peer;
    }

    internal AccessibilityBridge? Bridge => _controller.Bridge;

    internal PageLabelTable? PageLabels => _controller.PageLabels;

    /// <summary>Where a node is, in the view's coordinates turned into the window's.</summary>
    internal Rect BoundsOf(AccessibleNode node)
    {
        if (!_controller.TryGetNodeBounds(node, out LayoutRectangle bounds))
        {
            return default;
        }

        Rect view = GetBoundingRectangle();
        return new Rect(view.X + bounds.X, view.Y + bounds.Y, Math.Max(bounds.Width, 0), Math.Max(bounds.Height, 0));
    }

    internal void OnAccessibilityChanged(AccessibilityChange change)
    {
        switch (change.Kind)
        {
            case AccessibilityChangeKind.FocusChanged when change.Node is { } node && !change.FollowsViewer:
                PeerFor(node).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
                break;
            case AccessibilityChangeKind.ChildrenChanged:
                // Pages loaded or left, or a page's nodes were rebuilt after an edit: peers are made again as they are asked for.
                if (change.Node is null)
                {
                    _peers.Clear();
                }
                else
                {
                    foreach (AccessibleNode stale in _peers.Keys.Where(n => n.PageIndex == change.Node.PageIndex).ToList())
                    {
                        _peers.Remove(stale);
                    }
                }

                RaiseAutomationEvent(AutomationEvents.StructureChanged);
                break;
        }
    }
}

/// <summary>One node of the accessibility bridge's tree (a page, heading, paragraph, cell, link or form field) as an automation peer.</summary>
/// <remarks>ISO 32000-2 §14.8.4 (standard structure types), §14.9.3 (alternate descriptions), §12.7 (interactive forms).</remarks>
public sealed partial class AccessibleNodeAutomationPeer : AutomationPeer
{
    private readonly PdfViewAutomationPeer _owner;

    internal AccessibleNodeAutomationPeer(PdfViewAutomationPeer owner, AccessibleNode node)
    {
        _owner = owner;
        Node = node;
    }

    /// <summary>Gets the node this peer stands for.</summary>
    public AccessibleNode Node { get; }

    /// <inheritdoc/>
    protected override string GetNameCore() => _owner.Bridge is { } bridge ? AccessibleText.Describe(bridge, Node, _owner.PageLabels) : string.Empty;

    /// <inheritdoc/>
    protected override string GetClassNameCore() => Node.Role.ToString();

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => Node.Role switch
    {
        AccessibleRole.Page or AccessibleRole.Document => AutomationControlType.Document,
        AccessibleRole.Heading => AutomationControlType.Header,
        AccessibleRole.List => AutomationControlType.List,
        AccessibleRole.ListItem => AutomationControlType.ListItem,
        AccessibleRole.Table => AutomationControlType.Table,
        AccessibleRole.Row => AutomationControlType.DataItem,
        AccessibleRole.Cell => AutomationControlType.DataItem,
        AccessibleRole.ColumnHeader or AccessibleRole.RowHeader => AutomationControlType.HeaderItem,
        AccessibleRole.Figure => AutomationControlType.Image,
        AccessibleRole.Link => AutomationControlType.Hyperlink,
        AccessibleRole.Button => AutomationControlType.Button,
        AccessibleRole.CheckBox => AutomationControlType.CheckBox,
        AccessibleRole.RadioButton => AutomationControlType.RadioButton,
        AccessibleRole.TextBox => AutomationControlType.Edit,
        AccessibleRole.ComboBox => AutomationControlType.ComboBox,
        AccessibleRole.ListBox => AutomationControlType.List,
        AccessibleRole.Section or AccessibleRole.Group => AutomationControlType.Group,
        _ => AutomationControlType.Text,
    };

    /// <inheritdoc/>
    protected override AutomationHeadingLevel GetHeadingLevelCore() => Node.Role != AccessibleRole.Heading ? AutomationHeadingLevel.None : Math.Clamp(Node.Level, 1, 9) switch
    {
        1 => AutomationHeadingLevel.Level1,
        2 => AutomationHeadingLevel.Level2,
        3 => AutomationHeadingLevel.Level3,
        4 => AutomationHeadingLevel.Level4,
        5 => AutomationHeadingLevel.Level5,
        6 => AutomationHeadingLevel.Level6,
        7 => AutomationHeadingLevel.Level7,
        8 => AutomationHeadingLevel.Level8,
        _ => AutomationHeadingLevel.Level9,
    };

    /// <inheritdoc/>
    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>(Node.Children.Count);
        foreach (AccessibleNode child in Node.Children)
        {
            children.Add(_owner.PeerFor(child));
        }

        return children;
    }

    /// <inheritdoc/>
    protected override Rect GetBoundingRectangleCore() => _owner.BoundsOf(Node);

    /// <inheritdoc/>
    protected override bool IsOffscreenCore() => _owner.Bridge is not { } bridge || (bridge.GetStates(Node) & AccessibleStates.Offscreen) != 0;

    /// <inheritdoc/>
    protected override bool IsContentElementCore() => true;

    /// <inheritdoc/>
    protected override bool IsControlElementCore() => true;

    /// <inheritdoc/>
    protected override bool IsKeyboardFocusableCore() => _owner.Bridge is { } bridge && (bridge.GetStates(Node) & AccessibleStates.Focusable) != 0;

    /// <inheritdoc/>
    protected override bool HasKeyboardFocusCore() => _owner.Bridge is { } bridge && (bridge.GetStates(Node) & AccessibleStates.Focused) != 0;

    /// <inheritdoc/>
    protected override void SetFocusCore() => _owner.Bridge?.SetFocus(Node);

    /// <inheritdoc/>
    protected override string GetHelpTextCore() => Node.Value ?? string.Empty;

    /// <inheritdoc/>
    protected override string GetLocalizedControlTypeCore() => Node.Role switch
    {
        AccessibleRole.Page => "page",
        AccessibleRole.Annotation or AccessibleRole.Note => "comment",
        _ => base.GetLocalizedControlTypeCore() ?? string.Empty,
    };
}
