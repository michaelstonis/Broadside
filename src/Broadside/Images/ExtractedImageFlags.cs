namespace Broadside.Images;

/// <summary>What sets an extracted image apart from an ordinary visible image XObject.</summary>
/// <remarks>ISO 32000-2 §8.9.1, §8.9.5, §8.9.6.2, §8.9.7, §8.11.3.1 and §14.8.2.2.</remarks>
[Flags]
public enum ExtractedImageFlags
{
    /// <summary>A visible image XObject outside any <c>Artifact</c> sequence.</summary>
    None = 0,

    /// <summary>The image is an inline image (<c>BI ID EI</c>).</summary>
    /// <remarks>ISO 32000-2 §8.9.7.</remarks>
    Inline = 1 << 0,

    /// <summary>The image is an image mask (<c>ImageMask true</c>), painted with the current nonstroking colour.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2.</remarks>
    ImageMask = 1 << 1,

    /// <summary>
    /// Optional content hides the image. Hidden images are extracted only when
    /// <see cref="ImageExtractionOptions.IncludeHiddenContent"/> asks for them.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1.</remarks>
    Hidden = 1 << 2,

    /// <summary>
    /// The image is an artifact: inside a marked-content sequence tagged <c>Artifact</c>. Artifacts are extracted only when
    /// <see cref="ImageExtractionOptions.IncludeArtifacts"/> asks for them.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    Artifact = 1 << 3,
}
