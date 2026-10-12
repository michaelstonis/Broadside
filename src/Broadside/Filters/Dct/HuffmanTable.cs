namespace Broadside.Filters.Dct;

/// <summary>
/// One Huffman decoding table built from a DHT segment's BITS and HUFFVAL lists: a 9-bit lookup for the short codes and the
/// MAXCODE / VALPTR arrays for codes of 10 to 16 bits. Built in place, so a decoder reuses its tables across images.
/// </summary>
/// <remarks>ITU-T T.81 Annex C (Figures C.1 and C.2: HUFFSIZE and HUFFCODE), §F.2.2.3 (Figure F.15: MAXCODE, MINCODE, VALPTR).</remarks>
internal sealed class HuffmanTable
{
    /// <summary>The number of leading bits the lookup table resolves.</summary>
    public const int LookupBits = 9;

    /// <summary>Per <see cref="LookupBits"/>-bit prefix: (code length &lt;&lt; 8) | value, or 0 when the code is longer.</summary>
    public readonly ushort[] Lookup = new ushort[1 << LookupBits];

    /// <summary>Per code length 1 to 16: the greatest code of that length, or -1 when there is none; index 17 is a sentinel.</summary>
    public readonly int[] MaxCode = new int[18];

    /// <summary>Per code length: the index into <see cref="Values"/> of a code minus the code (VALPTR - MINCODE).</summary>
    public readonly int[] ValueOffset = new int[18];

    /// <summary>The HUFFVAL list.</summary>
    public readonly byte[] Values = new byte[256];

    private readonly int[] _codes = new int[257];
    private readonly byte[] _sizes = new byte[257];

    /// <summary>Gets a value indicating whether the table holds a valid definition.</summary>
    public bool IsDefined { get; private set; }

    /// <summary>Forgets the definition.</summary>
    public void Clear() => IsDefined = false;

    /// <summary>Builds the table.</summary>
    /// <param name="counts">BITS: the number of codes of each length 1 to 16.</param>
    /// <param name="values">HUFFVAL: the symbols in order of increasing code length.</param>
    /// <param name="maxValue">The greatest symbol the table class allows (15 for DC tables, 255 for AC tables).</param>
    /// <returns><see langword="false"/> when the lists do not form a prefix code (too many codes of a length), or a symbol is out of range.</returns>
    public bool Build(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values, int maxValue)
    {
        IsDefined = false;
        int total = 0;
        for (int length = 1; length <= 16; length++)
        {
            total += counts[length - 1];
        }

        if (total > 256 || values.Length < total)
        {
            return false;
        }

        // Figure C.1: HUFFSIZE.
        int p = 0;
        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++)
            {
                _sizes[p++] = (byte)length;
            }
        }

        _sizes[p] = 0;

        // Figure C.2: HUFFCODE, rejecting a length whose codes overflow it (libjpeg's check; the all-ones code stays unused).
        int code = 0;
        int size = _sizes[0];
        p = 0;
        while (_sizes[p] != 0)
        {
            while (_sizes[p] == size)
            {
                _codes[p++] = code++;
            }

            if (code >= 1 << size)
            {
                return false;
            }

            code <<= 1;
            size++;
        }

        // Figure F.15: MAXCODE and VALPTR - MINCODE.
        p = 0;
        for (int length = 1; length <= 16; length++)
        {
            int count = counts[length - 1];
            if (count == 0)
            {
                MaxCode[length] = -1;
                ValueOffset[length] = 0;
                continue;
            }

            ValueOffset[length] = p - _codes[p];
            p += count;
            MaxCode[length] = _codes[p - 1];
        }

        MaxCode[0] = -1;
        MaxCode[17] = int.MaxValue;

        for (int i = 0; i < total; i++)
        {
            if (values[i] > maxValue)
            {
                return false;
            }

            Values[i] = values[i];
        }

        // The lookup: every LookupBits-bit prefix that starts with a code of at most LookupBits bits.
        Array.Clear(Lookup);
        p = 0;
        for (int length = 1; length <= LookupBits; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++, p++)
            {
                int first = _codes[p] << (LookupBits - length);
                int entries = 1 << (LookupBits - length);
                ushort entry = (ushort)((length << 8) | Values[p]);
                Lookup.AsSpan(first, entries).Fill(entry);
            }
        }

        IsDefined = true;
        return true;
    }
}
