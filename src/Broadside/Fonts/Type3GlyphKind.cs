namespace Broadside.Fonts;

/// <summary>How a Type 3 glyph description declares itself: the operator it begins with.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.4, Table 111. <c>d0</c> declares a glyph that specifies both its shape and its colour; <c>d1</c> one that
/// specifies only its shape, painted with the colour in effect where the glyph is shown (colour operators inside it are ignored,
/// §8.6.8), so its appearance can be cached as a mask.
/// </remarks>
public enum Type3GlyphKind
{
    /// <summary>Not a Type 3 glyph, or a Type 3 glyph without a description (its code selects no <c>CharProcs</c> stream).</summary>
    None = 0,

    /// <summary><c>wx wy d0</c>: the description specifies the glyph's shape and its colour.</summary>
    ShapeAndColor = 1,

    /// <summary><c>wx wy llx lly urx ury d1</c>: the description specifies only the glyph's shape, inside a declared bounding box.</summary>
    ShapeOnly = 2,

    /// <summary>
    /// The description begins with neither operator, or with one whose operands are unusable: it runs as if it began with
    /// <c>d0</c> (the repair pdf.js and PDFBox make), and running it reports <c>ContentType3GlyphMetricsMissing</c> when the first
    /// operator is neither.
    /// </summary>
    Undeclared = 3,
}
