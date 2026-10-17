using System.Globalization;

namespace Broadside.Graphics;

/// <summary>
/// A quadrilateral given by its four corners, such as a glyph's box mapped from text space into page space: a rectangle in its own
/// space, which a rotation, skew or reflection turns into a general parallelogram.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.3.2 (coordinate spaces) and §9.4.4 (the text rendering matrix). The corners are named for the box they come from
/// (bottom on the baseline side, left at the origin side), not for where they land: in text rotated by 180 degrees the
/// <see cref="BottomLeft"/> corner is at the top right of the page.
/// </remarks>
/// <param name="BottomLeft">The corner at the box's origin side, on its bottom edge.</param>
/// <param name="BottomRight">The corner at the box's far side, on its bottom edge.</param>
/// <param name="TopRight">The corner at the box's far side, on its top edge.</param>
/// <param name="TopLeft">The corner at the box's origin side, on its top edge.</param>
public readonly record struct Quad(PathPoint BottomLeft, PathPoint BottomRight, PathPoint TopRight, PathPoint TopLeft)
{
    /// <summary>Gets the smallest axis-aligned rectangle that holds the four corners.</summary>
    /// <remarks>ISO 32000-2 §7.9.5 (rectangles).</remarks>
    public PdfRectangle Bounds => new(
        Math.Min(Math.Min(BottomLeft.X, BottomRight.X), Math.Min(TopRight.X, TopLeft.X)),
        Math.Min(Math.Min(BottomLeft.Y, BottomRight.Y), Math.Min(TopRight.Y, TopLeft.Y)),
        Math.Max(Math.Max(BottomLeft.X, BottomRight.X), Math.Max(TopRight.X, TopLeft.X)),
        Math.Max(Math.Max(BottomLeft.Y, BottomRight.Y), Math.Max(TopRight.Y, TopLeft.Y)));

    /// <summary>Maps the rectangle from <paramref name="left"/> to <paramref name="right"/> and <paramref name="bottom"/> to <paramref name="top"/> through <paramref name="matrix"/>.</summary>
    /// <param name="matrix">The matrix from the rectangle's space.</param>
    /// <param name="left">The rectangle's left edge.</param>
    /// <param name="bottom">The rectangle's bottom edge.</param>
    /// <param name="right">The rectangle's right edge.</param>
    /// <param name="top">The rectangle's top edge.</param>
    /// <returns>The mapped corners.</returns>
    /// <remarks>ISO 32000-2 §8.3.4 (transformation matrices).</remarks>
    public static Quad FromRectangle(Matrix matrix, double left, double bottom, double right, double top) => new(
        matrix.Transform(left, bottom),
        matrix.Transform(right, bottom),
        matrix.Transform(right, top),
        matrix.Transform(left, top));

    /// <summary>Returns the corners as <c>[bottom-left bottom-right top-right top-left]</c> in the invariant culture.</summary>
    /// <returns>The quad text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{BottomLeft} {BottomRight} {TopRight} {TopLeft}]");
}
