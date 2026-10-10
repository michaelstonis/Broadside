using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The entropy decoder of one segment: the MQ decoder and the arithmetic statistics (SDHUFF / SBHUFF = 0), or the bit reader and
/// Huffman tables (= 1). Every procedure a segment invokes (aggregate text regions inside a symbol dictionary, single refinements,
/// collective bitmaps) takes the coder by reference, so they continue the same decoder and contexts (ITU-T T.88 §7.4.2.2, §7.4.3.2).
/// </summary>
internal ref struct Jbig2Coder
{
    /// <summary>The marker bytes an MQ decoder may meet before a counting loop stops as over damaged data.</summary>
    public const int MarkerAllowance = 1 << 10;

    /// <summary>The MQ decoder (arithmetic coding).</summary>
    public MqDecoder Mq;

    /// <summary>The bit reader (Huffman coding).</summary>
    public Jbig2BitReader Bits;

    /// <summary>Initializes a new instance of the <see cref="Jbig2Coder"/> struct over a segment's coded data.</summary>
    /// <param name="data">The coded data after the segment's data header.</param>
    /// <param name="huffman">Whether the segment is Huffman coded.</param>
    /// <param name="statistics">The arithmetic statistics.</param>
    public Jbig2Coder(ReadOnlySpan<byte> data, bool huffman, Jbig2Statistics statistics)
    {
        Huffman = huffman;
        Statistics = statistics;
        Mq = huffman ? default : new MqDecoder(data);
        Bits = new Jbig2BitReader(data);
    }

    /// <summary>Gets a value indicating whether the segment is Huffman coded.</summary>
    public readonly bool Huffman { get; }

    /// <summary>Gets the arithmetic statistics.</summary>
    public readonly Jbig2Statistics Statistics { get; }

    /// <summary>Gets or sets a value indicating whether a value could not be decoded (an invalid Huffman code, an OOB where none is allowed).</summary>
    public bool Invalid { get; set; }

    /// <summary>Gets a value indicating whether the data ran out long ago: counting loops stop.</summary>
    public readonly bool IsExhausted => Invalid || (Huffman ? Bits.IsExhausted : Mq.MarkerBytes > MarkerAllowance);

    /// <summary>Decodes an integer with <paramref name="table"/> or the arithmetic <paramref name="procedure"/>; <see langword="false"/> for OOB.</summary>
    /// <param name="procedure">The integer procedure (arithmetic coding).</param>
    /// <param name="table">The Huffman table (Huffman coding).</param>
    /// <param name="value">The value, 0 for OOB or an invalid code.</param>
    /// <returns><see langword="false"/> for OOB, or for an invalid code (which also sets <see cref="Invalid"/>).</returns>
    public bool TryDecode(Jbig2IntegerProcedure procedure, Jbig2HuffmanTable? table, out long value)
    {
        if (!Huffman)
        {
            return Jbig2IntegerDecoder.Decode(ref Mq, Statistics.Integer(procedure), out value);
        }

        long decoded = table!.Decode(ref Bits);
        if (decoded == Jbig2HuffmanTable.Oob)
        {
            value = 0;
            return false;
        }

        if (decoded == Jbig2HuffmanTable.Invalid)
        {
            Invalid = true;
            value = 0;
            return false;
        }

        value = decoded;
        return true;
    }

    /// <summary>Decodes an integer that cannot be OOB; an OOB marks the data <see cref="Invalid"/> and gives 0.</summary>
    /// <param name="procedure">The integer procedure (arithmetic coding).</param>
    /// <param name="table">The Huffman table (Huffman coding).</param>
    /// <returns>The value.</returns>
    public long Decode(Jbig2IntegerProcedure procedure, Jbig2HuffmanTable? table)
    {
        if (!TryDecode(procedure, table, out long value))
        {
            Invalid = true;
        }

        return value;
    }
}
