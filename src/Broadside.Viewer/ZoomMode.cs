namespace Broadside.Viewer;

/// <summary>A zoom a viewer computes from the viewport's size: see <see cref="PageLayout.GetZoom"/>.</summary>
/// <remarks>Spec #33 story 54 (viewer capability 14).</remarks>
public enum ZoomMode
{
    /// <summary>One point of the page is one point on the screen.</summary>
    ActualSize,

    /// <summary>The spread and the padding beside it fill the viewport across the strip.</summary>
    FitWidth,

    /// <summary>The whole spread and the padding around it fit in the viewport.</summary>
    FitPage,
}
