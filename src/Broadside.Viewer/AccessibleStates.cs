namespace Broadside.Viewer;

/// <summary>The states of an <see cref="AccessibleNode"/>, as <see cref="AccessibilityBridge.GetStates"/> reports them.</summary>
/// <remarks>Spec #33 viewer capability 8.</remarks>
[Flags]
public enum AccessibleStates
{
    /// <summary>No state.</summary>
    None = 0,

    /// <summary>The node can take the accessible focus.</summary>
    Focusable = 1,

    /// <summary>The node has the accessible focus.</summary>
    Focused = 2,

    /// <summary>The node is outside the viewport, or the bridge has no viewport yet.</summary>
    Offscreen = 4,

    /// <summary>A check box or radio button that is on.</summary>
    Checked = 8,

    /// <summary>A control whose value cannot be changed.</summary>
    ReadOnly = 16,

    /// <summary>A control that must have a value.</summary>
    Required = 32,

    /// <summary>A control that cannot be used.</summary>
    Disabled = 64,
}
