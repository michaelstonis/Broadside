namespace Broadside.Graphics;

/// <summary>
/// A path as verbs and points, without a copy: a view over storage the producer owns, valid only while the call that handed it
/// out runs. Copy what you keep.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.5.2. Each <see cref="PathVerb"/> takes its points from <see cref="Points"/> in order: one for
/// <see cref="PathVerb.MoveTo"/> and <see cref="PathVerb.LineTo"/>, two for <see cref="PathVerb.QuadTo"/>, three for
/// <see cref="PathVerb.CubicTo"/>, none for <see cref="PathVerb.Close"/>.
/// </para>
/// <para>
/// A content stream path is reported exactly as constructed, in the user space current when it was painted: degenerate subpaths,
/// a closed single point and a trailing lone <c>m</c> are kept. Applying the painting rules for them (§8.5.3.2: a degenerate subpath
/// is stroked as a dot only with round caps, a trailing moveto paints nothing; §8.5.3.3.1: open subpaths are closed for filling) is
/// the renderer's job. After an <c>h</c>, a later segment starts with an explicit <see cref="PathVerb.MoveTo"/> to the closed
/// subpath's first point (Table 58), so every subpath begins with a moveto.
/// </para>
/// </remarks>
public readonly ref struct PathView
{
    /// <summary>Initializes a new instance of the <see cref="PathView"/> struct.</summary>
    /// <param name="verbs">The segments.</param>
    /// <param name="points">The points the segments take, in order.</param>
    public PathView(ReadOnlySpan<PathVerb> verbs, ReadOnlySpan<PathPoint> points)
    {
        Verbs = verbs;
        Points = points;
    }

    /// <summary>Gets the segments, in construction order.</summary>
    public ReadOnlySpan<PathVerb> Verbs { get; }

    /// <summary>Gets the points the segments take, in order.</summary>
    public ReadOnlySpan<PathPoint> Points { get; }

    /// <summary>Gets a value indicating whether the path has no segments at all.</summary>
    public bool IsEmpty => Verbs.IsEmpty;

    /// <summary>Gets a value indicating whether the last segment is a moveto: a single-point open subpath that paints nothing (§8.5.3.2).</summary>
    public bool HasTrailingMoveTo => !Verbs.IsEmpty && Verbs[^1] == PathVerb.MoveTo;

    /// <summary>
    /// Gets the smallest rectangle containing every point, control points included, in the path's own space; for an empty path, a
    /// rectangle of zero size at the origin.
    /// </summary>
    /// <remarks>Control points bound the curve (a Bézier curve lies inside the convex hull of its points), so this may be larger than the painted area.</remarks>
    public PdfRectangle Bounds
    {
        get
        {
            if (Points.IsEmpty)
            {
                return default;
            }

            double left = double.PositiveInfinity;
            double bottom = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double top = double.NegativeInfinity;
            foreach (PathPoint point in Points)
            {
                left = Math.Min(left, point.X);
                right = Math.Max(right, point.X);
                bottom = Math.Min(bottom, point.Y);
                top = Math.Max(top, point.Y);
            }

            return new PdfRectangle(left, bottom, right, top);
        }
    }
}
