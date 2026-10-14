using System.Numerics;

namespace Broadside.Writing;

/// <summary>What the page offset, shared object and outline hint tables of a linearized file say (ISO 32000-2 F.4.2, F.4.3, F.4.5).</summary>
/// <param name="FirstPageOffset">The first page's page object's offset, as if the primary hint stream were not present (F.4.1).</param>
/// <param name="PageObjectCounts">The number of objects of each page, the page object included (Table F.4, item 1).</param>
/// <param name="PageLengths">The length of each page in bytes (Table F.4, item 2).</param>
/// <param name="SharedIdentifiers">The shared object hint table entries each page uses (Table F.4, items 3 and 4).</param>
/// <param name="FirstPageSharedCount">The number of shared object entries for the first page's objects (Table F.5, item 3).</param>
/// <param name="SharedLengths">The length of each shared object group, the first page's first (Table F.6, item 1).</param>
/// <param name="FirstSharedObject">The number of the first object of part 8, or 0 when it is empty (Table F.5, item 1).</param>
/// <param name="FirstSharedOffset">Its offset, as if the primary hint stream were not present, or 0 (Table F.5, item 2).</param>
/// <param name="Outlines">The outline hierarchy's first object, offset, object count and length, or <see langword="null"/> (F.4.5, Table F.7).</param>
internal sealed record LinearizationHints(
    long FirstPageOffset,
    IReadOnlyList<int> PageObjectCounts,
    IReadOnlyList<long> PageLengths,
    IReadOnlyList<int[]> SharedIdentifiers,
    int FirstPageSharedCount,
    IReadOnlyList<long> SharedLengths,
    int FirstSharedObject,
    long FirstSharedOffset,
    (int FirstObject, long Offset, int Count, long Length)? Outlines);

/// <summary>Encodes the hint tables of a linearized file into the data of its primary hint stream (ISO 32000-2 F.4).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.4.1 to F.4.3 and F.4.5. The data is a bit stream, most significant bit first; each table starts on a byte boundary,
/// and so does each column of items (all pages' item 1, all pages' item 2, …), as qpdf writes it and as the reader
/// (<c>HintTableReader</c>) reads it. Following qpdf, which follows Acrobat's implementation notes: every shared object group holds
/// one object, the content stream offset and length items repeat the page's offset and length (Acrobat ignores them), shared object
/// references have no fractional position (numerator width 0, denominator 4), and no group carries an MD5 signature.
/// </para>
/// </remarks>
internal static class HintTableWriter
{
    /// <summary>Writes the tables.</summary>
    /// <param name="hints">What the tables say.</param>
    /// <param name="sharedTable">The position of the shared object hint table in the data: the hint stream's <c>S</c> entry (Table F.2).</param>
    /// <param name="outlineTable">The position of the outline hint table, the hint stream's <c>O</c> entry, or <see langword="null"/>.</param>
    /// <returns>The decoded hint stream data.</returns>
    public static byte[] Write(LinearizationHints hints, out int sharedTable, out int? outlineTable)
    {
        var writer = new BitWriter();
        WritePageOffsetTable(writer, hints);
        sharedTable = writer.Length;
        WriteSharedObjectTable(writer, hints);
        outlineTable = null;
        if (hints.Outlines is { } outlines)
        {
            outlineTable = writer.Length;
            writer.Write((ulong)outlines.FirstObject, 32);
            writer.Write((ulong)outlines.Offset, 32);
            writer.Write((ulong)outlines.Count, 32);
            writer.Write((ulong)outlines.Length, 32);
        }

        return writer.ToArray();
    }

    /// <summary>Writes the page offset hint table (F.4.2, Tables F.3 and F.4).</summary>
    private static void WritePageOffsetTable(BitWriter writer, LinearizationHints hints)
    {
        int pages = hints.PageObjectCounts.Count;
        int minObjects = hints.PageObjectCounts.Min();
        int maxObjects = hints.PageObjectCounts.Max();
        long minLength = hints.PageLengths.Min();
        long maxLength = hints.PageLengths.Max();
        int maxShared = hints.SharedIdentifiers.Max(identifiers => identifiers.Length);
        int bitsObjects = BitsFor(maxObjects - minObjects);
        int bitsLength = BitsFor(maxLength - minLength);
        int bitsShared = BitsFor(maxShared);
        int bitsIdentifier = BitsFor(hints.SharedLengths.Count);

        writer.Write((ulong)minObjects, 32); // 1
        writer.Write((ulong)hints.FirstPageOffset, 32); // 2
        writer.Write((ulong)bitsObjects, 16); // 3
        writer.Write((ulong)minLength, 32); // 4
        writer.Write((ulong)bitsLength, 16); // 5
        writer.Write(0, 32); // 6: least content stream offset
        writer.Write(0, 16); // 7
        writer.Write((ulong)minLength, 32); // 8: least content stream length (the page length, as Acrobat)
        writer.Write((ulong)bitsLength, 16); // 9
        writer.Write((ulong)bitsShared, 16); // 10
        writer.Write((ulong)bitsIdentifier, 16); // 11
        writer.Write(0, 16); // 12: numerator width
        writer.Write(4, 16); // 13: denominator

        for (int page = 0; page < pages; page++)
        {
            writer.Write((ulong)(hints.PageObjectCounts[page] - minObjects), bitsObjects);
        }

        writer.Align();
        for (int page = 0; page < pages; page++)
        {
            writer.Write((ulong)(hints.PageLengths[page] - minLength), bitsLength);
        }

        writer.Align();
        for (int page = 0; page < pages; page++)
        {
            writer.Write((ulong)hints.SharedIdentifiers[page].Length, bitsShared);
        }

        writer.Align();
        foreach (int[] identifiers in hints.SharedIdentifiers)
        {
            foreach (int identifier in identifiers)
            {
                writer.Write((ulong)identifier, bitsIdentifier);
            }
        }

        writer.Align();

        // Items 5 (numerators, 0 bits wide) and 6 (content offsets, 0 bits wide) take no space; item 7 repeats item 2.
        for (int page = 0; page < pages; page++)
        {
            writer.Write((ulong)(hints.PageLengths[page] - minLength), bitsLength);
        }

        writer.Align();
    }

    /// <summary>Writes the shared object hint table (F.4.3, Tables F.5 and F.6), one object per group.</summary>
    private static void WriteSharedObjectTable(BitWriter writer, LinearizationHints hints)
    {
        long minLength = hints.SharedLengths.Min();
        int bitsLength = BitsFor(hints.SharedLengths.Max() - minLength);
        writer.Write((ulong)hints.FirstSharedObject, 32); // 1
        writer.Write((ulong)hints.FirstSharedOffset, 32); // 2
        writer.Write((ulong)hints.FirstPageSharedCount, 32); // 3
        writer.Write((ulong)hints.SharedLengths.Count, 32); // 4
        writer.Write(0, 16); // 5: every group holds one object
        writer.Write((ulong)minLength, 32); // 6
        writer.Write((ulong)bitsLength, 16); // 7
        foreach (long length in hints.SharedLengths)
        {
            writer.Write((ulong)(length - minLength), bitsLength);
        }

        writer.Align();
        foreach (long _ in hints.SharedLengths)
        {
            writer.Write(0, 1); // item 2: no signature
        }

        writer.Align();
    }

    /// <summary>The number of bits that hold <paramref name="value"/>: 0 for 0.</summary>
    private static int BitsFor(long value) => value <= 0 ? 0 : 64 - BitOperations.LeadingZeroCount((ulong)value);

    /// <summary>Writes values of any width up to 64 bits, most significant bit first.</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _bits;

        /// <summary>Gets the number of whole bytes written; call <see cref="Align"/> first for a table position.</summary>
        public int Length => _bytes.Count;

        public void Write(ulong value, int bits)
        {
            for (int bit = bits - 1; bit >= 0; bit--)
            {
                _current = (_current << 1) | (int)((value >> bit) & 1);
                if (++_bits == 8)
                {
                    _bytes.Add((byte)_current);
                    _current = 0;
                    _bits = 0;
                }
            }
        }

        /// <summary>Pads the last byte with zero bits.</summary>
        public void Align()
        {
            if (_bits > 0)
            {
                Write(0, 8 - _bits);
            }
        }

        public byte[] ToArray()
        {
            Align();
            return [.. _bytes];
        }
    }
}
