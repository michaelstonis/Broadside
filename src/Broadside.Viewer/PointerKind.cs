namespace Broadside.Viewer;

/// <summary>What a pointer of the <see cref="InputModel"/> is: it decides whether a drag flings and whether a second pointer pinches.</summary>
/// <remarks>Spec #33 viewer capability 14. Matches the DOM's <c>PointerEvent.pointerType</c>.</remarks>
public enum PointerKind
{
    /// <summary>A mouse: a drag scrolls without inertia, a double-click zooms, and a second pointer is ignored.</summary>
    Mouse,

    /// <summary>A finger: a drag flings on release, two fingers pinch, a double-tap zooms.</summary>
    Touch,

    /// <summary>A pen or stylus: behaves as a finger.</summary>
    Pen,
}
