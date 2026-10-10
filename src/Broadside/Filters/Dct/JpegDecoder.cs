using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters.Codecs;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>
/// Decodes one JPEG interchange-format stream: reads the marker segments, decodes the scans' entropy-coded data into quantized
/// coefficients, and turns MCU rows into output rows (inverse DCT, upsampling by replication, colour transform).
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.81 Annex B (syntax), Annex D (arithmetic decoding), Annex E (decoder control: §E.2.4 restart handling), Annex F
/// (§F.2 sequential decoding, baseline and extended, 8 and 12 bits, Huffman and arithmetic), Annex G (progressive decoding),
/// Annex A (geometry, IDCT); ISO 32000-2 §7.4.8 and Table 13 (colour transform); Adobe Technical Note #5116.
/// </para>
/// <para>
/// Coefficient path. A sequential frame whose first scan holds every component (the usual baseline file) streams: one MCU row
/// of coefficients is decoded and turned into output rows at a time. Any other layout (one scan per component, every
/// progressive frame) is buffered: the whole image's coefficients are stored, every scan adds to them (DC first and refinement,
/// AC first and refinement, Huffman- or arithmetic-coded), and the output rows are produced after the last scan. The scan
/// decoders only write coefficients; the output stage (IDCT, upsampling, colour, the 12-bit reduction) is shared.
/// </para>
/// <para>
/// One instance decodes one image at a time on one thread; <see cref="Rent"/> and <see cref="Return"/> keep one per thread, so a
/// decode allocates nothing once warm except the pooled per-image buffers. Lenient: every deviation is reported once per image
/// through the filter context and decoding goes on where it can; missing blocks are left at zero (mid-grey).
/// </para>
/// </remarks>
internal sealed class JpegDecoder
{
    /// <summary>The most components in one scan (ITU-T T.81 §B.2.3, Table B.3: Ns 1 to 4).</summary>
    private const int MaxScanComponents = 4;

    /// <summary>
    /// The most components in a frame: T.81 §B.2.2 allows 255, a PDF colour space has at most 32 (ISO 32000-2 Annex C, the image
    /// buffer's <see cref="ImageGeometry.MaxComponents"/>). More than four are coded in several scans.
    /// </summary>
    private const int MaxFrameComponents = ImageGeometry.MaxComponents;
    private const int DcStatisticsBins = 64;
    private const int AcStatisticsBins = 256;

    [ThreadStatic]
    private static JpegDecoder? _cached;

    private readonly HuffmanTable[] _dcTables = [new(), new(), new(), new()];
    private readonly HuffmanTable[] _acTables = [new(), new(), new(), new()];
    private readonly int[][] _quantization = [new int[64], new int[64], new int[64], new int[64]];
    private readonly bool[] _quantizationDefined = new bool[4];
    private readonly bool[] _quantizationWide = new bool[4];
    private readonly int[] _scanComponents = new int[MaxScanComponents];
    private readonly CodecReporter _reporter = new();
    private readonly byte[] _dcStatistics = new byte[4 * DcStatisticsBins];
    private readonly byte[] _acStatistics = new byte[4 * AcStatisticsBins];
    private readonly byte[] _dcLower = [0, 0, 0, 0];
    private readonly byte[] _dcUpper = [1, 1, 1, 1];
    private readonly byte[] _acSplit = [5, 5, 5, 5];

    private int _position;
    private bool _frameFound;
    private int _componentCount;
    private JpegComponent[] _components = [new(), new(), new(), new()];
    private int _hMax;
    private int _vMax;
    private int _mcusX;
    private int _mcusY;
    private int _restartInterval;
    private int _adobeTransform;
    private bool _adobe;
    private int _scanLength;
    private int _scansDecoded;
    private bool _streaming;
    private bool _storageReady;
    private int _firstMissingRow;
    private bool _truncated;
    private OutputKind _output;
    private bool _progressive;
    private bool _arithmetic;
    private int _precision = 8;
    private ScanKind _scanKind;
    private int _spectralStart;
    private int _spectralEnd = 63;
    private int _approximationLow;
    private int _eobRun;
    private byte _fixedBin = ArithmeticDecoder.FixedState;

    private enum OutputKind
    {
        Gray,
        Interleaved,
        YccToRgb,
        YcckToCmyk,
    }

    /// <summary>What a scan codes (ITU-T T.81 §G.1.1.1: spectral selection Ss..Se, successive approximation Ah, Al).</summary>
    private enum ScanKind
    {
        /// <summary>A sequential scan: every coefficient of each block, at full precision.</summary>
        Sequential,

        /// <summary>A progressive DC scan with Ah = 0: the DC coefficients, shifted left by Al.</summary>
        DcFirst,

        /// <summary>A progressive DC scan with Ah != 0: one more bit of each DC coefficient.</summary>
        DcRefine,

        /// <summary>A progressive AC scan with Ah = 0: the band Ss..Se of one component, shifted left by Al.</summary>
        AcFirst,

        /// <summary>A progressive AC scan with Ah != 0: one more bit of every coefficient in the band.</summary>
        AcRefine,
    }

    /// <summary>Gets the number of samples per row (X).</summary>
    public int Width { get; private set; }

    /// <summary>Gets the number of rows (Y, or the DNL value when Y is 0).</summary>
    public int Height { get; private set; }

    /// <summary>Gets the number of components (Nf).</summary>
    public int ComponentCount => _componentCount;

    /// <summary>Gets the APP14 transform code, or <see langword="null"/> without an Adobe APP14 segment.</summary>
    public int? AdobeTransform => _adobe ? _adobeTransform : null;

    /// <summary>Gets a value indicating whether a YCbCr or YCCK transform is applied.</summary>
    public bool ColorTransformApplied => _output is OutputKind.YccToRgb or OutputKind.YcckToCmyk;

    /// <summary>Gets the colour model of the output.</summary>
    public ImageColorModel ColorModel => _componentCount switch
    {
        1 => ImageColorModel.Gray,
        3 => ImageColorModel.Rgb,
        4 => ImageColorModel.Cmyk,
        _ => ImageColorModel.Unknown,
    };

    /// <summary>Gets the number of rows decoded from data; the rows below are mid-grey.</summary>
    public int DecodedRows => (int)Math.Min(Height, (long)_firstMissingRow * 8 * _vMax);

    /// <summary>Returns this thread's decoder, or a new one when it is in use.</summary>
    /// <returns>The decoder.</returns>
    public static JpegDecoder Rent()
    {
        JpegDecoder decoder = _cached ?? new JpegDecoder();
        _cached = null;
        return decoder;
    }

    /// <summary>Releases the per-image buffers and keeps the decoder for this thread's next image.</summary>
    /// <param name="decoder">The decoder from <see cref="Rent"/>.</param>
    public static void Return(JpegDecoder decoder)
    {
        decoder.Reset();
        _cached = decoder;
    }

    /// <summary>Reads the marker segments up to the first scan header: tables, restart interval, APP14, the frame.</summary>
    /// <param name="data">The JPEG data.</param>
    /// <param name="context">Where deviations are reported.</param>
    /// <param name="silent">Whether to report nothing (a header-only read before the decode reports them).</param>
    /// <param name="fallbackHeight">The image dictionary's Height, for a frame whose Y is 0 without a DNL segment.</param>
    /// <returns><see langword="false"/> when there is no decodable frame and scan.</returns>
    /// <remarks>ITU-T T.81 §B.2.1, Figure B.2; §B.2.2 to §B.2.4.</remarks>
    public bool ReadHeaders(ReadOnlySpan<byte> data, FilterContext context, bool silent, int fallbackHeight)
    {
        _reporter.Reset(context, silent);
        int start = data.IndexOf([(byte)0xFF, JpegMarkers.Soi]);
        if (start < 0)
        {
            Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has no SOI marker; it is not decoded.");
            return false;
        }

        if (start > 0)
        {
            Report(DiagnosticCodes.DctLeadingJunk, DiagnosticSeverity.Warning, Invariant($"{start} bytes before the SOI marker of the DCT data were skipped."));
        }

        int position = start + 2;
        while (true)
        {
            int marker = NextMarker(data, ref position);
            switch (marker)
            {
                case < 0:
                case JpegMarkers.Eoi:
                    if (_frameFound)
                    {
                        Report(DiagnosticCodes.DctTruncated, DiagnosticSeverity.Warning, "The DCT data ends before its first scan; it is not decoded.");
                    }
                    else
                    {
                        Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has no frame header; it is not decoded.");
                    }

                    return false;
                case JpegMarkers.Soi:
                case JpegMarkers.Tem:
                case >= JpegMarkers.Rst0 and <= JpegMarkers.Rst7:
                    break;
                case JpegMarkers.Sof0 or JpegMarkers.Sof1 or JpegMarkers.Sof2 or JpegMarkers.Sof9 or JpegMarkers.Sof10:
                    {
                        ReadOnlySpan<byte> body = ReadSegment(data, ref position);
                        if (_frameFound)
                        {
                            Report(DiagnosticCodes.DctFrameRepeated, DiagnosticSeverity.Warning, "A second DCT frame header was ignored.");
                        }
                        else
                        {
                            _progressive = marker is JpegMarkers.Sof2 or JpegMarkers.Sof10;
                            _arithmetic = marker is JpegMarkers.Sof9 or JpegMarkers.Sof10;
                            if (!ReadFrame((byte)marker, body, data, position, fallbackHeight))
                            {
                                return false;
                            }
                        }

                        break;
                    }

                case JpegMarkers.Dhp:
                case JpegMarkers.Exp:
                case var sof when JpegMarkers.IsStartOfFrame((byte)sof):
                    Report(
                        DiagnosticCodes.DctProcessUnsupported,
                        DiagnosticSeverity.Error,
                        Invariant($"The DCT data uses the lossless or hierarchical process (marker FF{marker:X2}), which PDF does not use; it is not decoded."));
                    return false;
                case JpegMarkers.Sos:
                    if (!_frameFound)
                    {
                        Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has a scan before its frame header; it is not decoded.");
                        return false;
                    }

                    _position = position;
                    return true;
                default:
                    ReadTableOrMiscellaneous((byte)marker, data, ref position);
                    break;
            }
        }
    }

    /// <summary>Chooses the colour transform from the APP14 segment, the <c>ColorTransform</c> parameter and the component identifiers.</summary>
    /// <param name="colorTransform">The DecodeParms <c>ColorTransform</c> value, or -1 when absent.</param>
    /// <remarks>
    /// ISO 32000-2 §7.4.8, Table 13: the APP14 transform flag wins; otherwise the parameter; otherwise 1 for three components and 0
    /// for others; ignored for one or two components. Compatibility fallback, a documented deviation from Table 13's "shall be 1":
    /// three components identified as R, G, B (what libjpeg writes for RGB data, and libjpeg and pdf.js read so) are not transformed
    /// without APP14 or the parameter, and <c>DctColorTransformInferred</c> (Information) says so; the one real-world file of this
    /// kind in the corpora (pdf.js issue11931.pdf) differs from libjpeg-turbo by up to 135 per sample when transformed. An APP14
    /// code that does not fit the
    /// component count (2 or more with three components, 1 or more than 2 with four) is reported and read as YCbCr or YCCK, as
    /// libjpeg, pdf.js and PDFBox do (Adobe Technical Note #5116 §18: 1 = YCbCr, 2 = YCCK).
    /// </remarks>
    public void SelectColorTransform(int colorTransform)
    {
        if (_adobe && ((_componentCount == 3 && _adobeTransform > 1) || (_componentCount == 4 && _adobeTransform is 1 or > 2)))
        {
            Report(
                DiagnosticCodes.DctAdobeTransformInvalid,
                DiagnosticSeverity.Warning,
                Invariant($"The APP14 transform code {_adobeTransform} does not fit {_componentCount} components; {(_componentCount == 3 ? "YCbCr" : "YCCK")} is assumed."));
        }

        bool rgbIdentifiers = _componentCount == 3 && _components[0].Id == 'R' && _components[1].Id == 'G' && _components[2].Id == 'B';
        if (rgbIdentifiers && !_adobe && colorTransform < 0)
        {
            Report(
                DiagnosticCodes.DctColorTransformInferred,
                DiagnosticSeverity.Information,
                "The three DCT components are identified R, G, B and there is neither an APP14 segment nor a ColorTransform parameter; they are read as RGB (as libjpeg does), not with the default ColorTransform 1 of ISO 32000-2 Table 13.");
        }

        bool transform = _componentCount switch
        {
            3 when _adobe => _adobeTransform != 0,
            3 when colorTransform >= 0 => colorTransform == 1,
            3 => !rgbIdentifiers,
            4 when _adobe => _adobeTransform != 0,
            4 => colorTransform == 1,
            _ => false,
        };

        _output = _componentCount switch
        {
            1 => OutputKind.Gray,
            3 when transform => OutputKind.YccToRgb,
            4 when transform => OutputKind.YcckToCmyk,
            _ => OutputKind.Interleaved,
        };
    }

    /// <summary>Decodes the scans and writes every output row to <paramref name="sink"/>.</summary>
    /// <typeparam name="TSink">The row destination.</typeparam>
    /// <param name="data">The JPEG data given to <see cref="ReadHeaders"/>.</param>
    /// <param name="sink">The destination.</param>
    /// <param name="maxBytes">The most bytes the whole-image coefficient buffer may take.</param>
    /// <returns><see langword="false"/> when nothing could be decoded (no usable scan, a missing quantization table, too large).</returns>
    /// <remarks>ITU-T T.81 §E.2.2 and §E.2.3 (Figures E.6, E.7: decode frame, decode scan).</remarks>
    public bool Decode<TSink>(ReadOnlySpan<byte> data, ref TSink sink, long maxBytes)
        where TSink : struct, IJpegRowSink
    {
        int position = _position;
        _firstMissingRow = int.MaxValue;
        bool ended = false;
        while (!ended)
        {
            // At a scan header.
            ReadOnlySpan<byte> header = ReadSegment(data, ref position);
            bool usable = ReadScanHeader(header);
            if (usable && !_storageReady && !AllocateStorage(maxBytes))
            {
                return false;
            }

            if (usable && _streaming && _scansDecoded > 0)
            {
                Report(DiagnosticCodes.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan repeats components an earlier scan decoded; it was ignored.");
                usable = false;
            }

            if (usable && !LatchQuantization())
            {
                return false;
            }

            if (usable)
            {
                DecodeScan(data, ref position, ref sink);
                _scansDecoded++;
            }
            else
            {
                // A skipped scan was reported; its entropy-coded data is not extraneous.
                position = SkipEntropyCodedData(data, position);
            }

            // Between scans: tables and miscellaneous segments, the next scan or the end.
            while (true)
            {
                int marker = NextMarker(data, ref position);
                if (marker == JpegMarkers.Sos)
                {
                    break;
                }

                if (marker is < 0 or JpegMarkers.Eoi)
                {
                    if (marker < 0 && !_truncated && _scansDecoded > 0)
                    {
                        Report(DiagnosticCodes.DctEndMissing, DiagnosticSeverity.Warning, "The DCT data ends without the EOI marker.");
                    }

                    ended = true;
                    break;
                }

                if (JpegMarkers.IsStartOfFrame((byte)marker))
                {
                    Report(DiagnosticCodes.DctFrameRepeated, DiagnosticSeverity.Warning, "The DCT data has a second frame; only the first is decoded.");
                    ended = true;
                    break;
                }

                if (!JpegMarkers.IsStandalone((byte)marker))
                {
                    ReadTableOrMiscellaneous((byte)marker, data, ref position);
                }
            }
        }

        if (!_storageReady)
        {
            return false;
        }

        for (int c = 0; c < _componentCount; c++)
        {
            if (!_components[c].Scanned)
            {
                MarkTruncated(0, "The DCT data ends before every component was decoded; the missing samples are mid-grey.");
            }
        }

        if (!_streaming)
        {
            for (int row = 0; row < _mcusY; row++)
            {
                EmitMcuRow(row, ref sink);
            }
        }

        return true;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Finds the next marker from <paramref name="position"/>, skipping fill bytes; returns the code after FF, or -1 at the end.</summary>
    private int NextMarker(ReadOnlySpan<byte> data, ref int position)
    {
        int skipped = 0;
        int marker = -1;
        while (position < data.Length)
        {
            if (data[position] != 0xFF)
            {
                position++;
                skipped++;
                continue;
            }

            while (position + 1 < data.Length && data[position + 1] == 0xFF)
            {
                position++;
            }

            if (position + 1 >= data.Length)
            {
                position = data.Length;
                break;
            }

            byte code = data[position + 1];
            position += 2;
            if (code == 0)
            {
                skipped += 2;
                continue;
            }

            marker = code;
            break;
        }

        if (skipped > 0)
        {
            ReportExtraneous(skipped);
        }

        return marker;
    }

    private void ReportExtraneous(int count)
    {
        if (ShouldReport(DiagnosticCodes.DctExtraneousData))
        {
            Report(DiagnosticCodes.DctExtraneousData, DiagnosticSeverity.Warning, Invariant($"{count} bytes of the DCT data that are not part of a marker segment were skipped."));
        }
    }

    /// <summary>Reads a marker segment's length and returns its parameters, advancing past it; a bad length is reported.</summary>
    private ReadOnlySpan<byte> ReadSegment(ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 2 > data.Length)
        {
            position = data.Length;
            Report(DiagnosticCodes.DctTruncated, DiagnosticSeverity.Warning, "The DCT data ends inside a marker segment.");
            _truncated = true;
            return [];
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        if (length < 2)
        {
            Report(DiagnosticCodes.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT marker segment has a length below 2; the decoder looks for the next marker.");
            position += 2;
            return [];
        }

        if (position + length > data.Length)
        {
            ReadOnlySpan<byte> rest = data[(position + 2)..];
            position = data.Length;
            Report(DiagnosticCodes.DctTruncated, DiagnosticSeverity.Warning, "A DCT marker segment runs past the end of the data.");
            _truncated = true;
            return rest;
        }

        ReadOnlySpan<byte> body = data.Slice(position + 2, length - 2);
        position += length;
        return body;
    }

    /// <summary>Reads a DHT, DQT, DRI, APPn segment, or skips any other one by its length.</summary>
    private void ReadTableOrMiscellaneous(byte marker, ReadOnlySpan<byte> data, ref int position)
    {
        ReadOnlySpan<byte> body = ReadSegment(data, ref position);
        switch (marker)
        {
            case JpegMarkers.Dht:
                ReadHuffmanTables(body);
                break;
            case JpegMarkers.Dqt:
                ReadQuantizationTables(body);
                break;
            case JpegMarkers.Dri:
                if (body.Length < 2)
                {
                    Report(DiagnosticCodes.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT DRI segment is too short; it was ignored.");
                }
                else
                {
                    _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(body);
                }

                break;
            case JpegMarkers.Dac:
                ReadArithmeticConditioning(body);
                break;
            case JpegMarkers.App14:
                // Adobe Technical Note #5116 §18: "Adobe", version, flags0, flags1, transform; the last one counts.
                if (body.Length >= 12 && body.StartsWith("Adobe"u8))
                {
                    _adobe = true;
                    _adobeTransform = body[11];
                }

                break;
        }
    }

    /// <summary>Reads a DAC segment: conditioning values L and U of DC tables, Kx of AC tables.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.3, Table B.6 (0 ≤ L ≤ U ≤ 15, 1 ≤ Kx ≤ 63); §F.1.4.4.1.2 and §F.1.4.4.2 (defaults L = 0, U = 1, Kx = 5).</remarks>
    private void ReadArithmeticConditioning(ReadOnlySpan<byte> body)
    {
        for (int i = 0; i + 1 < body.Length; i += 2)
        {
            int tableClass = body[i] >> 4;
            int destination = body[i] & 15;
            int value = body[i + 1];
            if (tableClass > 1 || destination > 3 || (tableClass == 0 && (value & 15) > value >> 4) || (tableClass == 1 && value is < 1 or > 63))
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT arithmetic conditioning table is invalid; it was ignored.");
                continue;
            }

            if (tableClass == 0)
            {
                _dcLower[destination] = (byte)(value & 15);
                _dcUpper[destination] = (byte)(value >> 4);
            }
            else
            {
                _acSplit[destination] = (byte)value;
            }
        }

        if ((body.Length & 1) != 0)
        {
            Report(DiagnosticCodes.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT DAC segment has an odd length; its last byte was ignored.");
        }
    }

    /// <summary>Reads a DQT segment: one or more 8- or 16-bit tables in zig-zag order, stored in natural order.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.1, Table B.4.</remarks>
    private void ReadQuantizationTables(ReadOnlySpan<byte> body)
    {
        int i = 0;
        while (i < body.Length)
        {
            int precision = body[i] >> 4;
            int destination = body[i] & 15;
            int size = precision == 0 ? 64 : 128;
            if (precision > 1 || destination > 3 || i + 1 + size > body.Length)
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT quantization table is invalid; it was ignored.");
                return;
            }

            int[] table = _quantization[destination];
            ReadOnlySpan<byte> values = body.Slice(i + 1, size);
            for (int k = 0; k < 64; k++)
            {
                table[JpegMarkers.NaturalOrder[k]] = precision == 0 ? values[k] : BinaryPrimitives.ReadUInt16BigEndian(values[(2 * k)..]);
            }

            _quantizationDefined[destination] = true;
            _quantizationWide[destination] = precision == 1;
            i += 1 + size;
        }
    }

    /// <summary>Reads a DHT segment: one or more tables, each BITS and HUFFVAL.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.2, Table B.5; Annex C.</remarks>
    private void ReadHuffmanTables(ReadOnlySpan<byte> body)
    {
        int i = 0;
        while (i < body.Length)
        {
            if (i + 17 > body.Length)
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table is truncated; it was ignored.");
                return;
            }

            int tableClass = body[i] >> 4;
            int destination = body[i] & 15;
            ReadOnlySpan<byte> counts = body.Slice(i + 1, 16);
            int total = 0;
            foreach (byte count in counts)
            {
                total += count;
            }

            if (tableClass > 1 || destination > 3 || total > 256 || i + 17 + total > body.Length)
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table is invalid; it was ignored.");
                return;
            }

            HuffmanTable table = tableClass == 0 ? _dcTables[destination] : _acTables[destination];
            if (!table.Build(counts, body.Slice(i + 17, total), tableClass == 0 ? 15 : 255))
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table does not form a valid code; it was ignored.");
            }

            i += 17 + total;
        }
    }

    /// <summary>Reads the frame header and computes the geometry.</summary>
    /// <remarks>
    /// ITU-T T.81 §B.2.2, Table B.2 (P = 8 for baseline, 8 or 12 for the extended and progressive processes); §A.1.1; §B.2.5 (DNL).
    /// ISO 32000-2 §8.9.5.1 Table 87: 12-bit samples are delivered reduced to 8 bits, which is reported as Information.
    /// </remarks>
    private bool ReadFrame(byte marker, ReadOnlySpan<byte> body, ReadOnlySpan<byte> data, int position, int fallbackHeight)
    {
        if (body.Length < 6)
        {
            Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT frame header is truncated; the image is not decoded.");
            return false;
        }

        int precision = body[0];
        int height = BinaryPrimitives.ReadUInt16BigEndian(body[1..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(body[3..]);
        int count = body[5];
        if (precision is not (8 or 12))
        {
            Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, Invariant($"The DCT frame has a sample precision of {precision} bits; the image is not decoded."));
            return false;
        }

        if (precision == 12)
        {
            if (marker == JpegMarkers.Sof0)
            {
                Report(DiagnosticCodes.DctSegmentInvalid, DiagnosticSeverity.Warning, "A baseline DCT frame declares 12-bit samples, which only the extended and progressive processes allow; it is decoded as extended.");
            }

            Report(DiagnosticCodes.DctPrecisionReduced, DiagnosticSeverity.Information, "The DCT data has 12-bit samples; they are delivered reduced to 8 bits, as ISO 32000-2 Table 87 requires.");
        }

        if (width == 0 || count is 0 or > MaxFrameComponents || body.Length < 6 + (3 * count))
        {
            Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, Invariant($"The DCT frame header is unusable ({width} samples per line, {count} components); the image is not decoded."));
            return false;
        }

        if (count == 2)
        {
            Report(DiagnosticCodes.DctComponentCountInvalid, DiagnosticSeverity.Warning, "The DCT data has two components, which PDF does not allow; they are decoded without a colour transform.");
        }

        EnsureComponents(count);
        for (int c = 0; c < count; c++)
        {
            JpegComponent component = _components[c];
            ReadOnlySpan<byte> entry = body.Slice(6 + (3 * c), 3);
            component.Id = entry[0];
            component.H = entry[1] >> 4;
            component.V = entry[1] & 15;
            component.QuantizationTable = entry[2];
            if (component.H is < 1 or > 4 || component.V is < 1 or > 4 || component.QuantizationTable > 3)
            {
                Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "A DCT frame component has an invalid sampling factor or quantization table; the image is not decoded.");
                return false;
            }

            if (count == 1)
            {
                // One component: the MCU is one block whatever the factors say (§A.2), so the factors play no part.
                component.H = 1;
                component.V = 1;
            }
        }

        if (height == 0)
        {
            height = FindNumberOfLines(data, position);
            if (height > 0)
            {
                Report(DiagnosticCodes.DctLinesFromDnl, DiagnosticSeverity.Warning, "The DCT frame's number of lines is 0; the DNL segment's is used.");
            }
            else if (fallbackHeight > 0)
            {
                height = fallbackHeight;
                Report(DiagnosticCodes.DctLinesFromDnl, DiagnosticSeverity.Warning, "The DCT frame's number of lines is 0 and there is no DNL segment; the image dictionary's Height is used.");
            }
            else
            {
                Report(DiagnosticCodes.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT frame's number of lines is 0 and no DNL segment defines it; the image is not decoded.");
                return false;
            }
        }

        Width = width;
        Height = height;
        _precision = precision;
        _componentCount = count;
        _hMax = 1;
        _vMax = 1;
        for (int c = 0; c < count; c++)
        {
            _hMax = Math.Max(_hMax, _components[c].H);
            _vMax = Math.Max(_vMax, _components[c].V);
        }

        _mcusX = (width + (8 * _hMax) - 1) / (8 * _hMax);
        _mcusY = (height + (8 * _vMax) - 1) / (8 * _vMax);
        for (int c = 0; c < count; c++)
        {
            JpegComponent component = _components[c];
            int componentWidth = (int)((((long)width * component.H) + _hMax - 1) / _hMax);
            int componentHeight = (int)((((long)height * component.V) + _vMax - 1) / _vMax);
            component.BlocksWide = (componentWidth + 7) / 8;
            component.BlocksHigh = (componentHeight + 7) / 8;
            component.BlocksPerLine = _mcusX * component.H;
            component.BlockRows = _mcusY * component.V;
        }

        _frameFound = true;
        return true;
    }

    /// <summary>Finds a DNL segment (FF DC, length 4) after the frame header and returns its number of lines, or 0.</summary>
    private static int FindNumberOfLines(ReadOnlySpan<byte> data, int position)
    {
        ReadOnlySpan<byte> pattern = [0xFF, JpegMarkers.Dnl, 0x00, 0x04];
        int found = data[position..].IndexOf(pattern);
        return found >= 0 && position + found + 6 <= data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[(position + found + 4)..]) : 0;
    }

    /// <summary>
    /// Reads a scan header: its components, the spectral selection and successive approximation parameters, and the tables it
    /// codes with (Huffman tables, the Annex K ones for undefined tables; or arithmetic conditioning tables).
    /// </summary>
    /// <remarks>
    /// ITU-T T.81 §B.2.3, Table B.3; §G.1.1.1 (progressive: a DC scan has Ss = Se = 0 and may interleave components, an AC scan
    /// has 1 ≤ Ss ≤ Se ≤ 63 and one component; a refinement has Al = Ah - 1; Al ≤ 13). Invalid progressive parameters skip the
    /// scan; a scan that does not follow the earlier ones (an AC scan before the component's first DC scan, Ah not the Al of the
    /// scan before) is reported and decoded as given, as libjpeg does.
    /// </remarks>
    private bool ReadScanHeader(ReadOnlySpan<byte> header)
    {
        int count = header.IsEmpty ? 0 : header[0];
        if (count is < 1 or > MaxScanComponents || header.Length < 1 + (2 * count) + 3)
        {
            Report(DiagnosticCodes.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan header is invalid; the scan was skipped.");
            return false;
        }

        for (int j = 0; j < count; j++)
        {
            int id = header[1 + (2 * j)];
            int tables = header[2 + (2 * j)];
            int index = -1;
            for (int c = 0; c < _componentCount && index < 0; c++)
            {
                if (_components[c].Id == id && !_scanComponents.AsSpan(0, j).Contains(c))
                {
                    index = c;
                }
            }

            if (index < 0 || tables >> 4 > 3 || (tables & 15) > 3)
            {
                Report(DiagnosticCodes.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan names a component the frame does not have, or a table destination above 3; the scan was skipped.");
                return false;
            }

            _scanComponents[j] = index;
        }

        int spectralStart = header[1 + (2 * count)];
        int spectralEnd = header[2 + (2 * count)];
        int high = header[3 + (2 * count)] >> 4;
        int low = header[3 + (2 * count)] & 15;
        if (_progressive)
        {
            bool valid = (spectralStart == 0 ? spectralEnd == 0 : spectralEnd >= spectralStart && spectralEnd <= 63 && count == 1)
                && (high == 0 || low == high - 1)
                && low <= 13;
            if (!valid)
            {
                Report(
                    DiagnosticCodes.DctScanInvalid,
                    DiagnosticSeverity.Warning,
                    Invariant($"A progressive DCT scan has invalid parameters (Ss {spectralStart}, Se {spectralEnd}, Ah {high}, Al {low}, {count} components); the scan was skipped."));
                return false;
            }

            _scanKind = spectralStart == 0 ? (high == 0 ? ScanKind.DcFirst : ScanKind.DcRefine) : (high == 0 ? ScanKind.AcFirst : ScanKind.AcRefine);
            CheckProgression(count, spectralStart, spectralEnd, high, low);
        }
        else
        {
            if (spectralStart != 0 || spectralEnd != 63 || high != 0 || low != 0)
            {
                Report(DiagnosticCodes.DctScanInvalid, DiagnosticSeverity.Warning, "A sequential DCT scan has spectral selection or approximation parameters other than 0, 63, 0; they were ignored.");
            }

            _scanKind = ScanKind.Sequential;
            spectralStart = 0;
            spectralEnd = 63;
            low = 0;
        }

        _spectralStart = spectralStart;
        _spectralEnd = spectralEnd;
        _approximationLow = low;
        bool codesDc = _scanKind is ScanKind.Sequential or ScanKind.DcFirst;
        bool codesAc = _scanKind is ScanKind.Sequential or ScanKind.AcFirst or ScanKind.AcRefine;
        for (int j = 0; j < count; j++)
        {
            JpegComponent component = _components[_scanComponents[j]];
            int tables = header[2 + (2 * j)];
            if (_arithmetic)
            {
                component.DcConditioning = tables >> 4;
                component.AcConditioning = tables & 15;
                continue;
            }

            component.DcTable = codesDc ? Bind(_dcTables, tables >> 4, ac: false) : null;
            component.AcTable = codesAc ? Bind(_acTables, tables & 15, ac: true) : null;
        }

        _scanLength = count;
        return true;
    }

    /// <summary>Records which coefficient bits a progressive scan codes and reports a scan that does not follow the earlier ones.</summary>
    /// <remarks>ITU-T T.81 §G.1.1.1.1 (the first DC scan of a component precedes its AC scans), §G.1.1.1.2 (Ah is the Al of the scan before); libjpeg-turbo <c>coef_bits</c>.</remarks>
    private void CheckProgression(int count, int spectralStart, int spectralEnd, int high, int low)
    {
        bool bogus = false;
        for (int j = 0; j < count; j++)
        {
            int[] bits = _components[_scanComponents[j]].CoefficientBits;
            bogus |= spectralStart > 0 && bits[0] < 0;
            for (int k = spectralStart; k <= spectralEnd; k++)
            {
                bogus |= high != Math.Max(bits[k], 0);
                bits[k] = low;
            }
        }

        if (bogus)
        {
            Report(
                DiagnosticCodes.DctScanInvalid,
                DiagnosticSeverity.Warning,
                "A progressive DCT scan does not follow the scans before it (an AC scan before the first DC scan, or a refinement of bits not yet coded); it was decoded as given.");
        }
    }

    private HuffmanTable Bind(HuffmanTable[] tables, int destination, bool ac)
    {
        HuffmanTable table = tables[destination];
        if (!table.IsDefined)
        {
            StandardHuffmanTables.Install(table, ac, destination);
            Report(DiagnosticCodes.DctTableMissing, DiagnosticSeverity.Warning, "A DCT scan uses a Huffman table that was never defined; the typical table of ITU-T T.81 Annex K is used.");
        }

        return table;
    }

    /// <summary>Copies each scan component's quantization table, the first time the component is in a scan.</summary>
    private bool LatchQuantization()
    {
        for (int j = 0; j < _scanLength; j++)
        {
            JpegComponent component = _components[_scanComponents[j]];
            if (component.QuantizationLatched)
            {
                continue;
            }

            int destination = component.QuantizationTable;
            if (!_quantizationDefined[destination])
            {
                Report(DiagnosticCodes.DctTableMissing, DiagnosticSeverity.Error, "A DCT component uses a quantization table that was never defined; the image is not decoded.");
                return false;
            }

            if (_quantizationWide[destination] && _precision == 8)
            {
                Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "An 8-bit DCT frame uses a 16-bit quantization table; its values are used.");
            }

            _quantization[destination].CopyTo(component.Quantization, 0);
            component.QuantizationLatched = true;
        }

        return true;
    }

    /// <summary>Chooses streaming or buffering at the first usable scan and rents the buffers.</summary>
    private bool AllocateStorage(long maxBytes)
    {
        _streaming = !_progressive && _scanLength == _componentCount;
        long blocks = 0;
        for (int c = 0; c < _componentCount; c++)
        {
            JpegComponent component = _components[c];
            blocks += (long)component.BlocksPerLine * (_streaming ? component.V : component.BlockRows);
        }

        long bytes = blocks * 64 * sizeof(short);
        if (blocks * 64 > Array.MaxLength || bytes > maxBytes)
        {
            Report(
                DiagnosticCodes.ImageTooLarge,
                DiagnosticSeverity.Error,
                Invariant($"The DCT image is not decoded: its coefficients would take {bytes} bytes, more than the {maxBytes} allowed."));
            return false;
        }

        for (int c = 0; c < _componentCount; c++)
        {
            JpegComponent component = _components[c];
            component.Allocate(_streaming ? component.V : component.BlockRows, Width, component.H != _hMax, _precision == 12);
        }

        _storageReady = true;
        return true;
    }

    /// <summary>Decodes one scan's entropy-coded data, with its restart intervals.</summary>
    /// <remarks>
    /// ITU-T T.81 §E.2.3 to §E.2.5 (Figures E.7 to E.10); §A.2.2 (non-interleaved order: one block per MCU, the component's own
    /// block extent; every progressive AC scan), §A.2.3 (interleaved order: Hi x Vi blocks per component per MCU, padded extent);
    /// §F.2.4.4 (restart-based error recovery, here libjpeg's resynchronization); §G.1.2.2 (EOBRUN, reset with the DC predictors
    /// at each restart); §F.1.4.4 and §G.1.3 (arithmetic statistics, reset at the start of the scan and at each restart).
    /// </remarks>
    private void DecodeScan<TSink>(ReadOnlySpan<byte> data, ref int position, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        bool single = _scanLength == 1;
        JpegComponent first = _components[_scanComponents[0]];
        int mcusPerRow = single ? first.BlocksWide : _mcusX;
        int mcuRows = single ? first.BlocksHigh : _mcusY;
        for (int j = 0; j < _scanLength; j++)
        {
            _components[_scanComponents[j]].Scanned = true;
        }

        ResetEntropyState();
        var reader = new JpegBitReader(data, position);
        var arithmetic = new ArithmeticDecoder(data, position);
        bool exhausted = StartsAtMarker(data, position);
        int intervalLeft = _restartInterval;
        int expectedRestart = 0;
        bool exhaustedArithmetic = false;

        // Rows a later progressive scan leaves incomplete were decoded by the first DC scan: they still count as decoded.
        bool countsRows = _scanKind is ScanKind.Sequential or ScanKind.DcFirst;
        for (int mcuRow = 0; mcuRow < mcuRows; mcuRow++)
        {
            int frameRow = !countsRows ? int.MaxValue : single ? mcuRow / first.V : mcuRow;
            if (_streaming)
            {
                for (int c = 0; c < _componentCount; c++)
                {
                    _components[c].FirstStoredRow = mcuRow * _components[c].V;
                    _components[c].ClearCoefficients();
                }
            }

            for (int mcuColumn = 0; mcuColumn < mcusPerRow; mcuColumn++)
            {
                if (_restartInterval > 0)
                {
                    if (intervalLeft == 0)
                    {
                        exhaustedArithmetic |= arithmetic.Exhausted;
                        int end = EndOfEntropyCodedData(data, ref reader, ref arithmetic);
                        exhausted = Restart(data, end, ref expectedRestart, out int next);
                        reader = new JpegBitReader(data, next);
                        arithmetic = new ArithmeticDecoder(data, next);
                        ResetEntropyState();
                        intervalLeft = _restartInterval;
                    }

                    intervalLeft--;
                }

                if (exhausted)
                {
                    MarkTruncated(frameRow, "The DCT data ends before every block of a scan was decoded; the missing samples are mid-grey.");
                    continue;
                }

                if (single)
                {
                    DecodeUnit(ref reader, ref arithmetic, first, first.Block(mcuRow, mcuColumn));
                }
                else
                {
                    for (int j = 0; j < _scanLength; j++)
                    {
                        JpegComponent component = _components[_scanComponents[j]];
                        for (int v = 0; v < component.V; v++)
                        {
                            int row = (mcuRow * component.V) + v;
                            for (int h = 0; h < component.H; h++)
                            {
                                DecodeUnit(ref reader, ref arithmetic, component, component.Block(row, (mcuColumn * component.H) + h));
                            }
                        }
                    }
                }

                if (reader.Overread)
                {
                    exhausted = true;
                    MarkTruncated(frameRow, "The DCT data ends before every block of a scan was decoded; the missing samples are mid-grey.");
                }
            }

            if (_streaming)
            {
                EmitMcuRow(mcuRow, ref sink);
            }
        }

        if (exhaustedArithmetic || arithmetic.Exhausted)
        {
            // Arithmetic decoding goes on with zero bits at the end of the data (§D.2.6); nothing tells truncation from a lost EOI.
            MarkTruncated(int.MaxValue, "The arithmetic-coded DCT data ends without a marker; the rest of the scan was decoded from zero bits.");
        }

        position = EndOfEntropyCodedData(data, ref reader, ref arithmetic);
    }

    /// <summary>
    /// Returns where the entropy-coded data of the current interval ends. A Huffman reader stops at the bytes it loaded, and the
    /// whole bytes it did not use are reported; arithmetic data runs to the next marker (the decoder may stop before the bytes
    /// the encoder's flush wrote, §D.1.8).
    /// </summary>
    private int EndOfEntropyCodedData(ReadOnlySpan<byte> data, ref JpegBitReader reader, ref ArithmeticDecoder arithmetic)
    {
        if (!_arithmetic)
        {
            if (!reader.Overread && reader.UnusedBytes > 0)
            {
                ReportExtraneous(reader.UnusedBytes);
            }

            return reader.Position;
        }

        return SkipEntropyCodedData(data, arithmetic.Position);
    }

    /// <summary>Returns the position of the next marker from <paramref name="position"/>, skipping entropy-coded bytes (FF 00 is data), or the end.</summary>
    private static int SkipEntropyCodedData(ReadOnlySpan<byte> data, int position)
    {
        while (position + 1 < data.Length && !(data[position] == 0xFF && data[position + 1] != 0 && data[position + 1] != 0xFF))
        {
            position++;
        }

        return position + 1 < data.Length ? position : data.Length;
    }

    /// <summary>Resets the state every scan and restart interval starts with: DC predictors, EOBRUN, arithmetic statistics.</summary>
    /// <remarks>ITU-T T.81 §E.2.4 (Reset_decoder), §G.1.2.2 (EOBRUN), §F.1.4.4 and §G.1.3.1 (statistics areas and DC context set to zero).</remarks>
    private void ResetEntropyState()
    {
        _eobRun = 0;
        bool codesDc = _scanKind is ScanKind.Sequential or ScanKind.DcFirst;
        bool codesAc = _scanKind is ScanKind.Sequential or ScanKind.AcFirst or ScanKind.AcRefine;
        for (int j = 0; j < _scanLength; j++)
        {
            JpegComponent component = _components[_scanComponents[j]];
            component.Predictor = 0;
            component.DcContext = 0;
            if (!_arithmetic)
            {
                continue;
            }

            if (codesDc)
            {
                _dcStatistics.AsSpan(component.DcConditioning * DcStatisticsBins, DcStatisticsBins).Clear();
            }

            if (codesAc)
            {
                _acStatistics.AsSpan(component.AcConditioning * AcStatisticsBins, AcStatisticsBins).Clear();
            }
        }
    }

    /// <summary>Decodes one block of the current scan with the scan's entropy coder and kind.</summary>
    private void DecodeUnit(ref JpegBitReader reader, ref ArithmeticDecoder arithmetic, JpegComponent component, Span<short> block)
    {
        if (_arithmetic)
        {
            if (arithmetic.Failed)
            {
                return;
            }

            bool ok = _scanKind switch
            {
                ScanKind.Sequential => DecodeArithmeticBlock(ref arithmetic, component, block),
                ScanKind.DcFirst => DecodeArithmeticDcFirst(ref arithmetic, component, block),
                ScanKind.DcRefine => DecodeArithmeticDcRefine(ref arithmetic, block),
                ScanKind.AcFirst => DecodeArithmeticAcFirst(ref arithmetic, component, block),
                _ => DecodeArithmeticAcRefine(ref arithmetic, component, block),
            };
            if (!ok)
            {
                arithmetic.Failed = true;
                ReportInvalidData("The arithmetic-coded DCT data decodes to an impossible value; the rest of its restart interval is left as decoded so far.");
            }

            return;
        }

        switch (_scanKind)
        {
            case ScanKind.Sequential:
                DecodeBlock(ref reader, component, block);
                break;
            case ScanKind.DcFirst:
                DecodeDcFirst(ref reader, component, block);
                break;
            case ScanKind.DcRefine:
                if (reader.ReadBit() != 0)
                {
                    block[0] |= (short)(1 << _approximationLow);
                }

                break;
            case ScanKind.AcFirst:
                DecodeAcFirst(ref reader, component, block);
                break;
            default:
                DecodeAcRefine(ref reader, component, block);
                break;
        }
    }

    private void ReportInvalidData(string message)
    {
        if (ShouldReport(DiagnosticCodes.DctDataInvalid))
        {
            Report(DiagnosticCodes.DctDataInvalid, DiagnosticSeverity.Warning, message);
        }
    }

    private void ReportInvalidCode() =>
        ReportInvalidData("The DCT entropy-coded data holds an invalid Huffman code; a zero was used in its place.");

    /// <summary>Decodes one block of a sequential scan: the DC difference and the AC run/size codes up to EOB.</summary>
    /// <remarks>ITU-T T.81 §F.2.2.1 (DC), §F.2.2.2 (Figures F.13 and F.14: AC, ZRL 0xF0, EOB 0x00).</remarks>
    private void DecodeBlock(ref JpegBitReader reader, JpegComponent component, Span<short> block)
    {
        bool valid = true;
        int category = reader.Decode(component.DcTable!, ref valid);
        component.Predictor += reader.ReceiveExtend(category);
        block[0] = (short)component.Predictor;
        HuffmanTable ac = component.AcTable!;
        byte[] natural = JpegMarkers.NaturalOrder;
        for (int k = 1; k < 64; k++)
        {
            int symbol = reader.Decode(ac, ref valid);
            int run = symbol >> 4;
            int size = symbol & 15;
            if (size != 0)
            {
                k += run;
                block[natural[k]] = (short)reader.ReceiveExtend(size);
            }
            else if (run == 15)
            {
                k += 15;
            }
            else
            {
                break;
            }
        }

        if (!valid)
        {
            ReportInvalidCode();
        }
    }

    /// <summary>Decodes the DC coefficient of a block in a first progressive DC scan: the difference, then the predictor shifted by Al.</summary>
    /// <remarks>ITU-T T.81 §G.1.2.1 (the DC first scan is coded as in §F.1.2.1, on values point-transformed by Al, §A.4).</remarks>
    private void DecodeDcFirst(ref JpegBitReader reader, JpegComponent component, Span<short> block)
    {
        bool valid = true;
        int category = reader.Decode(component.DcTable!, ref valid);
        component.Predictor += reader.ReceiveExtend(category);
        block[0] = (short)(component.Predictor << _approximationLow);
        if (!valid)
        {
            ReportInvalidCode();
        }
    }

    /// <summary>Decodes the band Ss..Se of a block in a first progressive AC scan, with end-of-band runs.</summary>
    /// <remarks>ITU-T T.81 §G.1.2.2, Table G.1 (EOBn: a run of 2^n + n appended bits end-of-bands, this block included).</remarks>
    private void DecodeAcFirst(ref JpegBitReader reader, JpegComponent component, Span<short> block)
    {
        if (_eobRun > 0)
        {
            _eobRun--;
            return;
        }

        bool valid = true;
        HuffmanTable ac = component.AcTable!;
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = _spectralEnd;
        int shift = _approximationLow;
        for (int k = _spectralStart; k <= end; k++)
        {
            int symbol = reader.Decode(ac, ref valid);
            int run = symbol >> 4;
            int size = symbol & 15;
            if (size != 0)
            {
                k += run;
                block[natural[k]] = (short)(reader.ReceiveExtend(size) << shift);
            }
            else if (run == 15)
            {
                k += 15;
            }
            else
            {
                _eobRun = (1 << run) - 1 + reader.Receive(run);
                break;
            }
        }

        if (!valid)
        {
            ReportInvalidCode();
        }
    }

    /// <summary>
    /// Decodes one more bit of the band Ss..Se of a block in a progressive AC refinement scan: newly non-zero coefficients (size 1,
    /// sign bit) and a correction bit for each coefficient that was already non-zero, inside zero runs and after the end of band.
    /// </summary>
    /// <remarks>ITU-T T.81 §G.1.2.3 (Figure G.7, reversed as §G.2 says); libjpeg-turbo <c>decode_mcu_AC_refine</c> (src/jdphuff.c).</remarks>
    private void DecodeAcRefine(ref JpegBitReader reader, JpegComponent component, Span<short> block)
    {
        int end = _spectralEnd;
        int plusOne = 1 << _approximationLow;
        int minusOne = -1 << _approximationLow;
        byte[] natural = JpegMarkers.NaturalOrder;
        int k = _spectralStart;
        if (_eobRun == 0)
        {
            bool valid = true;
            HuffmanTable ac = component.AcTable!;
            for (; k <= end; k++)
            {
                int symbol = reader.Decode(ac, ref valid);
                int run = symbol >> 4;
                int size = symbol & 15;
                int value = 0;
                if (size != 0)
                {
                    // A newly non-zero coefficient always has size 1; its sign bit comes before any correction bit.
                    valid &= size == 1;
                    value = reader.ReadBit() != 0 ? plusOne : minusOne;
                }
                else if (run != 15)
                {
                    // EOBn: this block ends here and n more; the rest of this band gets correction bits below.
                    _eobRun = (1 << run) + reader.Receive(run);
                    break;
                }

                // Skip `run` zero-history coefficients (and, for ZRL, 16), giving a correction bit to each non-zero one.
                do
                {
                    int z = natural[k];
                    if (block[z] != 0)
                    {
                        if (reader.ReadBit() != 0 && (block[z] & plusOne) == 0)
                        {
                            block[z] += (short)(block[z] >= 0 ? plusOne : minusOne);
                        }
                    }
                    else if (--run < 0)
                    {
                        break;
                    }

                    k++;
                }
                while (k <= end);

                if (value != 0 && k <= end)
                {
                    block[natural[k]] = (short)value;
                }
            }

            if (!valid)
            {
                ReportInvalidCode();
            }
        }

        if (_eobRun > 0)
        {
            for (; k <= end; k++)
            {
                int z = natural[k];
                if (block[z] != 0 && reader.ReadBit() != 0 && (block[z] & plusOne) == 0)
                {
                    block[z] += (short)(block[z] >= 0 ? plusOne : minusOne);
                }
            }

            _eobRun--;
        }
    }

    /// <summary>
    /// Decodes a DC difference with the arithmetic coder and adds it to the predictor (modulo 2^16), updating the DC context.
    /// Returns <see langword="false"/> when the magnitude category overflows.
    /// </summary>
    /// <remarks>
    /// ITU-T T.81 §F.2.4.1 (Figure F.19 Decode_DC_DIFF; Figures F.21 to F.24: zero, sign, magnitude category, magnitude bits) and
    /// §F.1.4.4.1, Table F.4 (statistics bins S0 at the DC context, SS, SP, SN, X1 = 20, M = X + 14); §F.1.4.4.1.2 (the context
    /// from the conditioning bounds L and U).
    /// </remarks>
    private bool DecodeArithmeticDc(ref ArithmeticDecoder decoder, JpegComponent component)
    {
        int table = component.DcConditioning;
        Span<byte> bins = _dcStatistics.AsSpan(table * DcStatisticsBins, DcStatisticsBins);
        int s = component.DcContext;
        if (decoder.Decode(ref bins[s]) == 0)
        {
            component.DcContext = 0;
            return true;
        }

        int sign = decoder.Decode(ref bins[s + 1]);
        s += 2 + sign;
        int m = decoder.Decode(ref bins[s]);
        if (m != 0)
        {
            s = 20;
            while (decoder.Decode(ref bins[s]) != 0)
            {
                if ((m <<= 1) == 0x8000)
                {
                    return false;
                }

                s++;
            }
        }

        component.DcContext = m < (1 << _dcLower[table]) >> 1 ? 0
            : m > (1 << _dcUpper[table]) >> 1 ? 12 + (sign * 4)
            : 4 + (sign * 4);
        int v = m;
        s += 14;
        while ((m >>= 1) != 0)
        {
            if (decoder.Decode(ref bins[s]) != 0)
            {
                v |= m;
            }
        }

        v++;
        component.Predictor = (component.Predictor + (sign != 0 ? -v : v)) & 0xFFFF;
        return true;
    }

    /// <summary>
    /// Decodes the AC coefficients Ss..Se of a block with the arithmetic coder: end-of-block decisions, zero runs, sign and
    /// magnitude, each value shifted left by <paramref name="shift"/>. Returns <see langword="false"/> on an overflow.
    /// </summary>
    /// <remarks>
    /// ITU-T T.81 §F.2.4.2 (Figure F.20 Decode_AC_coefficients) and §F.1.4.4.2, Table F.5 (SE = 3 (K - 1), S0 = SE + 1, SN = S0 + 1,
    /// X1 = SN; X2 = 189 below Kx and 217 above; M = X + 14; the sign at a fixed probability of one half); §G.1.3.2 for the band
    /// of a progressive first scan.
    /// </remarks>
    private bool DecodeArithmeticAc(ref ArithmeticDecoder decoder, JpegComponent component, Span<short> block, int start, int shift)
    {
        int table = component.AcConditioning;
        Span<byte> bins = _acStatistics.AsSpan(table * AcStatisticsBins, AcStatisticsBins);
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = _spectralEnd;
        int split = _acSplit[table];
        for (int k = start; k <= end; k++)
        {
            int s = 3 * (k - 1);
            if (decoder.Decode(ref bins[s]) != 0)
            {
                break; // end of block
            }

            while (decoder.Decode(ref bins[s + 1]) == 0)
            {
                s += 3;
                if (++k > end)
                {
                    return false;
                }
            }

            int sign = decoder.Decode(ref _fixedBin);
            s += 2;
            int m = decoder.Decode(ref bins[s]);
            if (m != 0 && decoder.Decode(ref bins[s]) != 0)
            {
                m <<= 1;
                s = k <= split ? 189 : 217;
                while (decoder.Decode(ref bins[s]) != 0)
                {
                    if ((m <<= 1) == 0x8000)
                    {
                        return false;
                    }

                    s++;
                }
            }

            int v = m;
            s += 14;
            while ((m >>= 1) != 0)
            {
                if (decoder.Decode(ref bins[s]) != 0)
                {
                    v |= m;
                }
            }

            v++;
            block[natural[k]] = (short)((sign != 0 ? -v : v) << shift);
        }

        return true;
    }

    /// <summary>Decodes one block of a sequential arithmetic-coded scan.</summary>
    /// <remarks>ITU-T T.81 §F.2.4 (Figure F.18 decode_DC, Figure F.20 decode_AC).</remarks>
    private bool DecodeArithmeticBlock(ref ArithmeticDecoder decoder, JpegComponent component, Span<short> block)
    {
        if (!DecodeArithmeticDc(ref decoder, component))
        {
            return false;
        }

        block[0] = (short)component.Predictor;
        return DecodeArithmeticAc(ref decoder, component, block, 1, 0);
    }

    /// <summary>Decodes the DC coefficient of a block in a first progressive arithmetic DC scan, shifted left by Al.</summary>
    /// <remarks>ITU-T T.81 §G.1.3.1 (coded as the sequential DC difference, §F.1.4.1).</remarks>
    private bool DecodeArithmeticDcFirst(ref ArithmeticDecoder decoder, JpegComponent component, Span<short> block)
    {
        if (!DecodeArithmeticDc(ref decoder, component))
        {
            return false;
        }

        block[0] = (short)(component.Predictor << _approximationLow);
        return true;
    }

    /// <summary>Decodes one more bit of a block's DC coefficient in an arithmetic DC refinement scan (fixed probability).</summary>
    /// <remarks>ITU-T T.81 §G.1.3.1 (the correction bit is coded with a fixed Qe of X'5A1D', no adaptation).</remarks>
    private bool DecodeArithmeticDcRefine(ref ArithmeticDecoder decoder, Span<short> block)
    {
        if (decoder.Decode(ref _fixedBin) != 0)
        {
            block[0] |= (short)(1 << _approximationLow);
        }

        return true;
    }

    /// <summary>Decodes the band of a block in a first progressive arithmetic AC scan.</summary>
    /// <remarks>ITU-T T.81 §G.1.3.2 (as §F.1.4.4.2 over Ss..Se, values point-transformed by Al).</remarks>
    private bool DecodeArithmeticAcFirst(ref ArithmeticDecoder decoder, JpegComponent component, Span<short> block) =>
        DecodeArithmeticAc(ref decoder, component, block, _spectralStart, _approximationLow);

    /// <summary>
    /// Decodes one more bit of the band of a block in an arithmetic AC refinement scan: past the previous end of block (EOBx) an
    /// end-of-block decision, then per coefficient a correction bit (already non-zero) or a new-coefficient decision and its sign.
    /// </summary>
    /// <remarks>ITU-T T.81 §G.1.3.3 (Figures G.10 and G.11 reversed; Table G.2 statistics bins).</remarks>
    private bool DecodeArithmeticAcRefine(ref ArithmeticDecoder decoder, JpegComponent component, Span<short> block)
    {
        Span<byte> bins = _acStatistics.AsSpan(component.AcConditioning * AcStatisticsBins, AcStatisticsBins);
        byte[] natural = JpegMarkers.NaturalOrder;
        int end = _spectralEnd;
        int plusOne = 1 << _approximationLow;
        int minusOne = -1 << _approximationLow;
        int previousEnd = end;
        while (previousEnd > 0 && block[natural[previousEnd]] == 0)
        {
            previousEnd--;
        }

        for (int k = _spectralStart; k <= end; k++)
        {
            int s = 3 * (k - 1);
            if (k > previousEnd && decoder.Decode(ref bins[s]) != 0)
            {
                break; // end of block
            }

            while (true)
            {
                int z = natural[k];
                if (block[z] != 0)
                {
                    if (decoder.Decode(ref bins[s + 2]) != 0)
                    {
                        block[z] += (short)(block[z] < 0 ? minusOne : plusOne);
                    }

                    break;
                }

                if (decoder.Decode(ref bins[s + 1]) != 0)
                {
                    block[z] = (short)(decoder.Decode(ref _fixedBin) != 0 ? minusOne : plusOne);
                    break;
                }

                s += 3;
                if (++k > end)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Ends a restart interval at <paramref name="position"/>: finds the marker and returns, in <paramref name="next"/>, where the
    /// next interval's data starts. Returns whether the new interval has no data (missing or out-of-sequence marker).
    /// </summary>
    /// <remarks>ITU-T T.81 §E.2.4 (Reset_decoder), §F.2.4.4; libjpeg-turbo <c>jpeg_resync_to_restart</c> for damaged sequences.</remarks>
    private bool Restart(ReadOnlySpan<byte> data, int position, ref int expected, out int next)
    {
        bool empty;
        while (true)
        {
            int markerStart = position;
            int marker = NextMarker(data, ref position);
            if (!JpegMarkers.IsRestart((byte)Math.Max(marker, 0)) || marker < 0)
            {
                // A marker that is not RSTn, or the end: the rest of the scan's data is missing.
                position = markerStart;
                empty = true;
                MarkTruncated(int.MaxValue, "The DCT data ends, or another marker comes, where a restart marker should; the rest of the scan is mid-grey.");
                break;
            }

            int found = marker - JpegMarkers.Rst0;
            if (found == expected)
            {
                empty = StartsAtMarker(data, position);
                break;
            }

            Report(DiagnosticCodes.DctRestartInvalid, DiagnosticSeverity.Warning, Invariant($"The DCT data has restart marker RST{found} where RST{expected} should come; the decoder resynchronized."));
            if (found == ((expected + 1) & 7) || found == ((expected + 2) & 7))
            {
                // The expected marker was lost: leave this one for a later interval; this interval has no data.
                position = markerStart;
                empty = true;
                break;
            }

            if (found == ((expected - 1) & 7) || found == ((expected - 2) & 7))
            {
                // A marker of an earlier interval: skip it and look again.
                continue;
            }

            empty = StartsAtMarker(data, position);
            break;
        }

        expected = (expected + 1) & 7;
        next = position;
        return empty;
    }

    private static bool StartsAtMarker(ReadOnlySpan<byte> data, int position) =>
        position + 1 >= data.Length || (data[position] == 0xFF && data[position + 1] != 0);

    private void MarkTruncated(int frameRow, string? message)
    {
        if (frameRow < _firstMissingRow)
        {
            _firstMissingRow = frameRow;
        }

        if (message is not null && !_truncated)
        {
            _truncated = true;
            Report(DiagnosticCodes.DctTruncated, DiagnosticSeverity.Warning, message);
        }
    }

    /// <summary>Turns one MCU row of coefficients into output rows: IDCT per block, replication upsampling, colour transform.</summary>
    /// <remarks>
    /// ITU-T T.81 §A.3.3, §A.3.4 (IDCT, dequantization), §A.2.4 (padding removed); Adobe Technical Note #5116 §15 Table 2
    /// (upsampling by replication, which is libjpeg's box filter for integral factors); ISO 32000-2 §7.4.8 Table 13.
    /// </remarks>
    private void EmitMcuRow<TSink>(int mcuRow, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        if (_precision == 12)
        {
            EmitMcuRow12(mcuRow, ref sink);
            return;
        }

        for (int c = 0; c < _componentCount; c++)
        {
            JpegComponent component = _components[c];
            int stride = component.StripStride;
            byte[] strip = component.Strip!;
            for (int v = 0; v < component.V; v++)
            {
                int row = (mcuRow * component.V) + v;
                if (row >= component.BlocksHigh)
                {
                    break;
                }

                for (int column = 0; column < component.BlocksWide; column++)
                {
                    InverseDct.Transform(component.Block(row, column), component.Quantization, strip.AsSpan((v * 8 * stride) + (column * 8)), stride);
                }
            }
        }

        int top = mcuRow * 8 * _vMax;
        int bottom = Math.Min(top + (8 * _vMax), Height);
        int width = Width;
        for (int y = top; y < bottom; y++)
        {
            int local = y - top;
            Span<byte> output = sink.BeginRow(y);
            ReadOnlySpan<byte> line0 = Line(_components[0], local, width);
            switch (_output)
            {
                case OutputKind.Gray:
                    line0.CopyTo(output);
                    break;
                case OutputKind.YccToRgb:
                    JpegColor.YccToRgb(line0, Line(_components[1], local, width), Line(_components[2], local, width), output[..(width * 3)]);
                    break;
                case OutputKind.YcckToCmyk:
                    JpegColor.YcckToCmyk(line0, Line(_components[1], local, width), Line(_components[2], local, width), Line(_components[3], local, width), output[..(width * 4)]);
                    break;
                default:
                    for (int c = 0; c < _componentCount; c++)
                    {
                        ReadOnlySpan<byte> line = c == 0 ? line0 : Line(_components[c], local, width);
                        for (int x = 0, o = c; x < width; x++, o += _componentCount)
                        {
                            output[o] = line[x];
                        }
                    }

                    break;
            }

            sink.EndRow();
        }
    }

    /// <summary>Returns a component's samples for output row <paramref name="local"/> of the MCU row, replicated to the image width.</summary>
    private ReadOnlySpan<byte> Line(JpegComponent component, int local, int width)
    {
        int stride = component.StripStride;
        ReadOnlySpan<byte> source = component.Strip.AsSpan(local * component.V / _vMax * stride, stride);
        if (component.Upsampled is not { } upsampled)
        {
            return source[..width];
        }

        Span<byte> target = upsampled.AsSpan(0, width);
        int h = component.H;
        if (_hMax == 2 * h)
        {
            for (int x = 0; x < width; x++)
            {
                target[x] = source[x >> 1];
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                target[x] = source[x * h / _hMax];
            }
        }

        return target;
    }

    /// <summary>The 12-bit counterpart of <see cref="EmitMcuRow"/>: the transforms run on 12-bit samples, which are then reduced to 8 bits.</summary>
    /// <remarks>ITU-T T.81 §A.3.1 and §F.2.1.5 (12-bit level shift and clamp); ISO 32000-2 §8.9.5.1 Table 87 (8-bit output).</remarks>
    private void EmitMcuRow12<TSink>(int mcuRow, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        for (int c = 0; c < _componentCount; c++)
        {
            JpegComponent component = _components[c];
            int stride = component.StripStride;
            ushort[] strip = component.Strip12!;
            for (int v = 0; v < component.V; v++)
            {
                int row = (mcuRow * component.V) + v;
                if (row >= component.BlocksHigh)
                {
                    break;
                }

                for (int column = 0; column < component.BlocksWide; column++)
                {
                    InverseDct.Transform12(component.Block(row, column), component.Quantization, strip.AsSpan((v * 8 * stride) + (column * 8)), stride);
                }
            }
        }

        int top = mcuRow * 8 * _vMax;
        int bottom = Math.Min(top + (8 * _vMax), Height);
        int width = Width;
        for (int y = top; y < bottom; y++)
        {
            int local = y - top;
            Span<byte> output = sink.BeginRow(y);
            ReadOnlySpan<ushort> line0 = Line12(_components[0], local, width);
            switch (_output)
            {
                case OutputKind.Gray:
                    JpegColor.Reduce12(line0, output, 1);
                    break;
                case OutputKind.YccToRgb:
                    JpegColor.YccToRgb12(line0, Line12(_components[1], local, width), Line12(_components[2], local, width), output[..(width * 3)]);
                    break;
                case OutputKind.YcckToCmyk:
                    JpegColor.YcckToCmyk12(line0, Line12(_components[1], local, width), Line12(_components[2], local, width), Line12(_components[3], local, width), output[..(width * 4)]);
                    break;
                default:
                    for (int c = 0; c < _componentCount; c++)
                    {
                        JpegColor.Reduce12(c == 0 ? line0 : Line12(_components[c], local, width), output[c..], _componentCount);
                    }

                    break;
            }

            sink.EndRow();
        }
    }

    /// <summary>The 12-bit counterpart of <see cref="Line"/>.</summary>
    private ReadOnlySpan<ushort> Line12(JpegComponent component, int local, int width)
    {
        int stride = component.StripStride;
        ReadOnlySpan<ushort> source = component.Strip12.AsSpan(local * component.V / _vMax * stride, stride);
        if (component.Upsampled12 is not { } upsampled)
        {
            return source[..width];
        }

        Span<ushort> target = upsampled.AsSpan(0, width);
        int h = component.H;
        for (int x = 0; x < width; x++)
        {
            target[x] = source[x * h / _hMax];
        }

        return target;
    }

    /// <summary>Grows the component records to <paramref name="count"/> (a frame of more than four components; kept for the thread).</summary>
    private void EnsureComponents(int count)
    {
        if (count <= _components.Length)
        {
            return;
        }

        int previous = _components.Length;
        Array.Resize(ref _components, count);
        for (int c = previous; c < count; c++)
        {
            _components[c] = new JpegComponent();
        }
    }

    private bool ShouldReport(string code) => _reporter.ShouldReport(code);

    private void Report(string code, DiagnosticSeverity severity, string message) => _reporter.Report(code, severity, message);

    private void Reset()
    {
        for (int i = 0; i < 4; i++)
        {
            _dcTables[i].Clear();
            _acTables[i].Clear();
            _quantizationDefined[i] = false;
            _quantizationWide[i] = false;
        }

        foreach (JpegComponent component in _components)
        {
            component.Release();
        }

        _reporter.Reset(null);
        _position = 0;
        _frameFound = false;
        _componentCount = 0;
        _restartInterval = 0;
        _adobe = false;
        _adobeTransform = 0;
        _scanLength = 0;
        _scansDecoded = 0;
        _streaming = false;
        _storageReady = false;
        _firstMissingRow = int.MaxValue;
        _truncated = false;
        _output = OutputKind.Gray;
        _progressive = false;
        _arithmetic = false;
        _precision = 8;
        _scanKind = ScanKind.Sequential;
        _spectralStart = 0;
        _spectralEnd = 63;
        _approximationLow = 0;
        _eobRun = 0;
        _fixedBin = ArithmeticDecoder.FixedState;
        _dcLower.AsSpan().Clear();
        _dcUpper.AsSpan().Fill(1);
        _acSplit.AsSpan().Fill(5);
        Width = 0;
        Height = 0;
    }
}
