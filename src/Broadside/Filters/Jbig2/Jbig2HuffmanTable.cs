using System.Buffers.Binary;

namespace Broadside.Filters.Jbig2;

/// <summary>The kind of a Huffman table line (ITU-T T.88 B.2): a normal range, the lower or upper open range, or the out-of-band value.</summary>
internal enum Jbig2HuffmanLineKind : byte
{
    /// <summary>A range starting at RANGELOW.</summary>
    Normal,

    /// <summary>The lower range line: values RANGELOW - offset.</summary>
    Lower,

    /// <summary>The upper range line: values RANGELOW + offset.</summary>
    Upper,

    /// <summary>The out-of-band line.</summary>
    OutOfBand,
}

/// <summary>One table line: prefix length, range length and lowest value (ITU-T T.88 B.2, Tables B.1 to B.15).</summary>
/// <param name="PrefixLength">PREFLEN; 0 marks a line that is never used.</param>
/// <param name="RangeLength">RANGELEN, 0 to 32.</param>
/// <param name="RangeLow">RANGELOW.</param>
/// <param name="Kind">Normal, lower, upper or out-of-band.</param>
internal readonly record struct Jbig2HuffmanLine(int PrefixLength, int RangeLength, long RangeLow, Jbig2HuffmanLineKind Kind = Jbig2HuffmanLineKind.Normal);

/// <summary>
/// A JBIG2 Huffman table (ITU-T T.88 Annex B): table lines whose prefix codes are assigned by B.3, decoded bit by bit (B.4). Built
/// from the standard tables B.1 to B.15, from a code table segment (B.2, segment type 53), or from code lengths whose values are the
/// line indices (the symbol ID tables of §7.4.3.1.7 and the run-code table that codes them). Immutable once built, so the standard
/// tables and the tables of a cached <c>JBIG2Globals</c> stream are shared across threads.
/// </summary>
/// <remarks>ITU-T T.88 Annex B.2 (code table structure), B.3 (assigning the prefix codes), B.4 (using a table), B.5 (standard tables).</remarks>
internal sealed class Jbig2HuffmanTable
{
    /// <summary>The value <see cref="Decode"/> returns for the out-of-band line.</summary>
    public const long Oob = long.MaxValue;

    /// <summary>The value <see cref="Decode"/> returns when the bits read match no code (an incomplete table met damaged data).</summary>
    public const long Invalid = long.MinValue;

    private const int MaxPrefixLength = 32;

    private static readonly Jbig2HuffmanTable?[] StandardTables = new Jbig2HuffmanTable?[15];

    // Lines sorted by (prefix length, line index); per length the first code, the number of codes and the first sorted index.
    private readonly Jbig2HuffmanLine[] _sorted;
    private readonly long[] _firstCode;
    private readonly int[] _count;
    private readonly int[] _start;
    private readonly int _maxLength;

    private Jbig2HuffmanTable(ReadOnlySpan<Jbig2HuffmanLine> lines)
    {
        _maxLength = 0;
        foreach (Jbig2HuffmanLine line in lines)
        {
            _maxLength = Math.Max(_maxLength, line.PrefixLength);
        }

        _count = new int[_maxLength + 1];
        foreach (Jbig2HuffmanLine line in lines)
        {
            _count[line.PrefixLength]++;
        }

        _count[0] = 0;
        _firstCode = new long[_maxLength + 1];
        _start = new int[_maxLength + 1];
        int used = 0;
        for (int length = 1; length <= _maxLength; length++)
        {
            _firstCode[length] = (_firstCode[length - 1] + _count[length - 1]) * 2;
            _start[length] = used;
            used += _count[length];
        }

        _sorted = new Jbig2HuffmanLine[used];
        int[] next = (int[])_start.Clone();
        foreach (Jbig2HuffmanLine line in lines)
        {
            if (line.PrefixLength > 0)
            {
                _sorted[next[line.PrefixLength]++] = line;
            }
        }

        foreach (Jbig2HuffmanLine line in lines)
        {
            HasOutOfBand |= line.Kind == Jbig2HuffmanLineKind.OutOfBand && line.PrefixLength > 0;
        }
    }

    /// <summary>Gets a value indicating whether the table can code the out-of-band value (HTOOB with a used OOB line).</summary>
    public bool HasOutOfBand { get; }

    /// <summary>Builds a table from its lines (B.3 assigns the codes in line order within each prefix length).</summary>
    /// <param name="lines">The table lines; prefix lengths above 32 are not allowed.</param>
    /// <returns>The table.</returns>
    public static Jbig2HuffmanTable Create(ReadOnlySpan<Jbig2HuffmanLine> lines) => new(lines);

    /// <summary>A table whose line <c>i</c> has the prefix length <c>lengths[i]</c>, no range bits, and the value <c>i</c>.</summary>
    /// <param name="lengths">The code lengths, 0 to 32 (0: never used).</param>
    /// <returns>The table.</returns>
    public static Jbig2HuffmanTable FromCodeLengths(ReadOnlySpan<int> lengths)
    {
        var lines = new Jbig2HuffmanLine[lengths.Length];
        for (int i = 0; i < lengths.Length; i++)
        {
            lines[i] = new Jbig2HuffmanLine(Math.Clamp(lengths[i], 0, MaxPrefixLength), 0, i);
        }

        return new Jbig2HuffmanTable(lines);
    }

    /// <summary>Standard table B.<paramref name="number"/> (1 to 15), built once per process.</summary>
    /// <param name="number">The table number in Annex B.5.</param>
    /// <returns>The table.</returns>
    public static Jbig2HuffmanTable Standard(int number)
    {
        Jbig2HuffmanTable? table = Volatile.Read(ref StandardTables[number - 1]);
        if (table is null)
        {
            table = new Jbig2HuffmanTable(StandardLines(number));
            Volatile.Write(ref StandardTables[number - 1], table);
        }

        return table;
    }

    /// <summary>Decodes a code table segment's data (B.2).</summary>
    /// <param name="data">The segment data: flags, HTLOW, HTHIGH, then the bit-packed table lines.</param>
    /// <param name="table">The table.</param>
    /// <param name="error">Why the data does not describe a table.</param>
    /// <returns><see langword="true"/> when a table was decoded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out Jbig2HuffmanTable? table, out string? error)
    {
        table = null;
        if (data.Length < 9)
        {
            error = "has fewer than the 9 bytes of its flags and value bounds (ITU-T T.88 B.2)";
            return false;
        }

        byte flags = data[0];
        bool outOfBand = (flags & 1) != 0;
        int prefixBits = ((flags >> 1) & 7) + 1;
        int rangeBits = ((flags >> 4) & 7) + 1;
        long low = BinaryPrimitives.ReadInt32BigEndian(data[1..]);
        long high = BinaryPrimitives.ReadInt32BigEndian(data[5..]);
        if (high <= low)
        {
            error = $"has the highest value {high} not above its lowest value {low} (ITU-T T.88 B.2.3)";
            return false;
        }

        var reader = new Jbig2BitReader(data[9..]);
        var lines = new List<Jbig2HuffmanLine>();
        long current = low;
        while (current < high)
        {
            int prefix = (int)reader.ReadBits(prefixBits);
            int range = (int)reader.ReadBits(rangeBits);
            if (range > 32)
            {
                error = $"has a table line with the range length {range}, more than 32 bits (ITU-T T.88 B.2)";
                return false;
            }

            lines.Add(new Jbig2HuffmanLine(prefix, range, current));
            current += 1L << range;
            if (reader.IsPastEnd)
            {
                error = "ends inside its table lines (ITU-T T.88 B.2)";
                return false;
            }
        }

        lines.Add(new Jbig2HuffmanLine((int)reader.ReadBits(prefixBits), 32, low - 1, Jbig2HuffmanLineKind.Lower));
        lines.Add(new Jbig2HuffmanLine((int)reader.ReadBits(prefixBits), 32, high, Jbig2HuffmanLineKind.Upper));
        if (outOfBand)
        {
            lines.Add(new Jbig2HuffmanLine((int)reader.ReadBits(prefixBits), 0, 0, Jbig2HuffmanLineKind.OutOfBand));
        }

        if (reader.IsPastEnd)
        {
            error = "ends inside its range and out-of-band lines (ITU-T T.88 B.2)";
            return false;
        }

        foreach (Jbig2HuffmanLine line in lines)
        {
            if (line.PrefixLength > MaxPrefixLength)
            {
                error = $"has a prefix length of {line.PrefixLength}, more than 32 bits";
                return false;
            }
        }

        table = new Jbig2HuffmanTable([.. lines]);
        error = null;
        return true;
    }

    /// <summary>Decodes one value (B.4): <see cref="Oob"/> for the out-of-band line, <see cref="Invalid"/> when no code matches.</summary>
    /// <param name="reader">The bit reader.</param>
    /// <returns>The value, <see cref="Oob"/> or <see cref="Invalid"/>.</returns>
    public long Decode(ref Jbig2BitReader reader)
    {
        long code = 0;
        for (int length = 1; length <= _maxLength; length++)
        {
            code = (code << 1) | (uint)reader.ReadBit();
            long index = code - _firstCode[length];
            if (index >= 0 && index < _count[length])
            {
                Jbig2HuffmanLine line = _sorted[_start[length] + (int)index];
                if (line.Kind == Jbig2HuffmanLineKind.OutOfBand)
                {
                    return Oob;
                }

                long offset = line.RangeLength == 0 ? 0 : reader.ReadBits(line.RangeLength);
                return line.Kind == Jbig2HuffmanLineKind.Lower ? line.RangeLow - offset : line.RangeLow + offset;
            }
        }

        return Invalid;
    }

    // Annex B.5, Tables B.1 to B.15: PREFLEN, RANGELEN, RANGELOW in table order (built from these, not from the printed ranges:
    // B.7 prints the range of its 5/5/-64 line as -64..-32 where RANGELOW + 2^RANGELEN gives -64..-33).
    private static Jbig2HuffmanLine[] StandardLines(int number) => number switch
    {
        1 => [N(1, 4, 0), N(2, 8, 16), N(3, 16, 272), U(3, 65808)],
        2 => [N(1, 0, 0), N(2, 0, 1), N(3, 0, 2), N(4, 3, 3), N(5, 6, 11), U(6, 75), O(6)],
        3 => [N(8, 8, -256), N(1, 0, 0), N(2, 0, 1), N(3, 0, 2), N(4, 3, 3), N(5, 6, 11), L(8, -257), U(7, 75), O(6)],
        4 => [N(1, 0, 1), N(2, 0, 2), N(3, 0, 3), N(4, 3, 4), N(5, 6, 12), U(5, 76)],
        5 => [N(7, 8, -255), N(1, 0, 1), N(2, 0, 2), N(3, 0, 3), N(4, 3, 4), N(5, 6, 12), L(7, -256), U(6, 76)],
        6 =>
        [
            N(5, 10, -2048), N(4, 9, -1024), N(4, 8, -512), N(4, 7, -256), N(5, 6, -128), N(5, 5, -64), N(4, 5, -32), N(2, 7, 0),
            N(3, 7, 128), N(3, 8, 256), N(4, 9, 512), N(4, 10, 1024), L(6, -2049), U(6, 2048),
        ],
        7 =>
        [
            N(4, 9, -1024), N(3, 8, -512), N(4, 7, -256), N(5, 6, -128), N(5, 5, -64), N(4, 5, -32), N(4, 5, 0), N(5, 5, 32),
            N(5, 6, 64), N(4, 7, 128), N(3, 8, 256), N(3, 9, 512), N(3, 10, 1024), L(5, -1025), U(5, 2048),
        ],
        8 =>
        [
            N(8, 3, -15), N(9, 1, -7), N(8, 1, -5), N(9, 0, -3), N(7, 0, -2), N(4, 0, -1), N(2, 1, 0), N(5, 0, 2), N(6, 0, 3),
            N(3, 4, 4), N(6, 1, 20), N(4, 4, 22), N(4, 5, 38), N(5, 6, 70), N(5, 7, 134), N(6, 7, 262), N(7, 8, 390), N(6, 10, 646),
            L(9, -16), U(9, 1670), O(2),
        ],
        9 =>
        [
            N(8, 4, -31), N(9, 2, -15), N(8, 2, -11), N(9, 1, -7), N(7, 1, -5), N(4, 1, -3), N(3, 1, -1), N(3, 1, 1), N(5, 1, 3),
            N(6, 1, 5), N(3, 5, 7), N(6, 2, 39), N(4, 5, 43), N(4, 6, 75), N(5, 7, 139), N(5, 8, 267), N(6, 8, 523), N(7, 9, 779),
            N(6, 11, 1291), L(9, -32), U(9, 3339), O(2),
        ],
        10 =>
        [
            N(7, 4, -21), N(8, 0, -5), N(7, 0, -4), N(5, 0, -3), N(2, 2, -2), N(5, 0, 2), N(6, 0, 3), N(7, 0, 4), N(8, 0, 5),
            N(2, 6, 6), N(5, 5, 70), N(6, 5, 102), N(6, 6, 134), N(6, 7, 198), N(6, 8, 326), N(6, 9, 582), N(6, 10, 1094),
            N(7, 11, 2118), L(8, -22), U(8, 4166), O(2),
        ],
        11 =>
        [
            N(1, 0, 1), N(2, 1, 2), N(4, 0, 4), N(4, 1, 5), N(5, 1, 7), N(5, 2, 9), N(6, 2, 13), N(7, 2, 17), N(7, 3, 21),
            N(7, 4, 29), N(7, 5, 45), N(7, 6, 77), U(7, 141),
        ],
        12 =>
        [
            N(1, 0, 1), N(2, 0, 2), N(3, 1, 3), N(5, 0, 5), N(5, 1, 6), N(6, 1, 8), N(7, 0, 10), N(7, 1, 11), N(7, 2, 13),
            N(7, 3, 17), N(7, 4, 25), N(8, 5, 41), U(8, 73),
        ],
        13 =>
        [
            N(1, 0, 1), N(3, 0, 2), N(4, 0, 3), N(5, 0, 4), N(4, 1, 5), N(3, 3, 7), N(6, 1, 15), N(6, 2, 17), N(6, 3, 21),
            N(6, 4, 29), N(6, 5, 45), N(7, 6, 77), U(7, 141),
        ],
        14 => [N(3, 0, -2), N(3, 0, -1), N(1, 0, 0), N(3, 0, 1), N(3, 0, 2)],
        15 =>
        [
            N(7, 4, -24), N(6, 2, -8), N(5, 1, -4), N(4, 0, -2), N(3, 0, -1), N(1, 0, 0), N(3, 0, 1), N(4, 0, 2), N(5, 1, 3),
            N(6, 2, 5), N(7, 4, 9), L(7, -25), U(7, 25),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(number)),
    };

    private static Jbig2HuffmanLine N(int prefix, int range, long low) => new(prefix, range, low);

    private static Jbig2HuffmanLine L(int prefix, long low) => new(prefix, 32, low, Jbig2HuffmanLineKind.Lower);

    private static Jbig2HuffmanLine U(int prefix, long low) => new(prefix, 32, low, Jbig2HuffmanLineKind.Upper);

    private static Jbig2HuffmanLine O(int prefix) => new(prefix, 0, 0, Jbig2HuffmanLineKind.OutOfBand);
}
