using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>The <c>LZWDecode</c> filter: Lempel-Ziv-Welch with 9- to 12-bit codes. Decode only.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.4.1 to §7.4.4.3. Codes are read high-order bit first; 256 clears the table, 257 ends the data, 258 and up are
/// table entries. The code length grows to 10, 11 and 12 bits as the table reaches 512, 1024 and 2048 entries, one code early when
/// the <c>EarlyChange</c> parameter is 1 (the default) and only when necessary when it is 0 (Table 8). The predictor parameters of
/// Table 8 are applied after this filter by the pipeline (§7.4.4.4).
/// </para>
/// <para>
/// Lenient repairs, one diagnostic each: data that ends without the end-of-data code decodes to its end
/// (<c>FilterDataTruncated</c>); a code that is not yet in the table ends the data, and a full table without a clear code stops
/// growing (<c>FilterDataInvalid</c>); an <c>EarlyChange</c> other than 0 or 1 is read as 1 (<c>DecodeParmsInvalid</c>).
/// </para>
/// <para>The table lives in arrays rented from the shared pool: no allocation per code.</para>
/// </remarks>
public sealed class LzwDecodeFilter : IStreamFilter
{
    private const int ClearTable = 256;
    private const int EndOfData = 257;
    private const int FirstEntry = 258;
    private const int TableSize = 4096;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4: <c>LZWDecode</c>; abbreviated <c>LZW</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.LzwDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4.2 and §7.4.4.3 Table 8 (<c>EarlyChange</c>).</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        int earlyChange = ReadEarlyChange(context);
        ushort[] prefixes = ArrayPool<ushort>.Shared.Rent(TableSize);
        ushort[] lengths = ArrayPool<ushort>.Shared.Rent(TableSize);
        byte[] suffixes = ArrayPool<byte>.Shared.Rent(TableSize);
        byte[] firsts = ArrayPool<byte>.Shared.Rent(TableSize);
        try
        {
            Outcome outcome = Decode(encoded.Span, output, earlyChange, prefixes, lengths, suffixes, firsts);
            if ((outcome & Outcome.InvalidCode) != 0)
            {
                context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Error, "LZWDecode data holds a code that is not in the table; the data ends there.");
            }

            if ((outcome & Outcome.TableFull) != 0)
            {
                context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Warning, "LZWDecode data fills the table without a clear-table code; no more entries are added.");
            }

            if ((outcome & Outcome.Truncated) != 0)
            {
                context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, "LZWDecode data ends without the end-of-data code 257; it is decoded to its end.");
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(prefixes);
            ArrayPool<ushort>.Shared.Return(lengths);
            ArrayPool<byte>.Shared.Return(suffixes);
            ArrayPool<byte>.Shared.Return(firsts);
        }
    }

    private static int ReadEarlyChange(FilterContext context)
    {
        long value = context.ReadInteger(FilterNames.EarlyChange, defaultValue: 1, long.MinValue, long.MaxValue);
        if (value is 0 or 1)
        {
            return (int)value;
        }

        context.Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, "The EarlyChange parameter shall be 0 or 1; it is read as 1.");
        return 1;
    }

    private static Outcome Decode(
        ReadOnlySpan<byte> input,
        IBufferWriter<byte> output,
        int earlyChange,
        Span<ushort> prefixes,
        Span<ushort> lengths,
        Span<byte> suffixes,
        Span<byte> firsts)
    {
        for (int code = 0; code < 256; code++)
        {
            lengths[code] = 1;
            suffixes[code] = (byte)code;
            firsts[code] = (byte)code;
        }

        var writer = new FilterOutput(output);
        Outcome outcome = Outcome.Truncated;
        int position = 0;
        uint bits = 0;
        int bitCount = 0;
        int nextCode = FirstEntry;
        int previous = -1;
        while (true)
        {
            int width = CodeWidth(nextCode + earlyChange);
            while (bitCount < width && position < input.Length)
            {
                bits = (bits << 8) | input[position++];
                bitCount += 8;
            }

            if (bitCount < width)
            {
                break;
            }

            bitCount -= width;
            int code = (int)(bits >> bitCount) & ((1 << width) - 1);
            if (code == ClearTable)
            {
                nextCode = FirstEntry;
                previous = -1;
                continue;
            }

            if (code == EndOfData)
            {
                outcome &= ~Outcome.Truncated;
                break;
            }

            byte first;
            if (code < 256 || (code >= FirstEntry && code < nextCode && previous >= 0))
            {
                WriteEntry(ref writer, code, prefixes, lengths, suffixes);
                first = firsts[code];
            }
            else if (code == nextCode && previous >= 0)
            {
                // The entry being defined by this very code: the previous string followed by its own first byte (§7.4.4.2).
                first = firsts[previous];
                WriteEntry(ref writer, previous, prefixes, lengths, suffixes);
                writer.Write(first);
            }
            else
            {
                outcome = (outcome & ~Outcome.Truncated) | Outcome.InvalidCode;
                break;
            }

            if (previous >= 0)
            {
                if (nextCode < TableSize)
                {
                    prefixes[nextCode] = (ushort)previous;
                    suffixes[nextCode] = first;
                    firsts[nextCode] = firsts[previous];
                    lengths[nextCode] = (ushort)(lengths[previous] + 1);
                    nextCode++;
                }
                else
                {
                    outcome |= Outcome.TableFull;
                }
            }

            previous = code;
        }

        writer.Flush();
        return outcome;
    }

    /// <summary>The code length once the table holds <paramref name="entries"/> entries counting the early change: 9 to 12 bits.</summary>
    private static int CodeWidth(int entries) => entries switch
    {
        < 512 => 9,
        < 1024 => 10,
        < 2048 => 11,
        _ => 12,
    };

    private static void WriteEntry(ref FilterOutput writer, int code, ReadOnlySpan<ushort> prefixes, ReadOnlySpan<ushort> lengths, ReadOnlySpan<byte> suffixes)
    {
        Span<byte> target = writer.Reserve(lengths[code]);
        for (int index = target.Length - 1; index >= 0; index--)
        {
            target[index] = suffixes[code];
            code = prefixes[code];
        }
    }

    [Flags]
    private enum Outcome
    {
        None = 0,
        Truncated = 1,
        InvalidCode = 2,
        TableFull = 4,
    }
}
