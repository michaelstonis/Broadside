namespace Broadside.Graphics;

/// <summary>One segment kind of a path, and how many points it takes from the path's point list.</summary>
/// <remarks>
/// ISO 32000-2 §8.5.2, Table 58. Content stream paths are normalized to move, line, cubic and close (<c>v</c>, <c>y</c> and
/// <c>re</c> are expressed through them); <see cref="QuadTo"/> exists for glyph outlines (TrueType quadratic curves), so text clips
/// and glyph outlines use the same path type.
/// </remarks>
public enum PathVerb
{
    /// <summary>Begins a subpath at one point.</summary>
    MoveTo,

    /// <summary>A straight line from the current point to one point.</summary>
    LineTo,

    /// <summary>A quadratic Bézier curve: a control point and an end point (two points). Never produced by content stream operators.</summary>
    QuadTo,

    /// <summary>A cubic Bézier curve: two control points and an end point (three points).</summary>
    CubicTo,

    /// <summary>Closes the current subpath with a straight line back to its first point (no points).</summary>
    Close,
}
