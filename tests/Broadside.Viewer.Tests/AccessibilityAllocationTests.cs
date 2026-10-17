using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// What a shell runs on every key press or frame of accessible navigation allocates nothing once the pages are loaded (scrolling an
/// attached viewer costs what any scroll of its tile scheduler costs).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class AccessibilityAllocationTests
{
    [Fact]
    public async Task Navigating_following_the_viewer_and_reading_states_and_bounds_allocate_nothing()
    {
        await using AccessibilityBridgeTests.Viewer viewer = await AccessibilityBridgeTests.Viewer.OpenAsync("text-tagged-spanning.pdf");
        var bridge = new AccessibilityBridge(viewer.Session);
        await bridge.LoadPageAsync(0, TestContext.Current.CancellationToken);
        await bridge.LoadPageAsync(1, TestContext.Current.CancellationToken);
        int changes = 0;
        bridge.Observer = _ => changes++;
        PageLayout layout = viewer.Zoom.Layout;
        Viewport first = viewer.Zoom.Viewport;
        Viewport second = layout.ScrollTo(first, 1);
        int found = 0;

        Action navigate = () =>
        {
            bridge.SetFocus(null);
            while (bridge.MoveFocus(AccessibleNavigation.Element))
            {
                found += (int)bridge.GetStates(bridge.Focus!);
                found += bridge.TryGetBounds(bridge.Focus!, out LayoutRectangle bounds) ? (int)bounds.Width : 0;
            }

            while (bridge.MoveFocus(AccessibleNavigation.Element, backward: true))
            {
                found++;
            }

            found += bridge.MoveFocus(AccessibleNavigation.Figure) ? 1 : 0;
            found += bridge.MoveFocus(AccessibleNavigation.Page, backward: true) ? 1 : 0;
            bridge.Update(layout, second);
            bridge.Update(layout, first);
            found += bridge.GetNodeAt(new PagePoint(0, 80, 700)).Index;
            found += bridge.TryGetViewportShowing(bridge.GetPage(1).Children[0], out Viewport showing) ? (int)showing.Y : 0;
        };

        Assert.Equal(0, Allocations.Measure(navigate));
        Assert.True(found > 0);
        Assert.True(changes > 0);
    }
}
