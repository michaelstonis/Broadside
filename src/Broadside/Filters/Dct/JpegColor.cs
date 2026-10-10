namespace Broadside.Filters.Dct;

/// <summary>
/// The colour transforms a DCT decoder applies after the inverse DCT: YCbCr to RGB and YCCK to CMYK, in libjpeg's 16-bit fixed
/// point so the samples equal libjpeg-turbo's.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.4.8, Table 13 (<c>ColorTransform</c>); Adobe Technical Note #5116 §13 (the YCC and YCCK transforms, CCIR 601-1).
/// The tables are those of libjpeg-turbo <c>src/jdcolor.c</c> (<c>build_ycc_rgb_table</c>, <c>ycc_rgb_convert</c>,
/// <c>ycck_cmyk_convert</c>; commit 43ea8097), the Independent JPEG Group's software (THIRD-PARTY-NOTICES.txt).
/// </remarks>
internal static class JpegColor
{
    private const int ScaleBits = 16;
    private const int ClampOffset = 384;

    private static readonly int[] CrToRed = new int[256];
    private static readonly int[] CbToBlue = new int[256];
    private static readonly int[] CrToGreen = new int[256];
    private static readonly int[] CbToGreen = new int[256];

    /// <summary>Clamps values from -384 to 639 to 0-255 (index = value + 384).</summary>
    private static readonly byte[] Clamp = new byte[1024];

#pragma warning disable CA1810 // The tables are filled in one loop; a static constructor is the clearest way.
    static JpegColor()
#pragma warning restore CA1810
    {
        const int half = 1 << (ScaleBits - 1);
        for (int i = 0; i < 256; i++)
        {
            int x = i - 128;
            CrToRed[i] = ((91881 * x) + half) >> ScaleBits; // FIX(1.40200)
            CbToBlue[i] = ((116130 * x) + half) >> ScaleBits; // FIX(1.77200)
            CrToGreen[i] = -46802 * x; // -FIX(0.71414)
            CbToGreen[i] = (-22554 * x) + half; // -FIX(0.34414), with the rounding term
        }

        for (int i = 0; i < Clamp.Length; i++)
        {
            Clamp[i] = (byte)Math.Clamp(i - ClampOffset, 0, 255);
        }
    }

    /// <summary>Converts one row of YCbCr samples to interleaved RGB.</summary>
    /// <param name="y">The luminance samples.</param>
    /// <param name="cb">The blue-difference samples.</param>
    /// <param name="cr">The red-difference samples.</param>
    /// <param name="rgb">Three bytes per sample.</param>
    public static void YccToRgb(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgb)
    {
        int width = rgb.Length / 3;
        y = y[..width];
        cb = cb[..width];
        cr = cr[..width];
        byte[] clamp = Clamp;
        for (int x = 0, o = 0; x < width; x++, o += 3)
        {
            int luma = y[x] + ClampOffset;
            int blue = cb[x];
            int red = cr[x];
            rgb[o] = clamp[luma + CrToRed[red]];
            rgb[o + 1] = clamp[luma + ((CbToGreen[blue] + CrToGreen[red]) >> ScaleBits)];
            rgb[o + 2] = clamp[luma + CbToBlue[blue]];
        }
    }

    /// <summary>Converts one row of YCCK samples to interleaved CMYK: the YCbCr transform, each result subtracted from 255, K unchanged.</summary>
    /// <param name="y">The luminance samples.</param>
    /// <param name="cb">The blue-difference samples.</param>
    /// <param name="cr">The red-difference samples.</param>
    /// <param name="k">The black samples.</param>
    /// <param name="cmyk">Four bytes per sample.</param>
    /// <remarks>Adobe Technical Note #5116 §13.2.</remarks>
    public static void YcckToCmyk(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, ReadOnlySpan<byte> k, Span<byte> cmyk)
    {
        int width = cmyk.Length / 4;
        y = y[..width];
        cb = cb[..width];
        cr = cr[..width];
        k = k[..width];
        byte[] clamp = Clamp;
        for (int x = 0, o = 0; x < width; x++, o += 4)
        {
            int inverse = 255 - y[x] + ClampOffset;
            int blue = cb[x];
            int red = cr[x];
            cmyk[o] = clamp[inverse - CrToRed[red]];
            cmyk[o + 1] = clamp[inverse - ((CbToGreen[blue] + CrToGreen[red]) >> ScaleBits)];
            cmyk[o + 2] = clamp[inverse - CbToBlue[blue]];
            cmyk[o + 3] = k[x];
        }
    }
}
