using Broadside.Images;

namespace Broadside.Filters;

/// <summary>An image codestream's header: its geometry as the codestream declares it, and what else it says about the samples.</summary>
/// <param name="Geometry">
/// The size, components and precision the codestream declares: the number of samples in each row and of rows, the number of color
/// components (without an opacity channel), and the precision of the components (the greatest when they differ).
/// </param>
/// <remarks>ISO 32000-2 §7.4.9 and §8.9.2. Returned by <see cref="IImageFilter.TryReadHeader"/>. Also §8.9.5.1.</remarks>
public readonly record struct ImageHeader(ImageGeometry Geometry)
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
    public int MaxReduction
    {
        get => _maxReduction;
        init => _maxReduction = (byte)Math.Clamp(value, 0, ImageGeometry.MaxReduction);
    }

    // A byte, so the header is no larger than it was: it is boxed once per image decode.
    private readonly byte _maxReduction;

    /// <summary>Gets the colour model the codestream declares, or <see cref="ImageColorModel.Unknown"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9.</remarks>
    public ImageColorModel ColorModel { get; init; }

    /// <summary>
    /// Gets the ICC profile the codestream declares for its colours (what <see cref="DecodedImage.IccProfile"/> of every decode will
    /// hold), or empty.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.4.9: a JPEG 2000 image without a <c>ColorSpace</c> entry takes its colour space from the codestream, so a
    /// display list can keep it encoded and decode it at the resolution it is drawn at only when the header already says which.
    /// </remarks>
    public ReadOnlyMemory<byte> IccProfile { get; init; }
}
