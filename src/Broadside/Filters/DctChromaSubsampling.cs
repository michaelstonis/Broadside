namespace Broadside.Filters;

/// <summary>How much a <see cref="DctEncoder"/> reduces the resolution of chrominance relative to luminance.</summary>
/// <remarks>
/// ITU-T T.81 §A.1.1 (sampling factors H and V) and §A.2 (MCUs). Applies only where the encoder converts colour: RGB to YCbCr and
/// CMYK to YCCK, whose two chrominance components are sampled at 1 x 1 while luminance (and K) take the factors below. Gray, and RGB
/// or CMYK stored without a colour transform, are never subsampled.
/// </remarks>
public enum DctChromaSubsampling
{
    /// <summary>4:4:4: chrominance at full resolution (luminance sampling factors 1 x 1).</summary>
    None = 0,

    /// <summary>4:2:2: chrominance at half the horizontal resolution (luminance sampling factors 2 x 1).</summary>
    Horizontal = 1,

    /// <summary>4:2:0: chrominance at half the resolution in both directions (luminance sampling factors 2 x 2), libjpeg's default.</summary>
    HorizontalAndVertical = 2,
}
