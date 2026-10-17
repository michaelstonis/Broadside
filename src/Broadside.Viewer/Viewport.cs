namespace Broadside.Viewer;

/// <summary>
/// The part of a <see cref="PageLayout"/> a shell shows: a scroll position and a size in viewport pixels, and a zoom in pixels per point.
/// </summary>
/// <param name="X">The horizontal scroll offset in pixels: the content's pixel column at the viewport's left edge.</param>
/// <param name="Y">The vertical scroll offset in pixels: the content's pixel row at the viewport's top edge.</param>
/// <param name="Width">The viewport's width in pixels.</param>
/// <param name="Height">The viewport's height in pixels.</param>
/// <param name="Zoom">
/// Pixels per point (the <see cref="Tile.Scale"/> of the tiles it shows): 1 is 72 DPI. Use device pixels (multiply by the display's
/// pixel ratio) so the tiles are sharp.
/// </param>
/// <remarks>
/// Spec #33 story 54 (viewer capability 14). Content narrower or shorter than the viewport is centred along that axis, and the scroll
/// offset along it is then ignored.
/// </remarks>
public readonly record struct Viewport(double X, double Y, double Width, double Height, double Zoom);
