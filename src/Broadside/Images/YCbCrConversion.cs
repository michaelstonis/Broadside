using Broadside.Filters.Dct;

namespace Broadside.Images;

/// <summary>
/// The YCbCr to RGB transform the managed DCT decoder applies, for a platform image decoder that hands back a JPEG's YCbCr planes
/// (a browser's <c>ImageDecoder</c>, Windows' <c>IWICPlanarBitmapSourceTransform</c>): put through this transform with the chroma
/// replicated, the planes give the managed decoder's samples.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8 Table 13 (<c>ColorTransform</c>) and Adobe Technical Note #5116 §13 (the YCC transform, CCIR 601-1), in
/// libjpeg's 16-bit fixed point (<c>jdcolor.c</c>, <c>ycc_rgb_convert</c>), so the samples equal libjpeg-turbo's.
/// </remarks>
public static class YCbCrConversion
{
    /// <summary>Converts one row of 8-bit YCbCr samples, one per pixel for each component, to interleaved RGB.</summary>
    /// <param name="y">The luminance samples, at least as many as <paramref name="rgb"/> has pixels.</param>
    /// <param name="cb">The blue-difference samples, at least as many as <paramref name="rgb"/> has pixels.</param>
    /// <param name="cr">The red-difference samples, at least as many as <paramref name="rgb"/> has pixels.</param>
    /// <param name="rgb">Three bytes per pixel.</param>
    /// <exception cref="ArgumentException">A component row is shorter than <paramref name="rgb"/> has pixels.</exception>
    /// <remarks>ISO 32000-2 §7.4.8 Table 13; Adobe Technical Note #5116 §13.</remarks>
    public static void ToRgb(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgb)
    {
        int width = rgb.Length / 3;
        if (y.Length < width || cb.Length < width || cr.Length < width)
        {
            throw new ArgumentException($"Each component row needs {width} samples.");
        }

        JpegColor.YccToRgb(y, cb, cr, rgb);
    }
}
