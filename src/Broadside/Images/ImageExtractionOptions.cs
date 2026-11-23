using Broadside.Content;

namespace Broadside.Images;

/// <summary>What image extraction includes and how the content runs, set fluently with <c>With*</c> methods.</summary>
/// <remarks>ISO 32000-2 §8.9.1, §8.9.5, §8.9.6.2, §8.9.7, §8.11.3.1 and §14.8.2.2.</remarks>
public sealed class ImageExtractionOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether images hidden by optional content are extracted, flagged
    /// <see cref="ExtractedImageFlags.Hidden"/>. Default <see langword="false"/>: hidden images are left out, as a viewer does not show them.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1. Which groups are on is <see cref="ContentOptions.OptionalContentState"/>.</remarks>
    public bool IncludeHiddenContent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether artifacts are extracted, flagged <see cref="ExtractedImageFlags.Artifact"/>: images in a
    /// marked-content sequence tagged <c>Artifact</c>. Default <see langword="false"/>: artifacts (watermarks, background decoration)
    /// are not part of the document's real content and are left out.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public bool IncludeArtifacts { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether image masks (<c>ImageMask true</c>, painted with the current nonstroking colour) are
    /// extracted, flagged <see cref="ExtractedImageFlags.ImageMask"/>. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.9.6.2.</remarks>
    public bool IncludeImageMasks { get; set; } = true;

    /// <summary>
    /// Gets or sets the options of the content run: limits, cancellation and the optional content state that decides what is hidden.
    /// <see langword="null"/>, the default, for the interpreter's defaults.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2 and §8.11.4.3.</remarks>
    public ContentOptions? ContentOptions { get; set; }

    /// <summary>Sets <see cref="IncludeHiddenContent"/>.</summary>
    /// <param name="include">Whether hidden images are extracted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §8.11.3.1.</remarks>
    public ImageExtractionOptions WithHiddenContent(bool include = true)
    {
        IncludeHiddenContent = include;
        return this;
    }

    /// <summary>Sets <see cref="IncludeArtifacts"/>.</summary>
    /// <param name="include">Whether artifacts are extracted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public ImageExtractionOptions WithArtifacts(bool include = true)
    {
        IncludeArtifacts = include;
        return this;
    }

    /// <summary>Sets <see cref="IncludeImageMasks"/>.</summary>
    /// <param name="include">Whether image masks are extracted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §8.9.6.2.</remarks>
    public ImageExtractionOptions WithImageMasks(bool include = true)
    {
        IncludeImageMasks = include;
        return this;
    }

    /// <summary>Sets <see cref="ContentOptions"/>.</summary>
    /// <param name="options">The content run's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.8.2.</remarks>
    public ImageExtractionOptions WithContentOptions(ContentOptions? options)
    {
        ContentOptions = options;
        return this;
    }
}
