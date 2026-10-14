using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A tiling-pattern paint in a display list: the pattern, the space it is laid out in, and its cell, already built into a display
/// list of its own. A backend replicates the cell over the painted area at multiples of XStep and YStep. Valid only during the
/// backend call that hands it over (the <see cref="Cell"/> list itself is immutable and may be kept, like any display list).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.3. The cell is the pattern's content stream run as §8.7.3.1 steps a to d say (the graphics state at the
/// beginning of the stream that selected the pattern, alpha constants and blend mode at their defaults as §11.6.7 requires,
/// clipped to the pattern's BBox), recorded relative to pattern space: <see cref="Cell"/> replays like a page's list, with pattern
/// space as its default user space. An uncoloured pattern's cell (§8.7.3.3) is already painted in the colour given with the
/// pattern, so a backend treats both paint types alike.
/// </para>
/// <para>
/// The cell is built once per pattern per list (per page): every paint with the same pattern, selected in the same stream and,
/// for an uncoloured pattern, with the same colour, hands over the same <see cref="Cell"/> instance, so a backend can cache what
/// it makes of it (a tile bitmap, a native pattern) by reference.
/// </para>
/// <para>
/// Tiles are placed at (i × <see cref="PdfTilingPattern.XStep"/>, j × <see cref="PdfTilingPattern.YStep"/>) in pattern space for
/// every integer i and j, the cell's <see cref="PdfTilingPattern.BoundingBox"/> clipping each one, so a step smaller than the
/// bounding box overlaps neighbouring tiles. <see cref="PdfTilingPattern.TilingType"/> says how a backend may adjust the spacing
/// to the device's pixel grid (Table 74).
/// </para>
/// </remarks>
public readonly ref struct DisplayTilingPattern
{
    internal DisplayTilingPattern(PdfTilingPattern pattern, Matrix matrix, DisplayList cell)
    {
        Pattern = pattern;
        Matrix = matrix;
        Cell = cell;
    }

    /// <summary>Gets the pattern: its bounding box, steps, paint type and tiling type. Always valid.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1, Table 74.</remarks>
    public PdfTilingPattern Pattern { get; }

    /// <summary>
    /// Gets the transform from pattern space to the list's default user space: the pattern matrix followed by the space of the stream
    /// that selected the pattern. It does not depend on the transform <see cref="IRenderingBackend.SetTransform"/> set.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.2: never the CTM at the paint.</remarks>
    public Matrix Matrix { get; }

    /// <summary>Gets the cell: the display list of one tile at the origin of pattern space, clipped to the bounding box.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.1 (the cell is painted by the pattern's content stream) and §8.7.3.3 (an uncoloured cell's colour).</remarks>
    public DisplayList Cell { get; }
}
