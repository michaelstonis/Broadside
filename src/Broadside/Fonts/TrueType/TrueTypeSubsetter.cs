using System.Buffers.Binary;

namespace Broadside.Fonts.TrueType;

/// <summary>
/// Reduces a TrueType program to the glyphs a text uses, keeping every glyph id: unused glyphs are emptied rather than removed, so a
/// CIDFontType2 with an <c>Identity</c> <c>CIDToGIDMap</c> addresses the subset with the original ids.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9 (Table 126: the tables a TrueType program embedded as <c>FontFile2</c> needs) and §9.9.2 (subset fonts). Kept:
/// <c>head</c>, <c>hhea</c>, <c>maxp</c>, <c>hmtx</c>, <c>loca</c>, <c>glyf</c>, and <c>cvt </c>, <c>fpgm</c>, <c>prep</c>,
/// <c>OS/2</c> and <c>name</c> when present; <c>cmap</c> is not needed for a CIDFontType2 (§9.7.4.2) and is dropped with every
/// other table. Composite glyphs keep the glyphs they are built from. The output is the same for the same input and glyph set.
/// </para>
/// <para>
/// Internal and minimal: written for form-field appearances (issue #167). Issue #150 (TrueType embedding with subsetting) owns the
/// general embedder with glyph renumbering; it should replace or absorb this.
/// </para>
/// </remarks>
internal static class TrueTypeSubsetter
{
    private const int ArgsAreWords = 0x0001;
    private const int HaveScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HaveXAndYScale = 0x0040;
    private const int HaveTwoByTwo = 0x0080;

    private static readonly uint[] KeptTables =
    [
        SfntFile.Tag("OS/2"), SfntFile.Tag("cvt "), SfntFile.Tag("fpgm"), SfntFile.Tag("glyf"), SfntFile.Tag("head"), SfntFile.Tag("hhea"),
        SfntFile.Tag("hmtx"), SfntFile.Tag("loca"), SfntFile.Tag("maxp"), SfntFile.Tag("name"), SfntFile.Tag("prep"),
    ];

    /// <summary>Writes the subset of <paramref name="font"/> that outlines <paramref name="glyphs"/>, glyph 0 and their components.</summary>
    /// <param name="font">The program's table directory: a TrueType (<c>glyf</c>) program.</param>
    /// <param name="glyphs">The glyph ids the text uses.</param>
    /// <returns>The subset program, or <see langword="null"/> when the program has no usable <c>head</c>, <c>maxp</c>, <c>loca</c> and <c>glyf</c>.</returns>
    public static byte[]? Subset(SfntFile font, IEnumerable<int> glyphs)
    {
        if (!font.TryGetTable(SfntFile.Tag("head"), out ReadOnlyMemory<byte> headTable) || headTable.Length < 54
            || !font.TryGetTable(SfntFile.Tag("maxp"), out ReadOnlyMemory<byte> maxp) || maxp.Length < 6
            || !font.TryGetTable(SfntFile.Tag("loca"), out ReadOnlyMemory<byte> loca)
            || !font.TryGetTable(SfntFile.Tag("glyf"), out ReadOnlyMemory<byte> glyf))
        {
            return null;
        }

        int glyphCount = BinaryPrimitives.ReadUInt16BigEndian(maxp.Span[4..]);
        bool longOffsets = BinaryPrimitives.ReadInt16BigEndian(headTable.Span[50..]) != 0;
        if (loca.Length < (glyphCount + 1) * (longOffsets ? 4 : 2))
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

        // The new glyf and loca (32-bit offsets): kept glyphs copied, padded to four bytes; the others empty.
        using var newGlyf = new MemoryStream();
        byte[] newLoca = new byte[(glyphCount + 1) * 4];
        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyph * 4), (uint)newGlyf.Length);
            if (kept.Contains(glyph))
            {
                ReadOnlySpan<byte> data = GlyphData(glyf.Span, loca.Span, longOffsets, glyph);
                newGlyf.Write(data);
                newGlyf.Write(new byte[(4 - (data.Length % 4)) % 4]);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyphCount * 4), (uint)newGlyf.Length);

        byte[] head = headTable.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

        var tables = new List<(uint Tag, byte[] Data)>();
        foreach (uint tag in KeptTables)
        {
            byte[]? data = tag switch
            {
                _ when tag == SfntFile.Tag("glyf") => newGlyf.ToArray(),
                _ when tag == SfntFile.Tag("loca") => newLoca,
                _ when tag == SfntFile.Tag("head") => head,
                _ => font.TryGetTable(tag, out ReadOnlyMemory<byte> table) ? table.ToArray() : null,
            };
            if (data is not null)
            {
                tables.Add((tag, data));
            }
        }

        return Write(tables);
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
        if (glyph.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(glyph) >= 0)
        {
            return components;
        }

        int offset = 10;
        while (offset + 4 <= glyph.Length)
        {
            int flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[offset..]);
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(glyph[(offset + 2)..]));
            offset += 4 + ((flags & ArgsAreWords) != 0 ? 4 : 2);
            offset += (flags & HaveScale) != 0 ? 2 : (flags & HaveXAndYScale) != 0 ? 4 : (flags & HaveTwoByTwo) != 0 ? 8 : 0;
            if ((flags & MoreComponents) == 0)
            {
                break;
            }
        }

        return components;
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
