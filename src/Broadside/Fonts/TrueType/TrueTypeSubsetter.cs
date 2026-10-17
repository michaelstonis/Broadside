using System.Buffers.Binary;

namespace Broadside.Fonts.TrueType;

/// <summary>
/// Reduces a TrueType program to the glyphs a text uses and the glyphs their composites are built from, renumbered in ascending order
/// of their original ids after <c>.notdef</c>; the result says which original glyph each new id is.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9 (Table 124: a <c>FontFile2</c> program for a CIDFontType2 has <c>glyf</c>, <c>head</c>, <c>hhea</c>,
/// <c>hmtx</c>, <c>loca</c> and <c>maxp</c>, and <c>cvt </c>, <c>fpgm</c> and <c>prep</c> when its instructions need them) and
/// §9.9.2 (subset fonts: <c>.notdef</c> is kept as glyph 0). Written: those tables (<c>loca</c> in the long format; <c>hmtx</c>,
/// <c>hhea</c> and <c>maxp</c> for the new glyph count, with valid versions; composite glyphs' component ids renumbered), plus <c>OS/2</c>, <c>name</c>
/// and a format 3 <c>post</c> when the program has them. <c>cmap</c> is not needed for a CIDFontType2 (§9.7.4.2), and every table
/// indexed by glyph id that is not rewritten (<c>hdmx</c>, <c>kern</c>, <c>LTSH</c>, <c>VDMX</c>, layout tables) is dropped. The
/// output depends only on the program and the glyph set, so the same input always gives the same bytes.
/// </para>
/// <para>
/// Instructions in <c>fpgm</c>, <c>prep</c> and the glyphs address no glyph ids, so they are copied unchanged. Hinting that relies
/// on dropped tables is a quality loss only.
/// </para>
/// </remarks>
internal static class TrueTypeSubsetter
{
    private const int ArgsAreWords = 0x0001;
    private const int HaveScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HaveXAndYScale = 0x0040;
    private const int HaveTwoByTwo = 0x0080;

    private static readonly uint[] CopiedTables = [SfntFile.Tag("OS/2"), SfntFile.Tag("cvt "), SfntFile.Tag("fpgm"), SfntFile.Tag("name"), SfntFile.Tag("prep")];

    /// <summary>Writes the subset of <paramref name="font"/> that outlines <paramref name="glyphs"/>, glyph 0 and their components.</summary>
    /// <param name="font">The program's table directory: a TrueType (<c>glyf</c>) program.</param>
    /// <param name="glyphs">The original glyph ids the text uses; ids past the program's glyph count are ignored.</param>
    /// <returns>The subset, or <see langword="null"/> when the program has no usable <c>head</c>, <c>hhea</c>, <c>maxp</c>, <c>loca</c> and <c>glyf</c>.</returns>
    public static SubsetProgram? Subset(SfntFile font, IEnumerable<int> glyphs)
    {
        if (!font.TryGetTable(SfntFile.Tag("head"), out ReadOnlyMemory<byte> headTable) || headTable.Length < 54
            || !font.TryGetTable(SfntFile.Tag("hhea"), out ReadOnlyMemory<byte> hheaTable) || hheaTable.Length < 36
            || !font.TryGetTable(SfntFile.Tag("maxp"), out ReadOnlyMemory<byte> maxpTable) || maxpTable.Length < 6
            || !font.TryGetTable(SfntFile.Tag("loca"), out ReadOnlyMemory<byte> loca)
            || !font.TryGetTable(SfntFile.Tag("glyf"), out ReadOnlyMemory<byte> glyf))
        {
            return null;
        }

        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxpTable.Span[4..]);
        bool longOffsets = BinaryPrimitives.ReadInt16BigEndian(headTable.Span[50..]) != 0;
        if (glyphCount == 0 || loca.Length < (glyphCount + 1) * (longOffsets ? 4 : 2))
        {
            return null;
        }

        var kept = new SortedSet<int> { 0 };
        var pending = new Stack<int>();
        foreach (int glyph in glyphs)
        {
            if ((uint)glyph < (uint)glyphCount && kept.Add(glyph))
            {
                pending.Push(glyph);
            }
        }

        while (pending.TryPop(out int glyph))
        {
            foreach (int component in Components(GlyphData(glyf.Span, loca.Span, longOffsets, glyph)))
            {
                if ((uint)component < (uint)glyphCount && kept.Add(component))
                {
                    pending.Push(component);
                }
            }
        }

        int[] order = [.. kept];
        int count = order.Length;

        // glyf and loca (32-bit offsets): each kept glyph copied in its new order, components renumbered, padded to four bytes.
        using var newGlyf = new MemoryStream();
        byte[] newLoca = new byte[(count + 1) * 4];
        for (int index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(index * 4), (uint)newGlyf.Length);
            byte[] data = GlyphData(glyf.Span, loca.Span, longOffsets, order[index]).ToArray();
            Renumber(data, order);
            newGlyf.Write(data);
            newGlyf.Write(new byte[(4 - (data.Length % 4)) % 4]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(count * 4), (uint)newGlyf.Length);
        if (newGlyf.Length == 0)
        {
            // Every kept glyph is empty: a glyf table of padding no loca entry reaches, since a table directory entry of length 0 reads as absent.
            newGlyf.Write(new byte[4]);
        }

        // hmtx: one long metric per new glyph, from the original's long metrics or its trailing left side bearings.
        font.TryGetTable(SfntFile.Tag("hmtx"), out ReadOnlyMemory<byte> hmtxTable);
        ReadOnlySpan<byte> hmtx = hmtxTable.Span;
        int longMetrics = Math.Clamp((int)BinaryPrimitives.ReadUInt16BigEndian(hheaTable.Span[34..]), 1, glyphCount);
        byte[] newHmtx = new byte[count * 4];
        for (int index = 0; index < count; index++)
        {
            int glyph = order[index];
            int advanceAt = Math.Min(glyph, longMetrics - 1) * 4;
            int bearingAt = glyph < longMetrics ? (glyph * 4) + 2 : (longMetrics * 4) + ((glyph - longMetrics) * 2);
            ushort advance = advanceAt + 2 <= hmtx.Length ? BinaryPrimitives.ReadUInt16BigEndian(hmtx[advanceAt..]) : (ushort)0;
            short bearing = bearingAt + 2 <= hmtx.Length ? BinaryPrimitives.ReadInt16BigEndian(hmtx[bearingAt..]) : (short)0;
            BinaryPrimitives.WriteUInt16BigEndian(newHmtx.AsSpan(index * 4), advance);
            BinaryPrimitives.WriteInt16BigEndian(newHmtx.AsSpan((index * 4) + 2), bearing);
        }

        // head, hhea and maxp are written with valid versions (and the head magic number), whatever the original held: a reader that
        // finds a table by its recognizable start (SfntFile's one-byte offset recovery) then reads each where the directory puts it.
        byte[] head = headTable.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        byte[] hhea = hheaTable.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)count);
        byte[] maxp = maxpTable[..(maxpTable.Length >= 32 ? 32 : 6)].ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(maxp, maxp.Length == 32 ? 0x00010000u : 0x00005000u);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)count);

        var tables = new List<(uint Tag, byte[] Data)>
        {
            (SfntFile.Tag("glyf"), newGlyf.ToArray()),
            (SfntFile.Tag("head"), head),
            (SfntFile.Tag("hhea"), hhea),
            (SfntFile.Tag("hmtx"), newHmtx),
            (SfntFile.Tag("loca"), newLoca),
            (SfntFile.Tag("maxp"), maxp),
        };
        foreach (uint tag in CopiedTables)
        {
            if (font.TryGetTable(tag, out ReadOnlyMemory<byte> table))
            {
                tables.Add((tag, table.ToArray()));
            }
        }

        // post: its 32-byte header as format 3 (no glyph names, which are indexed by the old ids).
        if (font.TryGetTable(SfntFile.Tag("post"), out ReadOnlyMemory<byte> post) && post.Length >= 32)
        {
            byte[] header = post[..32].ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(header, 0x00030000);
            tables.Add((SfntFile.Tag("post"), header));
        }

        return new SubsetProgram(Write(tables), order);
    }

    /// <summary>The bytes of one glyph description; empty when the glyph has no outline or its offsets are out of range.</summary>
    private static ReadOnlySpan<byte> GlyphData(ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, bool longOffsets, int glyph)
    {
        long start = longOffsets ? BinaryPrimitives.ReadUInt32BigEndian(loca[(glyph * 4)..]) : BinaryPrimitives.ReadUInt16BigEndian(loca[(glyph * 2)..]) * 2L;
        long end = longOffsets ? BinaryPrimitives.ReadUInt32BigEndian(loca[((glyph + 1) * 4)..]) : BinaryPrimitives.ReadUInt16BigEndian(loca[((glyph + 1) * 2)..]) * 2L;
        return start < end && end <= glyf.Length ? glyf[(int)start..(int)end] : [];
    }

    /// <summary>The glyph ids a composite glyph description is built from; none for a simple glyph.</summary>
    private static List<int> Components(ReadOnlySpan<byte> glyph)
    {
        var components = new List<int>();
        foreach (int offset in ComponentOffsets(glyph))
        {
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(glyph[offset..]));
        }

        return components;
    }

    /// <summary>Rewrites the component glyph ids of a composite description to their new ids (a component not kept becomes 0).</summary>
    private static void Renumber(byte[] glyph, int[] order)
    {
        foreach (int offset in ComponentOffsets(glyph))
        {
            int index = Array.BinarySearch(order, (int)BinaryPrimitives.ReadUInt16BigEndian(glyph.AsSpan(offset)));
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(offset), (ushort)Math.Max(index, 0));
        }
    }

    /// <summary>The offset of each component's glyph index in a composite glyph description; none for a simple glyph.</summary>
    private static List<int> ComponentOffsets(ReadOnlySpan<byte> glyph)
    {
        var offsets = new List<int>();
        if (glyph.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(glyph) >= 0)
        {
            return offsets;
        }

        int offset = 10;
        while (offset + 4 <= glyph.Length)
        {
            int flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[offset..]);
            offsets.Add(offset + 2);
            offset += 4 + ((flags & ArgsAreWords) != 0 ? 4 : 2);
            offset += (flags & HaveScale) != 0 ? 2 : (flags & HaveXAndYScale) != 0 ? 4 : (flags & HaveTwoByTwo) != 0 ? 8 : 0;
            if ((flags & MoreComponents) == 0)
            {
                break;
            }
        }

        return offsets;
    }

    /// <summary>Writes a TrueType program from its tables, in tag order, with checksums and the <c>head</c> checksum adjustment.</summary>
    private static byte[] Write(List<(uint Tag, byte[] Data)> tables)
    {
        tables.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        int count = tables.Count;
        int entrySelector = 0;
        while ((2 << entrySelector) <= count)
        {
            entrySelector++;
        }

        int searchRange = (1 << entrySelector) * 16;
        int length = 12 + (16 * count);
        foreach ((_, byte[] data) in tables)
        {
            length += (data.Length + 3) & ~3;
        }

        byte[] output = new byte[length];
        Span<byte> span = output;
        BinaryPrimitives.WriteUInt32BigEndian(span, SfntFile.TrueTypeVersion);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)((count * 16) - searchRange));
        int offset = 12 + (16 * count);
        int headOffset = -1;
        for (int index = 0; index < count; index++)
        {
            (uint tag, byte[] data) = tables[index];
            data.CopyTo(span[offset..]);
            Span<byte> record = span[(12 + (16 * index))..];
            BinaryPrimitives.WriteUInt32BigEndian(record, tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(span.Slice(offset, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
            if (tag == SfntFile.Tag("head"))
            {
                headOffset = offset;
            }

            offset += (data.Length + 3) & ~3;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], unchecked(0xB1B0AFBA - Checksum(span)));
        }

        return output;
    }

    /// <summary>The sum of the big-endian 32-bit words of <paramref name="data"/>, whose length is a multiple of four.</summary>
    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int offset = 0; offset + 4 <= data.Length; offset += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[offset..]));
        }

        return sum;
    }
}
