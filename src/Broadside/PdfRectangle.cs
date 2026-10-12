using System.Globalization;

namespace Broadside;

/// <summary>A rectangle in default user space, such as a page boundary.</summary>
/// <remarks>
/// ISO 32000-2 §7.9.5. A file writes a rectangle as two diagonally opposite corners in any order; this type always holds them
/// normalized, so <see cref="Left"/> is at most <see cref="Right"/> and <see cref="Bottom"/> at most <see cref="Top"/>.
/// </remarks>
public readonly struct PdfRectangle : IEquatable<PdfRectangle>
{
    /// <summary>Initializes a new instance of the <see cref="PdfRectangle"/> struct from two diagonally opposite corners, in any order.</summary>
    /// <param name="x1">The x coordinate of one corner.</param>
    /// <param name="y1">The y coordinate of the same corner.</param>
    /// <param name="x2">The x coordinate of the opposite corner.</param>
    /// <param name="y2">The y coordinate of the opposite corner.</param>
    public PdfRectangle(double x1, double y1, double x2, double y2)
    {
        Left = Math.Min(x1, x2);
        Right = Math.Max(x1, x2);
        Bottom = Math.Min(y1, y2);
        Top = Math.Max(y1, y2);
    }

    /// <summary>Gets the smaller x coordinate.</summary>
    public double Left { get; }

    /// <summary>Gets the smaller y coordinate.</summary>
    public double Bottom { get; }

    /// <summary>Gets the larger x coordinate.</summary>
    public double Right { get; }

    /// <summary>Gets the larger y coordinate.</summary>
    public double Top { get; }

    /// <summary>Gets the width, never negative.</summary>
    public double Width => Right - Left;

    /// <summary>Gets the height, never negative.</summary>
    public double Height => Top - Bottom;

    /// <summary>Returns whether two rectangles have the same coordinates.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfRectangle left, PdfRectangle right) => left.Equals(right);

    /// <summary>Returns whether two rectangles differ.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfRectangle left, PdfRectangle right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfRectangle other) =>
        Left.Equals(other.Left) && Bottom.Equals(other.Bottom) && Right.Equals(other.Right) && Top.Equals(other.Top);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfRectangle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Left, Bottom, Right, Top);

    /// <summary>Returns the rectangle as a PDF array would write it, such as <c>[0 0 612 792]</c>.</summary>
    /// <returns>The rectangle text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Left} {Bottom} {Right} {Top}]");

    /// <summary>Returns the overlap of two rectangles, or <see langword="null"/> when they do not overlap.</summary>
    internal PdfRectangle? Intersect(PdfRectangle other)
    {
        double left = Math.Max(Left, other.Left);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Max(Bottom, other.Bottom);
        double top = Math.Min(Top, other.Top);
        return left <= right && bottom <= top ? new PdfRectangle(left, bottom, right, top) : null;
    }
}
