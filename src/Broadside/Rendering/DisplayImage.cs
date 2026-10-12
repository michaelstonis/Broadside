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

    /// <summary>Gets one row of <see cref="Samples"/>.</summary>
    /// <param name="y">The row, from 0 at the top of the image.</param>
    /// <returns>The row's <see cref="Stride"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the image.</exception>
    /// <remarks>ISO 32000-2 §8.9.3.</remarks>
    public ReadOnlySpan<byte> GetRow(int y) => _samples.GetRow(y);
}
