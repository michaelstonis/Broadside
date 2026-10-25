namespace Broadside.Images;

/// <summary>How close a platform image decoder's samples are to the managed codec's.</summary>
/// <remarks>ISO 32000-2 §7.4.8. See <see cref="IPlatformImageDecoder.Fidelity"/> for the bars.</remarks>
public enum PlatformImageFidelity
{
    /// <summary>Bit-identical to the managed codec (for example, a browser's YCbCr planes put through the managed colour transform).</summary>
    Exact = 0,

    /// <summary>Within the measured tolerance of the managed codec (another upsampling of chroma, another reduced IDCT).</summary>
    Tolerance = 1,
}
