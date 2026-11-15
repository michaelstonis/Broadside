using System.Runtime.CompilerServices;
using Broadside.Graphics;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Foundation;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// The <see cref="PdfView"/>'s UI Automation peer: a document whose children are the loaded pages, each a tree of
/// <see cref="AccessibleNodeAutomationPeer"/>s mirroring the <see cref="AccessibilityBridge"/>'s nodes in reading order.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8 (tagged PDF), §14.9.3 (alternate descriptions). The tile images, paper and marks the view draws with are not
/// exposed: screen readers see the document's text, not its pixels. The open form-field editor is exposed after the pages.
/// </remarks>
internal sealed partial class PdfViewAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly PdfView _view;
    private readonly ConditionalWeakTable<AccessibleNode, AccessibleNodeAutomationPeer> _peers = [];

    public PdfViewAutomationPeer(PdfView owner)
        : base(owner)
    {
        _view = owner;
    }

    internal ViewerController Controller => _view.Controller;

    /// <summary>The peer of a node (one per node, so UI Automation sees a stable element).</summary>
    internal AccessibleNodeAutomationPeer PeerFor(AccessibleNode node) => _peers.GetValue(node, n => new AccessibleNodeAutomationPeer(this, n));

    /// <summary>The peers of the loaded pages, in page order.</summary>
    internal List<AutomationPeer> PagePeers()
    {
        var pages = new List<AutomationPeer>();
        if (Controller.Bridge is { } bridge)
        {
            int[] loaded = [.. Controller.AccessiblePages];
            Array.Sort(loaded);
            foreach (int page in loaded)
            {
                pages.Add(PeerFor(bridge.GetPage(page)));
            }
        }

        return pages;
    }

    /// <summary>A node's rectangle on the screen, in physical pixels (UI Automation's coordinates), from the view's own.</summary>
    internal Rect ScreenBounds(AccessibleNode node)
    {
        if (!Controller.TryGetNodeBounds(node, out LayoutRectangle bounds) || _view.ActualWidth <= 0)
        {
            return default;
        }

        Rect view = GetBoundingRectangle();
        double scale = view.Width / _view.ActualWidth;
        double left = Math.Max(view.X + (bounds.X * scale), view.X);
        double top = Math.Max(view.Y + (bounds.Y * scale), view.Y);
        double right = Math.Min(view.X + ((bounds.X + bounds.Width) * scale), view.X + view.Width);
        double bottom = Math.Min(view.Y + ((bounds.Y + bounds.Height) * scale), view.Y + view.Height);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : new Rect(view.X + (bounds.X * scale), view.Y + (bounds.Y * scale), 0, 0);
    }

    /// <summary>Raises what UI Automation clients need to hear about a change of the bridge.</summary>
    internal void OnAccessibilityChanged(AccessibilityChange change)
    {
        switch (change.Kind)
        {
            case AccessibilityChangeKind.ChildrenChanged when ListenerExists(AutomationEvents.StructureChanged):
                RaiseStructureChangedEvent(AutomationStructureChangeType.ChildrenInvalidated, this);
                break;

            // Focus that only follows scrolling (page changes) does not pull the screen reader away from what it is reading.
            case AccessibilityChangeKind.FocusChanged when change.Node is { } node && !change.FollowsViewer && ListenerExists(AutomationEvents.AutomationFocusChanged):
                PeerFor(node).RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
                break;
        }
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(PdfView);

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return Controller.Bridge?.Root.Name is { Length: > 0 } title ? title : name;
    }

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        List<AutomationPeer> children = PagePeers();
        if (_view.EditorView is { } editor && CreatePeerForElement(editor) is { } peer)
        {
            children.Add(peer);
        }

        return children;
    }

    protected override object GetFocusedElementCore() =>
        Controller.Bridge?.Focus is { } focus ? PeerFor(focus) : base.GetFocusedElementCore();

    protected override object? GetElementFromPointCore(Point pointInWindowCoordinates)
    {
        // Explore by touch: the deepest node under the point, from the view's own coordinates.
        if (Controller.Bridge is { } bridge && _view.XamlRoot is not null)
        {
            Point local = _view.TransformToVisual(null).Inverse.TransformPoint(pointInWindowCoordinates);
            if (Controller.TryGetPagePoint(local.X, local.Y, out PagePoint point) && bridge.IsPageLoaded(point.PageIndex))
            {
                return PeerFor(bridge.GetNodeAt(point));
            }
        }

        return base.GetElementFromPointCore(pointInWindowCoordinates);
    }
}
