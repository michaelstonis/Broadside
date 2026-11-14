using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>
/// Decodes one scan's entropy-coded data into the components' coefficients: walks its MCUs in interleaved or non-interleaved
/// order, hands each block to the Huffman or arithmetic decoder of the scan's kind, and handles restart intervals, truncation
/// and the end of the data. A streaming decode outputs each MCU row as soon as it is decoded.
/// </summary>
/// <remarks>
/// ITU-T T.81 Annex E (decoder control: §E.2.3 to §E.2.5, Figures E.7 to E.10); §A.2.2 and §A.2.3 (MCU order); §F.2.4.4 and
/// libjpeg-turbo <c>jpeg_resync_to_restart</c> (restart recovery). The block decoders are <see cref="JpegHuffmanDecoding"/> and
/// <see cref="JpegArithmeticDecoding"/>.
/// <para>ISO 32000-2 §7.4.8.</para>
/// </remarks>
internal static class JpegScanDecoder
{
    /// <summary>Decodes one scan's entropy-coded data, with its restart intervals.</summary>
    /// <remarks>
    /// ITU-T T.81 §E.2.3 to §E.2.5 (Figures E.7 to E.10); §A.2.2 (non-interleaved order: one block per MCU, the component's own
    /// block extent; every progressive AC scan), §A.2.3 (interleaved order: Hi x Vi blocks per component per MCU, padded extent);
    /// §F.2.4.4 (restart-based error recovery, here libjpeg's resynchronization); §G.1.2.2 (EOBRUN, reset with the DC predictors
    /// at each restart); §F.1.4.4 and §G.1.3 (arithmetic statistics, reset at the start of the scan and at each restart).
    /// </remarks>
    /// <typeparam name="TSink">The row destination.</typeparam>
    /// <param name="frame">The image, at a usable scan header.</param>
    /// <param name="data">The JPEG data.</param>
    /// <param name="position">Where the entropy-coded data starts; on return, where it ends.</param>
    /// <param name="sink">The destination of the output rows of a streaming decode.</param>
    public static void Decode<TSink>(JpegFrame frame, ReadOnlySpan<byte> data, ref int position, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        JpegScan scan = frame.Scan;
        bool single = scan.Length == 1;
        JpegComponent first = frame.Components[scan.Components[0]];
        int mcusPerRow = single ? first.BlocksWide : frame.McusX;
        int mcuRows = single ? first.BlocksHigh : frame.McusY;
        for (int j = 0; j < scan.Length; j++)
        {
            frame.Components[scan.Components[j]].Scanned = true;
        }

        scan.ResetEntropyState(frame.Components, frame.Arithmetic);
        var reader = new JpegBitReader(data, position);
        var arithmetic = new ArithmeticDecoder(data, position);
        bool exhausted = JpegMarkerReader.StartsAtMarker(data, position);
        int intervalLeft = frame.RestartInterval;
        int expectedRestart = 0;
        bool exhaustedArithmetic = false;

        // Rows a later progressive scan leaves incomplete were decoded by the first DC scan: they still count as decoded.
        bool countsRows = scan.Kind is JpegScanKind.Sequential or JpegScanKind.DcFirst;
        for (int mcuRow = 0; mcuRow < mcuRows; mcuRow++)
        {
            frame.Reporter.ThrowIfCancellationRequested();
            int frameRow = !countsRows ? int.MaxValue : single ? mcuRow / first.V : mcuRow;
            if (frame.Streaming)
            {
                for (int c = 0; c < frame.ComponentCount; c++)
                {
                    frame.Components[c].FirstStoredRow = mcuRow * frame.Components[c].V;
                    frame.Components[c].ClearCoefficients();
                }
            }

            for (int mcuColumn = 0; mcuColumn < mcusPerRow; mcuColumn++)
            {
                if (frame.RestartInterval > 0)
                {
                    if (intervalLeft == 0)
                    {
                        exhaustedArithmetic |= arithmetic.Exhausted;
                        int end = EndOfEntropyCodedData(frame, data, ref reader, ref arithmetic);
                        exhausted = Restart(frame, data, end, ref expectedRestart, out int next);
                        reader = new JpegBitReader(data, next);
                        arithmetic = new ArithmeticDecoder(data, next);
                        scan.ResetEntropyState(frame.Components, frame.Arithmetic);
                        intervalLeft = frame.RestartInterval;
                    }

                    intervalLeft--;
                }

                if (exhausted)
                {
                    frame.MarkTruncated(frameRow, "The DCT data ends before every block of a scan was decoded; the missing samples are mid-grey.");
                    continue;
                }

                if (single)
                {
                    DecodeUnit(frame, scan, ref reader, ref arithmetic, first, first.Block(mcuRow, mcuColumn));
                }
                else
                {
                    for (int j = 0; j < scan.Length; j++)
                    {
                        JpegComponent component = frame.Components[scan.Components[j]];
                        for (int v = 0; v < component.V; v++)
                        {
                            int row = (mcuRow * component.V) + v;
                            for (int h = 0; h < component.H; h++)
                            {
                                DecodeUnit(frame, scan, ref reader, ref arithmetic, component, component.Block(row, (mcuColumn * component.H) + h));
                            }
                        }
                    }
                }

                if (reader.Overread)
                {
                    exhausted = true;
                    frame.MarkTruncated(frameRow, "The DCT data ends before every block of a scan was decoded; the missing samples are mid-grey.");
                }
            }

            if (frame.Streaming)
            {
                JpegSampleWriter.EmitMcuRow(frame, mcuRow, ref sink);
            }
        }

        if (exhaustedArithmetic || arithmetic.Exhausted)
        {
            // Arithmetic decoding goes on with zero bits at the end of the data (§D.2.6); nothing tells truncation from a lost EOI.
            frame.MarkTruncated(int.MaxValue, "The arithmetic-coded DCT data ends without a marker; the rest of the scan was decoded from zero bits.");
        }

        position = EndOfEntropyCodedData(frame, data, ref reader, ref arithmetic);
    }

    /// <summary>
    /// Returns where the entropy-coded data of the current interval ends. A Huffman reader stops at the bytes it loaded, and the
    /// whole bytes it did not use are reported; arithmetic data runs to the next marker (the decoder may stop before the bytes
    /// the encoder's flush wrote, §D.1.8).
    /// </summary>
    private static int EndOfEntropyCodedData(JpegFrame frame, ReadOnlySpan<byte> data, ref JpegBitReader reader, ref ArithmeticDecoder arithmetic)
    {
        if (!frame.Arithmetic)
        {
            if (!reader.Overread && reader.UnusedBytes > 0)
            {
                JpegMarkerReader.ReportExtraneous(frame, reader.UnusedBytes);
            }

            return reader.Position;
        }

        return JpegMarkerReader.SkipEntropyCodedData(data, arithmetic.Position);
    }

    /// <summary>Decodes one block of the current scan with the scan's entropy coder and kind.</summary>
    private static void DecodeUnit(JpegFrame frame, JpegScan scan, ref JpegBitReader reader, ref ArithmeticDecoder arithmetic, JpegComponent component, Span<short> block)
    {
        if (frame.Arithmetic)
        {
            if (arithmetic.Failed)
            {
                return;
            }

            bool ok = scan.Kind switch
            {
                JpegScanKind.Sequential => JpegArithmeticDecoding.DecodeSequential(ref arithmetic, scan, frame.Tables, component, block),
                JpegScanKind.DcFirst => JpegArithmeticDecoding.DecodeDcFirst(ref arithmetic, scan, frame.Tables, component, block),
                JpegScanKind.DcRefine => JpegArithmeticDecoding.DecodeDcRefine(ref arithmetic, scan, block),
                JpegScanKind.AcFirst => JpegArithmeticDecoding.DecodeAcFirst(ref arithmetic, scan, frame.Tables, component, block),
                _ => JpegArithmeticDecoding.DecodeAcRefine(ref arithmetic, scan, component, block),
            };
            if (!ok)
            {
                arithmetic.Failed = true;
                ReportInvalidData(frame, "The arithmetic-coded DCT data decodes to an impossible value; the rest of its restart interval is left as decoded so far.");
            }

            return;
        }

        bool valid = true;
        switch (scan.Kind)
        {
            case JpegScanKind.Sequential:
                valid = JpegHuffmanDecoding.DecodeSequential(ref reader, component, block);
                break;
            case JpegScanKind.DcFirst:
                valid = JpegHuffmanDecoding.DecodeDcFirst(ref reader, scan, component, block);
                break;
            case JpegScanKind.DcRefine:
                JpegHuffmanDecoding.DecodeDcRefine(ref reader, scan, block);
                break;
            case JpegScanKind.AcFirst:
                valid = JpegHuffmanDecoding.DecodeAcFirst(ref reader, scan, component, block);
                break;
            default:
                valid = JpegHuffmanDecoding.DecodeAcRefine(ref reader, scan, component, block);
                break;
        }

        if (!valid)
        {
            ReportInvalidCode(frame);
        }
    }

    private static void ReportInvalidData(JpegFrame frame, string message)
    {
        if (frame.ShouldReport(DctDiagnostics.DctDataInvalid))
        {
            frame.Report(DctDiagnostics.DctDataInvalid, DiagnosticSeverity.Warning, message);
        }
    }

    private static void ReportInvalidCode(JpegFrame frame) =>
        ReportInvalidData(frame, "The DCT entropy-coded data holds an invalid Huffman code; a zero was used in its place.");

    /// <summary>
    /// Ends a restart interval at <paramref name="position"/>: finds the marker and returns, in <paramref name="next"/>, where the
    /// next interval's data starts. Returns whether the new interval has no data (missing or out-of-sequence marker).
    /// </summary>
    /// <remarks>ITU-T T.81 §E.2.4 (Reset_decoder), §F.2.4.4; libjpeg-turbo <c>jpeg_resync_to_restart</c> for damaged sequences.</remarks>
    private static bool Restart(JpegFrame frame, ReadOnlySpan<byte> data, int position, ref int expected, out int next)
    {
        bool empty;
        while (true)
        {
            int markerStart = position;
            int marker = JpegMarkerReader.NextMarker(frame, data, ref position);
            if (!JpegMarkers.IsRestart((byte)Math.Max(marker, 0)) || marker < 0)
            {
                // A marker that is not RSTn, or the end: the rest of the scan's data is missing.
                position = markerStart;
                empty = true;
                frame.MarkTruncated(int.MaxValue, "The DCT data ends, or another marker comes, where a restart marker should; the rest of the scan is mid-grey.");
                break;
            }

            int found = marker - JpegMarkers.Rst0;
            if (found == expected)
            {
                empty = JpegMarkerReader.StartsAtMarker(data, position);
                break;
            }

            frame.Report(DctDiagnostics.DctRestartInvalid, DiagnosticSeverity.Warning, JpegFrame.Invariant($"The DCT data has restart marker RST{found} where RST{expected} should come; the decoder resynchronized."));
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

            empty = JpegMarkerReader.StartsAtMarker(data, position);
            break;
        }

        expected = (expected + 1) & 7;
        next = position;
        return empty;
    }
}
