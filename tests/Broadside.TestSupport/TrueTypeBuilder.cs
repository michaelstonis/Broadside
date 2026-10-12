using System.Buffers.Binary;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Builds TrueType programs in memory for tests of the font program contract: glyphs, metrics and tables are given explicitly so a
/// test can make any of them malformed. Coordinates are written as int16 deltas, flags one per point; nothing is optimized.
/// </summary>
/// <remarks>OpenType specification: table directory, "head", "hhea", "hmtx", "maxp", "loca", "glyf", "cmap", "post".</remarks>
public sealed class TrueTypeBuilder
{
    /// <summary>Gets the glyph descriptions, by glyph id (empty for a glyph without outline).</summary>
    public List<byte[]> Glyphs { get; } = [];

    /// <summary>Gets or sets the advance and left side bearing per glyph; default 500 and the glyph's xMin.</summary>
    public IReadOnlyList<(ushort Advance, short LeftSideBearing)>? Metrics { get; set; }

    /// <summary>Gets or sets hhea's numberOfHMetrics; default the glyph count.</summary>
    public int? HorizontalMetricCount { get; set; }

    /// <summary>Gets or sets a value indicating whether "loca" uses 32-bit offsets.</summary>
    public bool LongOffsets { get; set; }

    /// <summary>Gets or sets the "cmap" table; default one (3, 1) format 4 subtable mapping nothing.</summary>
    public byte[]? Cmap { get; set; }

    /// <summary>Gets or sets the "post" table; default version 3.0.</summary>
    public byte[]? Post { get; set; }

    /// <summary>Gets or sets the PostScript name in "name".</summary>
    public string Name { get; set; } = "BroadsideTest";

    /// <summary>Gets the tables to replace (a value) or leave out (<see langword="null"/>) after the others are built.</summary>
    public Dictionary<string, byte[]?> Overrides { get; } = [];

    /// <summary>Gets or sets the sfnt version.</summary>
    public uint Version { get; set; } = 0x00010000;

    /// <summary>A simple glyph from contours of points (x, y, on-curve); int16 deltas, no instructions.</summary>
    public static byte[] Simple(params (int X, int Y, bool On)[][] contours)
    {
        (int X, int Y, bool On)[] points = [.. contours.SelectMany(contour => contour)];
        var bytes = new List<byte>();
        Header(bytes, contours.Length, points.Select(p => (p.X, p.Y)));
        int end = -1;
        foreach ((int X, int Y, bool On)[] contour in contours)
        {
            end += contour.Length;
            U16(bytes, end);
        }

        U16(bytes, 0);
        bytes.AddRange(points.Select(p => (byte)(p.On ? 1 : 0)));
        int previous = 0;
        foreach ((int x, _, _) in points)
        {
            I16(bytes, x - previous);
            previous = x;
        }

        previous = 0;
        foreach ((_, int y, _) in points)
        {
            I16(bytes, y - previous);
            previous = y;
        }

        return [.. bytes];
    }

    /// <summary>A rectangle glyph, on-curve points clockwise from the bottom left.</summary>
    public static byte[] Rectangle(int x0, int y0, int x1, int y1) => Simple([(x0, y0, true), (x0, y1, true), (x1, y1, true), (x1, y0, true)]);

    /// <summary>
    /// A composite glyph from component records: flags (MORE_COMPONENTS is added), glyph, two arguments, and the transform bytes; the
    /// arguments are words when flag 0x0001 is set. The header box is the given one.
    /// </summary>
    public static byte[] Composite((int XMin, int YMin, int XMax, int YMax) box, params (int Flags, int Glyph, int Argument1, int Argument2, byte[] Transform)[] components)
    {
        var bytes = new List<byte>();
        I16(bytes, -1);
        I16(bytes, box.XMin);
        I16(bytes, box.YMin);
        I16(bytes, box.XMax);
        I16(bytes, box.YMax);
        for (int index = 0; index < components.Length; index++)
        {
            (int flags, int glyph, int argument1, int argument2, byte[] transform) = components[index];
            flags |= index < components.Length - 1 ? 0x0020 : 0;
            U16(bytes, flags);
            U16(bytes, glyph);
            if ((flags & 0x0001) != 0)
            {
                I16(bytes, argument1);
                I16(bytes, argument2);
            }
            else
            {
                bytes.Add(unchecked((byte)argument1));
                bytes.Add(unchecked((byte)argument2));
            }

            bytes.AddRange(transform);
        }

        return [.. bytes];
    }

    /// <summary>F2Dot14 values, big-endian.</summary>
    public static byte[] F2Dot14(params double[] values)
    {
        var bytes = new List<byte>();
        foreach (double value in values)
        {
            I16(bytes, (int)Math.Round(value * 16384));
        }

        return [.. bytes];
    }

    /// <summary>A "cmap" table from (platform, encoding, subtable) records.</summary>
    public static byte[] CmapTable(params (int Platform, int Encoding, byte[] Subtable)[] subtables)
    {
        var bytes = new List<byte>();
        U16(bytes, 0);
        U16(bytes, subtables.Length);
        int offset = 4 + (8 * subtables.Length);
        foreach ((int platform, int encoding, byte[] subtable) in subtables)
        {
            U16(bytes, platform);
            U16(bytes, encoding);
            U32(bytes, (uint)offset);
            offset += subtable.Length;
        }

        foreach ((_, _, byte[] subtable) in subtables)
        {
            bytes.AddRange(subtable);
        }

        return [.. bytes];
    }

    /// <summary>A format 0 subtable.</summary>
    public static byte[] Format0(IReadOnlyDictionary<int, int> map)
    {
        var bytes = new List<byte>();
        U16(bytes, 0);
        U16(bytes, 262);
        U16(bytes, 0);
        for (int code = 0; code < 256; code++)
        {
            bytes.Add((byte)map.GetValueOrDefault(code));
        }

        return [.. bytes];
    }

    /// <summary>A format 4 subtable from (start, end, idDelta) segments, written in the order given, with idRangeOffset 0; a final 0xFFFF segment is added.</summary>
    public static byte[] Format4(params (int Start, int End, int Delta)[] segments)
    {
        (int Start, int End, int Delta)[] all = [.. segments, (0xFFFF, 0xFFFF, 1)];
        var bytes = new List<byte>();
        U16(bytes, 4);
        U16(bytes, 16 + (8 * all.Length));
        U16(bytes, 0);
        U16(bytes, 2 * all.Length);
        U16(bytes, 0);
        U16(bytes, 0);
        U16(bytes, 0);
        foreach ((_, int end, _) in all)
        {
            U16(bytes, end);
        }

        U16(bytes, 0);
        foreach ((int start, _, _) in all)
        {
            U16(bytes, start);
        }

        foreach ((_, _, int delta) in all)
        {
            U16(bytes, delta & 0xFFFF);
        }

        foreach (var unused in all)
        {
            U16(bytes, 0);
        }

        return [.. bytes];
    }

    /// <summary>A format 12 (or 13) subtable from (start, end, glyph) groups.</summary>
    public static byte[] Format12(int format, params (int Start, int End, int Glyph)[] groups)
    {
        var bytes = new List<byte>();
        U16(bytes, format);
        U16(bytes, 0);
        U32(bytes, (uint)(16 + (12 * groups.Length)));
        U32(bytes, 0);
        U32(bytes, (uint)groups.Length);
        foreach ((int start, int end, int glyph) in groups)
        {
            U32(bytes, (uint)start);
            U32(bytes, (uint)end);
            U32(bytes, (uint)glyph);
        }

        return [.. bytes];
    }

    /// <summary>
    /// A format 2 subtable: single-byte codes map through subheader 0 (first code 0, all 256), and each high byte given maps its low
    /// bytes from <c>First</c> through its own subheader to the given glyphs.
    /// </summary>
    public static byte[] Format2(int[] singleByteGlyphs, params (int High, int First, int[] Glyphs)[] twoByte)
    {
        var keys = new int[256];
        for (int index = 0; index < twoByte.Length; index++)
        {
            keys[twoByte[index].High] = (index + 1) * 8;
        }

        int subHeaderCount = twoByte.Length + 1;
        var arrays = new List<int[]> { singleByteGlyphs };
        arrays.AddRange(twoByte.Select(entry => entry.Glyphs));
        var bytes = new List<byte>();
        U16(bytes, 2);
        U16(bytes, 0);
        U16(bytes, 0);
        foreach (int key in keys)
        {
            U16(bytes, key);
        }

        int subHeaderStart = bytes.Count;
        int arrayStart = subHeaderStart + (8 * subHeaderCount);
        int arrayOffset = arrayStart;
        for (int index = 0; index < subHeaderCount; index++)
        {
            int first = index == 0 ? 0 : twoByte[index - 1].First;
            int rangeOffsetPosition = subHeaderStart + (8 * index) + 6;
            U16(bytes, first);
            U16(bytes, arrays[index].Length);
            U16(bytes, 0);
            U16(bytes, arrayOffset - rangeOffsetPosition);
            arrayOffset += 2 * arrays[index].Length;
        }

        foreach (int[] array in arrays)
        {
            foreach (int glyph in array)
            {
                U16(bytes, glyph);
            }
        }

        byte[] result = [.. bytes];
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)result.Length);
        return result;
    }

    /// <summary>A "post" table of version 2.0 (names are standard Macintosh names when they are one, else strings) or 2.5 (offsets).</summary>
    public static byte[] Post2(int declaredCount, params string?[] names)
    {
        string[] standard = MacStandardNames;
        var bytes = new List<byte>();
        U32(bytes, 0x00020000);
        bytes.AddRange(new byte[28]);
        U16(bytes, declaredCount);
        var strings = new List<string>();
        foreach (string? name in names)
        {
            int index = name is null ? 0 : Array.IndexOf(standard, name);
            if (index < 0)
            {
                index = 258 + strings.Count;
                strings.Add(name!);
            }

            U16(bytes, index);
        }

        foreach (string text in strings)
        {
            bytes.Add((byte)text.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(text));
        }

        return [.. bytes];
    }

    /// <summary>A "post" table of version 2.5: one signed offset per glyph into the standard order.</summary>
    public static byte[] Post25(params sbyte[] offsets)
    {
        var bytes = new List<byte>();
        U32(bytes, 0x00025000);
        bytes.AddRange(new byte[28]);
        U16(bytes, offsets.Length);
        bytes.AddRange(offsets.Select(offset => unchecked((byte)offset)));
        return [.. bytes];
    }

    /// <summary>A "post" table of version 1.0, 3.0 or another, header only.</summary>
    public static byte[] PostHeader(uint version)
    {
        var bytes = new List<byte>();
        U32(bytes, version);
        bytes.AddRange(new byte[28]);
        return [.. bytes];
    }

    /// <summary>A TrueType collection of whole fonts: each font's table offsets are moved to where it lands.</summary>
    public static byte[] Collection(params byte[][] fonts)
    {
        int header = 12 + (4 * fonts.Length);
        var result = new List<byte>();
        U32(result, 0x74746366);
        U16(result, 1);
        U16(result, 0);
        U32(result, (uint)fonts.Length);
        int position = header;
        foreach (byte[] font in fonts)
        {
            U32(result, (uint)position);
            position += font.Length;
        }

        position = header;
        foreach (byte[] font in fonts)
        {
            byte[] moved = (byte[])font.Clone();
            int count = BinaryPrimitives.ReadUInt16BigEndian(moved.AsSpan(4));
            for (int index = 0; index < count; index++)
            {
                Span<byte> offset = moved.AsSpan(12 + (16 * index) + 8, 4);
                BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)position);
            }

            result.AddRange(moved);
            position += font.Length;
        }

        return [.. result];
    }

    /// <summary>Builds the program.</summary>
    public byte[] Build()
    {
        int count = Glyphs.Count;
        var glyf = new List<byte>();
        var offsets = new List<int>();
        foreach (byte[] glyph in Glyphs)
        {
            offsets.Add(glyf.Count);
            glyf.AddRange(glyph);
            while (glyf.Count % 4 != 0)
            {
                glyf.Add(0);
            }
        }

        offsets.Add(glyf.Count);
        var loca = new List<byte>();
        foreach (int offset in offsets)
        {
            if (LongOffsets)
            {
                U32(loca, (uint)offset);
            }
            else
            {
                U16(loca, offset / 2);
            }
        }

        IReadOnlyList<(ushort Advance, short LeftSideBearing)> metrics = Metrics
            ?? [.. Glyphs.Select(glyph => ((ushort)500, glyph.Length >= 10 ? BinaryPrimitives.ReadInt16BigEndian(glyph.AsSpan(2)) : (short)0))];
        int horizontal = HorizontalMetricCount ?? count;
        var hmtx = new List<byte>();
        for (int index = 0; index < metrics.Count; index++)
        {
            if (index < horizontal)
            {
                U16(hmtx, metrics[index].Advance);
            }

            I16(hmtx, metrics[index].LeftSideBearing);
        }

        var head = new List<byte>();
        U32(head, 0x00010000);
        U32(head, 0x00010000);
        U32(head, 0);
        U32(head, 0x5F0F3CF5);
        U16(head, 0x000B);
        U16(head, 1000);
        head.AddRange(new byte[16]);
        I16(head, 0);
        I16(head, 0);
        I16(head, 1000);
        I16(head, 1000);
        U16(head, 0);
        U16(head, 8);
        I16(head, 2);
        I16(head, LongOffsets ? 1 : 0);
        I16(head, 0);

        var hhea = new List<byte>();
        U32(hhea, 0x00010000);
        I16(hhea, 800);
        I16(hhea, -200);
        I16(hhea, 0);
        U16(hhea, 1000);
        hhea.AddRange(new byte[22]);
        U16(hhea, horizontal);

        var maxp = new List<byte>();
        U32(maxp, 0x00005000);
        U16(maxp, count);

        var name = new List<byte>();
        byte[] text = Encoding.BigEndianUnicode.GetBytes(Name);
        U16(name, 0);
        U16(name, 1);
        U16(name, 18);
        U16(name, 3);
        U16(name, 1);
        U16(name, 0x409);
        U16(name, 6);
        U16(name, text.Length);
        U16(name, 0);
        name.AddRange(text);

        var tables = new Dictionary<string, byte[]>
        {
            ["cmap"] = Cmap ?? CmapTable((3, 1, Format4())),
            ["glyf"] = [.. glyf],
            ["head"] = [.. head],
            ["hhea"] = [.. hhea],
            ["hmtx"] = [.. hmtx],
            ["loca"] = [.. loca],
            ["maxp"] = [.. maxp],
            ["name"] = [.. name],
            ["post"] = Post ?? PostHeader(0x00030000),
        };
        foreach ((string tag, byte[]? table) in Overrides)
        {
            if (table is null)
            {
                tables.Remove(tag);
            }
            else
            {
                tables[tag] = table;
            }
        }

        return Sfnt(tables, Version);
    }

    /// <summary>An sfnt file from tables, in tag order, each padded to four bytes; checksums are left 0.</summary>
    public static byte[] Sfnt(IReadOnlyDictionary<string, byte[]> tables, uint version = 0x00010000)
    {
        string[] tags = [.. tables.Keys.Order(StringComparer.Ordinal)];
        var result = new List<byte>();
        U32(result, version);
        U16(result, tags.Length);
        U16(result, 0);
        U16(result, 0);
        U16(result, 0);
        int offset = 12 + (16 * tags.Length);
        foreach (string tag in tags)
        {
            result.AddRange(Encoding.ASCII.GetBytes(tag));
            U32(result, 0);
            U32(result, (uint)offset);
            U32(result, (uint)tables[tag].Length);
            offset += (tables[tag].Length + 3) & ~3;
        }

        foreach (string tag in tags)
        {
            result.AddRange(tables[tag]);
            while (result.Count % 4 != 0)
            {
                result.Add(0);
            }
        }

        return [.. result];
    }

    private static readonly string[] MacStandardNames =
    [
        ".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand",
        "quotesingle", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two",
        "three", "four", "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
    ];

    private static void Header(List<byte> bytes, int contours, IEnumerable<(int X, int Y)> points)
    {
        (int X, int Y)[] all = [.. points];
        I16(bytes, contours);
        I16(bytes, all.Length == 0 ? 0 : all.Min(p => p.X));
        I16(bytes, all.Length == 0 ? 0 : all.Min(p => p.Y));
        I16(bytes, all.Length == 0 ? 0 : all.Max(p => p.X));
        I16(bytes, all.Length == 0 ? 0 : all.Max(p => p.Y));
    }

    private static void U16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void I16(List<byte> bytes, int value) => U16(bytes, value & 0xFFFF);

    private static void U32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }
}
