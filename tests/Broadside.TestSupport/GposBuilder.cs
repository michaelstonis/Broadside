namespace Broadside.TestSupport;

/// <summary>
/// Builds the subtables of a <c>GPOS</c> table (OpenType specification, "GPOS — Glyph Positioning Table"). The table itself has the
/// same header, script, feature and lookup lists as GSUB, so <see cref="GsubBuilder"/> assembles it; contextual positioning (types
/// 7 and 8) uses GSUB's context formats (<see cref="GsubBuilder.ContextGlyphs"/> and the others) and extension (type 9)
/// <see cref="GsubBuilder.Extension"/>. Values are in font design units; an anchor is a format 1 Anchor table.
/// </summary>
public static class GposBuilder
{
    /// <summary>ValueFormat bit: horizontal placement.</summary>
    public const int XPlacement = 0x0001;

    /// <summary>ValueFormat bit: vertical placement.</summary>
    public const int YPlacement = 0x0002;

    /// <summary>ValueFormat bit: horizontal advance.</summary>
    public const int XAdvance = 0x0004;

    /// <summary>Single adjustment format 1: one ValueRecord for every covered glyph.</summary>
    public static OpenTypeNode Adjustment(int format, int[] value, params int[] glyphs) =>
        new OpenTypeNode().U16(1).Offset(GsubBuilder.Coverage(glyphs)).U16(format).U16(value);

    /// <summary>Single adjustment format 2: a ValueRecord per covered glyph.</summary>
    public static OpenTypeNode SingleEach(int format, IReadOnlyDictionary<int, int[]> values)
    {
        int[] keys = [.. values.Keys.Order()];
        var node = new OpenTypeNode().U16(2).Offset(GsubBuilder.Coverage(keys)).U16(format, keys.Length);
        foreach (int key in keys)
        {
            node.U16(values[key]);
        }

        return node;
    }

    /// <summary>Pair adjustment format 1: pairs of glyphs with a ValueRecord for each.</summary>
    public static OpenTypeNode Pairs(int format1, int format2, params (int First, int Second, int[] Value1, int[] Value2)[] pairs)
    {
        var byFirst = pairs.GroupBy(pair => pair.First).OrderBy(group => group.Key).ToArray();
        var node = new OpenTypeNode().U16(1).Offset(GsubBuilder.Coverage([.. byFirst.Select(group => group.Key)])).U16(format1, format2, byFirst.Length);
        foreach (var group in byFirst)
        {
            var set = new OpenTypeNode().U16(group.Count());
            foreach ((int _, int second, int[] value1, int[] value2) in group.OrderBy(pair => pair.Second))
            {
                set.U16(second).U16(value1).U16(value2);
            }

            node.Offset(set);
        }

        return node;
    }

    /// <summary>Pair adjustment format 2: class pairs; <paramref name="records"/>[class1][class2] holds Value1 then Value2.</summary>
    public static OpenTypeNode ClassPairs(int format1, int format2, int[] coverage, IReadOnlyDictionary<int, int> classes1, IReadOnlyDictionary<int, int> classes2, int[][][] records)
    {
        var node = new OpenTypeNode().U16(2).Offset(GsubBuilder.Coverage(coverage)).U16(format1, format2)
            .Offset(GsubBuilder.ClassDef(classes1)).Offset(GsubBuilder.ClassDef(classes2)).U16(records.Length, records[0].Length);
        foreach (int[][] row in records)
        {
            foreach (int[] record in row)
            {
                node.U16(record);
            }
        }

        return node;
    }

    /// <summary>Cursive attachment format 1: each glyph's entry and exit anchors (null for none).</summary>
    public static OpenTypeNode Cursive(params (int Glyph, (int X, int Y)? Entry, (int X, int Y)? Exit)[] glyphs)
    {
        var sorted = glyphs.OrderBy(glyph => glyph.Glyph).ToArray();
        var node = new OpenTypeNode().U16(1).Offset(GsubBuilder.Coverage([.. sorted.Select(glyph => glyph.Glyph)])).U16(sorted.Length);
        foreach ((int _, (int X, int Y)? entry, (int X, int Y)? exit) in sorted)
        {
            node.Offset(Anchor(entry)).Offset(Anchor(exit));
        }

        return node;
    }

    /// <summary>Mark-to-base (type 4) or mark-to-mark (type 6) attachment format 1: each mark's class and anchor, each base's anchor per class.</summary>
    public static OpenTypeNode MarkToBase(IReadOnlyDictionary<int, (int Class, int X, int Y)> marks, IReadOnlyDictionary<int, (int X, int Y)?[]> bases, int classCount)
    {
        int[] keys = [.. bases.Keys.Order()];
        var array = new OpenTypeNode().U16(keys.Length);
        foreach (int key in keys)
        {
            foreach ((int X, int Y)? anchor in bases[key])
            {
                array.Offset(Anchor(anchor));
            }
        }

        return new OpenTypeNode().U16(1).Offset(GsubBuilder.Coverage([.. marks.Keys])).Offset(GsubBuilder.Coverage(keys)).U16(classCount).Offset(MarkArray(marks)).Offset(array);
    }

    /// <summary>Mark-to-ligature attachment format 1: each ligature's anchors per component, per class.</summary>
    public static OpenTypeNode MarkToLigature(IReadOnlyDictionary<int, (int Class, int X, int Y)> marks, IReadOnlyDictionary<int, (int X, int Y)?[][]> ligatures, int classCount)
    {
        int[] keys = [.. ligatures.Keys.Order()];
        var array = new OpenTypeNode().U16(keys.Length);
        foreach (int key in keys)
        {
            var attach = new OpenTypeNode().U16(ligatures[key].Length);
            foreach ((int X, int Y)?[] component in ligatures[key])
            {
                foreach ((int X, int Y)? anchor in component)
                {
                    attach.Offset(Anchor(anchor));
                }
            }

            array.Offset(attach);
        }

        return new OpenTypeNode().U16(1).Offset(GsubBuilder.Coverage([.. marks.Keys])).Offset(GsubBuilder.Coverage(keys)).U16(classCount).Offset(MarkArray(marks)).Offset(array);
    }

    /// <summary>A format 1 Anchor table, or null.</summary>
    public static OpenTypeNode? Anchor((int X, int Y)? anchor) => anchor is { } point ? new OpenTypeNode().U16(1, point.X, point.Y) : null;

    /// <summary>A legacy <c>kern</c> table (version 0) with one horizontal format 0 subtable.</summary>
    public static byte[] Kern(params (int Left, int Right, int Value)[] pairs)
    {
        var sorted = pairs.OrderBy(pair => (pair.Left << 16) | pair.Right).ToArray();
        var node = new OpenTypeNode().U16(0, 1, 0, 14 + (sorted.Length * 6), 0x0001, sorted.Length, 0, 0, 0);
        foreach ((int left, int right, int value) in sorted)
        {
            node.U16(left, right, value);
        }

        return node.Build();
    }

    private static OpenTypeNode MarkArray(IReadOnlyDictionary<int, (int Class, int X, int Y)> marks)
    {
        var node = new OpenTypeNode().U16(marks.Count);
        foreach (int key in marks.Keys.Order())
        {
            node.U16(marks[key].Class).Offset(Anchor((marks[key].X, marks[key].Y)));
        }

        return node;
    }
}
