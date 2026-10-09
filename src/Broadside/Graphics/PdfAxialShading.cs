using Broadside.Graphics.Shadings;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>An axial shading (Type 2): colour varies along the axis between two points and is constant across it.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.3, Table 79. For a point (x, y) the axis parameter is
/// x′ = ((x₁ − x₀)(x − x₀) + (y₁ − y₀)(y − y₀)) / ((x₁ − x₀)² + (y₁ − y₀)²); t = t₀ + (t₁ − t₀)x′, and beyond the ends t₀ or t₁ when
/// that end is extended, else nothing is painted. <see cref="TryGetParameter"/> computes it; the colour is
/// <see cref="PdfShading.EvaluateFunction"/> of t. Coincident end points paint nothing.
/// </remarks>
public sealed class PdfAxialShading : PdfShading
{
    internal PdfAxialShading(ShadingReader reader)
        : base(reader, PdfShadingType.Axial)
    {
        if (reader.Numbers(ShadingNames.Coords) is { Length: 4 } coords)
        {
            Start = new PathPoint(coords[0], coords[1]);
            End = new PathPoint(coords[2], coords[3]);
        }
        else
        {
            reader.Invalid(DiagnosticCodes.ShadingCoordsInvalid, "An axial shading's Coords entry is not four numbers.");
        }

        Domain = Array.AsReadOnly(reader.Numbers(ShadingNames.Domain, 2, [0, 1], DiagnosticCodes.ShadingEntryInvalid));
        (ExtendStart, ExtendEnd) = reader.Extend();
        IsValid = reader.IsValid;
    }

    /// <summary>Gets the starting point of the axis, (x₀, y₀), in the target space.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (<c>Coords</c>).</remarks>
    public PathPoint Start { get; }

    /// <summary>Gets the ending point of the axis, (x₁, y₁), in the target space.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (<c>Coords</c>).</remarks>
    public PathPoint End { get; }

    /// <summary>Gets [t₀ t₁], the parametric values at the start and end of the axis. Default [0 1].</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (<c>Domain</c>).</remarks>
    public IReadOnlyList<double> Domain { get; }

    /// <summary>Gets a value indicating whether the shading extends beyond the starting point. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (<c>Extend</c>).</remarks>
    public bool ExtendStart { get; }

    /// <summary>Gets a value indicating whether the shading extends beyond the ending point. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (<c>Extend</c>).</remarks>
    public bool ExtendEnd { get; }

    /// <summary>Returns the parametric value t of a point of the target space.</summary>
    /// <param name="x">The point's x coordinate in the target space.</param>
    /// <param name="y">The point's y coordinate in the target space.</param>
    /// <param name="t">The value of t, to pass to <see cref="PdfShading.EvaluateFunction"/>.</param>
    /// <returns><see langword="false"/> when the point is not painted: beyond an end that is not extended, or the axis has no length.</returns>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79.</remarks>
    public bool TryGetParameter(double x, double y, out double t)
    {
        double dx = End.X - Start.X;
        double dy = End.Y - Start.Y;
        double length = (dx * dx) + (dy * dy);
        double t0 = Domain[0];
        double t1 = Domain[1];
        t = t0;
        if (!IsValid || length == 0)
        {
            return false;
        }

        double s = ((dx * (x - Start.X)) + (dy * (y - Start.Y))) / length;
        if (s < 0)
        {
            return ExtendStart;
        }

        if (s > 1)
        {
            t = t1;
            return ExtendEnd;
        }

        t = t0 + ((t1 - t0) * s);
        return true;
    }
}
