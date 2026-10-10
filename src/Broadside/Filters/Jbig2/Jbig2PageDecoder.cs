using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters.Ccitt;
using Broadside.Filters.Codecs;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The control decoding procedure and page make-up for one PDF JBIG2 image (ITU-T T.88 §7 and §8): the global segments, then the
/// page's segments, composed into the decoded image, which is then inverted into PDF polarity.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.7: the image is page 1 of an embedded (D.3), sequential (D.1) bit stream whose global segments are in the
/// <c>JBIG2Globals</c> stream. The page buffer is the image's <c>Width</c> x <c>Height</c> (the page information size, when it
/// differs, is reported and regions are clipped), filled with the page's default pixel; immediate generic regions are combined at
/// their location with their operator, or with the page default operator when the page does not allow overriding it (§7.4.8.5).
/// </para>
/// <para>
/// JBIG2 pixels are 1 for black, PDF 1-bit gray samples 1 for white (§8.9.5.2): the composed page is inverted and its row padding
/// cleared, so the image's <c>Decode</c> array then applies as for any image.
/// </para>
/// </remarks>
internal sealed class Jbig2PageDecoder
{
    private readonly Jbig2Reporter _reporter;
    private readonly ImageFilterContext _context;
    private bool _havePage;
    private Jbig2CombinationOperator _defaultOperator;
    private bool _operatorOverridden = true;
    private int _intermediateRegions;
    private byte[]? _contexts;

    private Jbig2PageDecoder(ImageFilterContext context)
    {
        _context = context;
        _reporter = new Jbig2Reporter(context.Filter);
    }

    /// <summary>Decodes the image: <paramref name="width"/> x <paramref name="height"/>, one 1-bit component, PDF polarity.</summary>
    /// <param name="page">The image stream's data (after any filters before JBIG2Decode).</param>
    /// <param name="globals">The decoded <c>JBIG2Globals</c> stream, or empty.</param>
    /// <param name="context">The image context: limits and diagnostics.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The image; <see langword="null"/> when it is too large or uses features not decoded yet.</returns>
    public static DecodedImage? Decode(ReadOnlySpan<byte> page, ReadOnlySpan<byte> globals, ImageFilterContext context, int width, int height)
    {
        var decoder = new Jbig2PageDecoder(context);
        try
        {
            return decoder.Run(page, globals, width, height);
        }
        finally
        {
            if (decoder._contexts is not null)
            {
                ArrayPool<byte>.Shared.Return(decoder._contexts);
            }
        }
    }

    /// <summary>
    /// The page size the page information segment gives (T.88 §7.4.8), for an image dictionary without <c>Width</c> or
    /// <c>Height</c>: an unknown height (0xFFFFFFFF) is taken from the last end-of-stripe row.
    /// </summary>
    /// <param name="page">The image stream's data.</param>
    /// <param name="width">The page width.</param>
    /// <param name="height">The page height.</param>
    /// <returns><see langword="true"/> when the stream has a page information segment with a usable size.</returns>
    public static bool TryReadPageSize(ReadOnlySpan<byte> page, out int width, out int height)
    {
        width = 0;
        height = 0;
        using Jbig2SegmentList list = Jbig2SegmentList.Parse(page, new Jbig2Reporter(null), "page");
        long pageHeight = -1;
        long stripeEnd = -1;
        foreach (Jbig2Segment segment in list.Segments)
        {
            ReadOnlySpan<byte> data = page.Slice(segment.DataStart, segment.DataLength);
            if (segment.Type == Jbig2SegmentType.PageInformation && width == 0 && data.Length >= 19)
            {
                width = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(data));
                uint h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                pageHeight = h == uint.MaxValue ? -1 : h;
            }
            else if (segment.Type == Jbig2SegmentType.EndOfStripe && data.Length >= 4)
            {
                stripeEnd = Math.Max(stripeEnd, BinaryPrimitives.ReadUInt32BigEndian(data));
            }
        }

        long resolved = pageHeight >= 0 ? pageHeight : stripeEnd + 1;
        height = (int)Math.Min(int.MaxValue, Math.Max(0, resolved));
        return width > 0 && height > 0;
    }

    private DecodedImage? Run(ReadOnlySpan<byte> page, ReadOnlySpan<byte> globals, int width, int height)
    {
        DecodedImageBuilder? image = null;
        try
        {
            if (!_context.TryCreateImage(width, height, 1, 1, out image))
            {
                return null;
            }

            var bitmap = new Jbig2Bitmap(image.Samples, width, height, image.Stride);
            if (!globals.IsEmpty)
            {
                using Jbig2SegmentList globalList = Jbig2SegmentList.Parse(globals, _reporter, "globals");
                foreach (Jbig2Segment segment in globalList.Segments)
                {
                    if (segment.Page != 0)
                    {
                        _reporter.Report(
                            DiagnosticCodes.Jbig2GlobalsInvalid,
                            DiagnosticSeverity.Warning,
                            "The JBIG2Globals stream holds segments associated with a page, which belong in the image stream (ISO 32000-2 §7.4.7); they are ignored.");
                        continue;
                    }

                    if (!ProcessUnassociated(segment, globals.Slice(segment.DataStart, segment.DataLength)))
                    {
                        break;
                    }
                }
            }

            using Jbig2SegmentList list = Jbig2SegmentList.Parse(page, _reporter, "page");
            uint pageNumber = SelectPage(list.Segments);
            foreach (Jbig2Segment segment in list.Segments)
            {
                ReadOnlySpan<byte> data = page.Slice(segment.DataStart, segment.DataLength);
                if (segment.Page == 0)
                {
                    if (!ProcessUnassociated(segment, data))
                    {
                        break;
                    }

                    continue;
                }

                if (segment.Page != pageNumber)
                {
                    _reporter.Report(
                        DiagnosticCodes.Jbig2PageNumberNotOne,
                        DiagnosticSeverity.Warning,
                        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 stream holds segments of more than one page; those of page {segment.Page} are ignored and page {pageNumber} is decoded (ISO 32000-2 §7.4.7)."),
                        "other");
                    continue;
                }

                if (!ProcessPageSegment(segment, data, bitmap))
                {
                    break;
                }
            }

            if (_reporter.Undecodable)
            {
                return null;
            }

            if (_intermediateRegions > 0)
            {
                _reporter.Report(
                    DiagnosticCodes.Jbig2IntermediateRegionUnused,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 page has {_intermediateRegions} intermediate region(s) no refinement region refers to (ITU-T T.88 §8.2 step 6); they are dropped."));
            }

            bitmap.Invert();
            return image.Build();
        }
        finally
        {
            image?.Dispose();
        }
    }

    /// <summary>The page to decode: that of the first page information segment, else of the first page-associated segment, else 1.</summary>
    private uint SelectPage(ReadOnlySpan<Jbig2Segment> segments)
    {
        uint page = 0;
        foreach (Jbig2Segment segment in segments)
        {
            if (segment.Type == Jbig2SegmentType.PageInformation && segment.Page != 0)
            {
                page = segment.Page;
                break;
            }

            if (page == 0 && segment.Page != 0)
            {
                page = segment.Page;
            }
        }

        if (page is not (0 or 1))
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2PageNumberNotOne,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 image's segments are associated with page {page}, where ISO 32000-2 §7.4.7 requires 1; that page is decoded."));
        }

        return page == 0 ? 1 : page;
    }

    /// <summary>A segment associated with no page: dictionaries, tables, profiles, extensions. Returns <see langword="false"/> to stop.</summary>
    private bool ProcessUnassociated(in Jbig2Segment segment, ReadOnlySpan<byte> data)
    {
        switch (segment.Type)
        {
            case Jbig2SegmentType.SymbolDictionary:
                _reporter.ReportUnsupported("symbol dictionaries (ITU-T T.88 §6.5)", paintsPage: false);
                return true;
            case Jbig2SegmentType.PatternDictionary:
                _reporter.ReportUnsupported("pattern dictionaries (ITU-T T.88 §6.7)", paintsPage: false);
                return true;
            case Jbig2SegmentType.Tables:
                _reporter.ReportUnsupported("code tables (ITU-T T.88 Annex B)", paintsPage: false);
                return true;
            case Jbig2SegmentType.Profiles:
                return true;
            case Jbig2SegmentType.Extension:
                ProcessExtension(data);
                return true;
            case Jbig2SegmentType.EndOfFile:
                ReportEnd("an end-of-file segment");
                return false;
            default:
                if (Jbig2SegmentType.IsRegion(segment.Type) || segment.Type is Jbig2SegmentType.PageInformation or Jbig2SegmentType.EndOfPage or Jbig2SegmentType.EndOfStripe)
                {
                    _reporter.Report(
                        DiagnosticCodes.Jbig2SegmentInvalid,
                        DiagnosticSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 segment {segment.Number} of type {segment.Type} is associated with no page, which ITU-T T.88 §7.3.2 does not allow; it is skipped."));
                    return true;
                }

                ReportReserved(segment);
                return true;
        }
    }

    /// <summary>A segment of the page being decoded. Returns <see langword="false"/> to stop decoding the page.</summary>
    private bool ProcessPageSegment(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2Bitmap bitmap)
    {
        switch (segment.Type)
        {
            case Jbig2SegmentType.PageInformation:
                ProcessPageInformation(segment, data, bitmap);
                return true;
            case Jbig2SegmentType.IntermediateGenericRegion:
            case Jbig2SegmentType.ImmediateGenericRegion:
            case Jbig2SegmentType.ImmediateLosslessGenericRegion:
                RequirePage();
                ProcessGenericRegion(segment, data, bitmap);
                return true;
            case Jbig2SegmentType.IntermediateTextRegion:
            case Jbig2SegmentType.ImmediateTextRegion:
            case Jbig2SegmentType.ImmediateLosslessTextRegion:
                _reporter.ReportUnsupported("text regions (ITU-T T.88 §6.4)", paintsPage: true);
                return true;
            case Jbig2SegmentType.IntermediateHalftoneRegion:
            case Jbig2SegmentType.ImmediateHalftoneRegion:
            case Jbig2SegmentType.ImmediateLosslessHalftoneRegion:
                _reporter.ReportUnsupported("halftone regions (ITU-T T.88 §6.6)", paintsPage: true);
                return true;
            case Jbig2SegmentType.IntermediateRefinementRegion:
            case Jbig2SegmentType.ImmediateRefinementRegion:
            case Jbig2SegmentType.ImmediateLosslessRefinementRegion:
                _reporter.ReportUnsupported("generic refinement regions (ITU-T T.88 §6.3)", paintsPage: true);
                return true;
            case Jbig2SegmentType.EndOfPage:
                ReportEnd("an end-of-page segment");
                return false;
            case Jbig2SegmentType.EndOfStripe:
                // T.88 §7.4.10: the end row only bounds later regions; the page buffer is the image's whole height already.
                return true;
            default:
                return ProcessUnassociated(segment, data);
        }
    }

    private void ReportEnd(string what) => _reporter.Report(
        DiagnosticCodes.Jbig2EndOfPagePresent,
        DiagnosticSeverity.Warning,
        $"The JBIG2 stream holds {what}, which ISO 32000-2 §7.4.7 does not allow; decoding of the page stops there.");

    private void ReportReserved(in Jbig2Segment segment) => _reporter.Report(
        DiagnosticCodes.Jbig2SegmentTypeReserved,
        DiagnosticSeverity.Warning,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 segment {segment.Number} has the reserved type {segment.Type} (ITU-T T.88 §7.3; type 54, colour palette, is forbidden by ISO 32000-2 §7.4.7); it is skipped."),
        segment.Type.ToString(CultureInfo.InvariantCulture));

    /// <summary>T.88 §7.4.14-7.4.15: comments are ignored; an unknown extension marked necessary cannot be honoured.</summary>
    private void ProcessExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(data);
        if ((type & 0x80000000) != 0 && type is not (0x20000000 or 0x20000002))
        {
            _reporter.ReportUnsupported(string.Create(CultureInfo.InvariantCulture, $"the necessary extension 0x{type:X8} (ITU-T T.88 §7.4.14)"), paintsPage: true);
        }
    }

    /// <summary>T.88 §7.4.8: sets the page's default pixel and operator; the size is the image's.</summary>
    private void ProcessPageInformation(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2Bitmap bitmap)
    {
        if (_havePage)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2SegmentInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 page has a second page information segment ({segment.Number}); it is ignored."),
                "page");
            return;
        }

        if (data.Length < 19)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2SegmentInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 page information segment {segment.Number} has {data.Length} data bytes where ITU-T T.88 §7.4.8 needs 19; the page is white with the OR operator."));
            RequirePage();
            return;
        }

        _havePage = true;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        byte flags = data[16];
        if (width != (uint)bitmap.Width || (height != uint.MaxValue && height != (uint)bitmap.Height))
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2PageSizeMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 page is {width} x {(height == uint.MaxValue ? "unknown" : height.ToString(CultureInfo.InvariantCulture))} but the image is {bitmap.Width} x {bitmap.Height}; the image size is used and regions are clipped to it."));
        }

        if ((flags & 0x80) != 0)
        {
            _reporter.ReportUnsupported("colour (the page colour extension flag, ITU-T T.88 Amendment 3, forbidden by ISO 32000-2 §7.4.7)", paintsPage: true);
        }

        _defaultOperator = (Jbig2CombinationOperator)((flags >> 3) & 3);
        _operatorOverridden = (flags & 0x40) != 0;
        bitmap.Fill((flags >> 2) & 1);
    }

    private void RequirePage()
    {
        if (_havePage)
        {
            return;
        }

        _havePage = true;
        _reporter.Report(
            DiagnosticCodes.Jbig2PageInformationMissing,
            DiagnosticSeverity.Warning,
            "The JBIG2 page has no page information segment before its regions (ITU-T T.88 §7.4.8); the page is white (0) and regions use their own operators.");
    }

    /// <summary>T.88 §7.4.6 and §8.2 step 5 a) and b).</summary>
    private void ProcessGenericRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2Bitmap page)
    {
        if (data.Length < 18)
        {
            ReportInvalid(segment, $"has {data.Length} data bytes, fewer than its 18-byte header (ITU-T T.88 §7.4.6.1)");
            return;
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        uint x = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        uint y = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        byte regionFlags = data[16];
        byte flags = data[17];
        if ((regionFlags & 0x08) != 0)
        {
            _reporter.ReportUnsupported("colour (the region colour extension flag, ITU-T T.88 Amendment 3, forbidden by ISO 32000-2 §7.4.7)", paintsPage: true);
            return;
        }

        if ((flags & 0x10) != 0)
        {
            _reporter.ReportUnsupported("extended templates (EXTTEMPLATE, ITU-T T.88 Amendment 2)", paintsPage: true);
            return;
        }

        int op = regionFlags & 7;
        if (op > 4)
        {
            ReportInvalid(segment, string.Create(CultureInfo.InvariantCulture, $"has the external combination operator {op}, which ITU-T T.88 §7.4.1.5 does not define; OR is used"));
            op = 0;
        }

        bool mmr = (flags & 1) != 0;
        int template = (flags >> 1) & 3;
        var parameters = Jbig2GenericParameters.Nominal(template, typicalPrediction: (flags & 8) != 0);
        int position = 18;
        if (!mmr)
        {
            int atBytes = template == 0 ? 8 : 2;
            if (data.Length < position + atBytes)
            {
                ReportInvalid(segment, "ends inside its AT flags (ITU-T T.88 §7.4.6.3)");
                return;
            }

            ReadOnlySpan<byte> at = data.Slice(position, atBytes);
            parameters = template == 0
                ? parameters with
                {
                    AtX1 = (sbyte)at[0],
                    AtY1 = (sbyte)at[1],
                    AtX2 = (sbyte)at[2],
                    AtY2 = (sbyte)at[3],
                    AtX3 = (sbyte)at[4],
                    AtY3 = (sbyte)at[5],
                    AtX4 = (sbyte)at[6],
                    AtY4 = (sbyte)at[7],
                }
                : parameters with { AtX1 = (sbyte)at[0], AtY1 = (sbyte)at[1] };
            position += atBytes;
            for (int i = 0; i < atBytes; i += 2)
            {
                int atX = (sbyte)at[i];
                int atY = (sbyte)at[i + 1];
                if (atY > 0 || (atY == 0 && atX >= 0))
                {
                    ReportInvalid(segment, string.Create(CultureInfo.InvariantCulture, $"places an AT pixel at ({atX}, {atY}), outside the field of ITU-T T.88 Figure 7; it reads pixels not decoded yet as 0"));
                    break;
                }
            }
        }

        ReadOnlySpan<byte> coded = data[position..];
        long rows = height;
        if (segment.UnknownLength)
        {
            rows = BinaryPrimitives.ReadUInt32BigEndian(data[^4..]);
            coded = data[position..^6];
            if (rows > height)
            {
                ReportInvalid(segment, string.Create(CultureInfo.InvariantCulture, $"has the row count {rows}, more than its height {height} (ITU-T T.88 §7.4.6.4); the height is used"));
                rows = height;
            }
        }

        if (width == 0 || rows == 0)
        {
            return;
        }

        int stride = Jbig2Bitmap.StrideOf((int)Math.Min(int.MaxValue - 7, width));
        if ((long)width * rows > _context.MaxPixels || width > int.MaxValue - 7 || (long)stride * rows > Math.Min(_context.MaxBytes, Array.MaxLength))
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2LimitExceeded,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 generic region segment {segment.Number} is {width} x {rows} pixels, more than the image limits allow; it is not decoded."));
            return;
        }

        int length = stride * (int)rows;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            buffer.AsSpan(0, length).Clear();
            var region = new Jbig2Bitmap(buffer, (int)width, (int)rows, stride);
            if (mmr)
            {
                MmrResult result = Jbig2GenericRegion.DecodeMmr(coded, region);
                ReportMmr(segment, result, region.Height);
            }
            else
            {
                Span<byte> contexts = Contexts();
                var decoder = new MqDecoder(coded);
                Jbig2GenericRegion.Decode(ref decoder, contexts, parameters, region, default);
            }

            if (segment.Type == Jbig2SegmentType.IntermediateGenericRegion)
            {
                // §8.2 step 5 b): an auxiliary buffer that only a refinement region (issue #65) would draw.
                _intermediateRegions++;
                return;
            }

            page.Compose(region, x, y, Operator(segment, (Jbig2CombinationOperator)op));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The operator a direct region is combined with: its own (§8.2 step 5 a), as jbig2dec and PDFium do; a page that does not allow
    /// overriding its default (§7.4.8.5 bit 6) yet meets another operator is reported.
    /// </summary>
    private Jbig2CombinationOperator Operator(in Jbig2Segment segment, Jbig2CombinationOperator own)
    {
        if (!_operatorOverridden && own != _defaultOperator)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2CombinationOperatorMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 region segment {segment.Number} uses the combination operator {own} but the page does not allow overriding its default {_defaultOperator} (ITU-T T.88 §7.4.8.5); the region's operator is used (§8.2 step 5 a)."));
        }

        return own;
    }

    private void ReportMmr(in Jbig2Segment segment, MmrResult result, int rows)
    {
        if (result.Status == MmrStatus.Invalid)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2RegionDataInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The MMR data of JBIG2 region segment {segment.Number} has an invalid code in row {result.Rows}; the rows after it are 0."));
        }
        else if (result.Rows < rows)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2RegionDataTruncated,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The MMR data of JBIG2 region segment {segment.Number} ends after {result.Rows} of its {rows} rows; the rest are 0."));
        }
    }

    private void ReportInvalid(in Jbig2Segment segment, string what) => _reporter.Report(
        DiagnosticCodes.Jbig2SegmentInvalid,
        DiagnosticSeverity.Error,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 generic region segment {segment.Number} {what}."));

    /// <summary>The GB contexts, reset for a new segment (§7.4.6.4 step 2, E.3.7: state 0, MPS 0).</summary>
    private Span<byte> Contexts()
    {
        _contexts ??= ArrayPool<byte>.Shared.Rent(Jbig2GenericRegion.ContextCount);
        Span<byte> contexts = _contexts.AsSpan(0, Jbig2GenericRegion.ContextCount);
        contexts.Clear();
        return contexts;
    }
}
