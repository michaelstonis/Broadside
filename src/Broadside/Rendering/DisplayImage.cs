using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// An image interned in a display list: its samples decoded once, with the colour space and Decode mapping that give them meaning,
/// shared by every paint of the same image XObject in the list. Never converted: a backend converts the samples to its device model.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5 (image dictionaries: Width, Height, ColorSpace, BitsPerComponent, Decode, Interpolate), §8.9.7 (inline
/// images, decoded once per paint) and §8.9.3 (the sample layout: rows top to bottom, samples left to right, components interleaved,
/// each row starting on a byte boundary, 16-bit samples big-endian). The image is painted into the unit square of user space
/// (§8.9.4): image space maps to it through <c>[1/Width 0 0 −1/Height 0 1]</c>, so the first row lands at the top, then through
/// the transform set before the paint.
/// </para>
/// <para>
/// ADR 0006: fonts, images and colours are resolved before an operation enters the list, so a backend never reads the file. One
/// instance per distinct image XObject per list: a backend may key a per-surface cache (a converted bitmap, a native image) on the
/// instance. Immutable; safe to read from any number of threads.
/// </para>
/// <para>
/// Convert with <c>list.Document.GetColorConverter(image.ColorSpace, conversion with { Target = … }, defaultColorSpaces)</c>, the
/// raw samples first mapped through <see cref="DecodeMap"/> (for an Indexed image, <see cref="ImageDecodeMap.MapToIndices"/>).
/// </para>
/// </remarks>
public sealed class DisplayImage
{
    private readonly DecodedImage _samples;
    private readonly double[] _colorKey = [];
    private readonly float[] _matte = [];
    private readonly byte[] _colorTable = [];

    internal DisplayImage(DecodedImage samples, PdfColorSpace colorSpace, ImageDecodeMap decodeMap, bool interpolate)
    {
        _samples = samples;
        ColorSpace = colorSpace;
        DecodeMap = decodeMap;
        Interpolate = interpolate;
    }

    /// <summary>Gets the width in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Width), or what the codec reported (§7.4.9).</remarks>
    public int Width => _samples.Width;

    /// <summary>Gets the height in samples.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (Height), or what the codec reported (§7.4.9).</remarks>
    public int Height => _samples.Height;

    /// <summary>Gets the number of colour components per sample: the colour space's (1 for an Indexed image).</summary>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public int Components => _samples.Components;

    /// <summary>Gets the logical bits per component n: raw values run from 0 to 2^n − 1.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (BitsPerComponent).</remarks>
    public int BitsPerComponent => _samples.BitsPerComponent;

    /// <summary>Gets the bits each component occupies in <see cref="Samples"/>: 1, 2, 4, 8 or 16.</summary>
    /// <remarks>Equal to <see cref="BitsPerComponent"/> except for depths other than those five, stored at the next wider one, values unchanged.</remarks>
    public int StorageBits => _samples.StorageBits;

    /// <summary>Gets the number of bytes per row of <see cref="Samples"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.3: each row starts on a byte boundary.</remarks>
    public int Stride => _samples.Stride;

    /// <summary>Gets the raw samples, <see cref="Stride"/> × <see cref="Height"/> bytes in the §8.9.3 layout.</summary>
    /// <remarks>ISO 32000-2 §8.9.3. Rows the data did not reach are zero.</remarks>
    public ReadOnlySpan<byte> Samples => _samples.Samples;

    /// <summary>Gets the colour space of the samples, as resolved before default colour spaces apply.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.5.1, Table 87 (ColorSpace); for a JPEG 2000 image without one, the codestream's (§7.4.9). Never a Pattern
    /// space; image masks (§8.9.6.2) are not interned here.
    /// </remarks>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Gets the mapping from raw sample values to colour components, the codec's polarity folded in.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.2, Table 88 (Decode).</remarks>
    public ImageDecodeMap DecodeMap { get; }

    /// <summary>Gets a value indicating whether the image asks to be interpolated (smoothed) when it is enlarged.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.3 and Table 87 (Interpolate; <c>I</c> for an inline image, Table 91).</remarks>
    public bool Interpolate { get; }

    /// <summary>
    /// Gets the image's soft mask: a one-component image (DeviceGray, its own Decode mapping) whose values, 0 to 1, are the opacity
    /// of the image at each point; <see langword="null"/> when the image has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.2 (a soft-mask image, <c>SMask</c>, Table 143) or §7.4.9 and §8.9.5.1 Table 87 (<c>SMaskInData</c>:
    /// the opacity channel of JPEG 2000 data). It maps onto the unit square like the image, at its own width and height, which may
    /// differ from the image's; a backend resamples it to the device, never to the image's size. It overrides any soft mask in the
    /// graphics state for this image (§11.6.4.3).
    /// </remarks>
    public DisplayImage? SoftMask { get; internal init; }

    /// <summary>Gets the image's explicit mask: where it paints, the image shows; elsewhere it leaves the backdrop; <see langword="null"/> when the image has none.</summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.3 (<c>Mask</c> naming an image mask): the mask maps onto the unit square like the image, at its own
    /// resolution.
    /// </remarks>
    public DisplayImageMask? Mask { get; internal init; }

    /// <summary>
    /// Gets the colour key ranges: a minimum and maximum raw sample value per component; a sample whose every component is in its
    /// range is not painted. Empty when the image has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.4 (<c>Mask</c> as an array): compared with the raw samples, before <see cref="DecodeMap"/>; for an Indexed
    /// image the values are indices. <see cref="ImageRows.ColorKey"/> computes the coverage of a row.
    /// </remarks>
    public ReadOnlySpan<double> ColorKey => _colorKey;

    /// <summary>
    /// Gets the colour the samples were pre-blended with, one value per component of <see cref="ColorSpace"/> (of its base for an
    /// Indexed space), to be removed with the <see cref="SoftMask"/>'s opacity before conversion; empty when there is none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.2, Table 144 (<c>Matte</c>): c = m + (c′ − m) / α (see <see cref="ImageRows.Unpremultiply"/>); the soft
    /// mask then has the image's size. JPEG 2000 opacity premultiplied into the colour (<c>SMaskInData</c> 2, §8.9.5.1 Table 87) is
    /// a matte of zeros.
    /// </remarks>
    public ReadOnlySpan<float> Matte => _matte;

    /// <summary>
    /// Gets the colour table of an Indexed image as stored (m bytes per colour, m the base space's component count), read when the
    /// list is built so that a backend never reads the file for it; empty for any other image.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.6.3: each byte maps onto its base component's range. A backend needs it where it works in the base space,
    /// such as removing a <see cref="Matte"/> from the table's colours (§11.6.5.2).
    /// </remarks>
    public ReadOnlySpan<byte> ColorTable => _colorTable;

    /// <summary>Gets the colour table's backing array.</summary>
    internal byte[] ColorTableBytes
    {
        get => _colorTable;
        init => _colorTable = value;
    }

    /// <summary>Gets the colour key ranges' backing array.</summary>
    internal double[] ColorKeyRanges
    {
        get => _colorKey;
        init => _colorKey = value;
    }

    /// <summary>Gets the matte's backing array.</summary>
    internal float[] MatteColor
    {
        get => _matte;
        init => _matte = value;
    }

    /// <summary>Gets one row of <see cref="Samples"/>.</summary>
    /// <param name="y">The row, from 0 at the top of the image.</param>
    /// <returns>The row's <see cref="Stride"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the image.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> GetRow(int y) => _samples.GetRow(y);

    /// <summary>
    /// Gets the bytes this image keeps: its samples (with a codec's opacity channel), its masks unless the soft mask is that channel,
    /// its colour key, matte and colour table; what a render cache charges for it (issue #103).
    /// </summary>
    internal long RetainedBytes =>
        _samples.RetainedBytes
        + (SoftMask is { } soft && !ReferenceEquals(soft._samples, _samples.Alpha) ? soft.RetainedBytes : 0L)
        + (Mask?.RetainedBytes ?? 0L)
        + (_colorKey.Length * (long)sizeof(double))
        + (_matte.Length * (long)sizeof(float))
        + _colorTable.Length;
    /// <summary>Returns the pooled samples (and the masks'), for an image no list will own: the build that decoded it failed.</summary>
    internal void Release()
    {
        _samples.Dispose();
        Mask?.Release();
        SoftMask?.Release();
    }
}
