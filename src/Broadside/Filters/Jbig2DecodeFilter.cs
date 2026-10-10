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
/// ISO 32000-2 §7.4.7 and Table 12; ITU-T T.88 §6.2 (generic region decoding: the four templates, AT pixels, typical prediction,
/// MMR), §7 (segment headers and syntaxes), §8 (page make-up), Annex D.3 (embedded organisation) and Annex E (the MQ arithmetic
/// decoder, shared with JPXDecode). The global segments are parsed first, then the image stream's page 1 segments; the page is the
/// image's <c>Width</c> x <c>Height</c> (else the page information size), filled with the page's default pixel, and immediate
/// generic regions are combined into it.
/// </para>
/// <para>
/// Output: one 1-bit component, <c>ceil(Width / 8)</c> bytes per row, in PDF polarity: JBIG2's black (1) becomes 0, as a 1-bit
/// <c>DeviceGray</c> sample or a painting image-mask sample reads it (§8.9.5.2, §8.9.6.2), and the bits after the last column are 0.
/// The image's <c>Decode</c> array applies as for any image (<see cref="DecodedImage.SamplesInverted"/> is <see langword="false"/>).
/// </para>
/// <para>
/// Not decoded yet (issue #65): symbol dictionaries and text regions, pattern dictionaries and halftone regions, generic refinement
/// regions and code tables. A page that uses them is reported as <c>Jbig2UnsupportedFeature</c> (Information: legal content) and the
/// image is not decoded. Lenient repair: a file header, end-of-page and end-of-file segments, segments of other pages, truncated
/// segments and a page size that disagrees with the image are tolerated with a diagnostic; strict mode throws instead.
/// </para>
/// <para>Stateless and thread-safe; a decode allocates per stream and per region, nothing per row or pixel.</para>
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

        ReadOnlyMemory<byte> globals = ReadGlobals(context.Filter);
        return Jbig2PageDecoder.Decode(encoded.Span, globals.Span, context, width, height);
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

    /// <summary>ISO 32000-2 Table 12: the decoded <c>JBIG2Globals</c> stream, or nothing (with a diagnostic when the entry is unusable).</summary>
    private static ReadOnlyMemory<byte> ReadGlobals(FilterContext context)
    {
        if (context.Parameters is not { } parameters || !parameters.TryGetValue(Globals, out CosObject? entry))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        switch (context.Resolve(entry))
        {
            case CosNull:
                return ReadOnlyMemory<byte>.Empty;
            case CosStream stream when !UsesJbig2(stream, context):
                return context.DecodeStream(stream);
            case CosStream:
                context.Report(
                    DiagnosticCodes.Jbig2GlobalsInvalid,
                    DiagnosticSeverity.Warning,
                    "The JBIG2Globals stream is itself JBIG2Decode-encoded; it holds raw segments (ISO 32000-2 Table 12), so it is ignored.");
                return ReadOnlyMemory<byte>.Empty;
            default:
                context.Report(
                    DiagnosticCodes.Jbig2GlobalsInvalid,
                    DiagnosticSeverity.Warning,
                    "The JBIG2Globals parameter shall be a stream (ISO 32000-2 Table 12); it is ignored and the image is decoded without global segments.");
                return ReadOnlyMemory<byte>.Empty;
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
