using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The line parameters a stroke in a display list is drawn with, as the graphics state held them at the stroking operator: width,
/// caps, joins, miter limit, dash pattern and stroke adjustment, all in user space units. Valid only during the backend call that
/// hands it over.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.4.3 (line width, cap, join, miter limit, dash pattern) and §10.7.5 (automatic stroke adjustment). Lengths are in
/// the user space of the stroke, the one the transform set before it maps to default user space, so a backend that strokes in that
/// space and maps the outline gets the elliptical pen a non-uniform transform implies (§8.4.3.2).
/// </para>
/// <para>
/// A backend applies the stroking rules of §8.5.3.2 itself: each subpath is stroked on its own and restarts the dash pattern;
/// closed subpaths get a join instead of caps; a degenerate subpath paints a dot only with round caps; a trailing lone moveto paints
/// nothing; a width of 0 is the thinnest line the device can render.
/// </para>
/// </remarks>
public readonly ref struct StrokeStyle
{
    internal StrokeStyle(double lineWidth, LineCap lineCap, LineJoin lineJoin, double miterLimit, ReadOnlySpan<double> dashArray, double dashPhase, bool strokeAdjustment)
    {
        LineWidth = lineWidth;
        LineCap = lineCap;
        LineJoin = lineJoin;
        MiterLimit = miterLimit;
        DashArray = dashArray;
        DashPhase = dashPhase;
        StrokeAdjustment = strokeAdjustment;
    }

    /// <summary>Gets the line width in user space units, never negative; 0 asks for the thinnest line the device can render.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.2.</remarks>
    public double LineWidth { get; }

    /// <summary>Gets the shape at the ends of open subpaths and of dashes.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.3, Table 53.</remarks>
    public LineCap LineCap { get; }

    /// <summary>Gets the shape at the corners where consecutive segments meet.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.4, Table 54.</remarks>
    public LineJoin LineJoin { get; }

    /// <summary>Gets the miter limit, at least 1: a miter join longer than this times the line width becomes a bevel.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.5: the ratio is 1 / sin(φ / 2) for the angle φ between the segments in user space.</remarks>
    public double MiterLimit { get; }

    /// <summary>Gets the lengths of the alternating dashes and gaps in user space units; empty for a solid line.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.6: non-negative and, when there are any, not all zero.</remarks>
    public ReadOnlySpan<double> DashArray { get; }

    /// <summary>Gets the distance into the dash pattern at which every subpath starts, never negative; 0 for a solid line.</summary>
    /// <remarks>ISO 32000-2 §8.4.3.6.</remarks>
    public double DashPhase { get; }

    /// <summary>Gets a value indicating whether the backend should adjust widths and coordinates for strokes of uniform thickness.</summary>
    /// <remarks>ISO 32000-2 §10.7.5 (the SA entry of a graphics state parameter dictionary).</remarks>
    public bool StrokeAdjustment { get; }
}
