using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The geometry of an axial or radial shading, reduced to the numbers that give the parametric value s ∈ [0, 1] of a point of the
/// target space, with the Extend flags and the BBox applied. A plain struct of doubles, so the per-pixel test reads no model.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.3 (axial: x′ = ((x₁ − x₀)(x − x₀) + (y₁ − y₀)(y − y₀)) / ((x₁ − x₀)² + (y₁ − y₀)²), clamped to 0 or 1 at an
/// extended end, undefined beyond an end that is not), §8.7.4.5.4 (radial: blend circles c(s) = c₀ + s(c₁ − c₀),
/// r(s) = r₀ + s(r₁ − r₀), painted in increasing s, so a point takes the greatest s with r(s) ≥ 0 whose circle passes through it;
/// both radii 0 paints nothing), §8.7.4.3 Table 77 (BBox: points outside it are not painted by the shading).
/// </remarks>
internal readonly struct ShadingGeometry
{
    private readonly bool _radial;
    private readonly bool _empty;
    private readonly bool _extendStart;
    private readonly bool _extendEnd;
    private readonly double _x0;
    private readonly double _y0;
    private readonly double _r0;
    private readonly double _dx;
    private readonly double _dy;
    private readonly double _dr;

    // Axial: 1 / |axis|². Radial: the s² coefficient a = |d|² − Δr², or 0 when it vanishes relative to the geometry.
    private readonly double _scale;
    private readonly bool _hasBox;
    private readonly double _boxLeft;
    private readonly double _boxBottom;
    private readonly double _boxRight;
    private readonly double _boxTop;

    private ShadingGeometry(PdfShading shading, bool clipToBox, bool radial, bool empty, bool extendStart, bool extendEnd, double x0, double y0, double r0, double dx, double dy, double dr, double scale)
    {
        _radial = radial;
        _empty = empty;
        _extendStart = extendStart;
        _extendEnd = extendEnd;
        _x0 = x0;
        _y0 = y0;
        _r0 = r0;
        _dx = dx;
        _dy = dy;
        _dr = dr;
        _scale = scale;
        if (clipToBox && shading.BoundingBox is { } box)
        {
            _hasBox = true;
            _boxLeft = box.Left;
            _boxBottom = box.Bottom;
            _boxRight = box.Right;
            _boxTop = box.Top;
        }
    }

    /// <summary>Gets a value indicating whether the shading defines no point at all (a zero-length axis, two zero radii).</summary>
    public bool IsEmpty => _empty;

    /// <summary>Returns the geometry of an axial or radial shading, or <see langword="false"/> for the other types.</summary>
    /// <param name="shading">The shading.</param>
    /// <param name="clipToBox">Whether points outside the shading's BBox are left out; false when the painted shape is the BBox itself.</param>
    /// <param name="geometry">The geometry.</param>
    public static bool TryCreate(PdfShading shading, bool clipToBox, out ShadingGeometry geometry)
    {
        switch (shading)
        {
            case PdfAxialShading axial:
                {
                    double dx = axial.End.X - axial.Start.X;
                    double dy = axial.End.Y - axial.Start.Y;
                    double length = (dx * dx) + (dy * dy);
                    bool empty = !(length > 0) || !double.IsFinite(1 / length);
                    geometry = new ShadingGeometry(axial, clipToBox, false, empty, axial.ExtendStart, axial.ExtendEnd, axial.Start.X, axial.Start.Y, 0, dx, dy, 0, empty ? 0 : 1 / length);
                    return true;
                }

            case PdfRadialShading radial:
                {
                    double dx = radial.EndCenter.X - radial.StartCenter.X;
                    double dy = radial.EndCenter.Y - radial.StartCenter.Y;
                    double dr = radial.EndRadius - radial.StartRadius;
                    double a = (dx * dx) + (dy * dy) - (dr * dr);
                    double magnitude = (dx * dx) + (dy * dy) + (dr * dr);
                    if (Math.Abs(a) <= magnitude * 1e-12)
                    {
                        a = 0;
                    }

                    bool empty = radial.StartRadius == 0 && radial.EndRadius == 0;
                    geometry = new ShadingGeometry(radial, clipToBox, true, empty, radial.ExtendStart, radial.ExtendEnd, radial.StartCenter.X, radial.StartCenter.Y, radial.StartRadius, dx, dy, dr, a);
                    return true;
                }

            default:
                geometry = default;
                return false;
        }
    }

    /// <summary>Returns s ∈ [0, 1] at (x, y), or <see langword="false"/> when the shading does not paint that point.</summary>
    public bool TryGetParameter(double x, double y, out double s)
    {
        s = 0;
        if (_empty || (_hasBox && (x < _boxLeft || x > _boxRight || y < _boxBottom || y > _boxTop)))
        {
            return false;
        }

        return _radial ? Radial(x - _x0, y - _y0, out s) : Axial(x - _x0, y - _y0, out s);
    }

    private bool Axial(double qx, double qy, out double s)
    {
        s = ((_dx * qx) + (_dy * qy)) * _scale;
        if (s < 0)
        {
            s = 0;
            return _extendStart;
        }

        if (s > 1)
        {
            s = 1;
            return _extendEnd;
        }

        return s >= 0;
    }

    private bool Radial(double qx, double qy, out double s)
    {
        // |q − s d| = r₀ + s Δr  ⇔  a s² − 2 b s + c = 0 with b = q·d + r₀Δr and c = |q|² − r₀².
        double b = (qx * _dx) + (qy * _dy) + (_r0 * _dr);
        double c = (qx * qx) + (qy * qy) - (_r0 * _r0);
        double a = _scale;
        s = 0;
        if (a == 0)
        {
            if (b == 0)
            {
                return false;
            }

            return Accept(c / (2 * b), out s);
        }

        double discriminant = (b * b) - (a * c);
        if (discriminant < 0)
        {
            return false;
        }

        double root = Math.Sqrt(discriminant);
        double first = (b + root) / a;
        double second = (b - root) / a;
        return Accept(Math.Max(first, second), out s) || Accept(Math.Min(first, second), out s);
    }

    /// <summary>Whether the circle of parameter <paramref name="value"/> is painted: a radius not negative, and s within [0, 1] or an extended range.</summary>
    private bool Accept(double value, out double s)
    {
        s = value < 0 ? 0 : value > 1 ? 1 : value;
        return _r0 + (value * _dr) >= 0
            && ((value >= 0 && value <= 1) || (value < 0 && _extendStart) || (value > 1 && _extendEnd));
    }
}
