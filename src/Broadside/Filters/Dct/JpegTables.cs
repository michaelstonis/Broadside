using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Filters.Codecs;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>
/// The tables a JPEG stream defines before or between its scans, by destination 0 to 3: Huffman tables, quantization tables and
/// arithmetic conditioning values.
/// </summary>
/// <remarks>
/// ITU-T T.81 §B.2.4.1 (DQT, Table B.4), §B.2.4.2 (DHT, Table B.5), §B.2.4.3 (DAC, Table B.6; defaults L = 0, U = 1, Kx = 5 from
/// §F.1.4.4.1.2 and §F.1.4.4.2). A later definition replaces an earlier one for the scans after it.
/// </remarks>
internal sealed class JpegTables
{
    /// <summary>Gets the DC Huffman tables.</summary>
    public HuffmanTable[] Dc { get; } = [new(), new(), new(), new()];

    /// <summary>Gets the AC Huffman tables.</summary>
    public HuffmanTable[] Ac { get; } = [new(), new(), new(), new()];

    /// <summary>Gets the quantization tables, natural order.</summary>
    public int[][] Quantization { get; } = [new int[64], new int[64], new int[64], new int[64]];

    /// <summary>Gets, per destination, whether a quantization table was defined.</summary>
    public bool[] QuantizationDefined { get; } = new bool[4];

    /// <summary>Gets, per destination, whether the quantization table has 16-bit values.</summary>
    public bool[] QuantizationWide { get; } = new bool[4];

    /// <summary>Gets the arithmetic DC conditioning lower bounds L.</summary>
    public byte[] DcLower { get; } = [0, 0, 0, 0];

    /// <summary>Gets the arithmetic DC conditioning upper bounds U.</summary>
    public byte[] DcUpper { get; } = [1, 1, 1, 1];

    /// <summary>Gets the arithmetic AC conditioning values Kx.</summary>
    public byte[] AcSplit { get; } = [5, 5, 5, 5];

    /// <summary>Reads a DAC segment: conditioning values L and U of DC tables, Kx of AC tables.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.3, Table B.6 (0 ≤ L ≤ U ≤ 15, 1 ≤ Kx ≤ 63); §F.1.4.4.1.2 and §F.1.4.4.2 (defaults L = 0, U = 1, Kx = 5).</remarks>
    /// <param name="body">The segment parameters.</param>
    /// <param name="reporter">Where deviations are reported.</param>
    public void ReadArithmeticConditioning(ReadOnlySpan<byte> body, CodecReporter reporter)
    {
        for (int i = 0; i + 1 < body.Length; i += 2)
        {
            int tableClass = body[i] >> 4;
            int destination = body[i] & 15;
            int value = body[i + 1];
            if (tableClass > 1 || destination > 3 || (tableClass == 0 && (value & 15) > value >> 4) || (tableClass == 1 && value is < 1 or > 63))
            {
                reporter.Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT arithmetic conditioning table is invalid; it was ignored.");
                continue;
            }

            if (tableClass == 0)
            {
                DcLower[destination] = (byte)(value & 15);
                DcUpper[destination] = (byte)(value >> 4);
            }
            else
            {
                AcSplit[destination] = (byte)value;
            }
        }

        if ((body.Length & 1) != 0)
        {
            reporter.Report(DiagnosticCodes.DctSegmentInvalid, DiagnosticSeverity.Warning, "A DCT DAC segment has an odd length; its last byte was ignored.");
        }
    }

    /// <summary>Reads a DQT segment: one or more 8- or 16-bit tables in zig-zag order, stored in natural order.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.1, Table B.4.</remarks>
    /// <param name="body">The segment parameters.</param>
    /// <param name="reporter">Where deviations are reported.</param>
    public void ReadQuantizationTables(ReadOnlySpan<byte> body, CodecReporter reporter)
    {
        int i = 0;
        while (i < body.Length)
        {
            int precision = body[i] >> 4;
            int destination = body[i] & 15;
            int size = precision == 0 ? 64 : 128;
            if (precision > 1 || destination > 3 || i + 1 + size > body.Length)
            {
                reporter.Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT quantization table is invalid; it was ignored.");
                return;
            }

            int[] table = Quantization[destination];
            ReadOnlySpan<byte> values = body.Slice(i + 1, size);
            for (int k = 0; k < 64; k++)
            {
                table[JpegMarkers.NaturalOrder[k]] = precision == 0 ? values[k] : BinaryPrimitives.ReadUInt16BigEndian(values[(2 * k)..]);
            }

            QuantizationDefined[destination] = true;
            QuantizationWide[destination] = precision == 1;
            i += 1 + size;
        }
    }

    /// <summary>Reads a DHT segment: one or more tables, each BITS and HUFFVAL.</summary>
    /// <remarks>ITU-T T.81 §B.2.4.2, Table B.5; Annex C.</remarks>
    /// <param name="body">The segment parameters.</param>
    /// <param name="reporter">Where deviations are reported.</param>
    public void ReadHuffmanTables(ReadOnlySpan<byte> body, CodecReporter reporter)
    {
        int i = 0;
        while (i < body.Length)
        {
            if (i + 17 > body.Length)
            {
                reporter.Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table is truncated; it was ignored.");
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
                reporter.Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table is invalid; it was ignored.");
                return;
            }

            HuffmanTable table = tableClass == 0 ? Dc[destination] : Ac[destination];
            if (!table.Build(counts, body.Slice(i + 17, total), tableClass == 0 ? 15 : 255))
            {
                reporter.Report(DiagnosticCodes.DctTableInvalid, DiagnosticSeverity.Warning, "A DCT Huffman table does not form a valid code; it was ignored.");
            }

            i += 17 + total;
        }
    }

    /// <summary>Forgets every table for the next image.</summary>
    public void Reset()
    {
        for (int i = 0; i < 4; i++)
        {
            Dc[i].Clear();
            Ac[i].Clear();
            QuantizationDefined[i] = false;
            QuantizationWide[i] = false;
        }

        DcLower.AsSpan().Clear();
        DcUpper.AsSpan().Fill(1);
        AcSplit.AsSpan().Fill(5);
    }
}
