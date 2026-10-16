namespace Broadside.Rendering;

/// <summary>The kinds of painting a render anti-aliases; a kind left out is painted with whole pixels only.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.7.4 (scan conversion rules). Anti-aliased, a pixel takes the share of its area a shape covers. Without
/// anti-aliasing every pixel is either painted whole or left alone: a filled or stroked path, its clipping paths included, paints
/// every pixel whose square it touches, "no matter how small the intersection" (§10.7.4); glyphs follow the same rule (§9.2.4 draws
/// them as filled and stroked outlines); an image paints the pixels whose centres lie inside its unit square, with the sample under
/// each pixel's centre rather than an average over the pixel (§10.7.4, §8.9.5.3).
/// </para>
/// <para>
/// A pixel only partly inside an image's outline counts as inside when at least half of it is: the same as the centre test for any
/// pixel a single edge crosses. Shadings keep their own <c>AntiAlias</c> entry (§8.7.4.3, Table 77) inside the area their path
/// covers.
/// </para>
/// </remarks>
[Flags]
public enum AntiAliasing
{
    /// <summary>Nothing is anti-aliased.</summary>
    None = 0,

    /// <summary>Filled and stroked paths and clipping paths (§8.5.3, §8.5.4).</summary>
    Paths = 1,

    /// <summary>Glyphs filled and stroked from their outlines (§9.3.6).</summary>
    Text = 2,

    /// <summary>Images and stencil masks: their outlines and the averaging of samples a pixel covers (§8.9).</summary>
    Images = 4,

    /// <summary>Everything: the default.</summary>
    All = Paths | Text | Images,
}
