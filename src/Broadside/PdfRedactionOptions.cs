using Broadside.Annotations;
using Broadside.Text;

namespace Broadside;

/// <summary>
/// How a redaction is marked and applied: the overlay drawn over each area once its content is removed, and what happens to images,
/// annotations and form fields under the areas.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.23 (Table 195: <c>IC</c>, <c>OverlayText</c>, <c>Repeat</c>). <see cref="PdfPage.AddRedactAnnotation(PdfRectangle, PdfRedactionOptions?)"/>
/// writes the overlay entries into the annotation, and <see cref="PdfPage.Redact(IReadOnlyList{Graphics.Quad}, PdfRedactionOptions?)"/> draws them; <see cref="PdfDocument.Redact(PdfRedactionOptions?)"/> draws
/// each annotation's own entries and uses only <see cref="ImageMode"/>, <see cref="RemoveIntersectingAnnotations"/> and
/// <see cref="ClearIntersectingFields"/> from these options.
/// </remarks>
public sealed class PdfRedactionOptions
{
    /// <summary>Gets or sets the color that fills each area after its content is removed (<c>IC</c>); black by default, <see langword="null"/> for no fill.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public PdfDeviceColor? FillColor { get; set; } = new(0);

    /// <summary>Gets or sets the text drawn in each area after its content is removed (<c>OverlayText</c>), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public string? OverlayText { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="OverlayText"/> repeats to fill each area (<c>Repeat</c>).</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public bool RepeatOverlayText { get; set; }

    /// <summary>Gets or sets what happens to an image that an area covers only in part.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.</remarks>
    public PdfRedactionImageMode ImageMode { get; set; }

    /// <summary>Gets or sets a value indicating whether annotations (other than widgets) whose rectangle meets an area are removed; true by default.</summary>
    /// <remarks>ISO 32000-2 §12.5.2.</remarks>
    public bool RemoveIntersectingAnnotations { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the fields of widgets whose rectangle meets an area lose their value; true by default.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.</remarks>
    public bool ClearIntersectingFields { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether redacting search matches removes only the matched glyphs; <see langword="false"/> (the
    /// default) also removes the images and subpaths under the match quads and the annotations there, so text drawn into an image under
    /// a match goes too. Redacting areas ignores it.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.6.23; §9.4.3 for the glyphs, §8.5 and §8.9 for paths and images.</remarks>
    public bool GlyphsOnly { get; set; }

    /// <summary>
    /// Gets or sets what text redacting search matches searches, or <see langword="null"/> (the default) for every glyph, hidden content
    /// and artifacts included, so an invisible OCR layer (rendering mode 3) is searched and redacted with what it lies over. Set it to
    /// narrow the search, as <see cref="PdfPage.FindText(TextQuery, TextExtractionOptions)"/> is narrowed.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10, §9.3.6 (Table 104, mode 3), §8.11.3.1 and §14.8.2.2.</remarks>
    public TextExtractionOptions? TextExtractionOptions { get; set; }

    /// <summary>Sets <see cref="GlyphsOnly"/>.</summary>
    /// <param name="glyphsOnly">Whether only the matched glyphs are removed.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithGlyphsOnly(bool glyphsOnly = true)
    {
        GlyphsOnly = glyphsOnly;
        return this;
    }

    /// <summary>Sets <see cref="TextExtractionOptions"/>.</summary>
    /// <param name="options">What text is searched, or <see langword="null"/> for everything.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithTextExtractionOptions(TextExtractionOptions? options)
    {
        TextExtractionOptions = options;
        return this;
    }

    /// <summary>Sets <see cref="FillColor"/>.</summary>
    /// <param name="color">The fill, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithFillColor(PdfDeviceColor? color)
    {
        FillColor = color;
        return this;
    }

    /// <summary>Sets <see cref="OverlayText"/>.</summary>
    /// <param name="text">The text, or <see langword="null"/> for none.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithOverlayText(string? text)
    {
        OverlayText = text;
        return this;
    }

    /// <summary>Sets <see cref="RepeatOverlayText"/>.</summary>
    /// <param name="repeat">Whether the overlay text repeats.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithRepeatOverlayText(bool repeat)
    {
        RepeatOverlayText = repeat;
        return this;
    }

    /// <summary>Sets <see cref="ImageMode"/>.</summary>
    /// <param name="mode">The image mode.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not defined.</exception>
    public PdfRedactionOptions WithImageMode(PdfRedactionImageMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a defined PdfRedactionImageMode.");
        }

        ImageMode = mode;
        return this;
    }

    /// <summary>Sets <see cref="RemoveIntersectingAnnotations"/>.</summary>
    /// <param name="remove">Whether intersecting annotations are removed.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithRemoveIntersectingAnnotations(bool remove)
    {
        RemoveIntersectingAnnotations = remove;
        return this;
    }

    /// <summary>Sets <see cref="ClearIntersectingFields"/>.</summary>
    /// <param name="clear">Whether intersecting fields are cleared.</param>
    /// <returns>These options.</returns>
    public PdfRedactionOptions WithClearIntersectingFields(bool clear)
    {
        ClearIntersectingFields = clear;
        return this;
    }
}
