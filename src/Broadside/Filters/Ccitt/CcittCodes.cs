namespace Broadside.Filters.Ccitt;

/// <summary>
/// The fax code tables of ITU-T T.4 (identical to T.6 Tables 1-3) as single-level lookup tables, built once when the type
/// initializes: a run table indexed by the next 12 (white) or 13 (black) bits and a mode table indexed by the next 7 bits.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.4 §4.1.2 Tables 2, 3a, 3b (terminating, make-up and extended make-up codes), §4.1.3 (EOL), §4.2.1.3.2 Table 4 (mode codes
/// and extensions); ITU-T T.6 §2.2.3 Tables 1-3. A run entry packs <c>length &lt;&lt; 12 | run</c>; length 0 marks bits that start no
/// code. Run 4095 marks EOL and run 4094 the 1-D extension code <c>000000001xxx</c> (the 3 bits after it in <see cref="ExtensionBits"/>).
/// </para>
/// </remarks>
internal static class CcittCodes
{
    /// <summary>The run value that marks the EOL code word in <see cref="White"/> and <see cref="Black"/>.</summary>
    public const int EolRun = 4095;

    /// <summary>The run value that marks a one-dimensional extension code (<c>000000001xxx</c>).</summary>
    public const int ExtensionRun = 4094;

    /// <summary>The run value of the longest make-up code; runs at or above 64 are make-ups.</summary>
    public const int MaxMakeUp = 2560;

    /// <summary>The terminating, make-up and extended make-up code words for white runs: (code, length, run). T.4 Tables 2, 3a and 3b.</summary>
    public static readonly (int Code, int Length, int Run)[] WhiteCodes =
    [
        (0b00110101, 8, 0), (0b000111, 6, 1), (0b0111, 4, 2), (0b1000, 4, 3),
        (0b1011, 4, 4), (0b1100, 4, 5), (0b1110, 4, 6), (0b1111, 4, 7),
        (0b10011, 5, 8), (0b10100, 5, 9), (0b00111, 5, 10), (0b01000, 5, 11),
        (0b001000, 6, 12), (0b000011, 6, 13), (0b110100, 6, 14), (0b110101, 6, 15),
        (0b101010, 6, 16), (0b101011, 6, 17), (0b0100111, 7, 18), (0b0001100, 7, 19),
        (0b0001000, 7, 20), (0b0010111, 7, 21), (0b0000011, 7, 22), (0b0000100, 7, 23),
        (0b0101000, 7, 24), (0b0101011, 7, 25), (0b0010011, 7, 26), (0b0100100, 7, 27),
        (0b0011000, 7, 28), (0b00000010, 8, 29), (0b00000011, 8, 30), (0b00011010, 8, 31),
        (0b00011011, 8, 32), (0b00010010, 8, 33), (0b00010011, 8, 34), (0b00010100, 8, 35),
        (0b00010101, 8, 36), (0b00010110, 8, 37), (0b00010111, 8, 38), (0b00101000, 8, 39),
        (0b00101001, 8, 40), (0b00101010, 8, 41), (0b00101011, 8, 42), (0b00101100, 8, 43),
        (0b00101101, 8, 44), (0b00000100, 8, 45), (0b00000101, 8, 46), (0b00001010, 8, 47),
        (0b00001011, 8, 48), (0b01010010, 8, 49), (0b01010011, 8, 50), (0b01010100, 8, 51),
        (0b01010101, 8, 52), (0b00100100, 8, 53), (0b00100101, 8, 54), (0b01011000, 8, 55),
        (0b01011001, 8, 56), (0b01011010, 8, 57), (0b01011011, 8, 58), (0b01001010, 8, 59),
        (0b01001011, 8, 60), (0b00110010, 8, 61), (0b00110011, 8, 62), (0b00110100, 8, 63),
        (0b11011, 5, 64), (0b10010, 5, 128), (0b010111, 6, 192), (0b0110111, 7, 256),
        (0b00110110, 8, 320), (0b00110111, 8, 384), (0b01100100, 8, 448), (0b01100101, 8, 512),
        (0b01101000, 8, 576), (0b01100111, 8, 640), (0b011001100, 9, 704), (0b011001101, 9, 768),
        (0b011010010, 9, 832), (0b011010011, 9, 896), (0b011010100, 9, 960), (0b011010101, 9, 1024),
        (0b011010110, 9, 1088), (0b011010111, 9, 1152), (0b011011000, 9, 1216), (0b011011001, 9, 1280),
        (0b011011010, 9, 1344), (0b011011011, 9, 1408), (0b010011000, 9, 1472), (0b010011001, 9, 1536),
        (0b010011010, 9, 1600), (0b011000, 6, 1664), (0b010011011, 9, 1728), (0b00000001000, 11, 1792),
        (0b00000001100, 11, 1856), (0b00000001101, 11, 1920), (0b000000010010, 12, 1984), (0b000000010011, 12, 2048),
        (0b000000010100, 12, 2112), (0b000000010101, 12, 2176), (0b000000010110, 12, 2240), (0b000000010111, 12, 2304),
        (0b000000011100, 12, 2368), (0b000000011101, 12, 2432), (0b000000011110, 12, 2496), (0b000000011111, 12, 2560),
    ];

    /// <summary>The terminating, make-up and extended make-up code words for black runs: (code, length, run). T.4 Tables 2, 3a and 3b.</summary>
    public static readonly (int Code, int Length, int Run)[] BlackCodes =
    [
        (0b0000110111, 10, 0), (0b010, 3, 1), (0b11, 2, 2), (0b10, 2, 3),
        (0b011, 3, 4), (0b0011, 4, 5), (0b0010, 4, 6), (0b00011, 5, 7),
        (0b000101, 6, 8), (0b000100, 6, 9), (0b0000100, 7, 10), (0b0000101, 7, 11),
        (0b0000111, 7, 12), (0b00000100, 8, 13), (0b00000111, 8, 14), (0b000011000, 9, 15),
        (0b0000010111, 10, 16), (0b0000011000, 10, 17), (0b0000001000, 10, 18), (0b00001100111, 11, 19),
        (0b00001101000, 11, 20), (0b00001101100, 11, 21), (0b00000110111, 11, 22), (0b00000101000, 11, 23),
        (0b00000010111, 11, 24), (0b00000011000, 11, 25), (0b000011001010, 12, 26), (0b000011001011, 12, 27),
        (0b000011001100, 12, 28), (0b000011001101, 12, 29), (0b000001101000, 12, 30), (0b000001101001, 12, 31),
        (0b000001101010, 12, 32), (0b000001101011, 12, 33), (0b000011010010, 12, 34), (0b000011010011, 12, 35),
        (0b000011010100, 12, 36), (0b000011010101, 12, 37), (0b000011010110, 12, 38), (0b000011010111, 12, 39),
        (0b000001101100, 12, 40), (0b000001101101, 12, 41), (0b000011011010, 12, 42), (0b000011011011, 12, 43),
        (0b000001010100, 12, 44), (0b000001010101, 12, 45), (0b000001010110, 12, 46), (0b000001010111, 12, 47),
        (0b000001100100, 12, 48), (0b000001100101, 12, 49), (0b000001010010, 12, 50), (0b000001010011, 12, 51),
        (0b000000100100, 12, 52), (0b000000110111, 12, 53), (0b000000111000, 12, 54), (0b000000100111, 12, 55),
        (0b000000101000, 12, 56), (0b000001011000, 12, 57), (0b000001011001, 12, 58), (0b000000101011, 12, 59),
        (0b000000101100, 12, 60), (0b000001011010, 12, 61), (0b000001100110, 12, 62), (0b000001100111, 12, 63),
        (0b0000001111, 10, 64), (0b000011001000, 12, 128), (0b000011001001, 12, 192), (0b000001011011, 12, 256),
        (0b000000110011, 12, 320), (0b000000110100, 12, 384), (0b000000110101, 12, 448), (0b0000001101100, 13, 512),
        (0b0000001101101, 13, 576), (0b0000001001010, 13, 640), (0b0000001001011, 13, 704), (0b0000001001100, 13, 768),
        (0b0000001001101, 13, 832), (0b0000001110010, 13, 896), (0b0000001110011, 13, 960), (0b0000001110100, 13, 1024),
        (0b0000001110101, 13, 1088), (0b0000001110110, 13, 1152), (0b0000001110111, 13, 1216), (0b0000001010010, 13, 1280),
        (0b0000001010011, 13, 1344), (0b0000001010100, 13, 1408), (0b0000001010101, 13, 1472), (0b0000001011010, 13, 1536),
        (0b0000001011011, 13, 1600), (0b0000001100100, 13, 1664), (0b0000001100101, 13, 1728), (0b00000001000, 11, 1792),
        (0b00000001100, 11, 1856), (0b00000001101, 11, 1920), (0b000000010010, 12, 1984), (0b000000010011, 12, 2048),
        (0b000000010100, 12, 2112), (0b000000010101, 12, 2176), (0b000000010110, 12, 2240), (0b000000010111, 12, 2304),
        (0b000000011100, 12, 2368), (0b000000011101, 12, 2432), (0b000000011110, 12, 2496), (0b000000011111, 12, 2560),
    ];

    /// <summary>White runs by the next 12 bits: <c>length &lt;&lt; 12 | run</c>, 0 when the bits start no code.</summary>
    public static readonly ushort[] White = BuildRunTable(WhiteCodes, 12);

    /// <summary>Black runs by the next 13 bits: <c>length &lt;&lt; 12 | run</c>, 0 when the bits start no code.</summary>
    public static readonly ushort[] Black = BuildRunTable(BlackCodes, 13);

    /// <summary>Two-dimensional mode codes by the next 7 bits: <c>length &lt;&lt; 4 | (int)mode</c>. T.4 Table 4, T.6 Table 1.</summary>
    public static readonly byte[] Modes = BuildModeTable();

    /// <summary>The length of an EOL code word: 11 zeros and a 1. T.4 §4.1.3.</summary>
    public const int EolLength = 12;

    /// <summary>The length of an extension code's prefix before its 3 <c>xxx</c> bits: <c>0000001</c> (2-D) or <c>000000001</c> (1-D).</summary>
    public const int ExtensionBits = 3;

    private static ushort[] BuildRunTable((int Code, int Length, int Run)[] codes, int bits)
    {
        ushort[] table = new ushort[1 << bits];
        foreach ((int code, int length, int run) in codes)
        {
            Fill(table, bits, code, length, run);
        }

        Fill(table, bits, 1, EolLength, EolRun);           // 000000000001
        Fill(table, bits, 1, 9, ExtensionRun);             // 000000001xxx: 1-D extension (xxx = 111: uncompressed)
        return table;
    }

    private static void Fill(ushort[] table, int bits, int code, int length, int run)
    {
        int shift = bits - length;
        int start = code << shift;
        for (int suffix = 0; suffix < 1 << shift; suffix++)
        {
            table[start + suffix] = (ushort)((length << 12) | run);
        }
    }

    private static byte[] BuildModeTable()
    {
        byte[] table = new byte[128];
        void Add(int code, int length, CcittMode mode)
        {
            int shift = 7 - length;
            for (int suffix = 0; suffix < 1 << shift; suffix++)
            {
                table[(code << shift) + suffix] = (byte)((length << 4) | (int)mode);
            }
        }

        Add(0b1, 1, CcittMode.Vertical0);
        Add(0b011, 3, CcittMode.VerticalRight1);
        Add(0b010, 3, CcittMode.VerticalLeft1);
        Add(0b001, 3, CcittMode.Horizontal);
        Add(0b0001, 4, CcittMode.Pass);
        Add(0b000011, 6, CcittMode.VerticalRight2);
        Add(0b000010, 6, CcittMode.VerticalLeft2);
        Add(0b0000011, 7, CcittMode.VerticalRight3);
        Add(0b0000010, 7, CcittMode.VerticalLeft3);
        Add(0b0000001, 7, CcittMode.Extension);
        return table;
    }
}

/// <summary>A two-dimensional coding mode. ITU-T T.4 §4.2.1.3.2 Table 4; ITU-T T.6 §2.2.3 Table 1.</summary>
internal enum CcittMode : byte
{
    /// <summary>No mode code starts here: seven zero bits (an EOL, fill, or invalid data).</summary>
    None = 0,

    /// <summary>Pass mode <c>0001</c>.</summary>
    Pass,

    /// <summary>Horizontal mode <c>001</c> + M(a0a1) + M(a1a2).</summary>
    Horizontal,

    /// <summary>Vertical mode a1 = b1, <c>1</c>.</summary>
    Vertical0,

    /// <summary>Vertical mode a1 = b1 + 1, <c>011</c>.</summary>
    VerticalRight1,

    /// <summary>Vertical mode a1 = b1 + 2, <c>000011</c>.</summary>
    VerticalRight2,

    /// <summary>Vertical mode a1 = b1 + 3, <c>0000011</c>.</summary>
    VerticalRight3,

    /// <summary>Vertical mode a1 = b1 - 1, <c>010</c>.</summary>
    VerticalLeft1,

    /// <summary>Vertical mode a1 = b1 - 2, <c>000010</c>.</summary>
    VerticalLeft2,

    /// <summary>Vertical mode a1 = b1 - 3, <c>0000010</c>.</summary>
    VerticalLeft3,

    /// <summary>The two-dimensional extension <c>0000001xxx</c> (xxx = 111: uncompressed mode).</summary>
    Extension,
}
