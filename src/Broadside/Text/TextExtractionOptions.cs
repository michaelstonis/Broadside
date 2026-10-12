using Broadside.Content;

namespace Broadside.Text;

/// <summary>What text extraction includes and how the content runs, set fluently with <c>With*</c> methods.</summary>
/// <remarks>ISO 32000-2 §9.10 and §8.11.3.1.</remarks>
public sealed class TextExtractionOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether glyphs hidden by optional content are extracted, flagged
    /// <see cref="TextGlyphFlags.Hidden"/>. Default <see langword="false"/>: hidden text is left out, as a viewer does not show it.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1. Which groups are on is <see cref="ContentOptions.OptionalContentState"/>.</remarks>
    public bool IncludeHiddenContent { get; set; }

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

    /// <summary>Sets <see cref="ContentOptions"/>.</summary>
    /// <param name="options">The content run's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>These options.</returns>
    public TextExtractionOptions WithContentOptions(ContentOptions? options)
    {
        ContentOptions = options;
        return this;
    }
}
