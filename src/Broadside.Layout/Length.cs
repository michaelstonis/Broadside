using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Broadside.Layout.Flow;

/// <summary>
/// A length on the page, stored in points (1/72 inch, the unit of PDF user space and of the flow engine). A <see cref="double"/> converts
/// implicitly as points; <see cref="Millimeters"/>, <see cref="Centimeters"/> and <see cref="Inches"/> make the others, and the opt-in
/// namespace <c>Broadside.Layout.Flow.Units</c> adds <c>20.Mm</c>, <c>2.5.Cm</c>, <c>1.In</c> and <c>12.Pt</c>.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.3.2.3: user space units default to 1/72 inch. A length is immutable and safe to share. Lengths add, subtract, scale by
/// a number and compare; <see cref="ToString()"/> prints the points with the unit, such as <c>56.6929pt</c>. Percentages are not lengths:
/// relative widths are <see cref="ColumnWidth"/> values.
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly record struct Length : IComparable<Length>
{
    private const double PointsPerInch = 72;
    private const double MillimetersPerInch = 25.4;

    private readonly double _points;

    private Length(double points)
    {
        _points = double.IsFinite(points) ? points : throw new ArgumentOutOfRangeException(nameof(points), points, "A length shall be finite.");
    }

    /// <summary>Gets the zero length.</summary>
    public static Length Zero => default;

    /// <summary>Returns a length of <paramref name="points"/> points.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="points"/> is not finite.</exception>
    public static Length Points(double points) => new(points);

    /// <summary>Returns a length of <paramref name="millimeters"/> millimeters.</summary>
    /// <param name="millimeters">The length in millimeters.</param>
    /// <returns>The length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="millimeters"/> is not finite.</exception>
    public static Length Millimeters(double millimeters) => new(millimeters * PointsPerInch / MillimetersPerInch);

    /// <summary>Returns a length of <paramref name="centimeters"/> centimeters.</summary>
    /// <param name="centimeters">The length in centimeters.</param>
    /// <returns>The length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="centimeters"/> is not finite.</exception>
    public static Length Centimeters(double centimeters) => new(centimeters * 10 * PointsPerInch / MillimetersPerInch);

    /// <summary>Returns a length of <paramref name="inches"/> inches.</summary>
    /// <param name="inches">The length in inches.</param>
    /// <returns>The length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="inches"/> is not finite.</exception>
    public static Length Inches(double inches) => new(inches * PointsPerInch);

    /// <summary>Returns the length in points.</summary>
    /// <returns>The points.</returns>
    public double ToPoints() => _points;

    /// <summary>Returns the length in millimeters.</summary>
    /// <returns>The millimeters.</returns>
    public double ToMillimeters() => _points * MillimetersPerInch / PointsPerInch;

    /// <summary>Returns the length in inches.</summary>
    /// <returns>The inches.</returns>
    public double ToInches() => _points / PointsPerInch;

    /// <summary>Returns a fixed column width of this length.</summary>
    /// <returns>The column width.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    public ColumnWidth ToColumnWidth() => ColumnWidth.Fixed(_points);

    /// <summary>Converts points to a length.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length.</returns>
    public static Length FromDouble(double points) => new(points);

    /// <summary>Converts points to a length.</summary>
    /// <param name="points">The length in points.</param>
    public static implicit operator Length(double points) => new(points);

    /// <summary>Converts a length to a fixed column width.</summary>
    /// <param name="length">The width.</param>
    public static implicit operator ColumnWidth(Length length) => length.ToColumnWidth();

    /// <summary>Adds two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>The sum.</returns>
    public static Length operator +(Length left, Length right) => new(left._points + right._points);

    /// <summary>Subtracts a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The length to subtract.</param>
    /// <returns>The difference.</returns>
    public static Length operator -(Length left, Length right) => new(left._points - right._points);

    /// <summary>Negates a length.</summary>
    /// <param name="value">The length.</param>
    /// <returns>The negated length.</returns>
    public static Length operator -(Length value) => new(-value._points);

    /// <summary>Scales a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The scaled length.</returns>
    public static Length operator *(Length left, double right) => new(left._points * right);

    /// <summary>Scales a length.</summary>
    /// <param name="left">The factor.</param>
    /// <param name="right">The length.</param>
    /// <returns>The scaled length.</returns>
    public static Length operator *(double left, Length right) => new(left * right._points);

    /// <summary>Divides a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided length.</returns>
    public static Length operator /(Length left, double right) => new(left._points / right);

    /// <summary>Compares two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>Whether the first is shorter.</returns>
    public static bool operator <(Length left, Length right) => left._points < right._points;

    /// <summary>Compares two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>Whether the first is longer.</returns>
    public static bool operator >(Length left, Length right) => left._points > right._points;

    /// <summary>Compares two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>Whether the first is not longer.</returns>
    public static bool operator <=(Length left, Length right) => left._points <= right._points;

    /// <summary>Compares two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>Whether the first is not shorter.</returns>
    public static bool operator >=(Length left, Length right) => left._points >= right._points;

    /// <summary>Adds two lengths.</summary>
    /// <param name="left">The first length.</param>
    /// <param name="right">The second length.</param>
    /// <returns>The sum.</returns>
    public static Length Add(Length left, Length right) => left + right;

    /// <summary>Subtracts a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The length to subtract.</param>
    /// <returns>The difference.</returns>
    public static Length Subtract(Length left, Length right) => left - right;

    /// <summary>Negates a length.</summary>
    /// <param name="value">The length.</param>
    /// <returns>The negated length.</returns>
    public static Length Negate(Length value) => -value;

    /// <summary>Scales a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The scaled length.</returns>
    public static Length Multiply(Length left, double right) => left * right;

    /// <summary>Divides a length.</summary>
    /// <param name="left">The length.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided length.</returns>
    public static Length Divide(Length left, double right) => left / right;

    /// <inheritdoc/>
    public int CompareTo(Length other) => _points.CompareTo(other._points);

    /// <summary>Prints the length in points with the unit, such as <c>12pt</c>, culture-invariant.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => _points.ToString("0.####", CultureInfo.InvariantCulture) + "pt";
}
