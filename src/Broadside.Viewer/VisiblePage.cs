using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>A page that intersects a <see cref="Viewport"/>, and which of its pixels show.</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Region">
/// The visible pixels, in the coordinates of <see cref="Tile.Region"/>: a render of the whole page at the viewport's zoom and the
/// layout's rotation, origin at its top-left corner.
/// </param>
/// <param name="X">The viewport x of the page's pixel origin: a tile's pixel (u, v) shows at (X + u, Y + v).</param>
/// <param name="Y">The viewport y of the page's pixel origin.</param>
/// <remarks>ISO 32000-2 §14.11.2 (the crop box is the region displayed). Spec #33 story 54 (viewer capabilities 1 and 14).</remarks>
public readonly record struct VisiblePage(int PageIndex, PixelRectangle Region, double X, double Y);
