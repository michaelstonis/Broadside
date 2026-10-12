using System.Globalization;

namespace Broadside.Graphics;

/// <summary>
/// A transformation matrix <c>[a b c d e f]</c>: the 3-by-3 matrix <c>[a b 0; c d 0; e f 1]</c> that maps a point of one
/// coordinate space to another, applied to row vectors.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.3.3 and §8.3.4. A point <c>(x, y)</c> maps to <c>(a·x + c·y + e, b·x + d·y + f)</c>. A transformation applied
/// after an existing one is premultiplied: <c>M′ = Mₜ × M</c>, which is how <c>cm</c> changes the current transformation matrix
/// (Table 56). Coordinates and elements are doubles so that large offsets with small scales keep their precision.
/// </para>
/// <para>
/// Any six numbers make a matrix, including singular ones (scaling by zero): they are legal operands and map everything to a line
/// or a point. <see cref="TryInvert"/> reports whether an inverse exists.
/// </para>
/// </remarks>
public readonly struct Matrix : IEquatable<Matrix>
{
    /// <summary>Initializes a new instance of the <see cref="Matrix"/> struct.</summary>
    /// <param name="a">The element <c>a</c>: x scale (with <paramref name="d"/>) or the cosine part of a rotation.</param>
    /// <param name="b">The element <c>b</c>.</param>
    /// <param name="c">The element <c>c</c>.</param>
    /// <param name="d">The element <c>d</c>.</param>
    /// <param name="e">The element <c>e</c>: the x translation.</param>
    /// <param name="f">The element <c>f</c>: the y translation.</param>
    public Matrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    /// <summary>Gets the identity matrix <c>[1 0 0 1 0 0]</c>.</summary>
    public static Matrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Gets the element <c>a</c>.</summary>
    public double A { get; }

    /// <summary>Gets the element <c>b</c>.</summary>
    public double B { get; }

    /// <summary>Gets the element <c>c</c>.</summary>
    public double C { get; }

    /// <summary>Gets the element <c>d</c>.</summary>
    public double D { get; }

    /// <summary>Gets the element <c>e</c>.</summary>
    public double E { get; }

    /// <summary>Gets the element <c>f</c>.</summary>
    public double F { get; }

    /// <summary>Gets a value indicating whether this is the identity matrix.</summary>
    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

    /// <summary>Gets the determinant <c>a·d − b·c</c>; zero for a matrix that has no inverse.</summary>
    public double Determinant => (A * D) - (B * C);

    /// <summary>Returns the composite transformation: first <paramref name="first"/>, then <paramref name="second"/>.</summary>
    /// <param name="first">The transformation applied first.</param>
    /// <param name="second">The transformation applied second.</param>
    /// <returns>The product <c>first × second</c>.</returns>
    /// <remarks>ISO 32000-2 §8.3.4: <c>cm</c> computes <c>Multiply(operand, CTM)</c>.</remarks>
    public static Matrix operator *(Matrix first, Matrix second) => Multiply(first, second);

    /// <summary>Returns whether two matrices have the same elements.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(Matrix left, Matrix right) => left.Equals(right);

    /// <summary>Returns whether two matrices differ.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(Matrix left, Matrix right) => !left.Equals(right);

    /// <summary>Returns the composite transformation: first <paramref name="first"/>, then <paramref name="second"/>.</summary>
    /// <param name="first">The transformation applied first.</param>
    /// <param name="second">The transformation applied second.</param>
    /// <returns>The product <c>first × second</c>.</returns>
    /// <remarks>ISO 32000-2 §8.3.4.</remarks>
    public static Matrix Multiply(Matrix first, Matrix second) => new(
        (first.A * second.A) + (first.B * second.C),
        (first.A * second.B) + (first.B * second.D),
        (first.C * second.A) + (first.D * second.C),
        (first.C * second.B) + (first.D * second.D),
        (first.E * second.A) + (first.F * second.C) + second.E,
        (first.E * second.B) + (first.F * second.D) + second.F);

    /// <summary>Returns a translation by (<paramref name="tx"/>, <paramref name="ty"/>): <c>[1 0 0 1 tx ty]</c>.</summary>
    /// <param name="tx">The x offset.</param>
    /// <param name="ty">The y offset.</param>
    /// <returns>The matrix.</returns>
    /// <remarks>ISO 32000-2 §8.3.3.</remarks>
    public static Matrix CreateTranslation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    /// <summary>Returns a scaling by (<paramref name="sx"/>, <paramref name="sy"/>): <c>[sx 0 0 sy 0 0]</c>.</summary>
    /// <param name="sx">The x scale.</param>
    /// <param name="sy">The y scale.</param>
    /// <returns>The matrix.</returns>
    /// <remarks>ISO 32000-2 §8.3.3.</remarks>
    public static Matrix CreateScale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Maps a point.</summary>
    /// <param name="x">The x coordinate in the source space.</param>
    /// <param name="y">The y coordinate in the source space.</param>
    /// <returns>The point in the target space: <c>(a·x + c·y + e, b·x + d·y + f)</c>.</returns>
    /// <remarks>ISO 32000-2 §8.3.4.</remarks>
    public PathPoint Transform(double x, double y) => new((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    /// <summary>Maps a point.</summary>
    /// <param name="point">The point in the source space.</param>
    /// <returns>The point in the target space.</returns>
    /// <remarks>ISO 32000-2 §8.3.4.</remarks>
    public PathPoint Transform(PathPoint point) => Transform(point.X, point.Y);

    /// <summary>Maps a distance vector: the linear part only, without the translation.</summary>
    /// <param name="dx">The x component.</param>
    /// <param name="dy">The y component.</param>
    /// <returns>The vector in the target space: <c>(a·dx + c·dy, b·dx + d·dy)</c>.</returns>
    /// <remarks>ISO 32000-2 §8.3.4.</remarks>
    public PathPoint TransformVector(double dx, double dy) => new((A * dx) + (C * dy), (B * dx) + (D * dy));

    /// <summary>Computes the inverse transformation, when one exists.</summary>
    /// <param name="inverse">The inverse; the identity when there is none.</param>
    /// <returns><see langword="false"/> when the matrix is singular (its determinant is zero or not finite).</returns>
    /// <remarks>ISO 32000-2 §8.3.4, NOTE 3: not every transformation is invertible.</remarks>
    public bool TryInvert(out Matrix inverse)
    {
        double determinant = Determinant;
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            inverse = Identity;
            return false;
        }

        double a = D / determinant;
        double b = -B / determinant;
        double c = -C / determinant;
        double d = A / determinant;
        inverse = new Matrix(a, b, c, d, -((E * a) + (F * c)), -((E * b) + (F * d)));
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(Matrix other) =>
        A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) && D.Equals(other.D) && E.Equals(other.E) && F.Equals(other.F);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Matrix other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);

    /// <summary>Returns the matrix as a PDF array would write it, such as <c>[1 0 0 1 0 0]</c>.</summary>
    /// <returns>The matrix text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{A} {B} {C} {D} {E} {F}]");
}
