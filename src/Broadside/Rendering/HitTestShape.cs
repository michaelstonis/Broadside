using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>The area an image or a path paints, as a <see cref="HitTestIndex"/> holds it, in page space.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.4 (an image fills the unit square of user space), §8.5.3 (path painting) and §8.5.4 (clipping), in default user
/// space (§8.3.2.3). <see cref="Quad"/> is the shape's box mapped by its transform: the unit square for an image or stencil mask, the
/// bounding box of the path's points for a path, widened on every side for a stroke by half the line width (by the miter limit times
/// half the width for mitered joins, and by √2 times it otherwise, which holds square caps). <see cref="Bounds"/> is the upright
/// rectangle around it, cut to the bounds of the clip in force; a shape the clip removes entirely is not in the index.
/// </para>
/// <para>
/// A point hits the shape when it is inside both. Both are conservative: a curve's control points and a clip's corners can reach
/// beyond what is painted. Shading paints with <c>sh</c> fill the clip and are not in the index.
/// </para>
/// </remarks>
/// <param name="Quad">The shape's box mapped into page space.</param>
/// <param name="Bounds">The upright rectangle around <paramref name="Quad"/>, within the clip's bounds.</param>
public readonly record struct HitTestShape(Quad Quad, PdfRectangle Bounds);
