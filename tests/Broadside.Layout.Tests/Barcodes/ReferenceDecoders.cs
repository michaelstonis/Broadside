// Module matrices are square grids; a rectangular array reads as the symbol does.
#pragma warning disable CA1814

using System.Text;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// Readers written from the symbology standards for the tests only, independent of the encoders: they take a module matrix (row 0 at the
/// top), check what a scanner checks (patterns, check characters, format information, Reed-Solomon syndromes) and return the data, or
/// throw <see cref="InvalidDataException"/>.
/// </summary>
internal static class ReferenceDecoders
{
    private const string Code128Widths =
        "212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 123122 123221 223211 221132 "
        + "221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 "
        + "231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 312113 312311 332111 "
        + "314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 142112 142211 241211 221114 413111 241112 134111 "
        + "111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 "
        + "114131 311141 411131 211412 211214 211232";

    /// <summary>The run lengths of a row of modules, starting with a dark run.</summary>
    public static List<int> Runs(bool[] row)
    {
        var runs = new List<int>();
        int index = 0;
        if (!row[0])
        {
            throw new InvalidDataException("A linear symbol starts with a bar.");
        }

        while (index < row.Length)
        {
            bool color = row[index];
            int start = index;
            while (index < row.Length && row[index] == color)
            {
                index++;
            }

            runs.Add(index - start);
        }

        return runs;
    }

    /// <summary>Reads Code 128: start, data, modulo 103 check and stop; FNC1 comes back as the GS1 separator U+001D.</summary>
    public static string Code128(bool[] row)
    {
        string[] patterns = Code128Widths.Split(' ');
        List<int> runs = Runs(row);
        if (runs.Count % 6 != 1 || string.Concat(runs.TakeLast(7)) != "2331112")
        {
            throw new InvalidDataException("No Code 128 stop pattern.");
        }

        var values = new List<int>();
        for (int at = 0; at < runs.Count - 7; at += 6)
        {
            int value = Array.IndexOf(patterns, string.Concat(runs.Skip(at).Take(6)));
            if (value < 0)
            {
                throw new InvalidDataException($"Unknown Code 128 pattern at element {at}.");
            }

            values.Add(value);
        }

        int sum = values[0];
        for (int index = 1; index < values.Count - 1; index++)
        {
            sum += index * values[index];
        }

        if (sum % 103 != values[^1])
        {
            throw new InvalidDataException("The Code 128 check character does not match.");
        }

        var text = new StringBuilder();
        char set = values[0] switch { 103 => 'A', 104 => 'B', 105 => 'C', _ => throw new InvalidDataException("No start character.") };
        bool shifted = false;
        for (int index = 1; index < values.Count - 1; index++)
        {
            int value = values[index];
            char current = shifted ? (set == 'A' ? 'B' : 'A') : set;
            shifted = false;
            if (value == 102)
            {
                text.Append('\u001D');
                continue;
            }

            if (current == 'C')
            {
                switch (value)
                {
                    case < 100:
                        text.Append(value.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case 100:
                        set = 'B';
                        break;
                    case 101:
                        set = 'A';
                        break;
                    default:
                        throw new InvalidDataException($"Value {value} in code set C.");
                }

                continue;
            }

            switch (value)
            {
                case 98:
                    shifted = true;
                    break;
                case 99:
                    set = 'C';
                    break;
                case 100 when current == 'A':
                    set = 'B';
                    break;
                case 101 when current == 'B':
                    set = 'A';
                    break;
                case < 96:
                    text.Append(current == 'A' ? (char)(value < 64 ? value + 32 : value - 64) : (char)(value + 32));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported value {value}.");
            }
        }

        return text.ToString();
    }

    /// <summary>Reads an EAN-13 (or a UPC-A as the EAN-13 with a leading 0): guards, parity, digits and the check digit.</summary>
    public static string Ean13(bool[] row)
    {
        if (row.Length != 95)
        {
            throw new InvalidDataException($"An EAN-13 is 95 modules; got {row.Length}.");
        }

        string bits = string.Concat(row.Select(static dark => dark ? '1' : '0'));
        if (bits[..3] != "101" || bits[45..50] != "01010" || bits[92..] != "101")
        {
            throw new InvalidDataException("The guard patterns are wrong.");
        }

        string[] odd = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
        string[] even = odd.Select(static code => new string(code.Select(static c => c == '0' ? '1' : '0').Reverse().ToArray())).ToArray();
        string[] right = odd.Select(static code => new string(code.Select(static c => c == '0' ? '1' : '0').ToArray())).ToArray();
        var digits = new StringBuilder();
        var parity = new StringBuilder();
        for (int index = 0; index < 6; index++)
        {
            string code = bits.Substring(3 + (index * 7), 7);
            int digit = Array.IndexOf(odd, code);
            if (digit >= 0)
            {
                parity.Append('O');
            }
            else
            {
                digit = Array.IndexOf(even, code);
                parity.Append('E');
            }

            if (digit < 0)
            {
                throw new InvalidDataException($"Left digit {index} is unreadable.");
            }

            digits.Append((char)('0' + digit));
        }

        for (int index = 0; index < 6; index++)
        {
            int digit = Array.IndexOf(right, bits.Substring(50 + (index * 7), 7));
            if (digit < 0)
            {
                throw new InvalidDataException($"Right digit {index} is unreadable.");
            }

            digits.Append((char)('0' + digit));
        }

        string[] leading = ["OOOOOO", "OOEOEE", "OOEEOE", "OOEEEO", "OEOOEE", "OEEOOE", "OEEEOO", "OEOEOE", "OEOEEO", "OEEOEO"];
        int first = Array.IndexOf(leading, parity.ToString());
        if (first < 0)
        {
            throw new InvalidDataException("The parity pattern is not one of the ten.");
        }

        string all = (char)('0' + first) + digits.ToString();
        int sum = 0;
        for (int index = 0; index < 13; index++)
        {
            sum += (all[index] - '0') * (index % 2 == 0 ? 1 : 3);
        }

        if (sum % 10 != 0)
        {
            throw new InvalidDataException("The check digit does not match.");
        }

        return all;
    }

    /// <summary>Reads Code 39: wide and narrow elements, start and stop, and the modulo 43 check character when asked to.</summary>
    public static string Code39(bool[] row, bool checkDigit)
    {
        const string characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*";
        string[] patterns =
        [
            "nnnwwnwnn", "wnnwnnnnw", "nnwwnnnnw", "wnwwnnnnn", "nnnwwnnnw", "wnnwwnnnn", "nnwwwnnnn", "nnnwnnwnw", "wnnwnnwnn", "nnwwnnwnn",
            "wnnnnwnnw", "nnwnnwnnw", "wnwnnwnnn", "nnnnwwnnw", "wnnnwwnnn", "nnwnwwnnn", "nnnnnwwnw", "wnnnnwwnn", "nnwnnwwnn", "nnnnwwwnn",
            "wnnnnnnww", "nnwnnnnww", "wnwnnnnwn", "nnnnwnnww", "wnnnwnnwn", "nnwnwnnwn", "nnnnnnwww", "wnnnnnwwn", "nnwnnnwwn", "nnnnwnwwn",
            "wwnnnnnnw", "nwwnnnnnw", "wwwnnnnnn", "nwnnwnnnw", "wwnnwnnnn", "nwwnwnnnn", "nwnnnnwnw", "wwnnnnwnn", "nwwnnnwnn", "nwnwnwnnn",
            "nwnwnnnwn", "nwnnnwnwn", "nnnwnwnwn", "nwnnwnwnn",
        ];
        List<int> runs = Runs(row);
        int narrow = runs.Min();
        var text = new StringBuilder();
        for (int at = 0; at < runs.Count; at += 10)
        {
            string code = string.Concat(runs.Skip(at).Take(9).Select(width => width > narrow * 1.5 ? 'w' : 'n'));
            int value = Array.IndexOf(patterns, code);
            if (value < 0)
            {
                throw new InvalidDataException($"Unknown Code 39 character at element {at}.");
            }

            text.Append(characters[value]);
        }

        string all = text.ToString();
        if (all.Length < 2 || all[0] != '*' || all[^1] != '*')
        {
            throw new InvalidDataException("No Code 39 start and stop characters.");
        }

        string data = all[1..^1];
        if (checkDigit)
        {
            int sum = data[..^1].Sum(character => characters.IndexOf(character, StringComparison.Ordinal));
            if (characters[sum % 43] != data[^1])
            {
                throw new InvalidDataException("The Code 39 check character does not match.");
            }
        }

        return data;
    }

    /// <summary>
    /// Reads a QR code: the format information (BCH-checked, both copies), the version information from 7 up, unmasking, the codewords in
    /// placement order, de-interleaving, the Reed-Solomon syndromes of each block, and the numeric, alphanumeric, byte and ECI segments.
    /// </summary>
    public static (string Text, byte[] Bytes, int Version, char Level) QrCode(bool[,] matrix)
    {
        int size = matrix.GetLength(0);
        int version = (size - 17) / 4;
        if (size != (version * 4) + 17 || version is < 1 or > 40)
        {
            throw new InvalidDataException($"A QR code is not {size} modules wide.");
        }

        bool At(int x, int y) => matrix[y, x];
        int first = 0;
        int second = 0;
        for (int i = 0; i < 15; i++)
        {
            (int x, int y) a = i switch
            {
                < 6 => (8, i),
                6 => (8, 7),
                7 => (8, 8),
                8 => (7, 8),
                _ => (14 - i, 8),
            };
            (int x, int y) b = i < 8 ? (size - 1 - i, 8) : (8, size - 15 + i);
            first |= (At(a.x, a.y) ? 1 : 0) << i;
            second |= (At(b.x, b.y) ? 1 : 0) << i;
        }

        if (first != second)
        {
            throw new InvalidDataException("The two copies of the format information differ.");
        }

        int format = first ^ 0x5412;
        int data5 = format >> 10;
        int check = data5;
        for (int i = 0; i < 10; i++)
        {
            check = (check << 1) ^ ((check >> 9) * 0x537);
        }

        if (((data5 << 10) | (check & 0x3FF)) != format)
        {
            throw new InvalidDataException("The format information fails its BCH check.");
        }

        if (!At(8, size - 8))
        {
            throw new InvalidDataException("The dark module is light.");
        }

        char level = (data5 >> 3) switch { 1 => 'L', 0 => 'M', 3 => 'Q', _ => 'H' };
        int mask = data5 & 7;
        if (version >= 7)
        {
            int bits = 0;
            for (int i = 0; i < 18; i++)
            {
                bits |= (At(size - 11 + (i % 3), i / 3) ? 1 : 0) << i;
            }

            if (bits >> 12 != version)
            {
                throw new InvalidDataException($"The version information says {bits >> 12}; the size says {version}.");
            }
        }

        bool[,] function = FunctionModules(version, size);
        var stream = new List<bool>();
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (int vertical = 0; vertical < size; vertical++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vertical : vertical;
                    if (!function[y, x])
                    {
                        bool invert = mask switch
                        {
                            0 => (y + x) % 2 == 0,
                            1 => y % 2 == 0,
                            2 => x % 3 == 0,
                            3 => (y + x) % 3 == 0,
                            4 => ((y / 2) + (x / 3)) % 2 == 0,
                            5 => ((y * x) % 2) + ((y * x) % 3) == 0,
                            6 => (((y * x) % 2) + ((y * x) % 3)) % 2 == 0,
                            _ => (((y + x) % 2) + ((y * x) % 3)) % 2 == 0,
                        };
                        stream.Add(At(x, y) ^ invert);
                    }
                }
            }
        }

        byte[] raw = new byte[stream.Count / 8];
        for (int i = 0; i < raw.Length * 8; i++)
        {
            if (stream[i])
            {
                raw[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        (int blocks, int ecc) = QrBlocks(version, level);
        int shortBlocks = blocks - (raw.Length % blocks);
        int shortLength = raw.Length / blocks;
        var blockBytes = new List<byte>[blocks];
        for (int b = 0; b < blocks; b++)
        {
            blockBytes[b] = [];
        }

        int at = 0;
        int dataShort = shortLength - ecc;
        for (int i = 0; i <= dataShort; i++)
        {
            for (int b = 0; b < blocks; b++)
            {
                if (i < dataShort || b >= shortBlocks)
                {
                    blockBytes[b].Add(raw[at++]);
                }
            }
        }

        for (int i = 0; i < ecc; i++)
        {
            for (int b = 0; b < blocks; b++)
            {
                blockBytes[b].Add(raw[at++]);
            }
        }

        var data = new List<byte>();
        foreach (List<byte> block in blockBytes)
        {
            if (!Syndromes(block, ecc, 0x11D, 0))
            {
                throw new InvalidDataException("A block's Reed-Solomon syndromes are not zero.");
            }

            data.AddRange(block.Take(block.Count - ecc));
        }

        (string text, byte[] bytes) = QrSegments([.. data], version);
        return (text, bytes, version, level);
    }

    /// <summary>
    /// Reads a square Data Matrix: the finder and timing patterns of every region, the codewords in the placement of ISO/IEC 16022 Annex
    /// F, the Reed-Solomon syndromes of each interleaved block, and ASCII encodation (with ECI 26 as UTF-8).
    /// </summary>
    public static string DataMatrix(bool[,] matrix, int region, int regions, int dataCount, int eccCount, int blocks)
    {
        int size = matrix.GetLength(0);
        int block = region + 2;
        int mapSize = region * regions;
        bool[,] map = new bool[mapSize, mapSize];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                int r = row % block;
                int c = column % block;
                bool expected = r == block - 1 || c == 0 || (r == 0 ? c % 2 == 0 : c == block - 1 ? r % 2 == 1 : matrix[row, column]);
                if (matrix[row, column] != expected)
                {
                    throw new InvalidDataException($"The finder or timing pattern is wrong at row {row}, column {column}.");
                }

                if (r > 0 && r < block - 1 && c > 0 && c < block - 1)
                {
                    map[((row / block) * region) + r - 1, ((column / block) * region) + c - 1] = matrix[row, column];
                }
            }
        }

        byte[] codewords = DataMatrixCodewords(map, dataCount + eccCount);
        int eccPerBlock = eccCount / blocks;
        for (int b = 0; b < blocks; b++)
        {
            var blockBytes = new List<byte>();
            for (int i = b; i < dataCount; i += blocks)
            {
                blockBytes.Add(codewords[i]);
            }

            for (int i = 0; i < eccPerBlock; i++)
            {
                blockBytes.Add(codewords[dataCount + b + (i * blocks)]);
            }

            if (!Syndromes(blockBytes, eccPerBlock, 0x12D, 1))
            {
                throw new InvalidDataException($"Block {b}'s Reed-Solomon syndromes are not zero.");
            }
        }

        var bytes = new List<byte>();
        bool utf8 = false;
        for (int i = 0; i < dataCount; i++)
        {
            int value = codewords[i];
            if (value == 129)
            {
                break;
            }

            if (value == 241)
            {
                utf8 = codewords[++i] == 27;
                continue;
            }

            if (value == 235)
            {
                bytes.Add((byte)(codewords[++i] + 127));
            }
            else if (value >= 130 && value <= 229)
            {
                bytes.AddRange(Encoding.ASCII.GetBytes((value - 130).ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (value is >= 1 and <= 128)
            {
                bytes.Add((byte)(value - 1));
            }
            else
            {
                throw new InvalidDataException($"Codeword {value} is not ASCII encodation.");
            }
        }

        return utf8 ? Encoding.UTF8.GetString([.. bytes]) : Encoding.Latin1.GetString([.. bytes]);
    }

    private static byte[] DataMatrixCodewords(bool[,] map, int count)
    {
        int rows = map.GetLength(0);
        int columns = map.GetLength(1);
        byte[] codewords = new byte[count];
        bool[,] seen = new bool[rows, columns];

        void Module(int row, int column, int position, int bit)
        {
            if (row < 0)
            {
                row += rows;
                column += 4 - ((rows + 4) % 8);
            }

            if (column < 0)
            {
                column += columns;
                row += 4 - ((columns + 4) % 8);
            }

            seen[row, column] = true;
            if (map[row, column] && position < count)
            {
                codewords[position] |= (byte)(1 << (8 - bit));
            }
        }

        void Shape(int position, ReadOnlySpan<(int Row, int Column)> modules)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                Module(modules[bit].Row, modules[bit].Column, position, bit + 1);
            }
        }

        int pos = 0;
        int r = 4;
        int c = 0;
        do
        {
            if (r == rows && c == 0)
            {
                Shape(pos++, [(rows - 1, 0), (rows - 1, 1), (rows - 1, 2), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1)]);
            }

            if (r == rows - 2 && c == 0 && columns % 4 != 0)
            {
                Shape(pos++, [(rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 4), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 1)]);
            }

            if (r == rows - 2 && c == 0 && columns % 8 == 4)
            {
                Shape(pos++, [(rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1)]);
            }

            if (r == rows + 4 && c == 2 && columns % 8 == 0)
            {
                Shape(pos++, [(rows - 1, 0), (rows - 1, columns - 1), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 3), (1, columns - 2), (1, columns - 1)]);
            }

            do
            {
                if (r < rows && c >= 0 && !seen[r, c])
                {
                    Shape(pos++, [(r - 2, c - 2), (r - 2, c - 1), (r - 1, c - 2), (r - 1, c - 1), (r - 1, c), (r, c - 2), (r, c - 1), (r, c)]);
                }

                r -= 2;
                c += 2;
            }
            while (r >= 0 && c < columns);
            r++;
            c += 3;
            do
            {
                if (r >= 0 && c < columns && !seen[r, c])
                {
                    Shape(pos++, [(r - 2, c - 2), (r - 2, c - 1), (r - 1, c - 2), (r - 1, c - 1), (r - 1, c), (r, c - 2), (r, c - 1), (r, c)]);
                }

                r += 2;
                c -= 2;
            }
            while (r < rows && c >= 0);
            r += 3;
            c++;
        }
        while (r < rows || c < columns);

        if (pos != count)
        {
            throw new InvalidDataException($"The placement read {pos} codewords; the symbol has {count}.");
        }

        return codewords;
    }

    /// <summary>Whether the codeword polynomial vanishes at every root of the generator: α^first to α^(first + ecc - 1).</summary>
    private static bool Syndromes(List<byte> block, int ecc, int primitive, int first)
    {
        int[] exp = new int[255];
        int value = 1;
        for (int i = 0; i < 255; i++)
        {
            exp[i] = value;
            value <<= 1;
            if (value > 255)
            {
                value ^= primitive;
            }
        }

        int Multiply(int a, int b)
        {
            int product = 0;
            while (b != 0)
            {
                if ((b & 1) != 0)
                {
                    product ^= a;
                }

                a <<= 1;
                if (a > 255)
                {
                    a ^= primitive;
                }

                b >>= 1;
            }

            return product;
        }

        for (int root = 0; root < ecc; root++)
        {
            int alpha = exp[(first + root) % 255];
            int sum = 0;
            foreach (byte codeword in block)
            {
                sum = Multiply(sum, alpha) ^ codeword;
            }

            if (sum != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool[,] FunctionModules(int version, int size)
    {
        bool[,] function = new bool[size, size];
        void Mark(int x0, int y0, int width, int height)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(size, y0 + height); y++)
            {
                for (int x = Math.Max(0, x0); x < Math.Min(size, x0 + width); x++)
                {
                    function[y, x] = true;
                }
            }
        }

        // Finders with separators and format areas, timing, the dark module, alignment patterns and version areas.
        Mark(0, 0, 9, 9);
        Mark(size - 8, 0, 8, 9);
        Mark(0, size - 8, 9, 8);
        Mark(6, 0, 1, size);
        Mark(0, 6, size, 1);
        if (version >= 2)
        {
            int[] centers = QrAlignmentCenters(version, size);
            foreach (int cy in centers)
            {
                foreach (int cx in centers)
                {
                    int last = size - 7;
                    if (!((cx == 6 && cy == 6) || (cx == 6 && cy == last) || (cx == last && cy == 6)))
                    {
                        Mark(cx - 2, cy - 2, 5, 5);
                    }
                }
            }
        }

        if (version >= 7)
        {
            Mark(size - 11, 0, 3, 6);
            Mark(0, size - 11, 6, 3);
        }

        return function;
    }

    /// <summary>The alignment pattern centers of ISO/IEC 18004 Annex E, Table E.1.</summary>
    private static int[] QrAlignmentCenters(int version, int size)
    {
        int count = (version / 7) + 2;
        int last = size - 7;
        int[] table = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0, 0];
        int step = table[version] != 0 ? table[version] : (int)Math.Ceiling((last - 6) / (double)(count - 1) / 2) * 2;
        int[] centers = new int[count];
        centers[0] = 6;
        for (int i = count - 1; i >= 1; i--)
        {
            centers[i] = last - ((count - 1 - i) * step);
        }

        return centers;
    }

    private static (int Blocks, int Ecc) QrBlocks(int version, char level)
    {
        // ISO/IEC 18004 Table 9: (blocks, error correction codewords per block) by version for L, M, Q, H.
        int[,] ecc =
        {
            { 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            { 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            { 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        };
        int[,] blocks =
        {
            { 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            { 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            { 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
        };
        int row = "LMQH".IndexOf(level, StringComparison.Ordinal);
        return (blocks[row, version - 1], ecc[row, version - 1]);
    }

    private static (string Text, byte[] Bytes) QrSegments(byte[] data, int version)
    {
        int position = 0;
        int Read(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++, position++)
            {
                value = (value << 1) | ((data[position >> 3] >> (7 - (position & 7))) & 1);
            }

            return value;
        }

        int range = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        var text = new StringBuilder();
        var bytes = new List<byte>();
        bool utf8 = false;
        var pending = new List<byte>();
        void Flush()
        {
            text.Append(utf8 ? Encoding.UTF8.GetString([.. pending]) : Encoding.Latin1.GetString([.. pending]));
            pending.Clear();
        }

        while (position + 4 <= data.Length * 8)
        {
            int mode = Read(4);
            if (mode == 0)
            {
                break;
            }

            switch (mode)
            {
                case 0b0111:
                    utf8 = Read(8) == 26;
                    break;
                case 0b0001:
                    {
                        Flush();
                        int count = Read(new[] { 10, 12, 14 }[range]);
                        for (; count >= 3; count -= 3)
                        {
                            text.Append(Read(10).ToString("000", System.Globalization.CultureInfo.InvariantCulture));
                        }

                        if (count == 2)
                        {
                            text.Append(Read(7).ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else if (count == 1)
                        {
                            text.Append(Read(4).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }

                        break;
                    }

                case 0b0010:
                    {
                        Flush();
                        const string table = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
                        int count = Read(new[] { 9, 11, 13 }[range]);
                        for (; count >= 2; count -= 2)
                        {
                            int pair = Read(11);
                            text.Append(table[pair / 45]).Append(table[pair % 45]);
                        }

                        if (count == 1)
                        {
                            text.Append(table[Read(6)]);
                        }

                        break;
                    }

                case 0b0100:
                    {
                        int count = Read(new[] { 8, 16, 16 }[range]);
                        for (int i = 0; i < count; i++)
                        {
                            byte value = (byte)Read(8);
                            pending.Add(value);
                            bytes.Add(value);
                        }

                        break;
                    }

                default:
                    throw new InvalidDataException($"Unsupported mode {mode}.");
            }
        }

        Flush();
        return (text.ToString(), [.. bytes]);
    }
}
