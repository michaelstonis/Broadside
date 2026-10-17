namespace Broadside.Viewer;

/// <summary>What changed in an <see cref="AccessibilityChange"/>.</summary>
/// <remarks>Spec #33 viewer capability 8.</remarks>
public enum AccessibilityChangeKind
{
    /// <summary>The accessible focus moved to <see cref="AccessibilityChange.Node"/> (null when it was cleared).</summary>
    FocusChanged,

    /// <summary>The children of <see cref="AccessibilityChange.Node"/> (a page) were built or rebuilt.</summary>
    ChildrenChanged,

    /// <summary>The viewport or layout changed, so every node's viewport bounds and offscreen state may have changed.</summary>
    BoundsChanged,
}

/// <summary>A change an <see cref="AccessibilityBridge"/> reports through <see cref="AccessibilityBridge.Observer"/>, for the shell to raise on its automation peers.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="Node">The node concerned: the newly focused node, the rebuilt page, or null for a bounds change.</param>
/// <remarks>Spec #33 viewer capability 8. A struct, so raising it allocates nothing.</remarks>
public readonly record struct AccessibilityChange(AccessibilityChangeKind Kind, AccessibleNode? Node)
{
    /// <summary>
    /// Gets whether a <see cref="AccessibilityChangeKind.FocusChanged"/> only follows the viewer to a new current page (scrolling), rather
    /// than answering a navigation request. A shell moves the platform's keyboard focus for it only while that focus is already on an
    /// accessible node, so scrolling never pulls the focus (or a focus ring) onto the page content.
    /// </summary>
    public bool FollowsViewer { get; init; }
}
