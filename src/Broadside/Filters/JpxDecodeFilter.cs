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
/// <c>ColorSpace</c> in the dictionary, the image has that many channels (the codestream's first components).
/// </para>
/// <para>
/// Implemented: main and tile-part headers (COD, COC, QCD, QCC with their precedence), any number of tiles, tile-parts and layers,
/// any precinct and code-block partition, LRCP and RLCP progressions, SOP and EPH markers, tier-1 with the "reset context
/// probabilities", "segmentation symbols" and "predictable termination" code-block styles, the reversible 5/3 filter, the
/// reversible component transformation and the DC level shift. Other Part 1 features are recorded as <c>JpxUnsupportedFeature</c>
/// (Information: legal content not decoded yet) and the image is not decoded.
/// </para>
/// <para>
/// Through <see cref="IStreamFilter.Decode"/> the output is the samples of <see cref="DecodeImage"/> in the §8.9.3 layout: one channel
/// per codestream component, at the bits per component of the greatest precision (rounded up to 8 or 16 for 3 to 7 and 9 to 15).
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
