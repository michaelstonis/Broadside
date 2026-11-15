using System.Globalization;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Foundation;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// The UI Automation peer of one node of the accessibility bridge (a page, a heading, a paragraph, a table cell, a link, a form
/// field): named by its alternate description or its text, typed by its role, placed over its text on the screen, with its
/// children in reading order.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8 (tagged PDF), §14.9.3 (alternate descriptions). It stands for no XAML element, so it navigates the tree itself
/// (<see cref="NavigateCore"/>): parent, siblings and children come from the bridge's nodes.
/// </remarks>
internal sealed partial class AccessibleNodeAutomationPeer : AutomationPeer
{
    private readonly PdfViewAutomationPeer _root;

    public AccessibleNodeAutomationPeer(PdfViewAutomationPeer root, AccessibleNode node)
    {
        _root = root;
        Node = node;
    }

    public AccessibleNode Node { get; }

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>(Node.Children.Count);
        foreach (AccessibleNode child in Node.Children)
        {
            children.Add(_root.PeerFor(child));
        }

        return children;
    }

    protected override object? NavigateCore(AutomationNavigationDirection direction)
    {
        switch (direction)
        {
            case AutomationNavigationDirection.Parent:
                return Node.Role == AccessibleRole.Page || Node.Parent is null ? _root : _root.PeerFor(Node.Parent);
            case AutomationNavigationDirection.FirstChild:
                return Node.Children.Count > 0 ? _root.PeerFor(Node.Children[0]) : null;
            case AutomationNavigationDirection.LastChild:
                return Node.Children.Count > 0 ? _root.PeerFor(Node.Children[^1]) : null;
            case AutomationNavigationDirection.NextSibling or AutomationNavigationDirection.PreviousSibling:
                bool next = direction == AutomationNavigationDirection.NextSibling;
                if (Node.Role == AccessibleRole.Page)
                {
                    // Pages are the document's children: only the loaded ones, in page order.
                    List<AutomationPeer> pages = _root.PagePeers();
                    int index = pages.IndexOf(this);
                    int sibling = next ? index + 1 : index - 1;
                    return index >= 0 && sibling >= 0 && sibling < pages.Count ? pages[sibling] : null;
                }

                if (Node.Parent is { } parent)
                {
                    IReadOnlyList<AccessibleNode> siblings = parent.Children;
                    for (int i = 0; i < siblings.Count; i++)
                    {
                        if (ReferenceEquals(siblings[i], Node))
                        {
                            int sibling = next ? i + 1 : i - 1;
                            return sibling >= 0 && sibling < siblings.Count ? _root.PeerFor(siblings[sibling]) : null;
                        }
                    }
                }

                return null;
            default:
                return null;
        }
    }

    protected override string GetNameCore() => AccessibleText.GetName(Node);

    protected override string GetAutomationIdCore() => Node.Role == AccessibleRole.Page
        ? string.Create(CultureInfo.InvariantCulture, $"page-{Node.PageIndex}")
        : string.Create(CultureInfo.InvariantCulture, $"node-{Node.PageIndex}-{Node.Index}");

    protected override string GetClassNameCore() => "AccessibleNode";

    protected override AutomationControlType GetAutomationControlTypeCore() => Node.Role switch
    {
        AccessibleRole.Document => AutomationControlType.Document,
        AccessibleRole.Heading or AccessibleRole.Paragraph or AccessibleRole.Text or AccessibleRole.Caption or AccessibleRole.ListMarker => AutomationControlType.Text,
        AccessibleRole.List or AccessibleRole.ListBox => AutomationControlType.List,
        AccessibleRole.ListItem => AutomationControlType.ListItem,
        AccessibleRole.Table => AutomationControlType.Table,
        AccessibleRole.ColumnHeader or AccessibleRole.RowHeader => AutomationControlType.HeaderItem,
        AccessibleRole.Cell => AutomationControlType.DataItem,
        AccessibleRole.Figure => AutomationControlType.Image,
        AccessibleRole.Link => AutomationControlType.Hyperlink,
        AccessibleRole.Button => AutomationControlType.Button,
        AccessibleRole.CheckBox => AutomationControlType.CheckBox,
        AccessibleRole.RadioButton => AutomationControlType.RadioButton,
        AccessibleRole.TextBox => AutomationControlType.Edit,
        AccessibleRole.ComboBox => AutomationControlType.ComboBox,
        _ => AutomationControlType.Group, // pages, sections, groups, rows, notes and annotations
    };

    protected override string GetLocalizedControlTypeCore() => Node.Role switch
    {
        AccessibleRole.Page => "page",
        AccessibleRole.Section => "section",
        AccessibleRole.Paragraph => "paragraph",
        AccessibleRole.Heading => "heading",
        AccessibleRole.Row => "row",
        AccessibleRole.Note or AccessibleRole.Annotation => "comment",
        _ => base.GetLocalizedControlTypeCore(),
    };

    protected override AutomationHeadingLevel GetHeadingLevelCore() => Node.Role != AccessibleRole.Heading
        ? AutomationHeadingLevel.None
        : Math.Clamp(Node.Level <= 0 ? 1 : Node.Level, 1, 9) switch
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

    protected override Rect GetBoundingRectangleCore() => _root.ScreenBounds(Node);

    protected override bool IsOffscreenCore()
    {
        Rect bounds = _root.ScreenBounds(Node);
        return bounds.Width <= 0 || bounds.Height <= 0;
    }

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => (States() & AccessibleStates.Disabled) == 0;

    protected override bool IsKeyboardFocusableCore() => (States() & AccessibleStates.Focusable) != 0;

    protected override bool HasKeyboardFocusCore() => ReferenceEquals(_root.Controller.Bridge?.Focus, Node);

    protected override void SetFocusCore() => _root.Controller.Bridge?.SetFocus(Node);

    protected override string GetHelpTextCore() => Node.Role is AccessibleRole.TextBox or AccessibleRole.ComboBox or AccessibleRole.ListBox ? Node.Value ?? string.Empty : string.Empty;

    protected override bool IsRequiredForFormCore() => (States() & AccessibleStates.Required) != 0;

    protected override string GetItemStatusCore() => (States() & AccessibleStates.Checked) != 0 ? "checked" : string.Empty;

    private AccessibleStates States() => _root.Controller.Bridge?.GetStates(Node) ?? AccessibleStates.None;
}
