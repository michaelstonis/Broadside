using System.IO.Compression;

namespace Broadside.TestSupport;

/// <summary>
/// Encoders for the standard filters, the counterparts of the decoders under test: ports of the encoders in
/// <c>tests/Corpus/generate.py</c> (<c>lzw_encode</c>, <c>ascii85_encode</c>, <c>runlength_encode</c>) and the BCL's zlib. Shared by
/// the filter tests and the filter benchmarks (linked as source there, as <see cref="CorpusLocator"/> is). ISO 32000-2 §7.4.
/// </summary>
public static class FilterEncoders
{
    /// <summary>Deterministic bytes from a small alphabet of letters: compressible, so LZW tables fill and clear several times.</summary>
    /// <param name="length">How many bytes.</param>
    /// <param name="alphabet">How many distinct letters.</param>
    /// <param name="seed">The generator's seed.</param>
    /// <returns>The bytes.</returns>
    public static byte[] SampleData(int length, int alphabet = 7, int seed = 38)
    {
        // A linear congruential generator: deterministic across runtimes, and not mistaken for a security use of Random.
        uint state = (uint)seed;
        byte[] data = new byte[length];
        for (int index = 0; index < length; index++)
        {
            state = (state * 1664525) + 1013904223;
            data[index] = (byte)('a' + ((state >> 16) % (uint)alphabet));
        }

        return data;
    }

    /// <summary>Compresses with the BCL's zlib (RFC 1950): what FlateDecode decodes.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The zlib stream.</returns>
    public static byte[] Zlib(ReadOnlySpan<byte> data)
    {
        var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// <c>lzw_encode</c> of <c>generate.py</c>, ported: 9 to 12-bit codes, clear first, EOD last, a clear code whenever the table
    /// fills, the code length growing one code early when <paramref name="earlyChange"/> is 1 (§7.4.4.2, Table 8).
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="earlyChange">The EarlyChange parameter, 0 or 1.</param>
    /// <returns>The LZW codes, packed high-order bit first.</returns>
    public static byte[] LzwEncode(ReadOnlySpan<byte> data, int earlyChange = 1)
    {
        var codes = new List<(int Code, int Width)>();
        Dictionary<string, int> table = NewTable();
        int nextCode = 258;
        int width = 9;
        codes.Add((256, width));
        string pending = string.Empty;
        foreach (byte value in data)
        {
            string extended = pending + (char)value;
            if (table.ContainsKey(extended))
            {
                pending = extended;
                continue;
            }

            codes.Add((table[pending], width));
            table[extended] = nextCode++;
            if (nextCode + earlyChange > (1 << width) && width < 12)
            {
                width++;
            }

            if (nextCode + earlyChange >= 4096)
            {
                codes.Add((256, width));
                table = NewTable();
                nextCode = 258;
                width = 9;
            }

            pending = ((char)value).ToString();
        }

        if (pending.Length > 0)
        {
            codes.Add((table[pending], width));
        }

        codes.Add((257, width));
        var output = new List<byte>();
        long accumulator = 0;
        int bits = 0;
        foreach ((int code, int codeWidth) in codes)
        {
            accumulator = (accumulator << codeWidth) | (uint)code;
            bits += codeWidth;
            while (bits >= 8)
            {
                output.Add((byte)(accumulator >> (bits - 8)));
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            output.Add((byte)(accumulator << (8 - bits)));
        }

        return [.. output];

        static Dictionary<string, int> NewTable() => Enumerable.Range(0, 256).ToDictionary(index => ((char)index).ToString(), index => index);
    }

    /// <summary><c>ascii85_encode</c> of <c>generate.py</c>: groups of five digits, a final partial group, <c>~&gt;</c> (§7.4.3).</summary>
    /// <param name="data">The data.</param>
    /// <returns>The ASCII base-85 text.</returns>
    public static byte[] Ascii85Encode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>();
        byte[] group = new byte[5];
        for (int index = 0; index < data.Length; index += 4)
        {
            int count = Math.Min(4, data.Length - index);
            uint value = 0;
            for (int offset = 0; offset < 4; offset++)
            {
                value = (value << 8) | (offset < count ? data[index + offset] : 0u);
            }

            for (int digit = 4; digit >= 0; digit--)
            {
                group[digit] = (byte)('!' + (value % 85));
                value /= 85;
            }

            output.AddRange(group.AsSpan(0, count + 1));
        }

        return [.. output, (byte)'~', (byte)'>'];
    }

    /// <summary><c>runlength_encode</c> of <c>generate.py</c>: repeated runs of 2 to 128, literal runs of up to 128, EOD 128 (§7.4.5).</summary>
    /// <param name="data">The data.</param>
    /// <returns>The run-length encoded data.</returns>
    public static byte[] RunLengthEncode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>();
        int index = 0;
        while (index < data.Length)
        {
            int run = 1;
            while (index + run < data.Length && run < 128 && data[index + run] == data[index])
            {
                run++;
            }

            if (run >= 2)
            {
                output.Add((byte)(257 - run));
                output.Add(data[index]);
                index += run;
                continue;
            }

            int start = index;
            while (index < data.Length && index - start < 128 && !(index + 1 < data.Length && data[index + 1] == data[index]))
            {
                index++;
            }

            output.Add((byte)(index - start - 1));
            output.AddRange(data[start..index]);
        }

        output.Add(0x80);
        return [.. output];
    }
}
