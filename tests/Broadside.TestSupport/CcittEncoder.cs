namespace Broadside.TestSupport;

/// <summary>How <see cref="CcittEncoder"/> frames its output: the <c>CCITTFaxDecode</c> parameters it encodes for (ISO 32000-2 §7.4.6, Table 11).</summary>
/// <param name="K">Negative: pure two-dimensional (Group 4); 0: one-dimensional (Group 3); positive: mixed, one 1-D line then K - 1 2-D lines.</param>
/// <param name="EndOfLine">Whether every line is preceded by an EOL (always so for K &gt; 0).</param>
/// <param name="EncodedByteAlign">Whether every line (or its EOL, which then ends on a byte boundary) is aligned to a byte.</param>
/// <param name="EndOfBlock">Whether the data ends with EOFB (K &lt; 0) or RTC (K &gt;= 0).</param>
public readonly record struct CcittEncoding(int K = 0, bool EndOfLine = false, bool EncodedByteAlign = false, bool EndOfBlock = true);

/// <summary>
/// A CCITT fax encoder (ITU-T T.4 §4.1-4.2, T.6 §2.2-2.4), the counterpart of the <c>CCITTFaxDecode</c> decoder under test: the C#
/// twin of <c>gen_ccitt_*</c>'s encoder in <c>tests/Corpus/generate.py</c>. Shared by the CCITT tests, the benchmarks and the fuzz seeds.
/// Bitmaps are rows of booleans, <see langword="true"/> = black.
/// </summary>
public static class CcittEncoder
{
    private const string Eol = "000000000001";

    // T.4 Table 2 (terminating, 0-63) and Table 3a (make-up, 64-1728): white and black code words.
    private static readonly string[] WhiteTerminating =
    [
        "00110101", "000111", "0111", "1000", "1011", "1100", "1110", "1111", "10011", "10100", "00111", "01000", "001000", "000011",
        "110100", "110101", "101010", "101011", "0100111", "0001100", "0001000", "0010111", "0000011", "0000100", "0101000", "0101011",
        "0010011", "0100100", "0011000", "00000010", "00000011", "00011010", "00011011", "00010010", "00010011", "00010100", "00010101",
        "00010110", "00010111", "00101000", "00101001", "00101010", "00101011", "00101100", "00101101", "00000100", "00000101",
        "00001010", "00001011", "01010010", "01010011", "01010100", "01010101", "00100100", "00100101", "01011000", "01011001",
        "01011010", "01011011", "01001010", "01001011", "00110010", "00110011", "00110100",
    ];

    private static readonly string[] BlackTerminating =
    [
        "0000110111", "010", "11", "10", "011", "0011", "0010", "00011", "000101", "000100", "0000100", "0000101", "0000111",
        "00000100", "00000111", "000011000", "0000010111", "0000011000", "0000001000", "00001100111", "00001101000", "00001101100",
        "00000110111", "00000101000", "00000010111", "00000011000", "000011001010", "000011001011", "000011001100", "000011001101",
        "000001101000", "000001101001", "000001101010", "000001101011", "000011010010", "000011010011", "000011010100",
        "000011010101", "000011010110", "000011010111", "000001101100", "000001101101", "000011011010", "000011011011",
        "000001010100", "000001010101", "000001010110", "000001010111", "000001100100", "000001100101", "000001010010",
        "000001010011", "000000100100", "000000110111", "000000111000", "000000100111", "000000101000", "000001011000",
        "000001011001", "000000101011", "000000101100", "000001011010", "000001100110", "000001100111",
    ];

    private static readonly string[] WhiteMakeUp =
    [
        "11011", "10010", "010111", "0110111", "00110110", "00110111", "01100100", "01100101", "01101000", "01100111", "011001100",
        "011001101", "011010010", "011010011", "011010100", "011010101", "011010110", "011010111", "011011000", "011011001",
        "011011010", "011011011", "010011000", "010011001", "010011010", "011000", "010011011",
    ];

    private static readonly string[] BlackMakeUp =
    [
        "0000001111", "000011001000", "000011001001", "000001011011", "000000110011", "000000110100", "000000110101",
        "0000001101100", "0000001101101", "0000001001010", "0000001001011", "0000001001100", "0000001001101", "0000001110010",
        "0000001110011", "0000001110100", "0000001110101", "0000001110110", "0000001110111", "0000001010010", "0000001010011",
        "0000001010100", "0000001010101", "0000001011010", "0000001011011", "0000001100100", "0000001100101",
    ];

    // T.4 Table 3b: extended make-up codes 1792-2560, shared by both colours.
    private static readonly string[] ExtendedMakeUp =
    [
        "00000001000", "00000001100", "00000001101", "000000010010", "000000010011", "000000010100", "000000010101", "000000010110",
        "000000010111", "000000011100", "000000011101", "000000011110", "000000011111",
    ];

    // T.4 Table 4: vertical mode codes for a1 - b1 = -3 .. 3.
    private static readonly string[] Vertical = ["0000010", "000010", "010", "1", "011", "000011", "0000011"];

    /// <summary>Encodes a bitmap.</summary>
    /// <param name="rows">The rows, all of the same length (the Columns parameter); <see langword="true"/> = black.</param>
    /// <param name="encoding">The framing.</param>
    /// <returns>The encoded bytes, MSB first, the last byte padded with zeros.</returns>
    public static byte[] Encode(bool[][] rows, CcittEncoding encoding)
    {
        var bits = new BitWriter();
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        bool[]? previous = null;
        for (int i = 0; i < rows.Length; i++)
        {
            bool twoD = encoding.K < 0 || (encoding.K > 0 && i % encoding.K != 0);
            if (encoding.EndOfLine || encoding.K > 0)
            {
                string eol = Eol + (encoding.K <= 0 ? string.Empty : twoD ? "0" : "1");
                if (encoding.EncodedByteAlign)
                {
                    bits.PadSoThatNextEndsOnByte(eol.Length);
                }

                bits.Write(eol);
            }
            else if (encoding.EncodedByteAlign)
            {
                bits.Align();
            }

            bits.Write(twoD ? Encode2D(rows[i], previous ?? new bool[width]) : Encode1D(rows[i]));
            previous = rows[i];
        }

        if (encoding.EndOfBlock)
        {
            if (encoding.EncodedByteAlign)
            {
                bits.Align();
            }

            string end = encoding.K < 0 ? Eol + Eol : string.Concat(Enumerable.Repeat(Eol + (encoding.K > 0 ? "1" : string.Empty), 6));
            bits.Write(end);
        }

        return bits.ToArray();
    }

    /// <summary>The code words of one run (make-ups then one terminating code). T.4 §4.1.2, Tables 2-3; T.6 §2.2.4 Step 2 iii.</summary>
    /// <param name="run">The run length.</param>
    /// <param name="black">The run's colour.</param>
    /// <returns>The bits as a string of 0 and 1.</returns>
    public static string RunCodes(int run, bool black)
    {
        var codes = new System.Text.StringBuilder();
        while (run >= 2560)
        {
            codes.Append(ExtendedMakeUp[^1]);
            run -= 2560;
        }

        if (run >= 64)
        {
            int makeUp = run / 64;
            codes.Append(makeUp <= 27 ? (black ? BlackMakeUp : WhiteMakeUp)[makeUp - 1] : ExtendedMakeUp[makeUp - 28]);
            run %= 64;
        }

        return codes.Append((black ? BlackTerminating : WhiteTerminating)[run]).ToString();
    }

    /// <summary>One line coded one-dimensionally (T.4 §4.1): alternating runs from white; the first white run may be 0.</summary>
    /// <param name="row">The line.</param>
    /// <returns>The bits.</returns>
    public static string Encode1D(bool[] row)
    {
        var codes = new System.Text.StringBuilder();
        int position = 0;
        bool black = false;
        while (position < row.Length)
        {
            int end = position;
            while (end < row.Length && row[end] == black)
            {
                end++;
            }

            codes.Append(RunCodes(end - position, black));
            position = end;
            black = !black;
        }

        return codes.ToString();
    }

    /// <summary>One line coded two-dimensionally against <paramref name="reference"/> (T.4 §4.2.1.3, Figure 7; T.6 §2.2).</summary>
    /// <param name="row">The coding line.</param>
    /// <param name="reference">The reference line (all white for the first line of a block).</param>
    /// <returns>The bits.</returns>
    public static string Encode2D(bool[] row, bool[] reference)
    {
        int width = row.Length;
        var codes = new System.Text.StringBuilder();
        int a0 = -1;
        bool color = false;
        while (a0 < width)
        {
            int a1 = NextChange(row, a0, color);
            int b1 = a0 + 1;
            while (b1 < width && !(Pixel(reference, b1) != Pixel(reference, b1 - 1) && Pixel(reference, b1) != color))
            {
                b1++;
            }

            int b2 = b1 + 1;
            while (b2 < width && Pixel(reference, b2) == Pixel(reference, b1))
            {
                b2++;
            }

            b2 = Math.Min(b2, width);
            if (b2 < a1)
            {
                codes.Append("0001");
                a0 = b2;
            }
            else if (Math.Abs(a1 - b1) <= 3)
            {
                codes.Append(Vertical[a1 - b1 + 3]);
                a0 = a1;
                color = !color;
            }
            else
            {
                int a2 = NextChange(row, a1, !color);
                codes.Append("001").Append(RunCodes(a1 - Math.Max(a0, 0), color)).Append(RunCodes(a2 - a1, !color));
                a0 = a2;
            }
        }

        return codes.ToString();
    }

    /// <summary>The expected decoded bytes of a bitmap: ceil(width / 8) bytes per row, MSB first, black = 0 unless <paramref name="blackIs1"/>, pad bits white.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="blackIs1">The BlackIs1 parameter.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Pack(bool[][] rows, bool blackIs1 = false)
    {
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        int stride = (width + 7) / 8;
        byte[] packed = new byte[stride * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < stride * 8; x++)
            {
                bool black = x < width && rows[y][x];
                if (black == blackIs1)
                {
                    packed[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return packed;
    }

    /// <summary>
    /// A deterministic test bitmap: text-like strokes, a dithered gradient, a full black row, rows starting black, and runs long
    /// enough for make-up codes (and extended ones when <paramref name="width"/> &gt; 1792).
    /// </summary>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="seed">Varies the strokes.</param>
    /// <returns>The rows.</returns>
    public static bool[][] SampleBitmap(int width, int height, int seed = 63)
    {
        uint state = (uint)seed;
        bool[][] rows = new bool[height][];
        for (int y = 0; y < height; y++)
        {
            bool[] row = rows[y] = new bool[width];
            switch (y % 8)
            {
                case 0:
                    break; // all white
                case 1:
                    Array.Fill(row, true); // all black
                    break;
                case 2:
                    // Dithered gradient: density grows to the right.
                    for (int x = 0; x < width; x++)
                    {
                        state = (state * 1664525) + 1013904223;
                        row[x] = (state >> 16) % (uint)width < (uint)x;
                    }

                    break;
                case 3:
                    // Starts black; a long black run then a long white run.
                    for (int x = 0; x < width; x++)
                    {
                        row[x] = x < (width * 2 / 3) || x % 97 == 5;
                    }

                    break;
                default:
                    // Text-like vertical strokes that drift a little from row to row (vertical and pass modes).
                    for (int x = 0; x < width; x++)
                    {
                        int phase = (x + (y / 3) + seed) % 23;
                        row[x] = phase < 3 || (phase is 9 or 10 && y % 3 != 0) || (x / 41 % 5 == y % 5 && phase < 15);
                    }

                    break;
            }
        }

        return rows;
    }

    private static int NextChange(bool[] row, int after, bool color)
    {
        // The first pixel after `after` whose colour is not `color`; the imaginary element after the last pixel when there is none.
        int position = after + 1;
        while (position < row.Length && row[position] == color)
        {
            position++;
        }

        return Math.Min(position, row.Length);
    }

    private static bool Pixel(bool[] line, int x) => x >= 0 && line[x];

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _accumulator;
        private int _count;

        public void Write(string bits)
        {
            foreach (char bit in bits)
            {
                _accumulator = (_accumulator << 1) | (bit - '0');
                if (++_count == 8)
                {
                    _bytes.Add((byte)_accumulator);
                    _accumulator = 0;
                    _count = 0;
                }
            }
        }

        public void Align()
        {
            while (_count != 0)
            {
                Write("0");
            }
        }

        public void PadSoThatNextEndsOnByte(int length)
        {
            while ((_count + length) % 8 != 0)
            {
                Write("0");
            }
        }

        public byte[] ToArray()
        {
            Align();
            return [.. _bytes];
        }
    }
}
