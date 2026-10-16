using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// One rectangle of pixels of one page at one zoom: what a <see cref="DocumentSession"/> renders and hands to an
/// <see cref="IRenderSurface"/>.
/// </summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Scale">Pixels per point (<see cref="RenderOptions.Scale"/>): 1 is 72 DPI.</param>
/// <param name="Region">
/// The pixels, in the coordinates of a render of the whole page at <paramref name="Scale"/> and <see cref="Rotation"/>: origin at the
/// top-left corner of the rotated crop box, y down (<see cref="RenderOptions.GetPixelRectangle"/>).
/// </param>
/// <remarks>
/// ISO 32000-2 §8.3.2.2 (device space) and §14.11.2 (the crop box is the region displayed). Spec #33 story 54 (viewer capability 1).
/// A tile's pixels equal the same pixels of a whole-page render (to one level at the last bit of the transform, see
/// <see cref="RenderOptions.Region"/>), so a mosaic of tiles shows no seams.
/// </remarks>
public readonly record struct Tile(int PageIndex, double Scale, PixelRectangle Region)
{
    /// <summary>Gets the rotation added to the page's own, in degrees clockwise, a multiple of 90 (<see cref="RenderOptions.Rotation"/>).</summary>
    public int Rotation { get; init; }

    /// <summary>
    /// Gets the layers drawn: content, annotations and form fields (all by default; ISO 32000-2 §12.5). A tile with the content layer
    /// is drawn on white paper; an overlay tile without it (annotations or form fields alone) is drawn on transparent pixels.
    /// </summary>
    public DisplayLayers Layers { get; init; } = DisplayLayers.All;
}
