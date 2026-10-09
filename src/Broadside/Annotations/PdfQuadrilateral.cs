using System.Globalization;
using Broadside.Graphics;

namespace Broadside.Annotations;

/// <summary>A quadrilateral of a <c>QuadPoints</c> array: four points in default user space, as the file gives them.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.10, Table 182, and §12.5.6.5, Table 176. The specification orders the points counterclockwise with the text
/// running along the edge from <see cref="Point1"/> to <see cref="Point2"/>; many writers use another order (top-left, top-right,
/// bottom-left, bottom-right), which cannot be told apart from rotated text, so the points are never reordered.
/// </remarks>
public readonly struct PdfQuadrilateral : IEquatable<PdfQuadrilateral>
{
    /// <summary>Initializes a new instance of the <see cref="PdfQuadrilateral"/> struct.</summary>
    /// <param name="point1">The first point, (x1, y1).</param>
    /// <param name="point2">The second point, (x2, y2).</param>
    /// <param name="point3">The third point, (x3, y3).</param>
    /// <param name="point4">The fourth point, (x4, y4).</param>
    public PdfQuadrilateral(PathPoint point1, PathPoint point2, PathPoint point3, PathPoint point4)
    {
        Point1 = point1;
        Point2 = point2;
        Point3 = point3;
        Point4 = point4;
    }

    /// <summary>Gets the first point.</summary>
    public PathPoint Point1 { get; }

    /// <summary>Gets the second point.</summary>
    public PathPoint Point2 { get; }

    /// <summary>Gets the third point.</summary>
    public PathPoint Point3 { get; }

    /// <summary>Gets the fourth point.</summary>
    public PathPoint Point4 { get; }

    /// <summary>Gets the smallest upright rectangle containing the four points.</summary>
    public PdfRectangle Bounds => new(
        Math.Min(Math.Min(Point1.X, Point2.X), Math.Min(Point3.X, Point4.X)),
        Math.Min(Math.Min(Point1.Y, Point2.Y), Math.Min(Point3.Y, Point4.Y)),
        Math.Max(Math.Max(Point1.X, Point2.X), Math.Max(Point3.X, Point4.X)),
        Math.Max(Math.Max(Point1.Y, Point2.Y), Math.Max(Point3.Y, Point4.Y)));

    /// <summary>Returns whether two quadrilaterals have the same points in the same order.</summary>
    /// <param name="left">The first quadrilateral.</param>
    /// <param name="right">The second quadrilateral.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfQuadrilateral left, PdfQuadrilateral right) => left.Equals(right);

    /// <summary>Returns whether two quadrilaterals differ.</summary>
    /// <param name="left">The first quadrilateral.</param>
    /// <param name="right">The second quadrilateral.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfQuadrilateral left, PdfQuadrilateral right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfQuadrilateral other) =>
        Point1.Equals(other.Point1) && Point2.Equals(other.Point2) && Point3.Equals(other.Point3) && Point4.Equals(other.Point4);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfQuadrilateral other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Point1, Point2, Point3, Point4);

    /// <summary>Returns the eight coordinates as a <c>QuadPoints</c> array would write them.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Point1.X} {Point1.Y} {Point2.X} {Point2.Y} {Point3.X} {Point3.Y} {Point4.X} {Point4.Y}]");
}
