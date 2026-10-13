using Broadside.Content;

namespace Broadside.Text;

/// <summary>What text extraction includes and how the content runs, set fluently with <c>With*</c> methods.</summary>
/// <remarks>ISO 32000-2 §9.10, §8.11.3.1, §14.8.2 and §14.9.</remarks>
public sealed class TextExtractionOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether glyphs hidden by optional content are extracted, flagged
    /// <see cref="TextGlyphFlags.Hidden"/>. Default <see langword="false"/>: hidden text is left out, as a viewer does not show it.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1. Which groups are on is <see cref="ContentOptions.OptionalContentState"/>.</remarks>
    public bool IncludeHiddenContent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether artifacts are extracted, flagged <see cref="TextGlyphFlags.Artifact"/>: content in a
    /// marked-content sequence tagged <c>Artifact</c>, and on a tagged page read in <see cref="TextReadingOrder.Structure"/> order,
    /// content under an <c>Artifact</c> structure element or not in the structure tree at all. Default <see langword="false"/>:
    /// artifacts (running heads, page numbers, decoration) are not part of the document's real content and are left out.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2. In structure order, included artifacts under an <c>Artifact</c> element keep the element's place;
    /// the others follow the structure's content, in content order.
    /// </remarks>
    public bool IncludeArtifacts { get; set; }

    /// <summary>
    /// Gets or sets the order <see cref="PdfPage.ExtractText(TextExtractionOptions)"/> gives the glyphs in. Default
    /// <see cref="TextReadingOrder.Structure"/>: logical content order on tagged pages, content order on the others.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1. A <see cref="TextExtractor"/> run directly always gives content order.</remarks>
    public TextReadingOrder ReadingOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether, in structure order, the alternate description (<c>Alt</c>) of a structure element
    /// replaces the text of the glyphs it covers, as a whole word or phrase, flagged <see cref="TextGlyphFlags.AlternateDescription"/>.
    /// Default <see langword="false"/>: the text is what the page shows; <c>ActualText</c>, a replacement for what a person sees,
    /// always applies.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3 and §14.8.2.2.1 (a processor may substitute an element with its alternate description).</remarks>
    public bool ApplyAlternateDescriptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether, in structure order, the expansion (<c>E</c>) of a structure element that is an
    /// abbreviation or acronym replaces the text of the glyphs it covers, as a whole word, flagged
    /// <see cref="TextGlyphFlags.Expansion"/>. Default <see langword="false"/>: the abbreviation is extracted as shown.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.5.</remarks>
    public bool ApplyExpansions { get; set; }

    /// <summary>
    /// Gets or sets the options of the content run: limits, cancellation and the optional content state that decides what is hidden.
    /// <see langword="null"/>, the default, for the interpreter's defaults.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2 and §8.11.4.3.</remarks>
    public ContentOptions? ContentOptions { get; set; }

    /// <summary>Sets <see cref="IncludeHiddenContent"/>.</summary>
    /// <param name="include">Whether hidden glyphs are extracted.</param>
    /// <returns>These options.</returns>
    public TextExtractionOptions WithHiddenContent(bool include = true)
    {
        IncludeHiddenContent = include;
        return this;
    }

    /// <summary>Sets <see cref="IncludeArtifacts"/>.</summary>
    /// <param name="include">Whether artifacts are extracted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public TextExtractionOptions WithArtifacts(bool include = true)
    {
        IncludeArtifacts = include;
        return this;
    }

    /// <summary>Sets <see cref="ReadingOrder"/>.</summary>
    /// <param name="order">The order of the extracted glyphs.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.5.1.</remarks>
    public TextExtractionOptions WithReadingOrder(TextReadingOrder order)
    {
        ReadingOrder = order;
        return this;
    }

    /// <summary>Sets <see cref="ApplyAlternateDescriptions"/>.</summary>
    /// <param name="apply">Whether structure elements' alternate descriptions replace their text.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    public TextExtractionOptions WithAlternateDescriptions(bool apply = true)
    {
        ApplyAlternateDescriptions = apply;
        return this;
    }

    /// <summary>Sets <see cref="ApplyExpansions"/>.</summary>
    /// <param name="apply">Whether structure elements' expansions replace their text.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.9.5.</remarks>
    public TextExtractionOptions WithExpansions(bool apply = true)
    {
        ApplyExpansions = apply;
        return this;
    }

    /// <summary>Sets <see cref="ContentOptions"/>.</summary>
    /// <param name="options">The content run's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>These options.</returns>
    public TextExtractionOptions WithContentOptions(ContentOptions? options)
    {
        ContentOptions = options;
        return this;
    }
}
