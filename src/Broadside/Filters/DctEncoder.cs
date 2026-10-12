using System.Buffers;
using Broadside.Filters.Dct;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// The <c>DCTDecode</c> encoder: a managed baseline JPEG encoder for 8-bit gray, RGB and CMYK image samples, with a quality setting
/// and chroma subsampling chosen when it is constructed. Encoding is lossy: <see cref="DctDecodeFilter"/> decodes the output to
/// samples close to the input, not equal to it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode, Table 13 <c>ColorTransform</c>); ITU-T T.81 (ISO/IEC 10918-1) baseline sequential process:
/// Annex A (level shift, FDCT, quantization), Annex B (interchange format), Annex C and §F.1.2 (Huffman encoding), Annex K
/// (the typical quantization tables, scaled by the IJG quality formula, and the typical Huffman tables); Adobe Technical Note #5116
/// (APP14). Every operation is integer arithmetic ported from libjpeg (the islow FDCT, the YCbCr tables, libjpeg's edge padding and
/// downsampling), so the output equals libjpeg-turbo's <c>cjpeg -dct int</c> (or TurboJPEG with the same settings) byte for byte,
/// and the same input encodes to the same bytes on every platform.
/// </para>
/// <para>
/// The stream dictionary describes the samples (§8.9.5, Table 87): <c>Width</c> and <c>Height</c> (1 to 65535), and the data holds
/// width x height pixels of 1, 3 or 4 interleaved 8-bit components, top to bottom with no row padding. <c>BitsPerComponent</c>, when
/// present, must be 8; a device <c>ColorSpace</c> name, when present, must have that many components; image masks are refused.
/// The component count decides the layout, and the <c>ColorTransform</c> parameter of the stream's <c>DecodeParms</c>
/// (Table 13) decides the transform the decoder undoes, so the encoder writes data that decodes with those same parameters:
/// </para>
/// <list type="bullet">
/// <item>1 component: gray, JFIF.</item>
/// <item>3 components, <c>ColorTransform</c> absent or 1: YCbCr (Rec. 601, full range), chroma subsampled per
/// <see cref="ChromaSubsampling"/>, JFIF, components 1 to 3.</item>
/// <item>3 components, <c>ColorTransform</c> 0: RGB untransformed, Adobe APP14 transform 0, components R, G, B.</item>
/// <item>4 components, <c>ColorTransform</c> absent or 0: CMYK untransformed, Adobe APP14 transform 0, components C, M, Y, K. The
/// samples are written as given, not inverted: a reader that follows Table 13 and the image's <c>Decode</c> array sees them
/// unchanged.</item>
/// <item>4 components, <c>ColorTransform</c> 1: YCCK (the complements of C, M, Y as RGB to YCbCr, K kept), chroma subsampled,
/// Adobe APP14 transform 2.</item>
/// </list>
/// <para>
/// The output is one interleaved SOF0 scan, Huffman-coded with the Annex K tables, without restart intervals. The
/// <see cref="StreamEncoderContext.CompressionLevel"/> is ignored: <see cref="Quality"/> trades size for fidelity. At quality 90
/// the test pictures decode through <see cref="DctDecodeFilter"/> above 38 dB PSNR in every layout (40 dB and more for RGB at
/// any subsampling, gray and untransformed CMYK).
/// </para>
/// <para>
/// Thread safety: immutable and stateless; one instance serves every thread. An encode allocates nothing on the managed heap
/// beyond pooled buffers for one row of MCUs, never per block or row.
/// </para>
/// </remarks>
public sealed class DctEncoder : IStreamEncoder
{
    /// <summary>The quality the parameterless constructor uses: 75, libjpeg's default.</summary>
    /// <remarks>ITU-T T.81 Annex K.1; the IJG quality scaling.</remarks>
    public const int DefaultQuality = 75;

    private readonly JpegQuantization _luminance;
    private readonly JpegQuantization _chrominance;

    /// <summary>Initializes a new instance of the <see cref="DctEncoder"/> class at quality 75 with 4:2:0 chroma subsampling, libjpeg's defaults.</summary>
    /// <remarks>ITU-T T.81 Annex K.1.</remarks>
    public DctEncoder()
        : this(DefaultQuality, DctChromaSubsampling.HorizontalAndVertical)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DctEncoder"/> class at a quality with 4:2:0 chroma subsampling.</summary>
    /// <param name="quality">The quality, 1 (smallest) to 100 (largest, quantization values of 1).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is outside 1 to 100.</exception>
    /// <remarks>ITU-T T.81 Annex K.1.</remarks>
    public DctEncoder(int quality)
        : this(quality, DctChromaSubsampling.HorizontalAndVertical)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DctEncoder"/> class.</summary>
    /// <param name="quality">
    /// The quality, 1 to 100: the Annex K quantization tables scaled by 5000 / quality percent below 50 and by 200 - 2 x quality
    /// percent from 50, each value rounded and kept within 1 to 255 (libjpeg's <c>jpeg_set_quality</c> with baseline tables).
    /// </param>
    /// <param name="chromaSubsampling">How much chrominance is subsampled when colour is transformed.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quality"/> is outside 1 to 100, or <paramref name="chromaSubsampling"/> is not a defined value.
    /// </exception>
    /// <remarks>ITU-T T.81 Annex K.1 (Tables K.1 and K.2), §A.1.1 (sampling factors).</remarks>
    public DctEncoder(int quality, DctChromaSubsampling chromaSubsampling)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        if (!Enum.IsDefined(chromaSubsampling))
        {
            throw new ArgumentOutOfRangeException(nameof(chromaSubsampling), chromaSubsampling, "Not a defined chroma subsampling.");
        }

        Quality = quality;
        ChromaSubsampling = chromaSubsampling;
        _luminance = JpegQuantization.CreateLuminance(quality);
        _chrominance = JpegQuantization.CreateChrominance(quality);
    }

    /// <summary>Gets the quality, 1 to 100.</summary>
    /// <remarks>ITU-T T.81 Annex K.1.</remarks>
    public int Quality { get; }

    /// <summary>Gets how much chrominance is subsampled when colour is transformed.</summary>
    /// <remarks>ITU-T T.81 §A.1.1.</remarks>
    public DctChromaSubsampling ChromaSubsampling { get; }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.8: <c>DCTDecode</c>.</remarks>
    public CosName Name => FilterNames.DctDecode;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    /// The dictionary has no valid <c>Width</c> or <c>Height</c>, is an image mask or has a <c>BitsPerComponent</c> other than 8, the
    /// data is not width x height pixels of 1, 3 or 4 components or disagrees with a device <c>ColorSpace</c>, or
    /// <c>ColorTransform</c> is not 0 or 1.
    /// </exception>
    /// <remarks>ISO 32000-2 §7.4.8, §8.9.5 Table 87; ITU-T T.81 §4.5 (baseline sequential), Annex B.</remarks>
    public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        CosDictionary dictionary = context.StreamDictionary;
        int width = Dimension(dictionary, ImageNames.Width);
        int height = Dimension(dictionary, ImageNames.Height);
        if (dictionary.TryGetValue(ImageNames.ImageMask, out CosObject? mask) && mask is CosBoolean { Value: true })
        {
            throw new ArgumentException("An image mask has one bit per sample; DCTDecode encodes 8-bit samples.", nameof(context));
        }

        if (dictionary.TryGetValue(ImageNames.BitsPerComponent, out CosObject? bits) && bits is not CosInteger { Value: 8 })
        {
            throw new ArgumentException("DCTDecode encodes 8-bit samples; BitsPerComponent must be 8.", nameof(context));
        }

        long pixels = (long)width * height;
        int components = data.Length % pixels == 0 ? (int)Math.Min(data.Length / pixels, 5) : 0;
        if (components is not (1 or 3 or 4))
        {
            throw new ArgumentException(
                FormattableString.Invariant($"{data.Length} bytes are not {width} x {height} pixels of 1, 3 or 4 components."), nameof(data));
        }

        if (dictionary.TryGetValue(ImageNames.ColorSpace, out CosObject? space) && space is CosName name && DeviceComponents(name) is int expected && expected != components)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"The data has {components} components per pixel; /{name.Value} has {expected}."), nameof(data));
        }

        bool transform = ColorTransform(context.Parameters) ?? components == 3;
        JpegColorLayout layout = (components, transform) switch
        {
            (1, _) => JpegColorLayout.Gray,
            (3, true) => JpegColorLayout.YCbCr,
            (3, false) => JpegColorLayout.Rgb,
            (_, true) => JpegColorLayout.Ycck,
            _ => JpegColorLayout.Cmyk,
        };
        (int horizontal, int vertical) = ChromaSubsampling switch
        {
            DctChromaSubsampling.None => (1, 1),
            DctChromaSubsampling.Horizontal => (2, 1),
            _ => (2, 2),
        };
        JpegEncoder.Encode(data, width, height, layout, horizontal, vertical, _luminance, _chrominance, output);
    }

    /// <summary>Reads <c>Width</c> or <c>Height</c>: an integer of 1 to 65535 (T.81 §B.2.2, X and Y).</summary>
    private static int Dimension(CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? value) && value is CosInteger { Value: >= 1 and <= ushort.MaxValue } integer
            ? (int)integer.Value
            : throw new ArgumentException($"The image dictionary needs /{key.Value} as an integer of 1 to 65535.", nameof(dictionary));

    /// <summary>The component count of a device colour space name, or <see langword="null"/> for any other name.</summary>
    private static int? DeviceComponents(CosName name) =>
        name.Equals(ImageNames.DeviceGray) ? 1 : name.Equals(ImageNames.DeviceRgb) ? 3 : name.Equals(ImageNames.DeviceCmyk) ? 4 : null;

    /// <summary>Reads <c>ColorTransform</c> (Table 13): 0 or 1, or <see langword="null"/> when absent.</summary>
    private static bool? ColorTransform(CosDictionary? parameters)
    {
        if (parameters is null || !parameters.TryGetValue(FilterNames.ColorTransform, out CosObject? value) || value is CosNull)
        {
            return null;
        }

        return value is CosInteger { Value: 0 or 1 } integer
            ? integer.Value == 1
            : throw new ArgumentException("ColorTransform must be 0 or 1.", nameof(parameters));
    }
}
