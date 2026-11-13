using Broadside.Diagnostics;
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
/// This type is the decoder's control (Figures E.6 and E.7: decode frame, decode scan) over one per-image state,
/// <see cref="JpegFrame"/>. The work is split by responsibility: <see cref="JpegMarkerReader"/> reads the frame, scan and table
/// syntax; <see cref="JpegScanDecoder"/> decodes a scan's entropy-coded data through <see cref="JpegHuffmanDecoding"/> or
/// <see cref="JpegArithmeticDecoding"/>; <see cref="JpegSampleWriter"/> produces the output rows.
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
    [ThreadStatic]
    private static JpegDecoder? _cached;

    private readonly JpegFrame _frame = new();

    /// <summary>Gets the number of samples per row (X).</summary>
    public int Width => _frame.Width;

    /// <summary>Gets the number of rows (Y, or the DNL value when Y is 0).</summary>
    public int Height => _frame.Height;

    /// <summary>Gets the number of components (Nf).</summary>
    public int ComponentCount => _frame.ComponentCount;

    /// <summary>Gets the APP14 transform code, or <see langword="null"/> without an Adobe APP14 segment.</summary>
    public AdobeColorTransform? AdobeTransform => _frame.Adobe ? _frame.AdobeTransform : null;

    /// <summary>Gets a value indicating whether a YCbCr or YCCK transform is applied.</summary>
    public bool ColorTransformApplied => _frame.Output is JpegOutputKind.YccToRgb or JpegOutputKind.YcckToCmyk;

    /// <summary>Gets the colour model of the output.</summary>
    public ImageColorModel ColorModel => _frame.ComponentCount switch
    {
        1 => ImageColorModel.Gray,
        3 => ImageColorModel.Rgb,
        4 => ImageColorModel.Cmyk,
        _ => ImageColorModel.Unknown,
    };

    /// <summary>Gets the number of output rows decoded from data; the rows below are mid-grey.</summary>
    public int DecodedRows => _frame.DecodedRows;

    /// <summary>Gets the reduction the image is decoded at, as a power of two: 0 to 3 (<see cref="SetReduction"/>).</summary>
    public int Reduction => _frame.Reduction;

    /// <summary>Gets the width of the output rows: ⌈<see cref="Width"/> / 2^<see cref="Reduction"/>⌉.</summary>
    public int OutputWidth => _frame.OutputWidth;

    /// <summary>Gets the number of output rows: ⌈<see cref="Height"/> / 2^<see cref="Reduction"/>⌉.</summary>
    public int OutputHeight => _frame.OutputHeight;

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
        decoder._frame.Reset();
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
        _frame.Reporter.Reset(context, silent);
        return JpegMarkerReader.ReadHeaders(_frame, data, fallbackHeight);
    }

    /// <summary>
    /// Decodes the image at a reduction of 2^<paramref name="reduction"/> (1/2, 1/4 or 1/8; 0 for the full resolution): every
    /// block goes through a reduced IDCT, as libjpeg-turbo does for <c>-scale 1/s</c>, so the output is ⌈W / s⌉ x ⌈H / s⌉
    /// samples equal to <c>djpeg -scale 1/s -dct int -nosmooth</c>'s.
    /// </summary>
    /// <param name="reduction">The reduction, clamped to 0 to 3.</param>
    /// <remarks>
    /// Issue #362. ITU-T T.81 §A.3.3 (the IDCT, here of reduced size: a block's samples averaged over 2 x 2, 4 x 4 or 8 x 8).
    /// The IDCT size of each component follows libjpeg-turbo's <c>jdmaster.c</c>: a subsampled component gets a larger IDCT
    /// instead of upsampling where the factors allow, so 4:2:0 at 1/2 decodes its chroma with the full 8 x 8 IDCT and upsamples
    /// nothing. At 1/8, when every component takes the 1 x 1 IDCT (the DC alone), a progressive frame's AC scans are skipped.
    /// Call after <see cref="ReadHeaders"/> and before <see cref="Decode"/>.
    /// </remarks>
    public void SetReduction(int reduction)
    {
        _frame.Reduction = Math.Clamp(reduction, 0, 3);
        _frame.MinScaled = 8 >> _frame.Reduction;
    }

    /// <summary>Chooses the colour transform from the APP14 segment, the <c>ColorTransform</c> parameter and the component identifiers.</summary>
    /// <param name="colorTransform">The DecodeParms <c>ColorTransform</c> value, or -1 when absent.</param>
    /// <remarks>ISO 32000-2 §7.4.8, Table 13; the rules are on <see cref="JpegSampleWriter.SelectColorTransform"/>.</remarks>
    public void SelectColorTransform(int colorTransform) => JpegSampleWriter.SelectColorTransform(_frame, colorTransform);

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
        JpegFrame frame = _frame;
        JpegScan scan = frame.Scan;
        int position = frame.Position;
        frame.FirstMissingRow = int.MaxValue;
        bool ended = false;
        while (!ended)
        {
            // At a scan header.
            ReadOnlySpan<byte> header = JpegMarkerReader.ReadSegment(frame, data, ref position);
            bool usable = JpegMarkerReader.ReadScanHeader(frame, header);
            if (usable && !frame.StorageReady && !AllocateStorage(maxBytes))
            {
                return false;
            }

            if (usable && frame.Streaming && frame.ScansDecoded > 0)
            {
                frame.Report(DiagnosticCodes.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan repeats components an earlier scan decoded; it was ignored.");
                usable = false;
            }

            if (usable && !JpegMarkerReader.LatchQuantization(frame))
            {
                return false;
            }

            bool skipped = false;
            if (usable && frame.Progressive && scan.SpectralStart > 0 && frame.DcOnly)
            {
                // Every component's 1 x 1 IDCT reads the DC alone, so the AC scans change nothing in the output (issue #362).
                for (int j = 0; j < scan.Length; j++)
                {
                    frame.Components[scan.Components[j]].Scanned = true;
                }

                usable = false;
                skipped = true;
            }

            if (usable)
            {
                JpegScanDecoder.Decode(frame, data, ref position, ref sink);
                frame.ScansDecoded++;
            }
            else if (skipped)
            {
                // The whole scan, its restart intervals included.
                position = JpegMarkerReader.SkipScan(data, position);
            }
            else
            {
                // A skipped scan was reported; its entropy-coded data is not extraneous.
                position = JpegMarkerReader.SkipEntropyCodedData(data, position);
            }

            // Between scans: tables and miscellaneous segments, the next scan or the end.
            while (true)
            {
                int marker = JpegMarkerReader.NextMarker(frame, data, ref position);
                if (marker == JpegMarkers.Sos)
                {
                    break;
                }

                if (marker is < 0 or JpegMarkers.Eoi)
                {
                    if (marker < 0 && !frame.Truncated && frame.ScansDecoded > 0)
                    {
                        frame.Report(DiagnosticCodes.DctEndMissing, DiagnosticSeverity.Warning, "The DCT data ends without the EOI marker.");
                    }

                    ended = true;
                    break;
                }

                if (JpegMarkers.IsStartOfFrame((byte)marker))
                {
                    frame.Report(DiagnosticCodes.DctFrameRepeated, DiagnosticSeverity.Warning, "The DCT data has a second frame; only the first is decoded.");
                    ended = true;
                    break;
                }

                if (!JpegMarkers.IsStandalone((byte)marker))
                {
                    JpegMarkerReader.ReadTableOrMiscellaneous(frame, (byte)marker, data, ref position);
                }
            }
        }

        if (!frame.StorageReady)
        {
            return false;
        }

        for (int c = 0; c < frame.ComponentCount; c++)
        {
            if (!frame.Components[c].Scanned)
            {
                frame.MarkTruncated(0, "The DCT data ends before every component was decoded; the missing samples are mid-grey.");
            }
        }

        if (!frame.Streaming)
        {
            for (int row = 0; row < frame.McusY; row++)
            {
                frame.Reporter.ThrowIfCancellationRequested();
                JpegSampleWriter.EmitMcuRow(frame, row, ref sink);
            }
        }

        return true;
    }


    /// <summary>Chooses streaming or buffering at the first usable scan and rents the buffers.</summary>
    private bool AllocateStorage(long maxBytes)
    {
        JpegFrame frame = _frame;
        JpegScan scan = frame.Scan;
        frame.Streaming = !frame.Progressive && scan.Length == frame.ComponentCount;
        long blocks = 0;
        for (int c = 0; c < frame.ComponentCount; c++)
        {
            JpegComponent component = frame.Components[c];
            blocks += (long)component.BlocksPerLine * (frame.Streaming ? component.V : component.BlockRows);
        }

        long bytes = blocks * 64 * sizeof(short);
        if (blocks * 64 > Array.MaxLength || bytes > maxBytes)
        {
            frame.Report(
                DiagnosticCodes.ImageTooLarge,
                DiagnosticSeverity.Error,
                JpegFrame.Invariant($"The DCT image is not decoded: its coefficients would take {bytes} bytes, more than the {maxBytes} allowed."));
            return false;
        }

        for (int c = 0; c < frame.ComponentCount; c++)
        {
            // libjpeg-turbo jdmaster.c: scale a subsampled component up through its IDCT rather than by upsampling.
            JpegComponent component = frame.Components[c];
            int size = frame.MinScaled;
            while (size < 8 && (frame.HMax * frame.MinScaled) % (component.H * size * 2) == 0 && (frame.VMax * frame.MinScaled) % (component.V * size * 2) == 0)
            {
                size *= 2;
            }

            component.ScaledSize = size;
            bool upsample = component.H * size != frame.HMax * frame.MinScaled;
            component.Allocate(frame.Streaming ? component.V : component.BlockRows, frame.OutputWidth, upsample, frame.Precision == 12);
        }

        frame.StorageReady = true;
        return true;
    }
}
