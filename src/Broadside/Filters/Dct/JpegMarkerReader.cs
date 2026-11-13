using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>
/// Reads the JPEG syntax around the entropy-coded data: markers, the frame header, scan headers and the table-specification and
/// miscellaneous segments, into a <see cref="JpegFrame"/>; and finds where entropy-coded data ends.
/// </summary>
/// <remarks>
/// ITU-T T.81 Annex B: §B.1.1.2 and §B.1.1.3 (markers, fill bytes), §B.2.1 (Figure B.2, the order of segments), §B.2.2 (frame
/// header), §B.2.3 (scan header), §B.2.4 (DQT, DHT, DAC, DRI, APPn), §B.2.5 (DNL); §G.1.1.1 (progressive scan parameters);
/// Adobe Technical Note #5116 §18 (APP14). Every deviation is reported through the frame, once per code.
/// </remarks>
internal static class JpegMarkerReader
{
    /// <summary>Reads the marker segments up to the first scan header: tables, restart interval, APP14, the frame.</summary>
    /// <param name="frame">The image the frame header and tables are read into.</param>
    /// <param name="data">The JPEG data.</param>
    /// <param name="fallbackHeight">The image dictionary's Height, for a frame whose Y is 0 without a DNL segment.</param>
    /// <returns><see langword="false"/> when there is no decodable frame and scan.</returns>
    /// <remarks>ITU-T T.81 §B.2.1, Figure B.2; §B.2.2 to §B.2.4.</remarks>
    public static bool ReadHeaders(JpegFrame frame, ReadOnlySpan<byte> data, int fallbackHeight)
    {
        int start = data.IndexOf([(byte)0xFF, JpegMarkers.Soi]);
        if (start < 0)
        {
            frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has no SOI marker; it is not decoded.");
            return false;
        }

        if (start > 0)
        {
            frame.Report(DctDiagnostics.DctLeadingJunk, DiagnosticSeverity.Warning, JpegFrame.Invariant($"{start} bytes before the SOI marker of the DCT data were skipped."));
        }

        int position = start + 2;
        while (true)
        {
            int marker = NextMarker(frame, data, ref position);
            switch (marker)
            {
                case < 0:
                case JpegMarkers.Eoi:
                    if (frame.FrameFound)
                    {
                        frame.Report(DctDiagnostics.DctTruncated, DiagnosticSeverity.Warning, "The DCT data ends before its first scan; it is not decoded.");
                    }
                    else
                    {
                        frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has no frame header; it is not decoded.");
                    }

                    return false;
                case JpegMarkers.Soi:
                case JpegMarkers.Tem:
                case >= JpegMarkers.Rst0 and <= JpegMarkers.Rst7:
                    break;
                case JpegMarkers.Sof0 or JpegMarkers.Sof1 or JpegMarkers.Sof2 or JpegMarkers.Sof9 or JpegMarkers.Sof10:
                    {
                        ReadOnlySpan<byte> body = ReadSegment(frame, data, ref position);
                        if (frame.FrameFound)
                        {
                            frame.Report(DctDiagnostics.DctFrameRepeated, DiagnosticSeverity.Warning, "A second DCT frame header was ignored.");
                        }
                        else
                        {
                            frame.Progressive = marker is JpegMarkers.Sof2 or JpegMarkers.Sof10;
                            frame.Arithmetic = marker is JpegMarkers.Sof9 or JpegMarkers.Sof10;
                            if (!ReadFrame(frame, (byte)marker, body, data, position, fallbackHeight))
                            {
                                return false;
                            }
                        }

                        break;
                    }

                case JpegMarkers.Dhp:
                case JpegMarkers.Exp:
                case var sof when JpegMarkers.IsStartOfFrame((byte)sof):
                    frame.Report(
                        DctDiagnostics.DctProcessUnsupported,
                        DiagnosticSeverity.Error,
                        JpegFrame.Invariant($"The DCT data uses the lossless or hierarchical process (marker FF{marker:X2}), which PDF does not use; it is not decoded."));
                    return false;
                case JpegMarkers.Sos:
                    if (!frame.FrameFound)
                    {
                        frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT data has a scan before its frame header; it is not decoded.");
                        return false;
                    }

                    frame.Position = position;
                    return true;
                default:
                    ReadTableOrMiscellaneous(frame, (byte)marker, data, ref position);
                    break;
            }
        }
    }

    /// <summary>Finds the next marker from <paramref name="position"/>, skipping fill bytes; returns the code after FF, or -1 at the end.</summary>
    public static int NextMarker(JpegFrame frame, ReadOnlySpan<byte> data, ref int position)
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
            ReportExtraneous(frame, skipped);
        }

        return marker;
    }

    public static void ReportExtraneous(JpegFrame frame, int count)
    {
        if (frame.ShouldReport(DctDiagnostics.DctExtraneousData))
        {
            frame.Report(DctDiagnostics.DctExtraneousData, DiagnosticSeverity.Warning, JpegFrame.Invariant($"{count} bytes of the DCT data that are not part of a marker segment were skipped."));
        }
    }

    /// <summary>Reads a marker segment's length and returns its parameters, advancing past it; a bad length is reported.</summary>
    public static ReadOnlySpan<byte> ReadSegment(JpegFrame frame, ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 2 > data.Length)
        {
            position = data.Length;
            frame.Report(DctDiagnostics.DctTruncated, DiagnosticSeverity.Warning, "The DCT data ends inside a marker segment.");
            frame.Truncated = true;
            return [];
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        if (length < 2)
        {
            frame.Report(DctDiagnostics.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT marker segment has a length below 2; the decoder looks for the next marker.");
            position += 2;
            return [];
        }

        if (position + length > data.Length)
        {
            ReadOnlySpan<byte> rest = data[(position + 2)..];
            position = data.Length;
            frame.Report(DctDiagnostics.DctTruncated, DiagnosticSeverity.Warning, "A DCT marker segment runs past the end of the data.");
            frame.Truncated = true;
            return rest;
        }

        ReadOnlySpan<byte> body = data.Slice(position + 2, length - 2);
        position += length;
        return body;
    }

    /// <summary>Reads a DHT, DQT, DRI, APPn segment, or skips any other one by its length.</summary>
    public static void ReadTableOrMiscellaneous(JpegFrame frame, byte marker, ReadOnlySpan<byte> data, ref int position)
    {
        ReadOnlySpan<byte> body = ReadSegment(frame, data, ref position);
        switch (marker)
        {
            case JpegMarkers.Dht:
                frame.Tables.ReadHuffmanTables(body, frame.Reporter);
                break;
            case JpegMarkers.Dqt:
                frame.Tables.ReadQuantizationTables(body, frame.Reporter);
                break;
            case JpegMarkers.Dri:
                if (body.Length < 2)
                {
                    frame.Report(DctDiagnostics.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT DRI segment is too short; it was ignored.");
                }
                else
                {
                    frame.RestartInterval = BinaryPrimitives.ReadUInt16BigEndian(body);
                }

                break;
            case JpegMarkers.Dac:
                frame.Tables.ReadArithmeticConditioning(body, frame.Reporter);
                break;
            case JpegMarkers.App14:
                // Adobe Technical Note #5116 §18: "Adobe", version, flags0, flags1, transform; the last one counts.
                if (body.Length >= 12 && body.StartsWith("Adobe"u8))
                {
                    frame.Adobe = true;
                    frame.AdobeTransform = (AdobeColorTransform)body[11];
                }

                break;
        }
    }

    /// <summary>Reads the frame header and computes the geometry.</summary>
    /// <remarks>
    /// ITU-T T.81 §B.2.2, Table B.2 (P = 8 for baseline, 8 or 12 for the extended and progressive processes); §A.1.1; §B.2.5 (DNL).
    /// ISO 32000-2 §8.9.5.1 Table 87: 12-bit samples are delivered reduced to 8 bits, which is reported as Information.
    /// </remarks>
    private static bool ReadFrame(JpegFrame frame, byte marker, ReadOnlySpan<byte> body, ReadOnlySpan<byte> data, int position, int fallbackHeight)
    {
        if (body.Length < 6)
        {
            frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT frame header is truncated; the image is not decoded.");
            return false;
        }

        int precision = body[0];
        int height = BinaryPrimitives.ReadUInt16BigEndian(body[1..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(body[3..]);
        int count = body[5];
        if (precision is not (8 or 12))
        {
            frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, JpegFrame.Invariant($"The DCT frame has a sample precision of {precision} bits; the image is not decoded."));
            return false;
        }

        if (precision == 12)
        {
            if (marker == JpegMarkers.Sof0)
            {
                frame.Report(DctDiagnostics.DctSegmentInvalid, DiagnosticSeverity.Warning, "A baseline DCT frame declares 12-bit samples, which only the extended and progressive processes allow; it is decoded as extended.");
            }

            frame.Report(DctDiagnostics.DctPrecisionReduced, DiagnosticSeverity.Information, "The DCT data has 12-bit samples; they are delivered reduced to 8 bits, as ISO 32000-2 Table 87 requires.");
        }

        if (width == 0 || count is 0 or > JpegFrame.MaxComponents || body.Length < 6 + (3 * count))
        {
            frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, JpegFrame.Invariant($"The DCT frame header is unusable ({width} samples per line, {count} components); the image is not decoded."));
            return false;
        }

        if (count == 2)
        {
            frame.Report(DctDiagnostics.DctComponentCountInvalid, DiagnosticSeverity.Warning, "The DCT data has two components, which PDF does not allow; they are decoded without a colour transform.");
        }

        frame.EnsureComponents(count);
        for (int c = 0; c < count; c++)
        {
            JpegComponent component = frame.Components[c];
            ReadOnlySpan<byte> entry = body.Slice(6 + (3 * c), 3);
            component.Id = entry[0];
            component.H = entry[1] >> 4;
            component.V = entry[1] & 15;
            component.QuantizationTable = entry[2];
            if (component.H is < 1 or > 4 || component.V is < 1 or > 4 || component.QuantizationTable > 3)
            {
                frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "A DCT frame component has an invalid sampling factor or quantization table; the image is not decoded.");
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
                frame.Report(DctDiagnostics.DctLinesFromDnl, DiagnosticSeverity.Warning, "The DCT frame's number of lines is 0; the DNL segment's is used.");
            }
            else if (fallbackHeight > 0)
            {
                height = fallbackHeight;
                frame.Report(DctDiagnostics.DctLinesFromDnl, DiagnosticSeverity.Warning, "The DCT frame's number of lines is 0 and there is no DNL segment; the image dictionary's Height is used.");
            }
            else
            {
                frame.Report(DctDiagnostics.DctFrameInvalid, DiagnosticSeverity.Error, "The DCT frame's number of lines is 0 and no DNL segment defines it; the image is not decoded.");
                return false;
            }
        }

        frame.Width = width;
        frame.Height = height;
        frame.Precision = precision;
        frame.ComponentCount = count;
        frame.HMax = 1;
        frame.VMax = 1;
        for (int c = 0; c < count; c++)
        {
            frame.HMax = Math.Max(frame.HMax, frame.Components[c].H);
            frame.VMax = Math.Max(frame.VMax, frame.Components[c].V);
        }

        frame.McusX = (width + (8 * frame.HMax) - 1) / (8 * frame.HMax);
        frame.McusY = (height + (8 * frame.VMax) - 1) / (8 * frame.VMax);
        for (int c = 0; c < count; c++)
        {
            JpegComponent component = frame.Components[c];
            int componentWidth = (int)((((long)width * component.H) + frame.HMax - 1) / frame.HMax);
            int componentHeight = (int)((((long)height * component.V) + frame.VMax - 1) / frame.VMax);
            component.BlocksWide = (componentWidth + 7) / 8;
            component.BlocksHigh = (componentHeight + 7) / 8;
            component.BlocksPerLine = frame.McusX * component.H;
            component.BlockRows = frame.McusY * component.V;
        }

        frame.FrameFound = true;
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
    public static bool ReadScanHeader(JpegFrame frame, ReadOnlySpan<byte> header)
    {
        JpegScan scan = frame.Scan;
        int count = header.IsEmpty ? 0 : header[0];
        if (count is < 1 or > JpegScan.MaxComponents || header.Length < 1 + (2 * count) + 3)
        {
            frame.Report(DctDiagnostics.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan header is invalid; the scan was skipped.");
            return false;
        }

        for (int j = 0; j < count; j++)
        {
            int id = header[1 + (2 * j)];
            int tables = header[2 + (2 * j)];
            int index = -1;
            for (int c = 0; c < frame.ComponentCount && index < 0; c++)
            {
                if (frame.Components[c].Id == id && !scan.Components.AsSpan(0, j).Contains(c))
                {
                    index = c;
                }
            }

            if (index < 0 || tables >> 4 > 3 || (tables & 15) > 3)
            {
                frame.Report(DctDiagnostics.DctScanInvalid, DiagnosticSeverity.Warning, "A DCT scan names a component the frame does not have, or a table destination above 3; the scan was skipped.");
                return false;
            }

            scan.Components[j] = index;
        }

        int spectralStart = header[1 + (2 * count)];
        int spectralEnd = header[2 + (2 * count)];
        int high = header[3 + (2 * count)] >> 4;
        int low = header[3 + (2 * count)] & 15;
        if (frame.Progressive)
        {
            bool valid = (spectralStart == 0 ? spectralEnd == 0 : spectralEnd >= spectralStart && spectralEnd <= 63 && count == 1)
                && (high == 0 || low == high - 1)
                && low <= 13;
            if (!valid)
            {
                frame.Report(
                    DctDiagnostics.DctScanInvalid,
                    DiagnosticSeverity.Warning,
                    JpegFrame.Invariant($"A progressive DCT scan has invalid parameters (Ss {spectralStart}, Se {spectralEnd}, Ah {high}, Al {low}, {count} components); the scan was skipped."));
                return false;
            }

            scan.Kind = spectralStart == 0 ? (high == 0 ? JpegScanKind.DcFirst : JpegScanKind.DcRefine) : (high == 0 ? JpegScanKind.AcFirst : JpegScanKind.AcRefine);
            CheckProgression(frame, count, spectralStart, spectralEnd, high, low);
        }
        else
        {
            if (spectralStart != 0 || spectralEnd != 63 || high != 0 || low != 0)
            {
                frame.Report(DctDiagnostics.DctScanInvalid, DiagnosticSeverity.Warning, "A sequential DCT scan has spectral selection or approximation parameters other than 0, 63, 0; they were ignored.");
            }

            scan.Kind = JpegScanKind.Sequential;
            spectralStart = 0;
            spectralEnd = 63;
            low = 0;
        }

        scan.SpectralStart = spectralStart;
        scan.SpectralEnd = spectralEnd;
        scan.ApproximationLow = low;
        bool codesDc = scan.Kind is JpegScanKind.Sequential or JpegScanKind.DcFirst;
        bool codesAc = scan.Kind is JpegScanKind.Sequential or JpegScanKind.AcFirst or JpegScanKind.AcRefine;
        for (int j = 0; j < count; j++)
        {
            JpegComponent component = frame.Components[scan.Components[j]];
            int tables = header[2 + (2 * j)];
            if (frame.Arithmetic)
            {
                component.DcConditioning = tables >> 4;
                component.AcConditioning = tables & 15;
                continue;
            }

            component.DcTable = codesDc ? Bind(frame, frame.Tables.Dc, tables >> 4, ac: false) : null;
            component.AcTable = codesAc ? Bind(frame, frame.Tables.Ac, tables & 15, ac: true) : null;
        }

        scan.Length = count;
        return true;
    }

    /// <summary>Records which coefficient bits a progressive scan codes and reports a scan that does not follow the earlier ones.</summary>
    /// <remarks>ITU-T T.81 §G.1.1.1.1 (the first DC scan of a component precedes its AC scans), §G.1.1.1.2 (Ah is the Al of the scan before); libjpeg-turbo <c>coef_bits</c>.</remarks>
    private static void CheckProgression(JpegFrame frame, int count, int spectralStart, int spectralEnd, int high, int low)
    {
        JpegScan scan = frame.Scan;
        bool bogus = false;
        for (int j = 0; j < count; j++)
        {
            int[] bits = frame.Components[scan.Components[j]].CoefficientBits;
            bogus |= spectralStart > 0 && bits[0] < 0;
            for (int k = spectralStart; k <= spectralEnd; k++)
            {
                bogus |= high != Math.Max(bits[k], 0);
                bits[k] = low;
            }
        }

        if (bogus)
        {
            frame.Report(
                DctDiagnostics.DctScanInvalid,
                DiagnosticSeverity.Warning,
                "A progressive DCT scan does not follow the scans before it (an AC scan before the first DC scan, or a refinement of bits not yet coded); it was decoded as given.");
        }
    }

    private static HuffmanTable Bind(JpegFrame frame, HuffmanTable[] tables, int destination, bool ac)
    {
        HuffmanTable table = tables[destination];
        if (!table.IsDefined)
        {
            StandardHuffmanTables.Install(table, ac, destination);
            frame.Report(DctDiagnostics.DctTableMissing, DiagnosticSeverity.Warning, "A DCT scan uses a Huffman table that was never defined; the typical table of ITU-T T.81 Annex K is used.");
        }

        return table;
    }

    /// <summary>Copies each scan component's quantization table, the first time the component is in a scan.</summary>
    public static bool LatchQuantization(JpegFrame frame)
    {
        JpegScan scan = frame.Scan;
        for (int j = 0; j < scan.Length; j++)
        {
            JpegComponent component = frame.Components[scan.Components[j]];
            if (component.QuantizationLatched)
            {
                continue;
            }

            int destination = component.QuantizationTable;
            if (!frame.Tables.QuantizationDefined[destination])
            {
                frame.Report(DctDiagnostics.DctTableMissing, DiagnosticSeverity.Error, "A DCT component uses a quantization table that was never defined; the image is not decoded.");
                return false;
            }

            if (frame.Tables.QuantizationWide[destination] && frame.Precision == 8)
            {
                frame.Report(DctDiagnostics.DctTableInvalid, DiagnosticSeverity.Warning, "An 8-bit DCT frame uses a 16-bit quantization table; its values are used.");
            }

            frame.Tables.Quantization[destination].CopyTo(component.Quantization, 0);
            component.QuantizationLatched = true;
        }

        return true;
    }

    /// <summary>Returns the position of the next marker from <paramref name="position"/>, skipping entropy-coded bytes (FF 00 is data), or the end.</summary>
    public static int SkipEntropyCodedData(ReadOnlySpan<byte> data, int position)
    {
        while (position + 1 < data.Length && !(data[position] == 0xFF && data[position + 1] != 0 && data[position + 1] != 0xFF))
        {
            position++;
        }

        return position + 1 < data.Length ? position : data.Length;
    }

    /// <summary>Returns the position of the marker that ends a scan's entropy-coded data, past its restart markers.</summary>
    public static int SkipScan(ReadOnlySpan<byte> data, int position)
    {
        while (true)
        {
            position = SkipEntropyCodedData(data, position);
            if (position + 1 >= data.Length || data[position + 1] is < 0xD0 or > 0xD7)
            {
                return position;
            }

            position += 2;
        }
    }

    public static bool StartsAtMarker(ReadOnlySpan<byte> data, int position) =>
        position + 1 >= data.Length || (data[position] == 0xFF && data[position + 1] != 0);
}
