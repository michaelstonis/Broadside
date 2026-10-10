using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters.Dct;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// The <c>DCTDecode</c> filter: JPEG baseline, extended sequential and progressive image data, Huffman- or arithmetic-coded, 8- or
/// 12-bit, decoded in managed code to 8-bit samples, interleaved, in the §8.9.3 layout. Decode only.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.8 (DCTDecode, Table 13 <c>ColorTransform</c>; progressive JPEG from PDF 1.3); ITU-T T.81 (ISO/IEC 10918-1)
/// Annex B (interchange syntax), Annex C (Huffman tables), Annex D (the QM arithmetic decoder, its own 113-state probability
/// estimation, not the MQ decoder of JBIG2 and JPEG 2000), Annex E (decoder control, restart intervals), Annex F §F.2 (sequential
/// DCT decoding: baseline and extended, 8 and 12 bits, Huffman with up to four tables of each class or arithmetic coding with DAC
/// conditioning), Annex G (progressive DCT decoding: spectral selection and successive approximation, Huffman or arithmetic),
/// Annex A (MCU geometry, IDCT), Annex K.3 (typical tables, used when a scan's table was never defined); Adobe Technical Note
/// #5116 (APP14 marker, colour transforms, replication upsampling). §8.9.5.1 Table 87: the filter always delivers 8 bits per
/// component, whatever the image dictionary says, so 12-bit samples are reduced to 8 bits (reported as <c>DctPrecisionReduced</c>,
/// Information).
/// </para>
/// <para>
/// Samples equal libjpeg-turbo's with <c>-dct int -nosmooth</c> (the islow IDCT and the integer YCbCr tables are ported from the
/// Independent JPEG Group's software; see THIRD-PARTY-NOTICES.txt): subsampled components are replicated, as Adobe's DCTDecode
/// does, never interpolated. Three components are converted from YCbCr to RGB and four from YCCK to CMYK when the Adobe APP14
/// segment's transform code says so (Table 13: a present APP14 segment decides and <c>ColorTransform</c> is ignored), else when
/// <c>ColorTransform</c> is 1, else (three components only) unless the components are identified as R, G, B. The codec never
/// inverts samples: an Adobe-inverted CMYK image carries its own <c>Decode</c> array, which the image layer applies (§8.9.5.2).
/// </para>
/// <para>
/// Never decoded (<c>DctProcessUnsupported</c>, Error, no samples): the lossless and hierarchical processes, which PDF does not
/// use.
/// </para>
/// <para>
/// Lenient repair, each reported once per image: junk before SOI or between segments is skipped; undefined Huffman tables fall
/// back to Annex K; a frame height of 0 comes from the DNL segment or the image dictionary; restart markers out of sequence are
/// resynchronized; data that ends early leaves the missing blocks mid-grey (<see cref="DecodedImage.DecodedRows"/> on the image
/// path, except that a truncated progressive image keeps every row its first DC scan reached); invalid progressive scans are
/// skipped and out-of-order ones decoded as given; a missing quantization table or an unusable frame decodes nothing. Strict mode
/// throws at the first deviation.
/// </para>
/// <para>
/// Stateless and thread-safe; a decode allocates nothing per block or row (the per-image buffers are pooled).
/// </para>
/// </remarks>
public sealed class DctDecodeFilter : IImageFilter
{
    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.8: <c>DCTDecode</c>; abbreviated <c>DCT</c> in inline images (§8.9.7, Table 92).</remarks>
    public CosName Name => FilterNames.DctDecode;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8 and §8.9.3: rows of width x components bytes, top to bottom, the colour transform applied. Nothing is
    /// written when the data has no decodable frame or is larger than <see cref="FilterContext.MaxDecodedLength"/>.
    /// </remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        JpegDecoder decoder = JpegDecoder.Rent();
        try
        {
            ReadOnlySpan<byte> data = encoded.Span;
            if (!decoder.ReadHeaders(data, context, silent: false, DictionaryHeight(context)))
            {
                return;
            }

            long size = (long)decoder.Width * decoder.Height * decoder.ComponentCount;
            if (size > context.MaxDecodedLength)
            {
                context.Report(
                    DiagnosticCodes.ImageTooLarge,
                    DiagnosticSeverity.Error,
                    FormattableString.Invariant($"The DCT image is not decoded: its {size} bytes of samples are more than the {context.MaxDecodedLength} allowed."));
                return;
            }

            decoder.SelectColorTransform(ReadColorTransform(context));
            var sink = new BufferWriterRowSink(output, decoder.Width * decoder.ComponentCount);
            decoder.Decode(data, ref sink, context.MaxDecodedLength);
        }
        finally
        {
            JpegDecoder.Return(decoder);
        }
    }

    /// <inheritdoc/>
    /// <remarks>ITU-T T.81 §B.2.2 (frame header: X, Y, Nf, P); ISO 32000-2 §8.9.5.1 Table 87 (always 8 bits per component).</remarks>
    public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(context);
        JpegDecoder decoder = JpegDecoder.Rent();
        try
        {
            if (!decoder.ReadHeaders(encoded, context.Filter, silent: true, context.Height))
            {
                header = default;
                return false;
            }

            header = new ImageHeader(decoder.Width, decoder.Height, decoder.ComponentCount, 8) { ColorModel = decoder.ColorModel };
            return true;
        }
        finally
        {
            JpegDecoder.Return(decoder);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §7.4.8 and §8.9.3. The image reports its <see cref="DecodedImage.ColorModel"/> (Gray, RGB or CMYK by component
    /// count), the APP14 <see cref="DecodedImage.AdobeTransform"/> and whether a colour transform was applied.
    /// </remarks>
    public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        JpegDecoder decoder = JpegDecoder.Rent();
        DecodedImageBuilder? image = null;
        try
        {
            ReadOnlySpan<byte> data = encoded.Span;
            if (!decoder.ReadHeaders(data, context.Filter, silent: false, context.Height))
            {
                return null;
            }

            decoder.SelectColorTransform(ReadColorTransform(context.Filter));
            if (!context.TryCreateImage(decoder.Width, decoder.Height, decoder.ComponentCount, 8, out image))
            {
                return null;
            }

            var sink = new ImageRowSink(image);
            if (!decoder.Decode(data, ref sink, context.MaxBytes))
            {
                return null;
            }

            image.DecodedRows = decoder.DecodedRows;
            image.ColorModel = decoder.ColorModel;
            image.AdobeTransform = decoder.AdobeTransform;
            image.ColorTransformApplied = decoder.ColorTransformApplied;
            DecodedImage decoded = image.Build();
            image = null;
            return decoded;
        }
        finally
        {
            image?.Dispose();
            JpegDecoder.Return(decoder);
        }
    }

    /// <summary>Reads <c>ColorTransform</c> from the parameters: 0 or 1, or -1 when absent or invalid.</summary>
    private static int ReadColorTransform(FilterContext context) => (int)context.ReadInteger(FilterNames.ColorTransform, -1, 0, 1);

    /// <summary>The image dictionary's <c>Height</c>, for a frame whose number of lines is 0 without a DNL segment.</summary>
    private static int DictionaryHeight(FilterContext context) =>
        context.StreamDictionary.TryGetValue(ImageNames.Height, out CosObject? value) && context.Resolve(value) is CosInteger { Value: > 0 and <= int.MaxValue } height
            ? (int)height.Value
            : 0;
}
