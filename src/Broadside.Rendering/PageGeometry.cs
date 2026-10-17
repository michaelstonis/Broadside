using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Where a page (or a region of it) lands on a bitmap: the transform from default user space to pixels, and the bitmap's size.</summary>
/// <param name="Transform">Default user space to device pixels, origin top-left, y down.</param>
/// <param name="PixelWidth">The bitmap width.</param>
/// <param name="PixelHeight">The bitmap height.</param>
internal readonly record struct PageGeometry(Matrix Transform, int PixelWidth, int PixelHeight)
{
    /// <summary>Gets the column of the whole page's render where the bitmap's first column lies (non-zero for a region).</summary>
    public int OffsetX { get; init; }

    /// <summary>Gets the row of the whole page's render where the bitmap's first row lies (non-zero for a region).</summary>
    public int OffsetY { get; init; }
}
