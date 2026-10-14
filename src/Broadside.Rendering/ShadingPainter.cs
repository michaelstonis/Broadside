using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Paints an axial or radial shading pixel by pixel: each device pixel's centre maps back into the shading's target space, the
/// parametric value s of that point is found from the spec's geometry, and the colour is read from the shading's lookup table
/// (<see cref="ShadingSource"/>). Points the shading does not define (beyond an end that is not extended, outside every blend
/// circle, outside the BBox) take the Background when the paint has one, and are otherwise left unpainted.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.3 (axial: x′ from the projection on the axis, clamped to an extended end), §8.7.4.5.4 (radial: the greatest
/// s whose blend circle passes through the point, with r(s) ≥ 0, from the quadratic of |p − c(s)| = r(s)), §8.7.4.3 Table 77 (BBox
/// clips the shading; Background fills the painted area outside it; the AntiAlias hint asks for filtering). With the AntiAlias hint
/// each pixel averages four samples of a 2 × 2 grid, so the shading's own edges (a non-extended end, a circle's rim, the BBox) are
/// smoothed; without it the centre decides, as the hint's default (no filtering) allows.
/// </para>
/// <para>
/// Hot path: allocates nothing; the per-pixel work is a few multiply-adds (axial) or a square root (radial) and a table read.
/// Compositing goes through a <see cref="Compositor"/> (§11.3: constant alpha and blend mode) by the coverage of the painted shape. It is a coverage sink of
/// <see cref="ScanlineRasterizer.Paint"/>, so the runs it receives are already cut by the clipping path (§8.5.4).
/// </para>
/// </remarks>
internal readonly ref struct ShadingPainter : ICoverageSink
{
    private readonly Span<byte> _target;
    private readonly int _stride;
    private readonly Matrix _inverse;
    private readonly ShadingGeometry _geometry;
    private readonly uint[] _table;
    private readonly bool _hasBackground;
    private readonly uint _background;
    private readonly bool _antiAlias;
    private readonly Compositor _compositor;

    /// <summary>Creates the painter.</summary>
    /// <param name="target">The device pixels painted into, premultiplied BGRA in memory order.</param>
    /// <param name="stride">The bytes from one row of <paramref name="target"/> to the next.</param>
    /// <param name="inverse">Device pixels to the shading's target space.</param>
    /// <param name="geometry">Where s comes from.</param>
    /// <param name="table">The colours by s (<see cref="ShadingSource.Table"/>).</param>
    /// <param name="hasBackground">Whether undefined points take <paramref name="background"/>.</param>
    /// <param name="background">The Background colour, opaque premultiplied BGRA.</param>
    /// <param name="antiAlias">Whether each pixel averages four samples.</param>
    /// <param name="compositor">The constant alpha and blend mode the shading composites with (§11.3); the default is opaque Normal.</param>
    public ShadingPainter(Span<byte> target, int stride, in Matrix inverse, in ShadingGeometry geometry, uint[] table, bool hasBackground, uint background, bool antiAlias, Compositor compositor = default)
    {
        _compositor = compositor;
        _target = target;
        _stride = stride;
        _inverse = inverse;
        _geometry = geometry;
        _table = table;
        _hasBackground = hasBackground;
        _background = background;
        _antiAlias = antiAlias;
    }

    /// <inheritdoc/>
    public void Span(int row, int x, int count, int coverage)
    {
        if (coverage <= 0)
        {
            return;
        }

        Span<uint> line = MemoryMarshal.Cast<byte, uint>(_target.Slice(row * _stride, (x + count) * 4));
        Matrix m = _inverse;
        double px = x + 0.5;
        double py = row + 0.5;
        double u = (m.A * px) + (m.C * py) + m.E;
        double v = (m.B * px) + (m.D * py) + m.F;

        // The 2 × 2 grid at ±¼ pixel, in shading space.
        double au = 0.25 * (m.A + m.C);
        double av = 0.25 * (m.B + m.D);
        double bu = 0.25 * (m.A - m.C);
        double bv = 0.25 * (m.B - m.D);
        for (int i = 0; i < count; i++, u += m.A, v += m.B)
        {
            ref uint pixel = ref line[x + i];
            if (!_antiAlias)
            {
                if (Sample(u, v, out uint color))
                {
                    pixel = _compositor.Composite(color, pixel, coverage);
                }

                continue;
            }

            int painted = 0;
            int blue = 0;
            int green = 0;
            int red = 0;
            Accumulate(u + au, v + av, ref painted, ref blue, ref green, ref red);
            Accumulate(u - au, v - av, ref painted, ref blue, ref green, ref red);
            Accumulate(u + bu, v + bv, ref painted, ref blue, ref green, ref red);
            Accumulate(u - bu, v - bv, ref painted, ref blue, ref green, ref red);
            if (painted > 0)
            {
                // The average of the four samples, unpainted ones transparent: premultiplied, alpha = painted / 4.
                uint average = (uint)((blue + 2) >> 2) | ((uint)((green + 2) >> 2) << 8) | ((uint)((red + 2) >> 2) << 16) | ((uint)(((painted * 255) + 2) >> 2) << 24);
                pixel = _compositor.Composite(average, pixel, coverage);
            }
        }
    }

    private void Accumulate(double u, double v, ref int painted, ref int blue, ref int green, ref int red)
    {
        if (Sample(u, v, out uint color))
        {
            painted++;
            blue += (int)(color & 0xFF);
            green += (int)((color >> 8) & 0xFF);
            red += (int)((color >> 16) & 0xFF);
        }
    }

    /// <summary>The colour at (u, v) of the shading's target space: the table's, the Background's, or none.</summary>
    private bool Sample(double u, double v, out uint color)
    {
        if (_geometry.TryGetParameter(u, v, out double s))
        {
            color = _table[(int)((s * (ShadingSource.Size - 1)) + 0.5)];
            return true;
        }

        color = _background;
        return _hasBackground;
    }
}
