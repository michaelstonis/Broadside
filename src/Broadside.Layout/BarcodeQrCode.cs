using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// The QR Code model 2 encoder (ISO/IEC 18004): the smallest version 1 to 40 that holds the data at the error correction level, numeric,
/// alphanumeric and byte segments chosen to minimize the bit stream, Reed-Solomon blocks interleaved, and the mask of the eight with the
/// lowest penalty.
/// </summary>
internal static class BarcodeQrCode
{
    /// <summary>The alphanumeric mode's 45 characters, in the order of their values (ISO/IEC 18004 Table 5).</summary>
    internal const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    /// <summary>The error correction codewords per block, by level (L, M, Q, H) and version (ISO/IEC 18004 Table 9).</summary>
    private static readonly byte[][] EccPerBlock =
    [
        [0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    /// <summary>The number of error correction blocks, by level (L, M, Q, H) and version (ISO/IEC 18004 Table 9).</summary>
    private static readonly byte[][] BlockCount =
    [
        [0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];

    private enum Mode
    {
        Numeric,
        Alphanumeric,
        Byte,
    }

    /// <summary>Encodes text: Latin-1 text as bytes without an ECI, other text as UTF-8 after an ECI 26 header.</summary>
    public static (bool[] Modules, int Size) Encode(string data, QrErrorCorrection level)
    {
        bool latin1 = true;
        foreach (char character in data)
        {
            if (character > 0xFF)
            {
                latin1 = false;
                break;
            }
        }

        var units = new List<Unit>(data.Length);
        if (latin1)
        {
            foreach (char character in data)
            {
                units.Add(new Unit(character, [(byte)character]));
            }
        }
        else
        {
            foreach (Rune rune in data.EnumerateRunes())
            {
                byte[] bytes = new byte[rune.Utf8SequenceLength];
                rune.EncodeToUtf8(bytes);
                units.Add(new Unit(rune.Value, bytes));
            }
        }

        return Encode(units, level, eci: !latin1);
    }

    /// <summary>Encodes bytes as they are, with no ECI.</summary>
    public static (bool[] Modules, int Size) Encode(ReadOnlySpan<byte> data, QrErrorCorrection level)
    {
        var units = new List<Unit>(data.Length);
        foreach (byte value in data)
        {
            units.Add(new Unit(value, [value]));
        }

        return Encode(units, level, eci: false);
    }

    /// <summary>The number of data codewords of a symbol (ISO/IEC 18004 Table 7).</summary>
    internal static int DataCodewords(int version, QrErrorCorrection level) =>
        (RawDataModules(version) / 8) - (EccPerBlock[(int)level][version] * BlockCount[(int)level][version]);

    /// <summary>The modules left for codewords and remainder bits once the function patterns are placed.</summary>
    internal static int RawDataModules(int version)
    {
        int result = ((16 * version) + 128) * version + 64;
        if (version >= 2)
        {
            int alignments = (version / 7) + 2;
            result -= ((25 * alignments) - 10) * alignments - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    /// <summary>The bit stream, then the codewords of the smallest version that holds it, interleaved, and the version chosen.</summary>
    internal static (byte[] Data, byte[] Codewords, int Version) Codewords(List<Unit> units, QrErrorCorrection level, bool eci)
    {
        for (int version = 1; version <= 40; version++)
        {
            List<Segment> segments = Segments(units, version);
            int bits = (eci ? 12 : 0) + segments.Sum(segment => segment.Bits(version));
            int capacity = DataCodewords(version, level) * 8;
            if (bits <= capacity)
            {
                var stream = new BitStream();
                if (eci)
                {
                    stream.Append(0b0111, 4);
                    stream.Append(26, 8);
                }

                foreach (Segment segment in segments)
                {
                    segment.Write(stream, version, units);
                }

                stream.Append(0, Math.Min(4, capacity - stream.Length));
                stream.Append(0, (8 - (stream.Length % 8)) % 8);
                byte[] data = new byte[capacity / 8];
                stream.CopyTo(data);
                for (int index = stream.Length / 8, pad = 0; index < data.Length; index++, pad++)
                {
                    data[index] = pad % 2 == 0 ? (byte)0xEC : (byte)0x11;
                }

                return (data, Interleave(data, version, level), version);
            }
        }

        int largest = DataCodewords(40, level);
        throw new ArgumentException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The data does not fit the largest QR code at level {level}: version 40 holds {largest} codewords ({largest * 8} bits, such as {ByteCapacity(largest)} bytes), and the data needs more."));
    }

    private static (bool[] Modules, int Size) Encode(List<Unit> units, QrErrorCorrection level, bool eci)
    {
        (_, byte[] codewords, int version) = Codewords(units, level, eci);
        var symbol = new QrSymbol(version);
        symbol.DrawFunctionPatterns();
        symbol.DrawCodewords(codewords);
        int best = 0;
        long bestPenalty = long.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            symbol.ApplyMask(mask);
            symbol.DrawFormat(level, mask);
            long penalty = symbol.Penalty();
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = mask;
            }

            symbol.ApplyMask(mask);
        }

        symbol.ApplyMask(best);
        symbol.DrawFormat(level, best);
        return (symbol.Modules, symbol.Size);
    }

    /// <summary>How many bytes fill <paramref name="codewords"/> data codewords in one byte segment, with its header and terminator.</summary>
    private static int ByteCapacity(int codewords) => ((codewords * 8) - 4 - 16) / 8;

    /// <summary>
    /// The segmentation with the fewest bits for a version's count field widths: a shortest path over the characters, with one state per
    /// mode, costs in sixths of a bit (a numeric digit is 10/3 bits, an alphanumeric character 11/2), and a new segment's header charged
    /// when the mode changes.
    /// </summary>
    private static List<Segment> Segments(List<Unit> units, int version)
    {
        int count = units.Count;
        var segments = new List<Segment>();
        if (count == 0)
        {
            return segments;
        }

        const int modeCount = 3;
        long[][] cost = new long[count + 1][];
        int[][] from = new int[count + 1][];
        for (int index = 0; index <= count; index++)
        {
            cost[index] = new long[modeCount];
            from[index] = new int[modeCount];
        }

        for (int index = 0; index < count; index++)
        {
            Unit unit = units[index];
            for (int mode = 0; mode < modeCount; mode++)
            {
                cost[index + 1][mode] = long.MaxValue;
                if (!Fits(unit, (Mode)mode))
                {
                    continue;
                }

                long step = (Mode)mode switch
                {
                    Mode.Numeric => 20,
                    Mode.Alphanumeric => 33,
                    _ => unit.Bytes.Length * 48,
                };
                for (int previous = 0; previous < modeCount; previous++)
                {
                    if (cost[index][previous] == long.MaxValue)
                    {
                        continue;
                    }

                    // A segment's header: the mode indicator and the count field; the first character always opens one.
                    long header = index == 0 || previous != mode ? (4 + CountBits((Mode)mode, version)) * 6 : 0;

                    // A segment's partial final group rounds up: charge it when a segment opens, so ties favor fewer segments.
                    long rounding = header == 0 ? 0 : (Mode)mode switch { Mode.Numeric => 4, Mode.Alphanumeric => 3, _ => 0 };
                    long total = cost[index][previous] + header + rounding + step;
                    if (total < cost[index + 1][mode])
                    {
                        cost[index + 1][mode] = total;
                        from[index + 1][mode] = previous;
                    }
                }
            }
        }

        int current = 0;
        for (int mode = 1; mode < modeCount; mode++)
        {
            if (cost[count][mode] < cost[count][current])
            {
                current = mode;
            }
        }

        Mode[] modes = new Mode[count];
        for (int index = count; index > 0; index--)
        {
            modes[index - 1] = (Mode)current;
            current = from[index][current];
        }

        int start = 0;
        for (int index = 1; index <= count; index++)
        {
            if (index == count || modes[index] != modes[start])
            {
                segments.Add(new Segment(modes[start], start, index - start, units));
                start = index;
            }
        }

        return segments;
    }

    private static bool Fits(Unit unit, Mode mode) => mode switch
    {
        Mode.Numeric => unit.Value is >= '0' and <= '9',
        Mode.Alphanumeric => unit.Value < 128 && Alphanumeric.Contains((char)unit.Value, StringComparison.Ordinal),
        _ => true,
    };

    private static int CountBits(Mode mode, int version)
    {
        int range = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        return mode switch
        {
            Mode.Numeric => 10 + (range * 2),
            Mode.Alphanumeric => 9 + (range * 2),
            _ => range == 0 ? 8 : 16,
        };
    }

    /// <summary>Splits the data codewords into blocks, appends each block's error correction, and interleaves (ISO/IEC 18004 §7.6).</summary>
    private static byte[] Interleave(byte[] data, int version, QrErrorCorrection level)
    {
        int blocks = BlockCount[(int)level][version];
        int ecc = EccPerBlock[(int)level][version];
        int raw = RawDataModules(version) / 8;
        int shortBlocks = blocks - (raw % blocks);
        int shortData = (raw / blocks) - ecc;
        byte[][] dataBlocks = new byte[blocks][];
        byte[][] eccBlocks = new byte[blocks][];
        int offset = 0;
        for (int block = 0; block < blocks; block++)
        {
            int length = shortData + (block < shortBlocks ? 0 : 1);
            dataBlocks[block] = data[offset..(offset + length)];
            eccBlocks[block] = BarcodeReedSolomon.Qr.Encode(dataBlocks[block], ecc);
            offset += length;
        }

        byte[] result = new byte[raw];
        int at = 0;
        for (int index = 0; index <= shortData; index++)
        {
            for (int block = 0; block < blocks; block++)
            {
                if (index < dataBlocks[block].Length)
                {
                    result[at++] = dataBlocks[block][index];
                }
            }
        }

        for (int index = 0; index < ecc; index++)
        {
            for (int block = 0; block < blocks; block++)
            {
                result[at++] = eccBlocks[block][index];
            }
        }

        return result;
    }

    /// <summary>One character of the input: its value (a code point, or a byte) and the bytes byte mode writes for it.</summary>
    internal readonly record struct Unit(int Value, byte[] Bytes);

    private readonly record struct Segment(Mode Mode, int Start, int Count, List<Unit> Units)
    {
        public int Bits(int version)
        {
            int data = Mode switch
            {
                Mode.Numeric => (Count / 3 * 10) + (Count % 3 == 0 ? 0 : (Count % 3 * 3) + 1),
                Mode.Alphanumeric => (Count / 2 * 11) + (Count % 2 * 6),
                _ => ByteLength * 8,
            };
            return 4 + CountBits(Mode, version) + data;
        }

        private int ByteLength
        {
            get
            {
                int length = 0;
                for (int index = Start; index < Start + Count; index++)
                {
                    length += Units[index].Bytes.Length;
                }

                return length;
            }
        }

        public void Write(BitStream stream, int version, List<Unit> units)
        {
            switch (Mode)
            {
                case Mode.Numeric:
                    stream.Append(0b0001, 4);
                    stream.Append(Count, CountBits(Mode, version));
                    for (int index = Start; index < Start + Count; index += 3)
                    {
                        int digits = Math.Min(3, Start + Count - index);
                        int value = 0;
                        for (int digit = 0; digit < digits; digit++)
                        {
                            value = (value * 10) + (units[index + digit].Value - '0');
                        }

                        stream.Append(value, (digits * 3) + 1);
                    }

                    break;
                case Mode.Alphanumeric:
                    stream.Append(0b0010, 4);
                    stream.Append(Count, CountBits(Mode, version));
                    for (int index = Start; index < Start + Count; index += 2)
                    {
                        int first = Alphanumeric.IndexOf((char)units[index].Value, StringComparison.Ordinal);
                        if (index + 1 < Start + Count)
                        {
                            stream.Append((first * 45) + Alphanumeric.IndexOf((char)units[index + 1].Value, StringComparison.Ordinal), 11);
                        }
                        else
                        {
                            stream.Append(first, 6);
                        }
                    }

                    break;
                default:
                    stream.Append(0b0100, 4);
                    stream.Append(ByteLength, CountBits(Mode, version));
                    for (int index = Start; index < Start + Count; index++)
                    {
                        foreach (byte value in units[index].Bytes)
                        {
                            stream.Append(value, 8);
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>A growing sequence of bits, most significant first.</summary>
    internal sealed class BitStream
    {
        private readonly List<bool> _bits = [];

        public int Length => _bits.Count;

        public void Append(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                _bits.Add(((value >> bit) & 1) != 0);
            }
        }

        public void CopyTo(byte[] bytes)
        {
            for (int index = 0; index < _bits.Count; index++)
            {
                if (_bits[index])
                {
                    bytes[index >> 3] |= (byte)(0x80 >> (index & 7));
                }
            }
        }
    }

    /// <summary>The module grid of one symbol, with the function patterns marked so codewords and masks skip them.</summary>
    private sealed class QrSymbol
    {
        private readonly bool[] _function;
        private readonly int _version;

        public QrSymbol(int version)
        {
            _version = version;
            Size = (version * 4) + 17;
            Modules = new bool[Size * Size];
            _function = new bool[Size * Size];
        }

        public int Size { get; }

        public bool[] Modules { get; }

        public void DrawFunctionPatterns()
        {
            for (int index = 0; index < Size; index++)
            {
                Set(6, index, index % 2 == 0);
                Set(index, 6, index % 2 == 0);
            }

            Finder(3, 3);
            Finder(Size - 4, 3);
            Finder(3, Size - 4);
            int[] positions = AlignmentPositions(_version);
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = 0; j < positions.Length; j++)
                {
                    bool corner = (i == 0 && j == 0) || (i == 0 && j == positions.Length - 1) || (i == positions.Length - 1 && j == 0);
                    if (!corner)
                    {
                        Alignment(positions[i], positions[j]);
                    }
                }
            }

            // Reserve the format areas (written per mask) and draw the version information.
            DrawFormat(QrErrorCorrection.Low, 0);
            if (_version >= 7)
            {
                int remainder = _version;
                for (int bit = 0; bit < 12; bit++)
                {
                    remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
                }

                int bits = (_version << 12) | remainder;
                for (int bit = 0; bit < 18; bit++)
                {
                    bool dark = ((bits >> bit) & 1) != 0;
                    int a = Size - 11 + (bit % 3);
                    int b = bit / 3;
                    Set(a, b, dark);
                    Set(b, a, dark);
                }
            }
        }

        /// <summary>Writes the 15 format bits (level and mask, BCH(15,5), masked with 101010000010010) in both copies, and the dark module.</summary>
        public void DrawFormat(QrErrorCorrection level, int mask)
        {
            int levelBits = level switch
            {
                QrErrorCorrection.Low => 1,
                QrErrorCorrection.Medium => 0,
                QrErrorCorrection.Quartile => 3,
                _ => 2,
            };
            int data = (levelBits << 3) | mask;
            int remainder = data;
            for (int bit = 0; bit < 10; bit++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            int bits = ((data << 10) | remainder) ^ 0x5412;
            for (int bit = 0; bit <= 5; bit++)
            {
                Set(8, bit, Bit(bits, bit));
            }

            Set(8, 7, Bit(bits, 6));
            Set(8, 8, Bit(bits, 7));
            Set(7, 8, Bit(bits, 8));
            for (int bit = 9; bit < 15; bit++)
            {
                Set(14 - bit, 8, Bit(bits, bit));
            }

            for (int bit = 0; bit < 8; bit++)
            {
                Set(Size - 1 - bit, 8, Bit(bits, bit));
            }

            for (int bit = 8; bit < 15; bit++)
            {
                Set(8, Size - 15 + bit, Bit(bits, bit));
            }

            Set(8, Size - 8, true);
        }

        /// <summary>Places the codeword bits in two-module columns, upward and downward from the bottom right, skipping function modules.</summary>
        public void DrawCodewords(byte[] codewords)
        {
            int index = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (int vertical = 0; vertical < Size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vertical : vertical;
                        if (!_function[(y * Size) + x] && index < codewords.Length * 8)
                        {
                            Modules[(y * Size) + x] = ((codewords[index >> 3] >> (7 - (index & 7))) & 1) != 0;
                            index++;
                        }
                    }
                }
            }
        }

        /// <summary>Inverts the data modules the mask pattern selects (ISO/IEC 18004 Table 10); applying it twice undoes it.</summary>
        public void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
                        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
                    };
                    if (invert && !_function[(y * Size) + x])
                    {
                        Modules[(y * Size) + x] ^= true;
                    }
                }
            }
        }

        /// <summary>The mask evaluation of ISO/IEC 18004 §7.8.3: runs, 2 by 2 blocks, finder-like patterns, and the balance of dark modules.</summary>
        public long Penalty()
        {
            long penalty = 0;
            for (int line = 0; line < Size; line++)
            {
                penalty += LinePenalty(line, horizontal: true) + LinePenalty(line, horizontal: false);
            }

            int dark = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool color = Get(x, y);
                    if (color)
                    {
                        dark++;
                    }

                    if (x < Size - 1 && y < Size - 1 && color == Get(x + 1, y) && color == Get(x, y + 1) && color == Get(x + 1, y + 1))
                    {
                        penalty += 3;
                    }
                }
            }

            int total = Size * Size;
            int k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
            return penalty + (k * 10L);
        }

        private long LinePenalty(int line, bool horizontal)
        {
            long penalty = 0;
            int run = 0;
            bool previous = false;
            for (int index = 0; index < Size; index++)
            {
                bool color = horizontal ? Get(index, line) : Get(line, index);
                if (index > 0 && color == previous)
                {
                    run++;
                }
                else
                {
                    if (run >= 5)
                    {
                        penalty += run - 2;
                    }

                    run = 1;
                    previous = color;
                }
            }

            if (run >= 5)
            {
                penalty += run - 2;
            }

            // 1:1:3:1:1 dark-light-dark-light-dark with four light modules on one side; outside the symbol counts as light.
            for (int index = -4; index < Size; index++)
            {
                if (Pattern(line, index, horizontal, [true, false, true, true, true, false, true, false, false, false, false])
                    || Pattern(line, index, horizontal, [false, false, false, false, true, false, true, true, true, false, true]))
                {
                    penalty += 40;
                }
            }

            return penalty;
        }

        private bool Pattern(int line, int start, bool horizontal, ReadOnlySpan<bool> pattern)
        {
            // Only patterns whose dark modules all lie inside the symbol count, so one finder is not scored twice from its quiet zone.
            for (int offset = 0; offset < pattern.Length; offset++)
            {
                int index = start + offset;
                bool inside = index >= 0 && index < Size;
                if (!inside && pattern[offset])
                {
                    return false;
                }

                bool color = inside && (horizontal ? Get(index, line) : Get(line, index));
                if (color != pattern[offset])
                {
                    return false;
                }
            }

            return true;
        }

        private static int[] AlignmentPositions(int version)
        {
            if (version == 1)
            {
                return [];
            }

            int count = (version / 7) + 2;
            int step = ((version * 8) + (count * 3) + 5) / ((count * 4) - 4) * 2;
            int[] result = new int[count];
            result[0] = 6;
            for (int index = count - 1, position = (version * 4) + 10; index >= 1; index--, position -= step)
            {
                result[index] = position;
            }

            return result;
        }

        private void Finder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int column = x + dx;
                    int row = y + dy;
                    if (column >= 0 && column < Size && row >= 0 && row < Size)
                    {
                        Set(column, row, distance != 2 && distance != 4);
                    }
                }
            }
        }

        private void Alignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private bool Get(int x, int y) => Modules[(y * Size) + x];

        private void Set(int x, int y, bool dark)
        {
            Modules[(y * Size) + x] = dark;
            _function[(y * Size) + x] = true;
        }

        private static bool Bit(int value, int bit) => ((value >> bit) & 1) != 0;
    }
}
