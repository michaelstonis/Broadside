namespace Broadside.Viewer;

/// <summary>The unit of a wheel event's deltas, as the DOM's <c>WheelEvent.deltaMode</c> gives it.</summary>
/// <remarks>Spec #33 viewer capability 14.</remarks>
public enum WheelDeltaMode
{
    /// <summary>Device-independent pixels.</summary>
    Pixel,

    /// <summary>Lines: one line scrolls <see cref="InputModel.LineScroll"/> device-independent pixels.</summary>
    Line,

    /// <summary>Pages: one page scrolls nine tenths of the viewport.</summary>
    Page,
}
