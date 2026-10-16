namespace Broadside.Viewer;

/// <summary>An axis-aligned rectangle with y down: in layout points (<see cref="PageLayout.GetPageBounds"/>) or in viewport pixels.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <remarks>Spec #33 story 54 (viewer capability 14).</remarks>
public readonly record struct LayoutRectangle(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Returns whether the point lies inside the rectangle or on its edge.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}
