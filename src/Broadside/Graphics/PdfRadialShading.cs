using Broadside.Graphics.Shadings;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A radial shading (Type 3): colour varies between two circles, through the blend circles between them.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.4, Table 80. The blend circle for s in [0, 1] has centre (x₀ + s(x₁ − x₀), y₀ + s(y₁ − y₀)) and radius
/// r₀ + s(r₁ − r₀); an extended end continues it below 0 or above 1 while the radius is not negative. Circles are painted in order
/// of increasing s, so a point on several of them takes the colour of the greatest s (§8.7.4.5.4: "the last of the enclosing
/// circles to be painted"). <see cref="TryGetParameter"/> computes that s and the t it maps to.
/// </para>
/// <para>Repairs: a negative radius reads as 0, with a diagnostic. Both radii 0 paint nothing.</para>
/// </remarks>
public sealed class PdfRadialShading : PdfShading
{
    internal PdfRadialShading(ShadingReader reader)
        : base(reader, PdfShadingType.Radial)
    {
        if (reader.Numbers(ShadingNames.Coords) is { Length: 6 } coords)
        {
            if (coords[2] < 0 || coords[5] < 0)
            {
                reader.Report(DiagnosticCodes.ShadingCoordsInvalid, "A radial shading has a negative radius; it is read as 0.");
            }

            StartCenter = new PathPoint(coords[0], coords[1]);
            StartRadius = Math.Max(coords[2], 0);
            EndCenter = new PathPoint(coords[3], coords[4]);
            EndRadius = Math.Max(coords[5], 0);
        }
        else
        {
            reader.Invalid(DiagnosticCodes.ShadingCoordsInvalid, "A radial shading's Coords entry is not six numbers.");
        }

        Domain = Array.AsReadOnly(reader.Numbers(ShadingNames.Domain, 2, [0, 1], DiagnosticCodes.ShadingEntryInvalid));
        (ExtendStart, ExtendEnd) = reader.Extend();
        IsValid = reader.IsValid;
    }

    /// <summary>Gets the centre of the starting circle, (x₀, y₀), in the target space.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Coords</c>).</remarks>
    public PathPoint StartCenter { get; }

    /// <summary>Gets the radius of the starting circle, r₀, never negative.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Coords</c>).</remarks>
    public double StartRadius { get; }

    /// <summary>Gets the centre of the ending circle, (x₁, y₁), in the target space.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Coords</c>).</remarks>
    public PathPoint EndCenter { get; }

    /// <summary>Gets the radius of the ending circle, r₁, never negative.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Coords</c>).</remarks>
    public double EndRadius { get; }

    /// <summary>Gets [t₀ t₁], the parametric values at the starting and ending circles. Default [0 1].</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Domain</c>).</remarks>
    public IReadOnlyList<double> Domain { get; }

    /// <summary>Gets a value indicating whether the shading extends beyond the starting circle. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Extend</c>).</remarks>
    public bool ExtendStart { get; }

    /// <summary>Gets a value indicating whether the shading extends beyond the ending circle. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (<c>Extend</c>).</remarks>
    public bool ExtendEnd { get; }

    /// <summary>Returns the parametric value t of a point of the target space: the greatest s whose blend circle passes through it.</summary>
    /// <param name="x">The point's x coordinate in the target space.</param>
    /// <param name="y">The point's y coordinate in the target space.</param>
    /// <param name="t">The value of t (t₀ for extended points before the start, t₁ after the end).</param>
    /// <returns><see langword="false"/> when no blend circle passes through the point, so it is not painted.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.5.4. Solving |p − c(s)| = r(s) gives a s² − 2b s + c = 0 with a = d·d − Δr², b = q·d + r₀Δr,
    /// c = q·q − r₀² (d the centre displacement, Δr = r₁ − r₀, q = p − c₀); the larger root is taken when r(s) ≥ 0 and s is in [0, 1]
    /// or in an extended range, else the smaller root under the same test.
    /// </remarks>
    public bool TryGetParameter(double x, double y, out double t)
    {
        double t0 = Domain[0];
        double t1 = Domain[1];
        t = t0;
        if (!IsValid || (StartRadius == 0 && EndRadius == 0))
        {
            return false;
        }

        double dx = EndCenter.X - StartCenter.X;
        double dy = EndCenter.Y - StartCenter.Y;
        double dr = EndRadius - StartRadius;
        double qx = x - StartCenter.X;
        double qy = y - StartCenter.Y;
        double a = (dx * dx) + (dy * dy) - (dr * dr);
        double b = (qx * dx) + (qy * dy) + (StartRadius * dr);
        double c = (qx * qx) + (qy * qy) - (StartRadius * StartRadius);
        double larger;
        double smaller;
        if (a == 0)
        {
            if (b == 0)
            {
                return false;
            }

            larger = smaller = c / (2 * b);
        }
        else
        {
            double discriminant = (b * b) - (a * c);
            if (discriminant < 0)
            {
                return false;
            }

            double root = Math.Sqrt(discriminant);
            double first = (b + root) / a;
            double second = (b - root) / a;
            larger = Math.Max(first, second);
            smaller = Math.Min(first, second);
        }

        double s;
        if (Accepts(larger, dr))
        {
            s = larger;
        }
        else if (Accepts(smaller, dr))
        {
            s = smaller;
        }
        else
        {
            return false;
        }

        t = t0 + ((t1 - t0) * Math.Clamp(s, 0, 1));
        return true;
    }

    private bool Accepts(double s, double dr) =>
        StartRadius + (s * dr) >= 0 && (s is >= 0 and <= 1 || (s < 0 && ExtendStart) || (s > 1 && ExtendEnd));
}
