using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>
/// What a platform image decoder is told about an image before it is offered the data: the codec, the frame's header facts, the
/// segments the data carries, and the reduction the backend will draw it at.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8 (DCTDecode; Table 13 <c>ColorTransform</c>), §8.9.5.1 Table 87 (Width, Height, BitsPerComponent) and ITU-T
/// T.81 Annex B (the frame header). Every request passed the shared rule of <see cref="IPlatformImageDecoder"/>: 8-bit samples of 1 or
/// 3 components, a frame of the dictionary's size with its height in the header, and three components transformed from YCbCr exactly
/// when a JFIF decoder transforms them.
/// </remarks>
public readonly record struct PlatformImageRequest
{
    /// <summary>Gets the image codec: <c>DCTDecode</c>.</summary>
    /// <remarks>ISO 32000-2 §7.4.8. JPEG 2000 (§7.4.9) is never offered yet.</remarks>
    public CosName Filter { get; init; }

    /// <summary>Gets the image's width in samples at its own resolution.</summary>
    /// <remarks>ITU-T T.81 B.2.2 (X), equal to the dictionary's Width (ISO 32000-2 Table 87).</remarks>
    public int Width { get; init; }

    /// <summary>Gets the image's height in samples at its own resolution.</summary>
    /// <remarks>ITU-T T.81 B.2.2 (Y), equal to the dictionary's Height (ISO 32000-2 Table 87); never 0 (a DNL segment is never offered).</remarks>
    public int Height { get; init; }

    /// <summary>Gets the number of components per sample, 1 (gray) or 3 (RGB, from YCbCr).</summary>
    /// <remarks>ITU-T T.81 B.2.2 (Nf). Four-component (CMYK, YCCK) data is never offered.</remarks>
    public int Components { get; init; }

    /// <summary>Gets the bits per component, always 8.</summary>
    /// <remarks>ITU-T T.81 B.2.2 (P); ISO 32000-2 §7.4.8 and Table 87.</remarks>
    public int BitsPerComponent { get; init; }

    /// <summary>Gets a value indicating whether the frame is progressive (SOF2, SOF10).</summary>
    /// <remarks>ITU-T T.81 Annex G; ISO 32000-2 §7.4.8 (PDF 1.3).</remarks>
    public bool Progressive { get; init; }

    /// <summary>Gets a value indicating whether the frame is arithmetic-coded (SOF9, SOF10); offered only to a decoder with <see cref="PlatformImageFeatures.ArithmeticCoding"/>.</summary>
    /// <remarks>ITU-T T.81 Annex D.</remarks>
    public bool ArithmeticCoding { get; init; }

    /// <summary>Gets a value indicating whether the three components are YCbCr, to be transformed to RGB.</summary>
    /// <remarks>ISO 32000-2 §7.4.8 Table 13 and Adobe Technical Note #5116 §18 (APP14). Always true for three components, false for one.</remarks>
    public bool ColorTransform { get; init; }

    /// <summary>
    /// Gets a value indicating whether a component is sampled at another rate than the first (4:2:0, 4:2:2): a decoder that upsamples
    /// chroma by interpolation rather than replication (libjpeg's "fancy" upsampling) differs from the managed decoder at chroma edges.
    /// </summary>
    /// <remarks>ITU-T T.81 A.1.1 and B.2.2 (Hi, Vi); always false for one component.</remarks>
    public bool ChromaSubsampled { get; init; }

    /// <summary>Gets a value indicating whether the data has an APP1 Exif segment (zero-filled for a decoder that applies orientation).</summary>
    public bool HasExif { get; init; }

    /// <summary>Gets a value indicating whether the data has APP2 <c>ICC_PROFILE</c> segments (zero-filled for a decoder that converts profiles regardless).</summary>
    /// <remarks>ICC.1 Annex B.4. The image dictionary's <c>ColorSpace</c> decides what the samples mean (ISO 32000-2 §8.9.5.1).</remarks>
    public bool HasIccProfile { get; init; }

    /// <summary>Gets the reduction wanted, as a power of two: 0 for the image's own resolution, 1 for half, up to 3 for an eighth.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.4: the backend draws the image so small that this reduction still leaves its samples per device pixel. A
    /// decoder may deliver a finer one (a smaller reduction) but never a coarser one.
    /// </remarks>
    public int Reduction { get; init; }

    /// <summary>Gets the width at <see cref="Reduction"/>: ⌈<see cref="Width"/> / 2^<see cref="Reduction"/>⌉, as libjpeg's DCT scaling delivers.</summary>
    public int ReducedWidth => ImageGeometry.Reduce(Width, Reduction);

    /// <summary>Gets the height at <see cref="Reduction"/>: ⌈<see cref="Height"/> / 2^<see cref="Reduction"/>⌉.</summary>
    public int ReducedHeight => ImageGeometry.Reduce(Height, Reduction);

    /// <summary>Creates the samples a decoder fills: ⌈W / 2^r⌉ × ⌈H / 2^r⌉ samples of <see cref="Components"/> 8-bit components, zeroed.</summary>
    /// <param name="reduction">The reduction delivered, from 0 to <see cref="Reduction"/>.</param>
    /// <returns>The builder; <see cref="DecodedImageBuilder.Build"/> turns it into the result of <see cref="IPlatformImageDecoder.DecodeAsync"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reduction"/> is negative or coarser than <see cref="Reduction"/>.</exception>
    /// <remarks>ISO 32000-2 §8.9.3: rows top to bottom, components interleaved; for three components the samples are RGB, after the YCbCr transform.</remarks>
    public DecodedImageBuilder CreateImage(int reduction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reduction);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reduction, Reduction);
        var image = new DecodedImageBuilder(ImageGeometry.Reduce(Width, reduction), ImageGeometry.Reduce(Height, reduction), Components, 8)
        {
            Reduction = reduction,
            ColorModel = Components == 1 ? ImageColorModel.Gray : ImageColorModel.Rgb,
            ColorTransformApplied = ColorTransform,
        };
        return image;
    }
}
