using Broadside.Fonts;

namespace Broadside;

/// <summary>How text in existing page content is replaced or deleted: the font for characters the original font cannot show, where later glyphs go, and the replacement text of tagged content.</summary>
/// <remarks>
/// ISO 32000-2 §9.4.3, §9.6.5, §9.7.6.2, §9.9.2, §9.10.3 and §14.9.4. See <see cref="PdfPage.ReplaceText"/> for the order in which fonts are
/// tried. A per-call option: each <c>With*</c> method sets a property and returns the same instance.
/// </remarks>
public sealed class PdfTextReplaceOptions
{
    /// <summary>
    /// Gets or sets the font used for characters the original font cannot show, tried first; <see langword="null"/> (the default) to try
    /// the font resolver's match for the original font, then Helvetica. A font of another document is ignored.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9.2: an embedded font gets only the glyphs it shows.</remarks>
    public PdfCanvasFont? FallbackFont { get; set; }

    /// <summary>Gets or sets where the glyphs after a replacement go; <see cref="PdfTextFit.KeepPosition"/> by default.</summary>
    /// <remarks>ISO 32000-2 §9.4.3 and §9.3.4.</remarks>
    public PdfTextFit Fit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the <c>ActualText</c>, <c>Alt</c> and <c>E</c> entries of the marked content and
    /// structure elements that hold replaced text are updated to read the new text; <see langword="true"/> by default.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3 and §14.9.4.</remarks>
    public bool UpdateReplacementText { get; set; } = true;

    /// <summary>Sets <see cref="FallbackFont"/>.</summary>
    /// <param name="font">The font, or <see langword="null"/> for the resolver's match, then Helvetica.</param>
    /// <returns>These options.</returns>
    public PdfTextReplaceOptions WithFallbackFont(PdfCanvasFont? font)
    {
        FallbackFont = font;
        return this;
    }

    /// <summary>Sets <see cref="Fit"/>.</summary>
    /// <param name="fit">Where later glyphs go.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fit"/> is not defined.</exception>
    public PdfTextReplaceOptions WithFit(PdfTextFit fit)
    {
        if (!Enum.IsDefined(fit))
        {
            throw new ArgumentOutOfRangeException(nameof(fit), fit, "Not a defined PdfTextFit.");
        }

        Fit = fit;
        return this;
    }

    /// <summary>Sets <see cref="UpdateReplacementText"/>.</summary>
    /// <param name="update">Whether replacement text is updated.</param>
    /// <returns>These options.</returns>
    public PdfTextReplaceOptions WithUpdateReplacementText(bool update = true)
    {
        UpdateReplacementText = update;
        return this;
    }
}
