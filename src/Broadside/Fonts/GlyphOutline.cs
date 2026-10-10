using Broadside.Graphics;

namespace Broadside.Fonts;

/// <summary>
/// The outline of one glyph in glyph space, as the path type content stream paths use: a reusable buffer of
/// <see cref="PathVerb"/>s and <see cref="PathPoint"/>s that a <see cref="FontProgram"/> fills and <see cref="Path"/> views.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.2.4 (glyph space) and §8.5.2. Coordinates are in the program's glyph space units (font units for TrueType,
/// mapped to text space by <see cref="FontProgram.FontMatrix"/>). TrueType outlines use <see cref="PathVerb.QuadTo"/>; CFF and Type 1
/// outlines use <see cref="PathVerb.CubicTo"/>. Every contour starts with <see cref="PathVerb.MoveTo"/> and ends with
/// <see cref="PathVerb.Close"/>. Glyph outlines are filled with the nonzero winding number rule.
/// </para>
/// <para>
/// One instance is meant to be reused for many glyphs: <see cref="FontProgram.GetOutline"/> clears it first, and the buffers grow to
/// the largest glyph and stay, so extracting outlines allocates nothing once warm. Not thread-safe: use one per thread.
/// </para>
/// </remarks>
public sealed class GlyphOutline
{
    private PathVerb[] _verbs = new PathVerb[16];
    private PathPoint[] _points = new PathPoint[32];
    private int _verbCount;
    private int _pointCount;

    /// <summary>Gets the outline as a path view over the buffers; valid until the outline is next changed.</summary>
    public PathView Path => new(_verbs.AsSpan(0, _verbCount), _points.AsSpan(0, _pointCount));

    /// <summary>Gets a value indicating whether the outline has no segments.</summary>
    public bool IsEmpty => _verbCount == 0;

    /// <summary>Removes every segment, keeping the buffers.</summary>
    public void Clear()
    {
        _verbCount = 0;
        _pointCount = 0;
    }

    /// <summary>Begins a contour at a point.</summary>
    /// <param name="x">The x coordinate, in glyph space.</param>
    /// <param name="y">The y coordinate, in glyph space.</param>
    public void MoveTo(double x, double y) => Append(PathVerb.MoveTo, new PathPoint(x, y));

    /// <summary>Adds a straight line from the current point.</summary>
    /// <param name="x">The x coordinate of the end point.</param>
    /// <param name="y">The y coordinate of the end point.</param>
    public void LineTo(double x, double y) => Append(PathVerb.LineTo, new PathPoint(x, y));

    /// <summary>Adds a quadratic Bézier curve from the current point (TrueType outlines).</summary>
    /// <param name="controlX">The x coordinate of the control point.</param>
    /// <param name="controlY">The y coordinate of the control point.</param>
    /// <param name="x">The x coordinate of the end point.</param>
    /// <param name="y">The y coordinate of the end point.</param>
    public void QuadTo(double controlX, double controlY, double x, double y)
    {
        EnsureCapacity(2);
        _verbs[_verbCount++] = PathVerb.QuadTo;
        _points[_pointCount++] = new PathPoint(controlX, controlY);
        _points[_pointCount++] = new PathPoint(x, y);
    }

    /// <summary>Adds a cubic Bézier curve from the current point (CFF and Type 1 outlines).</summary>
    /// <param name="control1X">The x coordinate of the first control point.</param>
    /// <param name="control1Y">The y coordinate of the first control point.</param>
    /// <param name="control2X">The x coordinate of the second control point.</param>
    /// <param name="control2Y">The y coordinate of the second control point.</param>
    /// <param name="x">The x coordinate of the end point.</param>
    /// <param name="y">The y coordinate of the end point.</param>
    public void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
    {
        EnsureCapacity(3);
        _verbs[_verbCount++] = PathVerb.CubicTo;
        _points[_pointCount++] = new PathPoint(control1X, control1Y);
        _points[_pointCount++] = new PathPoint(control2X, control2Y);
        _points[_pointCount++] = new PathPoint(x, y);
    }

    /// <summary>Closes the current contour with a straight line back to its first point.</summary>
    public void Close()
    {
        EnsureCapacity(0);
        _verbs[_verbCount++] = PathVerb.Close;
    }

    private void Append(PathVerb verb, PathPoint point)
    {
        EnsureCapacity(1);
        _verbs[_verbCount++] = verb;
        _points[_pointCount++] = point;
    }

    private void EnsureCapacity(int points)
    {
        if (_verbCount + 1 > _verbs.Length)
        {
            _verbs = Grow(_verbs, _verbCount);
        }

        if (_pointCount + points > _points.Length)
        {
            _points = Grow(_points, _pointCount);
        }
    }

    private static T[] Grow<T>(T[] array, int used)
    {
        var larger = new T[array.Length * 2];
        array.AsSpan(0, used).CopyTo(larger);
        return larger;
    }
}
