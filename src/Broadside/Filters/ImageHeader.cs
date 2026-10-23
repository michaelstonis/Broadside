using Broadside.Images;

namespace Broadside.Filters;

/// <summary>An image codestream's header: its size, components and precision as the codestream declares them.</summary>
/// <param name="Width">The number of samples in each row.</param>
/// <param name="Height">The number of rows.</param>
/// <param name="Components">The number of colour components (without an opacity channel).</param>
/// <param name="BitsPerComponent">The precision of the components; the greatest when they differ.</param>
/// <remarks>ISO 32000-2 §7.4.9 and §8.9.2. Returned by <see cref="IImageFilter.TryReadHeader"/>.</remarks>
public readonly record struct ImageHeader(int Width, int Height, int Components, int BitsPerComponent)
{
    /// <summary>Gets a value indicating whether the codestream also has an opacity channel.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 (<c>SMaskInData</c>).</remarks>
    public bool HasAlpha { get; init; }

    /// <summary>
    /// Gets the largest reduction, as a power of two, that <see cref="IImageFilter.DecodeImage"/> delivers when asked for it through
    /// <see cref="ImageFilterContext.Reduction"/>: 0 (the default) for a codec that always decodes at full resolution, 3 for one that
    /// decodes at 1/2, 1/4 and 1/8.
    /// </summary>
    /// <remarks>
    /// A display list keeps the encoded data of an image whose codec reduces, and decodes it when it is drawn, at the resolution it
    /// is drawn at (see <c>DisplayImage.GetSamplesAt</c>); other images are decoded when the list is built.
    /// </remarks>
    public int MaxReduction { get; init; }

    /// <summary>Gets the colour model the codestream declares, or <see cref="ImageColorModel.Unknown"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9.</remarks>
    public ImageColorModel ColorModel { get; init; }
}
