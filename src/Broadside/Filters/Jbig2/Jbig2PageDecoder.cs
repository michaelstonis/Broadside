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
/// <c>JBIG2Globals</c> stream (decoded once per stream, <see cref="Jbig2Globals"/>). The page buffer is the image's <c>Width</c> x
/// <c>Height</c> (the page information size, when it differs, is reported and regions are clipped), filled with the page's default
/// pixel. Immediate regions (generic, text, halftone, refinement) are combined at their location with their own operator (§8.2 step
/// 5 a); intermediate regions are kept in auxiliary buffers that refinement regions refine and finally draw (§8.2 steps 5 b to e);
/// a refinement region without a referred region refines the page itself.
/// </para>
/// <para>
/// JBIG2 pixels are 1 for black, PDF 1-bit gray samples 1 for white (§8.9.5.2): the composed page is inverted and its row padding
/// cleared, so the image's <c>Decode</c> array then applies as for any image.
/// </para>
/// </remarks>
internal sealed class Jbig2PageDecoder : IDisposable
{
    private const int RegionInfoLength = 17;

    private readonly Jbig2Reporter _reporter;
    private readonly long _maxPixels;
    private readonly long _maxBytes;
    private readonly IReadOnlyDictionary<uint, object>? _globals;
    private readonly Dictionary<uint, object> _results = [];
    private readonly Dictionary<uint, AuxiliaryRegion> _auxiliary = [];
    private readonly Jbig2Statistics _statistics = new();
    private bool _havePage;
    private Jbig2CombinationOperator _defaultOperator;
    private bool _operatorOverridden = true;

    /// <summary>Initializes a new instance of the <see cref="Jbig2PageDecoder"/> class.</summary>
    /// <param name="reporter">Where deviations go.</param>
    /// <param name="maxPixels">The image pixel limit, which (with eight times the byte limit) also bounds every region, dictionary and grid.</param>
    /// <param name="maxBytes">The image byte limit.</param>
    /// <param name="globals">The decoded global segments, or null.</param>
    internal Jbig2PageDecoder(Jbig2Reporter reporter, long maxPixels, long maxBytes, IReadOnlyDictionary<uint, object>? globals)
    {
        _reporter = reporter;
        _maxBytes = maxBytes <= 0 ? Array.MaxLength : maxBytes;

        // One bit per pixel: the byte limit bounds every region, dictionary, grid and instance budget as well.
        _maxPixels = Math.Min(maxPixels, _maxBytes * 8);
        _globals = globals;
    }

    /// <summary>Gets the results of the dictionary and table segments decoded so far, by segment number.</summary>
    internal IReadOnlyDictionary<uint, object> Results => _results;

    /// <summary>Decodes the image: <paramref name="width"/> x <paramref name="height"/>, one 1-bit component, PDF polarity.</summary>
    /// <param name="page">The image stream's data (after any filters before JBIG2Decode).</param>
    /// <param name="globals">The decoded <c>JBIG2Globals</c> segments, or null.</param>
    /// <param name="context">The image context: limits and diagnostics.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The image; <see langword="null"/> when it is too large or uses features not decoded.</returns>
    public static DecodedImage? Decode(ReadOnlySpan<byte> page, Jbig2Globals? globals, ImageFilterContext context, int width, int height)
    {
        var reporter = new Jbig2Reporter(context.Filter);
        if (globals is not null)
        {
            reporter.Replay(globals.Diagnostics);
            if (globals.Undecodable)
            {
                reporter.MarkUndecodable();
            }
        }

        using var decoder = new Jbig2PageDecoder(reporter, context.MaxPixels, context.MaxDecodedLength, globals?.Results);
        return decoder.Run(page, context, width, height);
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

    /// <inheritdoc/>
    public void Dispose() => _statistics.Dispose();

    /// <summary>
    /// Processes the segments of a <c>JBIG2Globals</c> stream (dictionaries and tables; page segments there are reported and
    /// ignored). Returns <see langword="false"/> when an end-of-file segment stopped it.
    /// </summary>
    /// <param name="globals">The decoded globals stream.</param>
    internal void ProcessGlobals(ReadOnlySpan<byte> globals)
    {
        using Jbig2SegmentList list = Jbig2SegmentList.Parse(globals, _reporter, "globals");
        foreach (Jbig2Segment segment in list.Segments)
        {
            if (segment.Page != 0)
            {
                _reporter.Report(
                    DiagnosticCodes.Jbig2GlobalsInvalid,
                    DiagnosticSeverity.Warning,
                    "The JBIG2Globals stream holds segments associated with a page, which belong in the image stream (ISO 32000-2 §7.4.7); they are ignored.");
                continue;
            }

            if (!ProcessUnassociated(segment, globals.Slice(segment.DataStart, segment.DataLength), list))
            {
                break;
            }
        }
    }

    private DecodedImage? Run(ReadOnlySpan<byte> page, ImageFilterContext context, int width, int height)
    {
        DecodedImageBuilder? image = null;
        try
        {
            if (!context.TryCreateImage(width, height, 1, 1, out image))
            {
                return null;
            }

            var bitmap = new Jbig2Bitmap(image.Samples, width, height, image.Stride);
            using Jbig2SegmentList list = Jbig2SegmentList.Parse(page, _reporter, "page");
            uint pageNumber = SelectPage(list.Segments);
            foreach (Jbig2Segment segment in list.Segments)
            {
                ReadOnlySpan<byte> data = page.Slice(segment.DataStart, segment.DataLength);
                if (segment.Page == 0)
                {
                    if (!ProcessUnassociated(segment, data, list))
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

                if (!ProcessPageSegment(segment, data, list, bitmap))
                {
                    break;
                }
            }

            if (_reporter.Undecodable)
            {
                return null;
            }

            int unused = _auxiliary.Count;
            if (unused > 0)
            {
                _reporter.Report(
                    DiagnosticCodes.Jbig2IntermediateRegionUnused,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 page has {unused} intermediate region(s) no refinement region refers to (ITU-T T.88 §8.2 step 6); they are dropped."));
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
    private bool ProcessUnassociated(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list)
    {
        switch (segment.Type)
        {
            case Jbig2SegmentType.SymbolDictionary:
                ProcessSymbolDictionary(segment, data, list);
                return true;
            case Jbig2SegmentType.PatternDictionary:
                if (Jbig2PatternDictionary.Decode(segment, data, _statistics, _reporter, _maxPixels) is { } patterns)
                {
                    _results[segment.Number] = patterns;
                }

                return true;
            case Jbig2SegmentType.Tables:
                if (Jbig2HuffmanTable.TryParse(data, out Jbig2HuffmanTable? table, out string? error))
                {
                    _results[segment.Number] = table!;
                }
                else
                {
                    _reporter.Report(
                        DiagnosticCodes.Jbig2TableInvalid,
                        DiagnosticSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 code table segment {segment.Number} {error}; segments that refer to it are not decoded."));
                }

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
    private bool ProcessPageSegment(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list, Jbig2Bitmap bitmap)
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
                RequirePage();
                ProcessTextRegion(segment, data, list, bitmap);
                return true;
            case Jbig2SegmentType.IntermediateHalftoneRegion:
            case Jbig2SegmentType.ImmediateHalftoneRegion:
            case Jbig2SegmentType.ImmediateLosslessHalftoneRegion:
                RequirePage();
                ProcessHalftoneRegion(segment, data, list, bitmap);
                return true;
            case Jbig2SegmentType.IntermediateRefinementRegion:
            case Jbig2SegmentType.ImmediateRefinementRegion:
            case Jbig2SegmentType.ImmediateLosslessRefinementRegion:
                RequirePage();
                ProcessRefinementRegion(segment, data, list, bitmap);
                return true;
            case Jbig2SegmentType.EndOfPage:
                ReportEnd("an end-of-page segment");
                return false;
            case Jbig2SegmentType.EndOfStripe:
                // T.88 §7.4.10: the end row only bounds later regions; the page buffer is the image's whole height already.
                return true;
            default:
                return ProcessUnassociated(segment, data, list);
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

    /// <summary>The region segment information field (§7.4.1), or false when the data is too short or uses colour.</summary>
    private bool TryReadRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, int headerLength, string kind, out RegionInfo region)
    {
        region = default;
        if (data.Length < headerLength)
        {
            ReportInvalid(segment, kind, string.Create(CultureInfo.InvariantCulture, $"has {data.Length} data bytes, fewer than its {headerLength}-byte data header (ITU-T T.88 §7.4)"));
            return false;
        }

        byte flags = data[16];
        if ((flags & 0x08) != 0)
        {
            _reporter.ReportUnsupported("colour (the region colour extension flag, ITU-T T.88 Amendment 3, forbidden by ISO 32000-2 §7.4.7)", paintsPage: true);
            return false;
        }

        int op = flags & 7;
        if (op > 4)
        {
            ReportInvalid(segment, kind, string.Create(CultureInfo.InvariantCulture, $"has the external combination operator {op}, which ITU-T T.88 §7.4.1.5 does not define; OR is used"));
            op = 0;
        }

        region = new RegionInfo(
            BinaryPrimitives.ReadUInt32BigEndian(data),
            BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[12..]),
            (Jbig2CombinationOperator)op);
        return true;
    }

    /// <summary>Whether a region of <paramref name="width"/> x <paramref name="height"/> fits the limits; reports when not.</summary>
    private bool FitsLimits(in Jbig2Segment segment, string kind, long width, long height)
    {
        if (width * height <= _maxPixels && width <= int.MaxValue - 7 && (long)Jbig2Bitmap.StrideOf((int)Math.Min(int.MaxValue - 7, width)) * height <= Math.Min(_maxBytes, Array.MaxLength))
        {
            return true;
        }

        _reporter.Report(
            DiagnosticCodes.Jbig2LimitExceeded,
            DiagnosticSeverity.Error,
            string.Create(CultureInfo.InvariantCulture, $"The JBIG2 {kind} segment {segment.Number} is {width} x {height} pixels, more than the image limits allow; it is not decoded."));
        return false;
    }

    /// <summary>T.88 §7.4.6 and §8.2 step 5 a) and b).</summary>
    private void ProcessGenericRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2Bitmap page)
    {
        const string kind = "generic region";
        if (!TryReadRegion(segment, data, 18, kind, out RegionInfo info))
        {
            return;
        }

        byte flags = data[17];
        if ((flags & 0x10) != 0)
        {
            _reporter.ReportUnsupported("extended templates (EXTTEMPLATE, ITU-T T.88 Amendment 2)", paintsPage: true);
            return;
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
                ReportInvalid(segment, kind, "ends inside its AT flags (ITU-T T.88 §7.4.6.3)");
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
                    ReportInvalid(segment, kind, string.Create(CultureInfo.InvariantCulture, $"places an AT pixel at ({atX}, {atY}), outside the field of ITU-T T.88 Figure 7; it reads pixels not decoded yet as 0"));
                    break;
                }
            }
        }

        ReadOnlySpan<byte> coded = data[position..];
        long rows = info.Height;
        if (segment.UnknownLength)
        {
            rows = BinaryPrimitives.ReadUInt32BigEndian(data[^4..]);
            coded = data[position..^6];
            if (rows > info.Height)
            {
                ReportInvalid(segment, kind, string.Create(CultureInfo.InvariantCulture, $"has the row count {rows}, more than its height {info.Height} (ITU-T T.88 §7.4.6.4); the height is used"));
                rows = info.Height;
            }
        }

        if (info.Width == 0 || rows == 0 || !FitsLimits(segment, kind, info.Width, rows))
        {
            return;
        }

        bool intermediate = segment.Type == Jbig2SegmentType.IntermediateGenericRegion;
        using var region = new RegionBuffer(intermediate, (int)info.Width, (int)rows);
        if (mmr)
        {
            MmrResult result = Jbig2GenericRegion.DecodeMmr(coded, region.View);
            ReportMmr(segment, result, (int)rows);
        }
        else
        {
            _statistics.ResetGeneric();
            var decoder = new MqDecoder(coded);
            Jbig2GenericRegion.Decode(ref decoder, _statistics.Generic, parameters, region.View, default);
        }

        Place(segment, intermediate, region, info, page);
    }

    /// <summary>T.88 §7.4.3 and §8.2: a text region from the referred symbol dictionaries and code tables.</summary>
    private void ProcessTextRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list, Jbig2Bitmap page)
    {
        const string kind = "text region";
        if (!TryReadRegion(segment, data, RegionInfoLength + 2, kind, out RegionInfo info))
        {
            return;
        }

        int flags = BinaryPrimitives.ReadUInt16BigEndian(data[RegionInfoLength..]);
        bool huffman = (flags & 1) != 0;
        bool refine = (flags & 2) != 0;
        int logStrips = (flags >> 2) & 3;
        int corner = (flags >> 4) & 3;
        bool transposed = (flags & 0x40) != 0;
        var combination = (Jbig2CombinationOperator)((flags >> 7) & 3);
        int defaultPixel = (flags >> 9) & 1;
        int dsOffset = (flags >> 10) & 31;
        if (dsOffset >= 16)
        {
            dsOffset -= 32;
        }

        int refinementTemplate = (flags >> 15) & 1;
        int position = RegionInfoLength + 2;
        int huffmanFlags = 0;
        if (huffman)
        {
            if (data.Length < position + 2)
            {
                ReportInvalid(segment, kind, "ends inside its Huffman flags (ITU-T T.88 §7.4.3.1.2)");
                return;
            }

            huffmanFlags = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            position += 2;
        }

        var refinement = Jbig2RefinementParameters.Nominal(refinementTemplate);
        if (refine && refinementTemplate == 0)
        {
            if (data.Length < position + 4)
            {
                ReportInvalid(segment, kind, "ends inside its refinement AT flags (ITU-T T.88 §7.4.3.1.3)");
                return;
            }

            refinement = refinement with { AtX1 = (sbyte)data[position], AtY1 = (sbyte)data[position + 1], AtX2 = (sbyte)data[position + 2], AtY2 = (sbyte)data[position + 3] };
            position += 4;
        }

        if (data.Length < position + 4)
        {
            ReportInvalid(segment, kind, "ends before its number of symbol instances (ITU-T T.88 §7.4.3.1.4)");
            return;
        }

        long instances = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        position += 4;

        var symbols = new List<Jbig2Image>();
        var tables = new List<Jbig2HuffmanTable>();
        long symbolTotal = 0;
        foreach (uint number in list.ReferredTo(segment))
        {
            switch (Find(number))
            {
                case Jbig2SymbolDictionary dictionary:
                    symbolTotal += dictionary.Exported.Length;
                    if (symbolTotal <= Jbig2SymbolDictionary.MaxSymbols)
                    {
                        symbols.AddRange(dictionary.Exported);
                    }

                    break;
                case Jbig2HuffmanTable table:
                    tables.Add(table);
                    break;
                case null:
                    ReportMissing(segment, kind, number);
                    break;
            }
        }

        if (symbolTotal > Jbig2SymbolDictionary.MaxSymbols)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2LimitExceeded,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 text region segment {segment.Number} refers to {symbolTotal} symbols, more than the {Jbig2SymbolDictionary.MaxSymbols} allowed; it is not decoded."));
            return;
        }

        if (!FitsLimits(segment, kind, info.Width, info.Height))
        {
            return;
        }

        int symbolCodeLength = Jbig2SymbolDictionary.CeilLog2(symbols.Count);
        _statistics.ResetGeneric();
        _statistics.ResetRefinement();
        _statistics.ResetIntegers(huffman ? 0 : symbolCodeLength);
        var coder = new Jbig2Coder(data[position..], huffman, _statistics);
        var parameters = new Jbig2TextParameters
        {
            Refine = refine,
            Instances = instances,
            LogStrips = logStrips,
            Symbols = symbols,
            SymbolCount = symbols.Count,
            SymbolCodeLength = symbolCodeLength,
            DefaultPixel = defaultPixel,
            CombinationOperator = combination,
            Transposed = transposed,
            ReferenceCorner = corner,
            DsOffset = dsOffset,
            Refinement = refinement,
        };
        if (huffman)
        {
            if (!TrySelectTextTables(huffmanFlags, refine, tables, ref parameters))
            {
                _reporter.Report(
                    DiagnosticCodes.Jbig2TableInvalid,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 text region segment {segment.Number} selects a reserved Huffman table or a custom table it does not refer to (ITU-T T.88 §7.4.3.1.6); it is not decoded."));
                return;
            }

            Jbig2HuffmanTable? codes = ReadSymbolCodes(ref coder.Bits, symbols.Count);
            if (codes is null)
            {
                ReportInvalid(segment, kind, "has a symbol ID Huffman table that cannot be decoded (ITU-T T.88 §7.4.3.1.7)");
                return;
            }

            parameters = parameters with { SymbolCodes = codes };
        }

        bool intermediate = segment.Type == Jbig2SegmentType.IntermediateTextRegion;
        using var region = new RegionBuffer(intermediate, (int)info.Width, (int)info.Height);
        if (defaultPixel != 0)
        {
            region.View.Fill(1);
        }

        Jbig2TextResult result = Jbig2TextRegion.Decode(ref coder, parameters, region.View, _maxPixels, 4 * _maxPixels);
        ReportText(segment, result);
        Place(segment, intermediate, region, info, page);
    }

    /// <summary>§7.4.3.1.6: the text region's tables, custom ones in the order FS, DS, DT, RDW, RDH, RDX, RDY, RSIZE.</summary>
    private static bool TrySelectTextTables(int flags, bool refine, List<Jbig2HuffmanTable> tables, ref Jbig2TextParameters parameters)
    {
        int custom = 0;
        bool ok = true;
        Jbig2HuffmanTable? Pick(int selector, int customSelector, ReadOnlySpan<int> standard, ref int custom, ref bool ok)
        {
            if (selector == customSelector)
            {
                if (custom < tables.Count)
                {
                    return tables[custom++];
                }
            }
            else if (selector < standard.Length)
            {
                return Jbig2HuffmanTable.Standard(standard[selector]);
            }

            ok = false;
            return null;
        }

        parameters = parameters with
        {
            FirstS = Pick(flags & 3, 3, [6, 7], ref custom, ref ok),
            DeltaS = Pick((flags >> 2) & 3, 3, [8, 9, 10], ref custom, ref ok),
            DeltaT = Pick((flags >> 4) & 3, 3, [11, 12, 13], ref custom, ref ok),
        };
        if (refine)
        {
            parameters = parameters with
            {
                RefinementDeltaWidth = Pick((flags >> 6) & 3, 3, [14, 15], ref custom, ref ok),
                RefinementDeltaHeight = Pick((flags >> 8) & 3, 3, [14, 15], ref custom, ref ok),
                RefinementX = Pick((flags >> 10) & 3, 3, [14, 15], ref custom, ref ok),
                RefinementY = Pick((flags >> 12) & 3, 3, [14, 15], ref custom, ref ok),
                RefinementSize = Pick((flags >> 14) & 1, 1, [1], ref custom, ref ok),
            };
        }

        return ok;
    }

    /// <summary>§7.4.3.1.7: the run-length coded symbol ID code lengths, then SBSYMCODES by B.3; null when the table is damaged.</summary>
    private static Jbig2HuffmanTable? ReadSymbolCodes(ref Jbig2BitReader bits, int symbolCount)
    {
        Span<int> runLengths = stackalloc int[35];
        for (int i = 0; i < 35; i++)
        {
            runLengths[i] = (int)bits.ReadBits(4);
        }

        Jbig2HuffmanTable runCodes = Jbig2HuffmanTable.FromCodeLengths(runLengths);
        int[] lengths = new int[symbolCount];
        int index = 0;
        int previous = -1;
        while (index < symbolCount)
        {
            long code = runCodes.Decode(ref bits);
            if (code is Jbig2HuffmanTable.Invalid or Jbig2HuffmanTable.Oob || bits.IsExhausted)
            {
                return null;
            }

            int repeat;
            int length;
            switch (code)
            {
                case < 32:
                    (length, repeat) = ((int)code, 1);
                    break;
                case 32:
                    if (previous < 0)
                    {
                        return null;
                    }

                    (length, repeat) = (previous, 3 + (int)bits.ReadBits(2));
                    break;
                case 33:
                    (length, repeat) = (0, 3 + (int)bits.ReadBits(3));
                    break;
                default:
                    (length, repeat) = (0, 11 + (int)bits.ReadBits(7));
                    break;
            }

            for (int i = 0; i < repeat && index < symbolCount; i++)
            {
                lengths[index++] = length;
            }

            previous = length;
        }

        bits.Align();
        return Jbig2HuffmanTable.FromCodeLengths(lengths);
    }

    /// <summary>T.88 §7.4.5 and §8.2: a halftone region drawn from its pattern dictionary.</summary>
    private void ProcessHalftoneRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list, Jbig2Bitmap page)
    {
        const string kind = "halftone region";
        if (!TryReadRegion(segment, data, RegionInfoLength + 21, kind, out RegionInfo info))
        {
            return;
        }

        byte flags = data[RegionInfoLength];
        ReadOnlySpan<byte> header = data[(RegionInfoLength + 1)..];
        int op = (flags >> 4) & 7;
        var parameters = new Jbig2HalftoneParameters
        {
            Mmr = (flags & 1) != 0,
            Template = (flags >> 1) & 3,
            EnableSkip = (flags & 8) != 0,
            CombinationOperator = op > 4 ? Jbig2CombinationOperator.Or : (Jbig2CombinationOperator)op,
            DefaultPixel = (flags >> 7) & 1,
            GridWidth = BinaryPrimitives.ReadUInt32BigEndian(header),
            GridHeight = BinaryPrimitives.ReadUInt32BigEndian(header[4..]),
            GridX = BinaryPrimitives.ReadInt32BigEndian(header[8..]),
            GridY = BinaryPrimitives.ReadInt32BigEndian(header[12..]),
            VectorX = BinaryPrimitives.ReadUInt16BigEndian(header[16..]),
            VectorY = BinaryPrimitives.ReadUInt16BigEndian(header[18..]),
        };

        Jbig2PatternDictionary? patterns = null;
        foreach (uint number in list.ReferredTo(segment))
        {
            object? found = Find(number);
            if (found is Jbig2PatternDictionary dictionary)
            {
                patterns ??= dictionary;
            }
            else if (found is null)
            {
                ReportMissing(segment, kind, number);
            }
        }

        if (patterns is null || patterns.Patterns.Length == 0)
        {
            ReportInvalid(segment, kind, "refers to no pattern dictionary (ITU-T T.88 §7.4.5.2); it is not decoded");
            return;
        }

        if (!FitsLimits(segment, kind, info.Width, info.Height))
        {
            return;
        }

        bool intermediate = segment.Type == Jbig2SegmentType.IntermediateHalftoneRegion;
        using var region = new RegionBuffer(intermediate, (int)info.Width, (int)info.Height);
        if (parameters.DefaultPixel != 0)
        {
            region.View.Fill(1);
        }

        uint segmentNumber = segment.Number;
        Jbig2Reporter reporter = _reporter;
        bool decoded = Jbig2HalftoneRegion.Decode(
            data[(RegionInfoLength + 21)..],
            parameters,
            patterns,
            _statistics,
            region.View,
            (code, severity, what) => reporter.Report(code, severity, string.Create(CultureInfo.InvariantCulture, $"The JBIG2 halftone region segment {segmentNumber} {what}.")),
            _maxPixels);
        if (!decoded)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2LimitExceeded,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 halftone region segment {segment.Number} has a grid of {parameters.GridWidth} x {parameters.GridHeight} cells, more than the image limits allow; it is not decoded."));
            return;
        }

        Place(segment, intermediate, region, info, page);
    }

    /// <summary>T.88 §7.4.7 and §8.2 steps 5 c) to e): refines an intermediate region's buffer, or the page itself.</summary>
    private void ProcessRefinementRegion(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list, Jbig2Bitmap page)
    {
        const string kind = "refinement region";
        if (!TryReadRegion(segment, data, RegionInfoLength + 1, kind, out RegionInfo info))
        {
            return;
        }

        byte flags = data[RegionInfoLength];
        int template = flags & 1;
        var parameters = Jbig2RefinementParameters.Nominal(template) with { TypicalPrediction = (flags & 2) != 0 };
        int position = RegionInfoLength + 1;
        if (template == 0)
        {
            if (data.Length < position + 4)
            {
                ReportInvalid(segment, kind, "ends inside its AT flags (ITU-T T.88 §7.4.7.3)");
                return;
            }

            parameters = parameters with { AtX1 = (sbyte)data[position], AtY1 = (sbyte)data[position + 1], AtX2 = (sbyte)data[position + 2], AtY2 = (sbyte)data[position + 3] };
            position += 4;
        }

        AuxiliaryRegion? referred = null;
        uint referredNumber = 0;
        foreach (uint number in list.ReferredTo(segment))
        {
            if (_auxiliary.TryGetValue(number, out AuxiliaryRegion? auxiliary))
            {
                referred = auxiliary;
                referredNumber = number;
                break;
            }
        }

        if (referred is null && segment.ReferredCount > 0)
        {
            _reporter.Report(
                DiagnosticCodes.Jbig2RefinementReferenceMissing,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The JBIG2 refinement region segment {segment.Number} refers to no intermediate region still held (ITU-T T.88 §7.4.7.4); it refines the page instead."));
        }

        if (info.Width == 0 || info.Height == 0 || !FitsLimits(segment, kind, info.Width, info.Height))
        {
            return;
        }

        bool intermediate = segment.Type == Jbig2SegmentType.IntermediateRefinementRegion;
        using var region = new RegionBuffer(intermediate, (int)info.Width, (int)info.Height);
        using var pageCopy = new RegionBuffer(false, referred is null ? (int)info.Width : 0, referred is null ? (int)info.Height : 0);
        Jbig2Bitmap reference;
        if (referred is not null)
        {
            reference = referred.Image.View;
            _auxiliary.Remove(referredNumber);
        }
        else
        {
            // §7.4.7.4: the page buffer as composed so far, restricted to the region's rectangle (outside the page reads 0).
            reference = pageCopy.View;
            reference.Compose(page, -(long)info.X, -(long)info.Y, Jbig2CombinationOperator.Replace);
            if (info.Operator != Jbig2CombinationOperator.Replace)
            {
                ReportInvalid(segment, kind, "refines the page with an external combination operator other than REPLACE (ITU-T T.88 §7.4.7.5 step 1); its own operator is used");
            }
        }

        _statistics.ResetRefinement();
        var decoder = new MqDecoder(data[position..]);
        Jbig2RefinementRegion.Decode(ref decoder, _statistics.Refinement, parameters, reference, region.View);

        // A refinement of the page itself replaces it whatever the page's default operator (§7.4.7.5 step 1).
        Place(segment, intermediate, region, info, page, checkOperator: referred is not null);
    }

    /// <summary>§8.2 step 5: an intermediate region goes to an auxiliary buffer, an immediate one onto the page with its operator.</summary>
    private void Place(in Jbig2Segment segment, bool intermediate, in RegionBuffer region, in RegionInfo info, Jbig2Bitmap page, bool checkOperator = true)
    {
        if (intermediate)
        {
            _auxiliary[segment.Number] = new AuxiliaryRegion(region.Image!);
            return;
        }

        page.Compose(region.View, info.X, info.Y, checkOperator ? Operator(segment, info.Operator) : info.Operator);
    }

    /// <summary>§7.4.2: a symbol dictionary from its referred dictionaries and tables.</summary>
    private void ProcessSymbolDictionary(in Jbig2Segment segment, ReadOnlySpan<byte> data, Jbig2SegmentList list)
    {
        var inputs = new List<Jbig2SymbolDictionary>();
        var tables = new List<Jbig2HuffmanTable>();
        foreach (uint number in list.ReferredTo(segment))
        {
            switch (Find(number))
            {
                case Jbig2SymbolDictionary dictionary:
                    inputs.Add(dictionary);
                    break;
                case Jbig2HuffmanTable table:
                    tables.Add(table);
                    break;
                case null:
                    ReportMissing(segment, "symbol dictionary", number);
                    break;
            }
        }

        if (Jbig2SymbolDictionary.Decode(segment, data, inputs, tables, _statistics, _reporter, _maxPixels) is { } decoded)
        {
            _results[segment.Number] = decoded;
        }
    }

    /// <summary>A referred segment's result: this stream's first, then the globals'.</summary>
    private object? Find(uint number) =>
        _results.TryGetValue(number, out object? result) ? result
        : _globals is not null && _globals.TryGetValue(number, out result) ? result
        : null;

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

    private void ReportText(in Jbig2Segment segment, Jbig2TextResult result)
    {
        switch (result)
        {
            case Jbig2TextResult.Truncated:
                _reporter.Report(
                    DiagnosticCodes.Jbig2RegionDataTruncated,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The data of JBIG2 text region segment {segment.Number} ends or becomes invalid before its last symbol instance; the instances decoded so far are drawn."));
                break;
            case Jbig2TextResult.SymbolIdOutOfRange:
                _reporter.Report(
                    DiagnosticCodes.Jbig2SymbolIdOutOfRange,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 text region segment {segment.Number} has a symbol instance whose ID is beyond its symbols (ITU-T T.88 §6.4.10); it is drawn as an empty symbol."));
                break;
            case Jbig2TextResult.LimitExceeded:
                _reporter.Report(
                    DiagnosticCodes.Jbig2LimitExceeded,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The JBIG2 text region segment {segment.Number} has more symbol instance pixels, or a larger refined instance, than the image limits allow; decoding of the region stops there."));
                break;
        }
    }

    private void ReportMissing(in Jbig2Segment segment, string kind, uint number) => _reporter.Report(
        DiagnosticCodes.Jbig2ReferredSegmentMissing,
        DiagnosticSeverity.Error,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 {kind} segment {segment.Number} refers to segment {number}, which is absent or could not be decoded (ITU-T T.88 §7.2.5); it is decoded without it."));

    private void ReportInvalid(in Jbig2Segment segment, string kind, string what) => _reporter.Report(
        DiagnosticCodes.Jbig2SegmentInvalid,
        DiagnosticSeverity.Error,
        string.Create(CultureInfo.InvariantCulture, $"The JBIG2 {kind} segment {segment.Number} {what}."));

    /// <summary>The region segment information field (§7.4.1).</summary>
    private readonly record struct RegionInfo(uint Width, uint Height, uint X, uint Y, Jbig2CombinationOperator Operator);

    /// <summary>A region's bitmap: kept (an auxiliary buffer) for an intermediate region, else pooled for the time of the segment.</summary>
    private readonly struct RegionBuffer : IDisposable
    {
        private readonly byte[]? _rented;
        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;

        public RegionBuffer(bool keep, int width, int height)
        {
            _width = width;
            _height = height;
            _stride = Jbig2Bitmap.StrideOf(width);
            if (keep)
            {
                Image = new Jbig2Image(width, height);
            }
            else if (width > 0 && height > 0)
            {
                int length = _stride * height;
                _rented = ArrayPool<byte>.Shared.Rent(length);
                _rented.AsSpan(0, length).Clear();
            }
        }

        public Jbig2Image? Image { get; }

        public Jbig2Bitmap View => Image is not null ? Image.View : _rented is null ? default : new Jbig2Bitmap(_rented, _width, _height, _stride);

        public void Dispose()
        {
            if (_rented is not null)
            {
                ArrayPool<byte>.Shared.Return(_rented);
            }
        }
    }

    /// <summary>An intermediate region's auxiliary buffer (§8.2 step 5 b).</summary>
    private sealed class AuxiliaryRegion(Jbig2Image image)
    {
        public Jbig2Image Image => image;
    }
}
