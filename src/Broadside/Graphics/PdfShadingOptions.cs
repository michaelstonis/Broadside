namespace Broadside.Graphics;

/// <summary>The optional entries of a shading the <see cref="PdfDocument"/> factories write.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.3, Table 77 (<c>Background</c>, <c>BBox</c>, <c>AntiAlias</c>, common to all shadings), and Tables 79 and 80
/// (<c>Extend</c>, axial and radial shadings only). The defaults write none of them.
/// </remarks>
public sealed class PdfShadingOptions
{
    /// <summary>Gets or sets the shading's bounding box in its target space, a temporary clip when it is painted; <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>BBox</c>).</remarks>
    public PdfRectangle? BoundingBox { get; set; }

    /// <summary>
    /// Gets or sets the colour, in the shading's colour space, that fills the area outside the shading when it is used in a shading
    /// pattern (the <c>sh</c> operator ignores it); <see langword="null"/> for none.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>Background</c>).</remarks>
    public PdfColor? Background { get; set; }

    /// <summary>Gets or sets a value indicating whether the shading's function should be filtered against aliasing (a hint). Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.3, Table 77 (<c>AntiAlias</c>).</remarks>
    public bool AntiAlias { get; set; }

    /// <summary>Gets or sets a value indicating whether an axial or radial shading extends beyond its starting point or circle. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79, and §8.7.4.5.4, Table 80 (<c>Extend</c>). The other shading types reject it.</remarks>
    public bool ExtendStart { get; set; }

    /// <summary>Gets or sets a value indicating whether an axial or radial shading extends beyond its ending point or circle. Default false.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79, and §8.7.4.5.4, Table 80 (<c>Extend</c>). The other shading types reject it.</remarks>
    public bool ExtendEnd { get; set; }

    /// <summary>Sets <see cref="BoundingBox"/>.</summary>
    /// <param name="boundingBox">The bounding box, in the shading's target space.</param>
    /// <returns>These options.</returns>
    public PdfShadingOptions WithBoundingBox(PdfRectangle boundingBox)
    {
        BoundingBox = boundingBox;
        return this;
    }

    /// <summary>Sets <see cref="Background"/>.</summary>
    /// <param name="background">The colour, in the shading's colour space.</param>
    /// <returns>These options.</returns>
    public PdfShadingOptions WithBackground(PdfColor background)
    {
        Background = background;
        return this;
    }

    /// <summary>Sets <see cref="AntiAlias"/>.</summary>
    /// <param name="antiAlias">Whether to ask for anti-aliasing.</param>
    /// <returns>These options.</returns>
    public PdfShadingOptions WithAntiAlias(bool antiAlias)
    {
        AntiAlias = antiAlias;
        return this;
    }

    /// <summary>Sets <see cref="ExtendStart"/> and <see cref="ExtendEnd"/>.</summary>
    /// <param name="start">Whether to extend beyond the start.</param>
    /// <param name="end">Whether to extend beyond the end.</param>
    /// <returns>These options.</returns>
    public PdfShadingOptions WithExtend(bool start, bool end)
    {
        ExtendStart = start;
        ExtendEnd = end;
        return this;
    }
}
