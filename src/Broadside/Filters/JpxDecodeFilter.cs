using System.Buffers;
using Broadside.Filters.Jpx;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// The <c>JPXDecode</c> filter: JPEG 2000 images, from a JP2/JPX file or a raw codestream, decoded in managed code. Decode only.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.9 and Table 87; ITU-T T.800 | ISO/IEC 15444-1 Annexes A (codestream syntax), B (packets and progression),
/// C (MQ decoder), D (EBCOT tier-1), E (reconstruction), F (inverse wavelet transform), G (DC level shift and component
/// transformation) and I (JP2 boxes). The codestream's width, height and precision win over the image dictionary; with a
/// <c>ColorSpace</c> in the dictionary, the image has that many channels.
/// </para>
/// <para>
/// All of Part 1: main and tile-part headers (COD, COC, QCD, QCC, RGN, POC, PPM, PPT, with their precedence), any number of tiles,
/// tile-parts (in TPsot order) and layers, any precinct and code-block partition, the five progressions and progression volumes,
/// SOP and EPH markers, packed packet headers, tier-1 with every code-block style, region-of-interest Maxshift, the 5/3 and 9/7
/// filters with dequantization, the RCT and ICT, the DC level shift and sub-sampled components (replicated over the image grid).
/// </para>
/// <para>
/// The JP2 wrapper follows ISO 32000-2 §7.4.9: with an <c>Indexed</c> ColorSpace the codestream's indices are the samples; with
/// another ColorSpace its first N colour channels (in channel definition order) are, and the JP2 colour boxes are ignored; without
/// one, the palette and component mapping, the channel definitions and the colour specification of highest precedence decide the
/// channels and the colour model (<see cref="DecodedImage.ColorModel"/>, <see cref="DecodedImage.IccProfile"/>), sYCC, YCbCr and
/// CIELab converted to RGB. With <c>SMaskInData</c> the opacity channel becomes <see cref="DecodedImage.Alpha"/>, premultiplied for
/// <c>SMaskInData 2</c> or a premultiplied channel definition.
/// </para>
/// <para>
/// Through <see cref="IStreamFilter.Decode"/> the output is the colour samples of <see cref="DecodeImage"/> in the §8.9.3 layout, at
/// the bits per component of the greatest precision (rounded up to 8 or 16 for 3 to 7 and 9 to 15).
/// Lenient repair: a truncated codestream decodes what is present (<c>JpxCodestreamTruncated</c>); strict mode throws instead.
/// Stateless and thread-safe.
/// </para>
/// </remarks>
public sealed class JpxDecodeFilter : IImageFilter
{
    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.9: <c>JPXDecode</c>; it has no abbreviation and shall not be used in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.JpxDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.9.3; ITU-T T.800 Annexes A to G.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        using DecodedImage? image = DecodeImage(encoded, new ImageFilterContext(context));
        if (image is not null)
        {
            var writer = new FilterOutput(output);
            writer.Write(image.Samples);
            writer.Flush();
        }
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 A.5.1 (SIZ) and I.5.4 (the contiguous codestream box).</remarks>
    public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(context);
        return JpxDecoder.TryReadHeader(encoded, context, out header);
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.9; ITU-T T.800 | ISO/IEC 15444-1 Annexes A to G and I.</remarks>
    public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return JpxDecoder.Decode(encoded.Span, context);
    }
}
