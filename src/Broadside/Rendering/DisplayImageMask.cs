using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// An image mask interned in a display list: one bit per sample saying where something is painted, at the mask's own resolution.
/// It is either a stencil mask, painted with the current fill colour (<see cref="IRenderingBackend.FillImageMask"/>), or the
/// explicit mask of an image (<see cref="DisplayImage.Mask"/>), which shows the image where it paints.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.6.2 (stencil masking) and §8.9.6.3 (explicit masking). Samples are 1 bit, in the §8.9.3 layout (rows top to
/// bottom, most significant bit first, each row starting on a byte boundary). With the default Decode array <c>[0 1]</c> a sample
/// 0 paints; with <c>[1 0]</c> (<see cref="IsInverted"/>) a sample 1 paints. Use <see cref="ImageRows.Stencil"/> to turn a row into
/// coverage. Rows the data did not reach paint nothing.
/// </para>
/// <para>
/// The mask maps onto the unit square of user space like any image (§8.9.4), whatever its size: an explicit mask whose resolution
/// differs from its image's is resampled to the device when drawn, never to the image's size. One instance per distinct mask
/// stream per list; immutable, safe to read from any number of threads.
/// </para>
/// </remarks>
public sealed class DisplayImageMask
{
    private readonly DecodedImage _samples;

    internal DisplayImageMask(DecodedImage samples, bool inverted, bool interpolate)
    {
        _samples = samples;
        IsInverted = inverted;
        Interpolate = interpolate;
    }

    /// <summary>Gets the width in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Width).</remarks>
    public int Width => _samples.Width;

    /// <summary>Gets the height in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Height).</remarks>
    public int Height => _samples.Height;

    /// <summary>Gets the number of bytes per row of <see cref="Samples"/>: ⌈<see cref="Width"/> / 8⌉.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: each row starts on a byte boundary.</remarks>
    public int Stride => _samples.Stride;

    /// <summary>Gets the packed samples, <see cref="Stride"/> × <see cref="Height"/> bytes, one bit per sample.</summary>
    /// <remarks>ISO 32000-2 §8.9.3 and §8.9.6.2.</remarks>
    public ReadOnlySpan<byte> Samples => _samples.Samples;

    /// <summary>Gets a value indicating whether a sample 1 paints (the Decode array is <c>[1 0]</c>, the codec's polarity folded in); otherwise a sample 0 paints.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2 and §8.9.5.2, Table 88.</remarks>
    public bool IsInverted { get; }

    /// <summary>Gets a value indicating whether the mask asks to be interpolated (smoothed) when it is enlarged.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3 and Table 87 (Interpolate).</remarks>
    public bool Interpolate { get; }

    /// <summary>Gets one row of <see cref="Samples"/>.</summary>
    /// <param name="y">The row, from 0 at the top of the mask.</param>
    /// <returns>The row's <see cref="Stride"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the mask.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> GetRow(int y) => _samples.GetRow(y);

    /// <summary>
    /// Returns the reduction, as a power of two, at which a viewer may resample the mask when it is drawn under
    /// <paramref name="transform"/>: the largest that still leaves <paramref name="samplesPerPixel"/> samples per device pixel along
    /// its most enlarged direction; each reduced sample is then the share of the samples it covers that paint (8-bit coverage).
    /// </summary>
    /// <param name="transform">The transform from the unit square (user space, §8.9.4) to device pixels.</param>
    /// <param name="samplesPerPixel">The fewest reduced samples per device pixel, at least 1 (see <see cref="DisplayImage.GetReduction"/>).</param>
    /// <returns>The reduction: 0 for a mask of fewer than 65,536 samples, at most 8 (1/256).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="samplesPerPixel"/> is not a finite number of at least 1.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.2 and §8.9.6.3 (issue #362): coverage averages linearly, so reducing before painting equals the box filter
    /// of the rasterizer over the mask's own samples, to within rounding. For displaying only.
    /// </remarks>
    public int GetReduction(in Matrix transform, double samplesPerPixel)
    {
        if (!double.IsFinite(samplesPerPixel) || samplesPerPixel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerPixel), samplesPerPixel, "The samples per pixel must be a finite number of at least 1.");
        }

        return (long)Width * Height < DisplayImage.MinimumReducedPixels ? 0 : DisplayImage.Reduction(Width, Height, transform, samplesPerPixel, 8);
    }

    /// <summary>Gets the bytes this mask keeps: its sample buffer; what a render cache charges for it (issue #103).</summary>
    internal long RetainedBytes => _samples.RetainedBytes;
    /// <summary>Returns the pooled samples, for a mask no list will own: the build that decoded it failed.</summary>
    internal void Release() => _samples.Dispose();
}
