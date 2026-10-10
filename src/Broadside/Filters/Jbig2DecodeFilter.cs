using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters.Jbig2;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// The <c>JBIG2Decode</c> filter: bitonal images coded with JBIG2 (ITU-T T.88 | ISO/IEC 14492) in the embedded organisation PDF uses,
/// with the global segments in the <c>JBIG2Globals</c> stream. Decode only.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.7 and Table 12; ITU-T T.88 §6.2 (generic regions: the four templates, AT pixels, typical prediction, MMR), §6.3
/// (generic refinement, both templates, TPGR), §6.4 (text regions, every reference corner, transposed, refined instances), §6.5
/// (symbol dictionaries: direct, refinement/aggregate, Huffman collective bitmaps, context reuse), §6.6 and §6.7 (halftone regions and
/// pattern dictionaries, Annex C gray-scale planes), §7 (segment headers and syntaxes), §8 (page make-up with intermediate regions and
/// auxiliary buffers), Annex A (arithmetic integer decoding), Annex B (standard and custom Huffman tables), Annex D.3 (embedded
/// organisation) and Annex E (the MQ arithmetic decoder, shared with JPXDecode). The global segments are decoded first (once per
/// <c>JBIG2Globals</c> stream), then the image stream's page 1 segments; the page is the image's <c>Width</c> x <c>Height</c> (else the
/// page information size), filled with the page's default pixel, and regions are combined into it.
/// </para>
/// <para>
/// Output: one 1-bit component, <c>ceil(Width / 8)</c> bytes per row, in PDF polarity: JBIG2's black (1) becomes 0, as a 1-bit
/// <c>DeviceGray</c> sample or a painting image-mask sample reads it (§8.9.5.2, §8.9.6.2), and the bits after the last column are 0.
/// The image's <c>Decode</c> array applies as for any image (<see cref="DecodedImage.SamplesInverted"/> is <see langword="false"/>).
/// </para>
/// <para>
/// Not decoded: extended templates (Amendment 2) and colour (Amendment 3, forbidden by §7.4.7), reported as
/// <c>Jbig2UnsupportedFeature</c> (Information) with the image not decoded. Lenient repair: a file header, end-of-page and
/// end-of-file segments, segments of other pages, truncated segments, missing referred segments, damaged Huffman or arithmetic data
/// and a page size that disagrees with the image are tolerated with a diagnostic; strict mode throws instead.
/// </para>
/// <para>
/// Thread-safe; decoded globals are cached per stream (immutable). A decode allocates per stream, per region and per symbol, nothing
/// per row, pixel, symbol instance or halftone cell.
/// </para>
/// </remarks>
public sealed class Jbig2DecodeFilter : IImageFilter
{
    private static readonly CosName Globals = new("JBIG2Globals");

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.7: <c>JBIG2Decode</c>; it has no abbreviation and shall not be used in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.Jbig2Decode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.7 and §8.9.3; the image's Width and Height come from the stream dictionary.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        var imageContext = new ImageFilterContext(context)
        {
            Width = ReadDimension(context, ImageNames.Width),
            Height = ReadDimension(context, ImageNames.Height),
            BitsPerComponent = 1,
        };
        using DecodedImage? image = DecodeImage(encoded, imageContext);
        if (image is not null)
        {
            var writer = new FilterOutput(output);
            writer.Write(image.Samples);
            writer.Flush();
        }
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.7 and §8.9.5: the dictionary's Width and Height, else the page information segment's (ITU-T T.88 §7.4.8); one 1-bit component.</remarks>
    public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryGetSize(encoded, context, out int width, out int height))
        {
            header = default;
            return false;
        }

        header = new ImageHeader(width, height, 1, 1) { ColorModel = ImageColorModel.Gray };
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.7 and Table 12; ITU-T T.88 §6.2, §7, §8, Annexes D.3 and E.</remarks>
    public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryGetSize(encoded.Span, context, out int width, out int height))
        {
            context.Filter.Report(
                DiagnosticCodes.ImageDimensionInvalid,
                DiagnosticSeverity.Error,
                "The JBIG2 image has no usable Width and Height and its stream no page information segment giving them; it is not decoded.");
            return null;
        }

        Jbig2Globals? globals = ReadGlobals(context);
        return Jbig2PageDecoder.Decode(encoded.Span, globals, context, width, height);
    }

    private static bool TryGetSize(ReadOnlySpan<byte> encoded, ImageFilterContext context, out int width, out int height)
    {
        width = context.Width;
        height = context.Height;
        if (width > 0 && height > 0)
        {
            return true;
        }

        if (!Jbig2PageDecoder.TryReadPageSize(encoded, out int pageWidth, out int pageHeight))
        {
            return false;
        }

        width = width > 0 ? width : pageWidth;
        height = height > 0 ? height : pageHeight;
        return true;
    }

    /// <summary>
    /// ISO 32000-2 Table 12: the decoded segments of the <c>JBIG2Globals</c> stream, decoded once per stream (and limits) and shared
    /// by the images that name it, or nothing (with a diagnostic when the entry is unusable).
    /// </summary>
    private static Jbig2Globals? ReadGlobals(ImageFilterContext image)
    {
        FilterContext context = image.Filter;
        if (context.Parameters is not { } parameters || !parameters.TryGetValue(Globals, out CosObject? entry))
        {
            return null;
        }

        switch (context.Resolve(entry))
        {
            case CosNull:
                return null;
            case CosStream stream when !UsesJbig2(stream, context):
                return Jbig2Globals.Get(stream, () => context.DecodeStream(stream), image.MaxPixels, image.MaxBytes);
            case CosStream:
                context.Report(
                    DiagnosticCodes.Jbig2GlobalsInvalid,
                    DiagnosticSeverity.Warning,
                    "The JBIG2Globals stream is itself JBIG2Decode-encoded; it holds raw segments (ISO 32000-2 Table 12), so it is ignored.");
                return null;
            default:
                context.Report(
                    DiagnosticCodes.Jbig2GlobalsInvalid,
                    DiagnosticSeverity.Warning,
                    "The JBIG2Globals parameter shall be a stream (ISO 32000-2 Table 12); it is ignored and the image is decoded without global segments.");
                return null;
        }
    }

    private static bool UsesJbig2(CosStream stream, FilterContext context)
    {
        if (!stream.Dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter))
        {
            return false;
        }

        return context.Resolve(filter) switch
        {
            CosName name => name.Equals(FilterNames.Jbig2Decode),
            CosArray array => array.Any(item => context.Resolve(item) is CosName name && name.Equals(FilterNames.Jbig2Decode)),
            _ => false,
        };
    }

    private static int ReadDimension(FilterContext context, CosName key) =>
        context.Resolve(context.StreamDictionary.TryGetValue(key, out CosObject? value) ? value : null) is CosInteger { Value: > 0 and <= int.MaxValue } integer
            ? (int)integer.Value
            : 0;
}
