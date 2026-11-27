using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// The Data Matrix ECC 200 encoder (ISO/IEC 16022): ASCII encodation with digit pairs and upper shift, the smallest square symbol that
/// holds the codewords, pad codewords, Reed-Solomon blocks interleaved, and the standard module placement.
/// </summary>
internal static class BarcodeDataMatrix
{
    private const int UpperShift = 235;
    private const int Pad = 129;
    private const int Eci = 241;

    /// <summary>
    /// The square symbols (ISO/IEC 16022 Table 7): size in modules, data region size, data regions per side, data codewords, error
    /// correction codewords and interleaved blocks.
    /// </summary>
    internal static readonly (int Size, int Region, int Regions, int Data, int Ecc, int Blocks)[] Symbols =
    [
        (10, 8, 1, 3, 5, 1), (12, 10, 1, 5, 7, 1), (14, 12, 1, 8, 10, 1), (16, 14, 1, 12, 12, 1), (18, 16, 1, 18, 14, 1),
        (20, 18, 1, 22, 18, 1), (22, 20, 1, 30, 20, 1), (24, 22, 1, 36, 24, 1), (26, 24, 1, 44, 28, 1), (32, 14, 2, 62, 36, 1),
        (36, 16, 2, 86, 42, 1), (40, 18, 2, 114, 48, 1), (44, 20, 2, 144, 56, 1), (48, 22, 2, 174, 68, 1), (52, 24, 2, 204, 84, 2),
        (64, 14, 4, 280, 112, 2), (72, 16, 4, 368, 144, 4), (80, 18, 4, 456, 192, 4), (88, 20, 4, 576, 224, 4), (96, 22, 4, 696, 272, 4),
        (104, 24, 4, 816, 336, 6), (120, 18, 6, 1050, 408, 6), (132, 20, 6, 1304, 496, 8), (144, 22, 6, 1558, 620, 10),
    ];

    /// <summary>Encodes text: Latin-1 as it is, other text as UTF-8 after ECI 000026.</summary>
    public static (bool[] Modules, int Size) Encode(string data)
    {
        bool latin1 = data.All(static character => character <= 0xFF);
        byte[] bytes = latin1 ? Encoding.Latin1.GetBytes(data) : Encoding.UTF8.GetBytes(data);
        List<byte> codewords = Ascii(bytes);
        if (!latin1)
        {
            codewords.InsertRange(0, [Eci, 27]);
        }

        int symbol = Array.FindIndex(Symbols, symbol => symbol.Data >= codewords.Count);
        if (symbol < 0)
        {
            int largest = Symbols[^1].Data;
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The data needs {codewords.Count} codewords; the largest Data Matrix symbol (144 by 144) holds {largest} (such as {largest * 2} digits or {largest} other Latin-1 characters)."),
                nameof(data));
        }

        (int size, int region, int regions, int dataCount, int ecc, int blocks) = Symbols[symbol];
        byte[] full = Codewords(codewords, dataCount, ecc, blocks);
        bool[] mapping = Place(full, region * regions, region * regions);
        bool[] modules = new bool[size * size];
        int block = region + 2;
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                int r = row % block;
                int c = column % block;
                bool dark;
                if (r == block - 1 || c == 0)
                {
                    dark = true;
                }
                else if (r == 0)
                {
                    dark = c % 2 == 0;
                }
                else if (c == block - 1)
                {
                    dark = r % 2 == 1;
                }
                else
                {
                    int mapRow = ((row / block) * region) + r - 1;
                    int mapColumn = ((column / block) * region) + c - 1;
                    dark = mapping[(mapRow * region * regions) + mapColumn];
                }

                modules[(row * size) + column] = dark;
            }
        }

        return (modules, size);
    }

    /// <summary>ASCII encodation (ISO/IEC 16022 §5.2.3): two digits per codeword, 1 to 128 plus one, 129 to 255 after upper shift.</summary>
    internal static List<byte> Ascii(ReadOnlySpan<byte> bytes)
    {
        var codewords = new List<byte>(bytes.Length);
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (index + 1 < bytes.Length && value is >= (byte)'0' and <= (byte)'9' && bytes[index + 1] is >= (byte)'0' and <= (byte)'9')
            {
                codewords.Add((byte)(130 + ((value - '0') * 10) + (bytes[index + 1] - '0')));
                index++;
            }
            else if (value < 128)
            {
                codewords.Add((byte)(value + 1));
            }
            else
            {
                codewords.Add(UpperShift);
                codewords.Add((byte)(value - 127));
            }
        }

        return codewords;
    }

    /// <summary>Pads the data codewords (ISO/IEC 16022 §5.2.3, 253-state randomizing) and appends the interleaved error correction.</summary>
    internal static byte[] Codewords(List<byte> data, int dataCount, int ecc, int blocks)
    {
        byte[] full = new byte[dataCount + ecc];
        data.CopyTo(full);
        for (int index = data.Count; index < dataCount; index++)
        {
            if (index == data.Count)
            {
                full[index] = Pad;
                continue;
            }

            int pseudo = ((149 * (index + 1)) % 253) + 1;
            int value = Pad + pseudo;
            full[index] = (byte)(value <= 254 ? value : value - 254);
        }

        // Block b takes every blocks-th data codeword from b; its error correction lands at the same stride after the data.
        int eccPerBlock = ecc / blocks;
        for (int block = 0; block < blocks; block++)
        {
            var blockData = new List<byte>();
            for (int index = block; index < dataCount; index += blocks)
            {
                blockData.Add(full[index]);
            }

            byte[] correction = BarcodeReedSolomon.DataMatrix.Encode(blockData.ToArray(), eccPerBlock);
            for (int index = 0; index < eccPerBlock; index++)
            {
                full[dataCount + block + (index * blocks)] = correction[index];
            }
        }

        return full;
    }

    /// <summary>The module placement of ISO/IEC 16022 Annex F over the mapping matrix (the data regions without their finders).</summary>
    internal static bool[] Place(byte[] codewords, int rows, int columns)
    {
        var placement = new Placement(codewords, rows, columns);
        placement.Run();
        return placement.Bits;
    }

    private sealed class Placement(byte[] codewords, int rows, int columns)
    {
        private readonly bool[] _visited = new bool[rows * columns];

        public bool[] Bits { get; } = new bool[rows * columns];

        public void Run()
        {
            int position = 0;
            int row = 4;
            int column = 0;
            do
            {
                if (row == rows && column == 0)
                {
                    Corner1(position++);
                }

                if (row == rows - 2 && column == 0 && columns % 4 != 0)
                {
                    Corner2(position++);
                }

                if (row == rows - 2 && column == 0 && columns % 8 == 4)
                {
                    Corner3(position++);
                }

                if (row == rows + 4 && column == 2 && columns % 8 == 0)
                {
                    Corner4(position++);
                }

                do
                {
                    if (row < rows && column >= 0 && !_visited[(row * columns) + column])
                    {
                        Utah(row, column, position++);
                    }

                    row -= 2;
                    column += 2;
                }
                while (row >= 0 && column < columns);
                row++;
                column += 3;

                do
                {
                    if (row >= 0 && column < columns && !_visited[(row * columns) + column])
                    {
                        Utah(row, column, position++);
                    }

                    row += 2;
                    column -= 2;
                }
                while (row < rows && column >= 0);
                row += 3;
                column++;
            }
            while (row < rows || column < columns);

            // The fixed pattern of symbols whose mapping matrix leaves the bottom right 2 by 2 unfilled.
            if (!_visited[(rows * columns) - 1])
            {
                Bits[(rows * columns) - 1] = true;
                Bits[((rows - 1) * columns) - 2] = true;
                _visited[(rows * columns) - 1] = true;
                _visited[((rows - 1) * columns) - 2] = true;
                _visited[((rows - 1) * columns) - 1] = true;
                _visited[(rows * columns) - 2] = true;
            }
        }

        private void Module(int row, int column, int position, int bit)
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

            int index = (row * columns) + column;
            _visited[index] = true;
            Bits[index] = position < codewords.Length && ((codewords[position] >> (8 - bit)) & 1) != 0;
        }

        private void Utah(int row, int column, int position)
        {
            Module(row - 2, column - 2, position, 1);
            Module(row - 2, column - 1, position, 2);
            Module(row - 1, column - 2, position, 3);
            Module(row - 1, column - 1, position, 4);
            Module(row - 1, column, position, 5);
            Module(row, column - 2, position, 6);
            Module(row, column - 1, position, 7);
            Module(row, column, position, 8);
        }

        private void Corner1(int position)
        {
            Module(rows - 1, 0, position, 1);
            Module(rows - 1, 1, position, 2);
            Module(rows - 1, 2, position, 3);
            Module(0, columns - 2, position, 4);
            Module(0, columns - 1, position, 5);
            Module(1, columns - 1, position, 6);
            Module(2, columns - 1, position, 7);
            Module(3, columns - 1, position, 8);
        }

        private void Corner2(int position)
        {
            Module(rows - 3, 0, position, 1);
            Module(rows - 2, 0, position, 2);
            Module(rows - 1, 0, position, 3);
            Module(0, columns - 4, position, 4);
            Module(0, columns - 3, position, 5);
            Module(0, columns - 2, position, 6);
            Module(0, columns - 1, position, 7);
            Module(1, columns - 1, position, 8);
        }

        private void Corner3(int position)
        {
            Module(rows - 3, 0, position, 1);
            Module(rows - 2, 0, position, 2);
            Module(rows - 1, 0, position, 3);
            Module(0, columns - 2, position, 4);
            Module(0, columns - 1, position, 5);
            Module(1, columns - 1, position, 6);
            Module(2, columns - 1, position, 7);
            Module(3, columns - 1, position, 8);
        }

        private void Corner4(int position)
        {
            Module(rows - 1, 0, position, 1);
            Module(rows - 1, columns - 1, position, 2);
            Module(0, columns - 3, position, 3);
            Module(0, columns - 2, position, 4);
            Module(0, columns - 1, position, 5);
            Module(1, columns - 3, position, 6);
            Module(1, columns - 2, position, 7);
            Module(1, columns - 1, position, 8);
        }
    }
}
