namespace Broadside.Filters.Dct;

/// <summary>
/// One Huffman encoding table: the code and code length of every symbol, derived from BITS and HUFFVAL lists. The encoder uses the
/// four typical tables of Annex K, built once for the process.
/// </summary>
/// <remarks>ITU-T T.81 Annex C (Figures C.1 to C.3: HUFFSIZE, HUFFCODE, and EHUFCO / EHUFSI ordered by symbol), §K.3.</remarks>
internal sealed class HuffmanEncodingTable
{
    /// <summary>Table K.3: the luminance DC table.</summary>
    public static readonly HuffmanEncodingTable DcLuminance = new(StandardHuffmanTables.DcLuminanceCounts, StandardHuffmanTables.DcValues);

    /// <summary>Table K.4: the chrominance DC table.</summary>
    public static readonly HuffmanEncodingTable DcChrominance = new(StandardHuffmanTables.DcChrominanceCounts, StandardHuffmanTables.DcValues);

    /// <summary>Table K.5: the luminance AC table.</summary>
    public static readonly HuffmanEncodingTable AcLuminance = new(StandardHuffmanTables.AcLuminanceCounts, StandardHuffmanTables.AcLuminanceValues);

    /// <summary>Table K.6: the chrominance AC table.</summary>
    public static readonly HuffmanEncodingTable AcChrominance = new(StandardHuffmanTables.AcChrominanceCounts, StandardHuffmanTables.AcChrominanceValues);

    private HuffmanEncodingTable(byte[] counts, byte[] values)
    {
        Counts = counts;
        Values = values;

        // Figure C.1 and C.2: codes are assigned in order of increasing length, consecutive within a length, doubled at each
        // new length. Figure C.3: then indexed by symbol.
        int code = 0;
        int index = 0;
        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++, index++)
            {
                byte symbol = values[index];
                Codes[symbol] = (ushort)code;
                Lengths[symbol] = (byte)length;
                code++;
            }

            code <<= 1;
        }
    }

    /// <summary>Gets BITS: the number of codes of each length 1 to 16, as a DHT segment carries them.</summary>
    public byte[] Counts { get; }

    /// <summary>Gets HUFFVAL: the symbols in order of increasing code length, as a DHT segment carries them.</summary>
    public byte[] Values { get; }

    /// <summary>Gets EHUFCO: the code of each symbol.</summary>
    public ushort[] Codes { get; } = new ushort[256];

    /// <summary>Gets EHUFSI: the code length of each symbol, 0 for a symbol without a code.</summary>
    public byte[] Lengths { get; } = new byte[256];
}
