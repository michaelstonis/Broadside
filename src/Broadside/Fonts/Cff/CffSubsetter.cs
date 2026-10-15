using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Broadside.Graphics;

namespace Broadside.Fonts.Cff;

/// <summary>
/// Reduces a CFF program (bare, or the <c>CFF </c> table of an OpenType program) to the glyphs a text uses, <c>.notdef</c> and the
/// components their <c>endchar</c> accents name, as a CID-keyed font whose charset maps each new glyph id back to its original glyph
/// id as the CID (so a CIDFontType0 font's CIDs are the original glyph ids, whatever the subset keeps).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4.2 (a CIDFontType0 program with CIDFont operators maps CIDs to glyphs through its charset), §9.9 (Table 124:
/// <c>FontFile3</c> <c>CIDFontType0C</c>) and §9.9.2 (subset fonts);
/// Adobe Technical Note #5176 (CFF: §8 Top DICT, §10 Private DICT, §13 charset, §16 Local/Global Subrs, §18 CID-keyed fonts, §19
/// FDSelect) and #5177 (Type 2 charstrings: §4.3 hints, §4.7 subroutines, Appendix C <c>endchar</c> accents).
/// </para>
/// <para>
/// Subroutines are inlined: every glyph kept is walked through the subroutines it calls (counting stem hints so a
/// <c>hintmask</c>'s mask bytes are skipped exactly as the interpreter does), and its charstring is written with each
/// <c>callsubr</c>/<c>callgsubr</c> and its number operand replaced by the subroutine's bytes without its <c>return</c>. Every
/// operator then sees the same operands, so the outlines cannot change, and the subset has no subroutines, so no call can dangle and
/// no subroutine the subset does not use costs anything: pruning in place would keep an offset per subroutine of the original
/// (tens of thousands in CJK fonts), and renumbering would rewrite the same operands under the same conditions. A charstring the
/// walk cannot follow statically (arithmetic, storage or conditional operators that may compute a subroutine number, or one past
/// the nesting, work or 65,535-byte limits) instead keeps every charstring as it is with every subroutine, numbering unchanged.
/// Only the Font DICTs a kept glyph uses are written, renumbered through a format 3 FDSelect.
/// </para>
/// <para>
/// A font is written CID-keyed with Registry <c>Adobe</c>, Ordering <c>Identity</c> (its CIDs are the original glyph ids, whatever
/// its own ROS); a name-keyed one gets one Font DICT holding its FontMatrix and Private DICT and an identity Top DICT FontMatrix
/// (#5014 §4.2). Top and Font DICT entries that name strings (version, Notice, FullName, FontName and the like) are dropped, so the
/// String INDEX holds only the ROS strings. A name-keyed font whose kept glyphs use <c>endchar</c> accents (which name
/// their components by StandardEncoding code, which a CID-keyed font cannot resolve) stays name-keyed, keeps its glyph ids (the
/// glyphs not kept become an empty <c>endchar</c>) and its charset, so its CIDs are still its glyph ids (§9.7.4.2). The output
/// depends only on the program and the glyph set.
/// </para>
/// </remarks>
internal static class CffSubsetter
{
    private const int OpCharset = 15;
    private const int OpEncoding = 16;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;
    private const int OpSubrs = 19;
    private const int OpUniqueId = 13;
    private const int OpXuid = 14;
    private const int OpCharstringType = 0x0C06;
    private const int OpFontMatrix = 0x0C07;
    private const int OpSyntheticBase = 0x0C14;
    private const int OpRos = 0x0C1E;
    private const int OpCidCount = 0x0C22;
    private const int OpUidBase = 0x0C23;
    private const int OpFdArray = 0x0C24;
    private const int OpFdSelect = 0x0C25;

    /// <summary>The subroutine nesting limit (Adobe Technical Note #5177, Appendix B).</summary>
    private const int MaxDepth = 10;

    /// <summary>Charstring bytes walked per glyph, subroutines included, before the walk gives up and keeps every subroutine.</summary>
    private const int Budget = 1 << 20;

    private static readonly int[] TopDropped = [OpCharset, OpEncoding, OpCharStrings, OpPrivate, OpUniqueId, OpXuid, OpCharstringType, OpSyntheticBase, OpRos, OpCidCount, OpUidBase, OpFdArray, OpFdSelect];

    /// <summary>The Top DICT entries a CID-keyed subset keeps: FontBBox, isFixedPitch, ItalicAngle, UnderlinePosition, UnderlineThickness, PaintType, StrokeWidth and the CIDFont version, revision and type (5176 Tables 9 and 10).</summary>
    private static readonly int[] TopKept = [5, 0x0C01, 0x0C02, 0x0C03, 0x0C04, 0x0C05, 0x0C08, 0x0C1F, 0x0C20, 0x0C21];

    /// <summary>Writes the subset of a bare CFF program that draws <paramref name="glyphs"/>, glyph 0 and their accent components.</summary>
    /// <param name="program">The CFF program (Type 2 charstrings).</param>
    /// <param name="glyphs">The original glyph ids; ids past the glyph count are ignored.</param>
    /// <returns>The subset, or <see langword="null"/> when the program is not a usable CFF font with Type 2 charstrings.</returns>
    public static SubsetProgram? Subset(ReadOnlyMemory<byte> program, IEnumerable<int> glyphs)
    {
        if (CffFont.Read(program, new FontProgramContext()) is not { OutlinesUnsupported: null } font || font.GlyphCount == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> data = font.Data.Span;
        CffIndex names = CffIndex.Read(data, data[2], out _);
        CffIndex tops = CffIndex.Read(data, names.End, out _);
        if (tops.Count == 0)
        {
            return null;
        }

        List<Entry> top = ReadDict(tops.Get(data, 0));

        // The glyphs kept: .notdef, those asked for and, transitively, the components of their endchar accents.
        int glyphCount = font.GlyphCount;
        var kept = new SortedSet<int> { 0 };
        foreach (int glyph in glyphs)
        {
            if ((uint)glyph < (uint)glyphCount)
            {
                _ = kept.Add(glyph);
            }
        }

        var reach = new Reach(font);
        var pending = new Stack<int>(kept);
        bool accents = false;
        while (pending.TryPop(out int glyph))
        {
            foreach (int code in reach.Walk(glyph))
            {
                accents = true;
                int component = font.GetStandardCodeGlyph(code);
                if (component > 0 && kept.Add(component))
                {
                    pending.Push(component);
                }
            }
        }

        if (!font.IsCidKeyed && accents)
        {
            return NameKeyed(font, top, kept, reach);
        }

        // The Font DICTs the kept glyphs use, in FDArray order, and their Private DICTs.
        int[] order = [.. kept];
        List<Entry>[] fontDicts;
        byte[][] fontDictBytes;
        if (font.IsCidKeyed)
        {
            CffIndex fdArray = CffIndex.Read(data, Value(top, OpFdArray, 0), out string? problem);
            if (problem is not null || fdArray.Count == 0)
            {
                return null;
            }

            fontDicts = new List<Entry>[fdArray.Count];
            fontDictBytes = new byte[fdArray.Count][];
            for (int index = 0; index < fdArray.Count; index++)
            {
                ReadOnlySpan<byte> dict = fdArray.Get(data, index);
                fontDicts[index] = ReadDict(dict);
                fontDictBytes[index] = Raw(dict, fontDicts[index].Where(entry => entry.Operator == OpFontMatrix));
            }
        }
        else
        {
            fontDicts = [[]];
            fontDictBytes = [];
        }

        int[] usedDicts = [.. order.Select(font.GetFontDict).Distinct().Order()];
        var newDict = new Dictionary<int, int>();
        for (int index = 0; index < usedDicts.Length; index++)
        {
            newDict[usedDicts[index]] = index;
        }

        // Strings: only the ROS's (SIDs 391 and 392); no copied entry names a string.
        bool flat = reach.Flattened;
        byte[] nameIndex = Index([Encoding.Latin1.GetBytes(font.Name ?? "Font")]);
        byte[] stringIndex = Index(["Adobe"u8.ToArray(), "Identity"u8.ToArray()]);
        byte[] globalIndex = flat ? Index([]) : Subroutines(data, font.GlobalSubrs, reach.Global, reach.All) ?? Index([]);
        byte[] charset = Charset(order);
        byte[] fdSelect = FdSelect(order, font, newDict);
        var glyphPrograms = new List<byte[]>(order.Length);
        foreach (int glyph in order)
        {
            glyphPrograms.Add(flat ? reach.Flat[glyph] : font.CharStrings.Get(data, glyph).ToArray());
        }

        byte[] charStrings = Index(glyphPrograms);
        byte[] topBytes = tops.Get(data, 0).ToArray();
        int cidCount = glyphCount;

        // Each used Font DICT's Private DICT, followed by its pruned local subroutines.
        var privateBlocks = new List<(byte[] Dict, byte[]? Subrs)>();
        foreach (int dict in usedDicts)
        {
            ReadOnlySpan<byte> privateDict = font.IsCidKeyed ? PrivateBytes(data, fontDicts[dict]) : PrivateBytes(data, top);
            (CffPrivate values, bool[] used) = reach.Local[dict];
            byte[]? subrs = flat ? null : Subroutines(data, values.Subrs, used, reach.All);
            privateBlocks.Add((PrivateDict(privateDict, values, subrs is not null), subrs));
        }

        byte[] TopDict(int charsetAt, int fdSelectAt, int charStringsAt, int fdArrayAt)
        {
            var dict = new List<byte>();
            Int32(dict, 391);
            Int32(dict, 392);
            Int32(dict, 0);
            dict.AddRange((ReadOnlySpan<byte>)[12, 30]);
            dict.AddRange(Raw(topBytes, top.Where(entry => TopKept.Contains(entry.Operator) || (font.IsCidKeyed && entry.Operator == OpFontMatrix))));
            if (!font.IsCidKeyed)
            {
                // FontMatrix [1 0 0 1 0 0]: the font's own matrix moves to its Font DICT.
                dict.AddRange((ReadOnlySpan<byte>)[140, 139, 139, 140, 139, 139, 12, 7]);
            }

            Int32(dict, cidCount);
            dict.AddRange((ReadOnlySpan<byte>)[12, 34]);
            Int32(dict, charsetAt);
            dict.Add(OpCharset);
            Int32(dict, fdSelectAt);
            dict.AddRange((ReadOnlySpan<byte>)[12, 37]);
            Int32(dict, charStringsAt);
            dict.Add(OpCharStrings);
            Int32(dict, fdArrayAt);
            dict.AddRange((ReadOnlySpan<byte>)[12, 36]);
            return [.. dict];
        }

        byte[] FontDict(int dict, int privateSize, int privateAt)
        {
            var bytes = new List<byte>();
            if (font.IsCidKeyed)
            {
                bytes.AddRange(fontDictBytes[dict]);
            }
            else
            {
                // The matrix the parser read (the Top DICT's, or the 0.001 default), stated: without one a Font DICT would inherit the identity.
                Matrix matrix = font.FontMatrix;
                foreach (double value in (ReadOnlySpan<double>)[matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F])
                {
                    Number(bytes, value);
                }

                bytes.AddRange((ReadOnlySpan<byte>)[12, 7]);
            }

            Int32(bytes, privateSize);
            Int32(bytes, privateAt);
            bytes.Add(OpPrivate);
            return [.. bytes];
        }

        int topLength = Index([TopDict(0, 0, 0, 0)]).Length;
        int charsetAt = 4 + nameIndex.Length + topLength + stringIndex.Length + globalIndex.Length;
        int fdSelectAt = charsetAt + charset.Length;
        int charStringsAt = fdSelectAt + fdSelect.Length;
        int fdArrayAt = charStringsAt + charStrings.Length;
        int fdArrayLength = Index([.. usedDicts.Select(dict => FontDict(dict, 0, 0))]).Length;
        var fdArrayDicts = new List<byte[]>();
        int privateAt = fdArrayAt + fdArrayLength;
        for (int index = 0; index < usedDicts.Length; index++)
        {
            (byte[] dict, byte[]? subrs) = privateBlocks[index];
            fdArrayDicts.Add(FontDict(usedDicts[index], dict.Length, privateAt));
            privateAt += dict.Length + (subrs?.Length ?? 0);
        }

        using var output = new MemoryStream();
        output.Write([1, 0, 4, 4]);
        output.Write(nameIndex);
        output.Write(Index([TopDict(charsetAt, fdSelectAt, charStringsAt, fdArrayAt)]));
        output.Write(stringIndex);
        output.Write(globalIndex);
        output.Write(charset);
        output.Write(fdSelect);
        output.Write(charStrings);
        output.Write(Index(fdArrayDicts));
        foreach ((byte[] dict, byte[]? subrs) in privateBlocks)
        {
            output.Write(dict);
            output.Write(subrs);
        }

        return new SubsetProgram(output.ToArray(), order);
    }

    /// <summary>
    /// The subset of a name-keyed font whose kept glyphs use <c>endchar</c> accents: glyph ids, charset and Top DICT kept (the
    /// encoding becomes StandardEncoding, which the accents name their components in), glyphs not kept emptied.
    /// </summary>
    private static SubsetProgram NameKeyed(CffFont font, List<Entry> top, SortedSet<int> kept, Reach reach)
    {
        ReadOnlySpan<byte> data = font.Data.Span;
        CffIndex tops = CffIndex.Read(data, CffIndex.Read(data, data[2], out _).End, out _);
        var strings = new List<byte[]>();
        for (int index = 0; index < font.Strings.Count; index++)
        {
            strings.Add(font.Strings.Get(data, index).ToArray());
        }

        byte[] nameIndex = Index([Encoding.Latin1.GetBytes(font.Name ?? "Font")]);
        byte[] stringIndex = Index(strings);
        bool flat = reach.Flattened;
        byte[] globalIndex = flat ? Index([]) : Subroutines(data, font.GlobalSubrs, reach.Global, reach.All) ?? Index([]);
        var charset = new List<byte> { 0 };
        for (int glyph = 1; glyph < font.GlyphCount; glyph++)
        {
            ushort sid = glyph < font.Charset.Length ? font.Charset[glyph] : (ushort)0;
            charset.Add((byte)(sid >> 8));
            charset.Add((byte)sid);
        }

        var glyphs = new List<byte[]>();
        for (int glyph = 0; glyph < font.GlyphCount; glyph++)
        {
            glyphs.Add(!kept.Contains(glyph) ? [14] : flat ? reach.Flat[glyph] : font.CharStrings.Get(data, glyph).ToArray());
        }

        byte[] charStrings = Index(glyphs);
        (CffPrivate values, bool[] used) = reach.Local[0];
        byte[]? subrs = flat ? null : Subroutines(data, values.Subrs, used, reach.All);
        byte[] privateDict = PrivateDict(PrivateBytes(data, top), values, subrs is not null);
        byte[] topBytes = tops.Get(data, 0).ToArray();

        byte[] TopDict(int charsetAt, int charStringsAt, int privateAt)
        {
            var dict = new List<byte>(Raw(topBytes, top.Where(entry => !TopDropped.Contains(entry.Operator))));
            Int32(dict, charsetAt);
            dict.Add(OpCharset);
            Int32(dict, charStringsAt);
            dict.Add(OpCharStrings);
            Int32(dict, privateDict.Length);
            Int32(dict, privateAt);
            dict.Add(OpPrivate);
            return [.. dict];
        }

        int charsetAt = 4 + nameIndex.Length + Index([TopDict(0, 0, 0)]).Length + stringIndex.Length + globalIndex.Length;
        int charStringsAt = charsetAt + charset.Count;
        int privateAt = charStringsAt + charStrings.Length;
        using var output = new MemoryStream();
        output.Write([1, 0, 4, 4]);
        output.Write(nameIndex);
        output.Write(Index([TopDict(charsetAt, charStringsAt, privateAt)]));
        output.Write(stringIndex);
        output.Write(globalIndex);
        output.Write([.. charset]);
        output.Write(charStrings);
        output.Write(privateDict);
        output.Write(subrs);
        return new SubsetProgram(output.ToArray(), [.. Enumerable.Range(0, font.GlyphCount)]);
    }

    /// <summary>A format 2 charset (5176 §13): each new glyph's CID, its original glyph id, as ranges of consecutive ids.</summary>
    private static byte[] Charset(int[] order)
    {
        var charset = new List<byte> { 2 };
        for (int index = 1; index < order.Length;)
        {
            int last = index;
            while (last + 1 < order.Length && order[last + 1] == order[last] + 1 && last - index < ushort.MaxValue)
            {
                last++;
            }

            Card16(charset, order[index]);
            Card16(charset, last - index);
            index = last + 1;
        }

        return [.. charset];
    }

    /// <summary>A format 3 FDSelect (5176 §19): ranges of new glyph ids with their renumbered Font DICT, then the sentinel.</summary>
    private static byte[] FdSelect(int[] order, CffFont font, Dictionary<int, int> newDict)
    {
        var ranges = new List<(int First, int Dict)>();
        for (int index = 0; index < order.Length; index++)
        {
            int dict = newDict[font.GetFontDict(order[index])];
            if (ranges.Count == 0 || ranges[^1].Dict != dict)
            {
                ranges.Add((index, dict));
            }
        }

        var select = new List<byte> { 3 };
        Card16(select, ranges.Count);
        foreach ((int first, int dict) in ranges)
        {
            Card16(select, first);
            select.Add((byte)dict);
        }

        Card16(select, order.Length);
        return [.. select];
    }

    /// <summary>
    /// A subroutine INDEX with the numbering of <paramref name="subrs"/>: reachable entries copied, the others a one-byte
    /// <c>return</c>, trailing unreachable ones dropped when the shorter count has the same bias (5177 §4.7);
    /// <see langword="null"/> when none is reachable.
    /// </summary>
    private static byte[]? Subroutines(ReadOnlySpan<byte> data, CffIndex subrs, bool[] used, bool all)
    {
        int last = all ? subrs.Count - 1 : Array.LastIndexOf(used, true);
        if (last < 0)
        {
            return null;
        }

        int count = CffFont.Bias(last + 1) == CffFont.Bias(subrs.Count) ? last + 1 : subrs.Count;
        var items = new List<byte[]>(count);
        for (int index = 0; index < count; index++)
        {
            items.Add(all || used[index] ? subrs.Get(data, index).ToArray() : [11]);
        }

        return Index(items);
    }

    /// <summary>
    /// A Private DICT: the original's entries but <c>Subrs</c>, then defaultWidthX and nominalWidthX as the parser read them (the
    /// last entry wins, so a damaged original gives its glyphs the same widths), then <c>Subrs</c> pointing just past the DICT when it
    /// has subroutines.
    /// </summary>
    private static byte[] PrivateDict(ReadOnlySpan<byte> original, CffPrivate values, bool subrs)
    {
        var dict = new List<byte>(Raw(original, ReadDict(original).Where(entry => entry.Operator != OpSubrs)));
        foreach ((double value, byte op) in (ReadOnlySpan<(double, byte)>)[(values.DefaultWidthX, 20), (values.NominalWidthX, 21)])
        {
            if (double.IsFinite(value))
            {
                Number(dict, value);
                dict.Add(op);
            }
        }
        if (subrs)
        {
            Int32(dict, dict.Count + 6);
            dict.Add(OpSubrs);
        }

        return [.. dict];
    }

    /// <summary>
    /// The bytes of the Private DICT a Top DICT or Font DICT points at (its last Private entry), as the parser reads them: empty
    /// when the pointer leaves the program, the part present when the DICT runs past its end.
    /// </summary>
    private static ReadOnlySpan<byte> PrivateBytes(ReadOnlySpan<byte> data, List<Entry> dict)
    {
        if (dict.LastOrDefault(entry => entry.Operator == OpPrivate) is { End: > 0 } entry)
        {
            // A missing operand reads as 0, as the parser reads it.
            int size = entry.Operands.Length > 0 ? entry.Operands[0] : 0;
            int offset = entry.Operands.Length > 1 ? entry.Operands[1] : 0;
            if (size >= 0 && offset >= 0 && offset <= data.Length)
            {
                return data.Slice(offset, Math.Min(size, data.Length - offset));
            }
        }

        return [];
    }

    private static int Value(List<Entry> dict, int op, int index) =>
        dict.LastOrDefault(entry => entry.Operator == op) is { End: > 0 } entry ? (index < entry.Operands.Length ? entry.Operands[index] : 0) : -1;

    /// <summary>The bytes of <paramref name="entries"/> (operands and operator) as they are in <paramref name="dict"/>.</summary>
    private static byte[] Raw(ReadOnlySpan<byte> dict, IEnumerable<Entry> entries)
    {
        var bytes = new List<byte>();
        foreach (Entry entry in entries)
        {
            bytes.AddRange(dict[entry.Start..entry.End]);
        }

        return [.. bytes];
    }

    /// <summary>
    /// Splits a DICT (5176 §4) into its entries as the parser reads them (<see cref="CffDictReader"/>, so a damaged DICT means the
    /// same when its entries are copied): each operator with the byte range of its operands and its operands as integers.
    /// </summary>
    private static List<Entry> ReadDict(ReadOnlySpan<byte> dict)
    {
        var entries = new List<Entry>();
        var reader = new CffDictReader(dict);
        int start = 0;
        while (reader.Next())
        {
            int[] operands = new int[reader.OperandCount];
            for (int index = 0; index < operands.Length; index++)
            {
                double value = reader[index];
                operands[index] = double.IsNaN(value) ? 0 : (int)Math.Clamp(value, int.MinValue, int.MaxValue);
            }

            int op = reader.Operator >= 1200 ? 0x0C00 | (reader.Operator - 1200) : reader.Operator;
            entries.Add(new Entry(op, start, reader.Position, operands));
            start = reader.Position;
        }

        return entries;
    }

    /// <summary>An INDEX (5176 §5) with the smallest offset size its data needs.</summary>
    private static byte[] Index(List<byte[]> items)
    {
        if (items.Count == 0)
        {
            return [0, 0];
        }

        long total = 1;
        foreach (byte[] item in items)
        {
            total += item.Length;
        }

        int offSize = total <= 0xFF ? 1 : total <= 0xFFFF ? 2 : total <= 0xFFFFFF ? 3 : 4;
        byte[] index = new byte[3 + ((items.Count + 1) * offSize) + (int)(total - 1)];
        BinaryPrimitives.WriteUInt16BigEndian(index, (ushort)items.Count);
        index[2] = (byte)offSize;
        int offset = 1;
        int data = 3 + ((items.Count + 1) * offSize);
        for (int item = 0; item <= items.Count; item++)
        {
            for (int shift = (offSize - 1) * 8, at = 3 + (item * offSize); shift >= 0; shift -= 8, at++)
            {
                index[at] = (byte)(offset >> shift);
            }

            if (item < items.Count)
            {
                items[item].CopyTo(index, data + offset - 1);
                offset += items[item].Length;
            }
        }

        return index;
    }

    /// <summary>A five-byte DICT integer (operator 29), so a DICT's size does not depend on the offsets it holds.</summary>
    private static void Int32(List<byte> dict, int value)
    {
        dict.Add(29);
        dict.Add((byte)(value >> 24));
        dict.Add((byte)(value >> 16));
        dict.Add((byte)(value >> 8));
        dict.Add((byte)value);
    }

    /// <summary>A DICT number: a five-byte integer when it is one, else a real.</summary>
    private static void Number(List<byte> dict, double value)
    {
        if (value == Math.Floor(value) && value is >= int.MinValue and <= int.MaxValue)
        {
            Int32(dict, (int)value);
        }
        else
        {
            Real(dict, value);
        }
    }

    /// <summary>A DICT real (operator 30, 5176 Table 5): the shortest round-trip decimal as nibbles.</summary>
    private static void Real(List<byte> dict, double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture).Replace("E-", "c", StringComparison.Ordinal).Replace("E+", "b", StringComparison.Ordinal);
        var nibbles = new List<int>();
        foreach (char character in text)
        {
            nibbles.Add(character switch
            {
                >= '0' and <= '9' => character - '0',
                '.' => 0xA,
                'b' => 0xB,
                'c' => 0xC,
                _ => 0xE,
            });
        }

        nibbles.Add(0xF);
        if (nibbles.Count % 2 != 0)
        {
            nibbles.Add(0xF);
        }

        dict.Add(30);
        for (int index = 0; index < nibbles.Count; index += 2)
        {
            dict.Add((byte)((nibbles[index] << 4) | nibbles[index + 1]));
        }
    }

    private static void Card16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    /// <summary>A DICT entry: its operator (escaped ones as 0x0C00 | second byte), the byte range of operands and operator, and its integer operands.</summary>
    private readonly record struct Entry(int Operator, int Start, int End, int[] Operands);

    /// <summary>
    /// Which subroutines the kept glyphs reach, and their charstrings with them inlined: a walk of each charstring that follows <c>callsubr</c> and <c>callgsubr</c> with the
    /// operand stack and stem count the interpreter has at that point (5177 §4.3, §4.7).
    /// </summary>
    private sealed class Reach(CffFont font)
    {
        /// <summary>The longest charstring (Adobe Technical Note #5177, Appendix B).</summary>
        private const int MaxCharString = 65535;

        private readonly double[] _stack = new double[48];
        private readonly int[] _tokens = new int[48];
        private readonly List<int> _accents = [];
        private readonly List<byte> _output = [];
        private int _count;
        private int _stems;
        private int _budget;
        private int _lastToken;
        private bool _flat = true;

        private enum Result
        {
            Ended,
            Return,
            End,
            Fail,
        }

        /// <summary>Gets the global subroutines reached.</summary>
        public bool[] Global { get; } = new bool[font.GlobalSubrs.Count];

        /// <summary>Gets each Font DICT's Private DICT values and the local subroutines reached, by FDArray index.</summary>
        public Dictionary<int, (CffPrivate Private, bool[] Used)> Local { get; } = [];

        /// <summary>Gets each walked glyph's charstring with its subroutine calls inlined.</summary>
        public Dictionary<int, byte[]> Flat { get; } = [];

        /// <summary>Gets a value indicating whether some charstring could not be followed, so every subroutine is kept.</summary>
        public bool All { get; private set; }

        /// <summary>Gets a value indicating whether every walked glyph has an inlined charstring (no subroutine is needed).</summary>
        public bool Flattened => _flat && !All;

        /// <summary>
        /// Walks a glyph's charstring, inlining the subroutines it calls; returns the StandardEncoding codes of the components its
        /// <c>endchar</c> accent names.
        /// </summary>
        public List<int> Walk(int glyph)
        {
            _accents.Clear();
            _output.Clear();
            _count = 0;
            _stems = 0;
            _budget = Budget;
            _lastToken = -1;
            int dict = font.GetFontDict(glyph);
            if (!Local.TryGetValue(dict, out (CffPrivate Private, bool[] Used) local))
            {
                CffPrivate value = font.GetPrivate(glyph);
                local = (value, new bool[value.Subrs.Count]);
                Local[dict] = local;
            }

            if (Run(font.CharStrings.Get(font.Data.Span, glyph), local.Private, local.Used, 0) == Result.Fail)
            {
                All = true;
            }

            _flat &= _output.Count <= MaxCharString;
            if (_flat)
            {
                Flat[glyph] = [.. _output];
            }

            return _accents;
        }

        private Result Run(ReadOnlySpan<byte> code, CffPrivate values, bool[] local, int depth)
        {
            int position = 0;
            while (position < code.Length)
            {
                if (--_budget < 0)
                {
                    return Result.Fail;
                }

                byte b0 = code[position];
                if (b0 >= 32 || b0 == 28)
                {
                    int size = b0 switch
                    {
                        28 => 3,
                        <= 246 => 1,
                        <= 254 => 2,
                        _ => 5,
                    };
                    if (position + size > code.Length || _count == _stack.Length)
                    {
                        return Result.Fail;
                    }

                    _stack[_count] = b0 switch
                    {
                        28 => BinaryPrimitives.ReadInt16BigEndian(code[(position + 1)..]),
                        <= 246 => b0 - 139,
                        <= 250 => ((b0 - 247) * 256) + code[position + 1] + 108,
                        <= 254 => (-(b0 - 251) * 256) - code[position + 1] - 108,
                        _ => BinaryPrimitives.ReadInt32BigEndian(code[(position + 1)..]) / 65536.0,
                    };
                    _tokens[_count++] = _lastToken = _output.Count;
                    _output.AddRange(code.Slice(position, size));
                    position += size;
                    continue;
                }

                int start = position;
                position++;
                switch (b0)
                {
                    case 1 or 3 or 18 or 23: // hstem, vstem, hstemhm, vstemhm
                        _stems += _count / 2;
                        _count = 0;
                        break;
                    case 19 or 20: // hintmask, cntrmask: an implicit vstemhm, then one mask bit per stem
                        _stems += _count / 2;
                        _count = 0;
                        position = Math.Min(position + ((_stems + 7) / 8), code.Length);
                        break;
                    case 10 or 29: // callsubr, callgsubr: inlined, without the number operand and the subroutine's return
                        CffIndex subrs = b0 == 10 ? values.Subrs : font.GlobalSubrs;
                        double operand = _count > 0 ? _stack[--_count] : double.NaN;
                        int number = (int)operand + (b0 == 10 ? values.SubrBias : font.GlobalSubrBias);
                        if (operand != Math.Floor(operand) || (uint)number >= (uint)subrs.Count || depth >= MaxDepth)
                        {
                            return Result.Fail;
                        }

                        // The operand is the last number written (no operator between a push and a call clears or computes it).
                        int token = _tokens[_count];
                        if (token == _lastToken && token >= 0)
                        {
                            _output.RemoveRange(token, _output.Count - token);
                        }
                        else
                        {
                            _flat = false;
                        }

                        _lastToken = -1;
                        (b0 == 10 ? local : Global)[number] = true;
                        Result result = Run(subrs.Get(font.Data.Span, number), values, local, depth + 1);
                        if (result is Result.End or Result.Fail)
                        {
                            return result;
                        }

                        continue;
                    case 11: // return
                        return Result.Return;
                    case 14: // endchar, with an accent: adx ady bchar achar
                        if (_count >= 4)
                        {
                            _accents.Add((int)_stack[_count - 2]);
                            _accents.Add((int)_stack[_count - 1]);
                        }

                        _output.Add(14);
                        return Result.End;
                    case 12:
                        int escaped = position < code.Length ? code[position++] : -1;
                        if (escaped is not (0 or 34 or 35 or 36 or 37))
                        {
                            // Arithmetic, storage, conditionals and reserved operators: what follows cannot be known without running it.
                            return Result.Fail;
                        }

                        _count = 0;
                        break;
                    default: // path construction and reserved operators clear the stack
                        _count = 0;
                        break;
                }

                _output.AddRange(code[start..position]);
                _lastToken = -1;
            }

            return Result.Ended;
        }
    }
}
