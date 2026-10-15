using System.Buffers.Binary;

namespace Broadside.TestSupport;

/// <summary>
/// A node of an OpenType layout table being built: big-endian fields and 16- or 32-bit offsets to child nodes, which are written
/// after the node's own fields (depth first) so every offset is relative to the start of the node that holds it.
/// </summary>
public sealed class OpenTypeNode
{
    private readonly List<(int Size, uint Value, OpenTypeNode? Child)> _items = [];

    /// <summary>Appends unsigned 16-bit fields.</summary>
    public OpenTypeNode U16(params int[] values)
    {
        foreach (int value in values)
        {
            _items.Add((2, (uint)(value & 0xFFFF), null));
        }

        return this;
    }

    /// <summary>Appends an unsigned 32-bit field.</summary>
    public OpenTypeNode U32(uint value)
    {
        _items.Add((4, value, null));
        return this;
    }

    /// <summary>Appends a four-character tag.</summary>
    public OpenTypeNode Tag(string tag) => U32(((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3]);

    /// <summary>Appends a 16-bit offset to <paramref name="child"/> (0 for none).</summary>
    public OpenTypeNode Offset(OpenTypeNode? child)
    {
        _items.Add((2, 0, child));
        return this;
    }

    /// <summary>Appends a 32-bit offset to <paramref name="child"/>.</summary>
    public OpenTypeNode Offset32(OpenTypeNode child)
    {
        _items.Add((4, 0, child));
        return this;
    }

    /// <summary>Serialises the node and its children.</summary>
    public byte[] Build()
    {
        var bytes = new List<byte>();
        var patches = new List<(int At, int Size, OpenTypeNode Child)>();
        foreach ((int size, uint value, OpenTypeNode? child) in _items)
        {
            if (child is not null)
            {
                patches.Add((bytes.Count, size, child));
            }

            for (int shift = (size - 1) * 8; shift >= 0; shift -= 8)
            {
                bytes.Add((byte)(value >> shift));
            }
        }

        byte[] result = [.. bytes];
        foreach ((int at, int size, OpenTypeNode child) in patches)
        {
            int offset = result.Length;
            result = [.. result, .. child.Build()];
            if (size == 2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(at), checked((ushort)offset));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at), (uint)offset);
            }
        }

        return result;
    }
}

/// <summary>
/// Builds <c>GSUB</c> and <c>GDEF</c> tables for tests: lookups from subtable nodes, features from lookups, scripts and language
/// systems from features. Each helper writes the subtable format the OpenType specification names, so a test reads like the feature
/// file it stands for.
/// </summary>
/// <remarks>OpenType specification: "OpenType Layout Common Table Formats", "GSUB — Glyph Substitution Table", "GDEF — Glyph Definition Table".</remarks>
public sealed class GsubBuilder
{
    private readonly List<OpenTypeNode> _lookups = [];
    private readonly List<(string Tag, int[] Lookups)> _features = [];
    private readonly List<(string Tag, int[] Features, (string Tag, int[] Features)[] Languages)> _scripts = [];

    /// <summary>Adds a lookup of <paramref name="type"/> and returns its index.</summary>
    public int Lookup(int type, int flag, params OpenTypeNode[] subtables) => Lookup(type, flag, null, subtables);

    /// <summary>Adds a lookup with a mark filtering set and returns its index.</summary>
    public int Lookup(int type, int flag, int? markFilteringSet, params OpenTypeNode[] subtables)
    {
        var lookup = new OpenTypeNode().U16(type, flag | (markFilteringSet is null ? 0 : 0x10), subtables.Length);
        foreach (OpenTypeNode subtable in subtables)
        {
            lookup.Offset(subtable);
        }

        if (markFilteringSet is { } set)
        {
            lookup.U16(set);
        }

        _lookups.Add(lookup);
        return _lookups.Count - 1;
    }

    /// <summary>Adds a feature and returns its index.</summary>
    public int Feature(string tag, params int[] lookups)
    {
        _features.Add((tag, lookups));
        return _features.Count - 1;
    }

    /// <summary>Adds a script with its default language system and other language systems.</summary>
    public GsubBuilder Script(string tag, int[] features, params (string Tag, int[] Features)[] languages)
    {
        _scripts.Add((tag, features, languages));
        return this;
    }

    /// <summary>Builds the GSUB table, version 1.0.</summary>
    public byte[] Build()
    {
        var scripts = new OpenTypeNode().U16(_scripts.Count);
        foreach ((string tag, int[] features, (string Tag, int[] Features)[] languages) in _scripts.OrderBy(script => script.Tag, StringComparer.Ordinal))
        {
            var script = new OpenTypeNode().Offset(LanguageSystem(features)).U16(languages.Length);
            foreach ((string language, int[] languageFeatures) in languages)
            {
                script.Tag(language).Offset(LanguageSystem(languageFeatures));
            }

            scripts.Tag(tag).Offset(script);
        }

        var featureList = new OpenTypeNode().U16(_features.Count);
        foreach ((string tag, int[] lookups) in _features)
        {
            featureList.Tag(tag).Offset(new OpenTypeNode().U16(0, lookups.Length).U16(lookups));
        }

        var lookupList = new OpenTypeNode().U16(_lookups.Count);
        foreach (OpenTypeNode lookup in _lookups)
        {
            lookupList.Offset(lookup);
        }

        return new OpenTypeNode().U16(1, 0).Offset(scripts).Offset(featureList).Offset(lookupList).Build();
    }

    /// <summary>A Coverage table, format 1 (glyph array).</summary>
    public static OpenTypeNode Coverage(params int[] glyphs)
    {
        int[] sorted = [.. glyphs.Order()];
        return new OpenTypeNode().U16(1, sorted.Length).U16(sorted);
    }

    /// <summary>A Coverage table, format 2 (one range per run of consecutive glyphs).</summary>
    public static OpenTypeNode CoverageRanges(params int[] glyphs)
    {
        int[] sorted = [.. glyphs.Order()];
        var ranges = new List<(int Start, int End, int Index)>();
        for (int index = 0; index < sorted.Length; index++)
        {
            if (ranges.Count > 0 && ranges[^1].End + 1 == sorted[index])
            {
                ranges[^1] = ranges[^1] with { End = sorted[index] };
            }
            else
            {
                ranges.Add((sorted[index], sorted[index], index));
            }
        }

        var node = new OpenTypeNode().U16(2, ranges.Count);
        foreach ((int start, int end, int index) in ranges)
        {
            node.U16(start, end, index);
        }

        return node;
    }

    /// <summary>A Class Definition table, format 2 (one range per glyph).</summary>
    public static OpenTypeNode ClassDef(IReadOnlyDictionary<int, int> classes)
    {
        var node = new OpenTypeNode().U16(2, classes.Count);
        foreach ((int glyph, int value) in classes.OrderBy(pair => pair.Key))
        {
            node.U16(glyph, glyph, value);
        }

        return node;
    }

    /// <summary>Single substitution format 1: every covered glyph plus <paramref name="delta"/>.</summary>
    public static OpenTypeNode SingleDelta(int delta, params int[] glyphs) => new OpenTypeNode().U16(1).Offset(Coverage(glyphs)).U16(delta);

    /// <summary>Single substitution format 2: each glyph to its substitute.</summary>
    public static OpenTypeNode SingleMap(IReadOnlyDictionary<int, int> map)
    {
        int[] keys = [.. map.Keys.Order()];
        return new OpenTypeNode().U16(2).Offset(Coverage(keys)).U16(keys.Length).U16([.. keys.Select(key => map[key])]);
    }

    /// <summary>Multiple substitution format 1: each glyph to a sequence.</summary>
    public static OpenTypeNode Multiple(IReadOnlyDictionary<int, int[]> map) => Sets(1, map, sequence => new OpenTypeNode().U16(sequence.Length).U16(sequence));

    /// <summary>Alternate substitution format 1: each glyph to its alternates.</summary>
    public static OpenTypeNode Alternate(IReadOnlyDictionary<int, int[]> map) => Sets(1, map, alternates => new OpenTypeNode().U16(alternates.Length).U16(alternates));

    /// <summary>Ligature substitution format 1: component sequences (first glyph first) to ligature glyphs, in priority order.</summary>
    public static OpenTypeNode Ligature(params (int[] Components, int Ligature)[] ligatures)
    {
        var byFirst = ligatures.GroupBy(ligature => ligature.Components[0]).OrderBy(group => group.Key).ToArray();
        var node = new OpenTypeNode().U16(1).Offset(Coverage([.. byFirst.Select(group => group.Key)])).U16(byFirst.Length);
        foreach (var group in byFirst)
        {
            var set = new OpenTypeNode().U16(group.Count());
            foreach ((int[] components, int ligature) in group)
            {
                set.Offset(new OpenTypeNode().U16(ligature, components.Length).U16(components[1..]));
            }

            node.Offset(set);
        }

        return node;
    }

    /// <summary>Contextual substitution format 1: one rule of glyphs, with (sequence index, lookup index) records.</summary>
    public static OpenTypeNode ContextGlyphs(int[] input, params (int Sequence, int Lookup)[] records)
    {
        var rule = new OpenTypeNode().U16(input.Length, records.Length).U16(input[1..]);
        Records(rule, records);
        return new OpenTypeNode().U16(1).Offset(Coverage(input[0])).U16(1).Offset(new OpenTypeNode().U16(1).Offset(rule));
    }

    /// <summary>Contextual substitution format 2: one rule of classes; the class sets before the first glyph's class are empty.</summary>
    public static OpenTypeNode ContextClasses(IReadOnlyDictionary<int, int> classes, int[] coverage, int[] input, params (int Sequence, int Lookup)[] records)
    {
        var rule = new OpenTypeNode().U16(input.Length, records.Length).U16(input[1..]);
        Records(rule, records);
        var node = new OpenTypeNode().U16(2).Offset(Coverage(coverage)).Offset(ClassDef(classes)).U16(input[0] + 1);
        for (int value = 0; value < input[0]; value++)
        {
            node.Offset(null);
        }

        return node.Offset(new OpenTypeNode().U16(1).Offset(rule));
    }

    /// <summary>Contextual substitution format 3: one coverage per input position.</summary>
    public static OpenTypeNode ContextCoverages(int[][] input, params (int Sequence, int Lookup)[] records)
    {
        var node = new OpenTypeNode().U16(3, input.Length, records.Length);
        foreach (int[] glyphs in input)
        {
            node.Offset(CoverageRanges(glyphs));
        }

        Records(node, records);
        return node;
    }

    /// <summary>Chained contextual substitution format 1: one rule of glyphs (backtrack nearest first).</summary>
    public static OpenTypeNode ChainGlyphs(int[] backtrack, int[] input, int[] lookahead, params (int Sequence, int Lookup)[] records)
    {
        var rule = new OpenTypeNode().U16(backtrack.Length).U16(backtrack).U16(input.Length).U16(input[1..]).U16(lookahead.Length).U16(lookahead).U16(records.Length);
        Records(rule, records);
        return new OpenTypeNode().U16(1).Offset(Coverage(input[0])).U16(1).Offset(new OpenTypeNode().U16(1).Offset(rule));
    }

    /// <summary>Chained contextual substitution format 2: one rule of classes, one class definition per sequence.</summary>
    public static OpenTypeNode ChainClasses(
        (IReadOnlyDictionary<int, int> Backtrack, IReadOnlyDictionary<int, int> Input, IReadOnlyDictionary<int, int> Lookahead) classes,
        int[] coverage,
        int[] backtrack,
        int[] input,
        int[] lookahead,
        params (int Sequence, int Lookup)[] records)
    {
        var rule = new OpenTypeNode().U16(backtrack.Length).U16(backtrack).U16(input.Length).U16(input[1..]).U16(lookahead.Length).U16(lookahead).U16(records.Length);
        Records(rule, records);
        var node = new OpenTypeNode().U16(2).Offset(Coverage(coverage)).Offset(ClassDef(classes.Backtrack)).Offset(ClassDef(classes.Input))
            .Offset(ClassDef(classes.Lookahead)).U16(input[0] + 1);
        for (int value = 0; value < input[0]; value++)
        {
            node.Offset(null);
        }

        return node.Offset(new OpenTypeNode().U16(1).Offset(rule));
    }

    /// <summary>Chained contextual substitution format 3: one coverage per position (backtrack nearest first).</summary>
    public static OpenTypeNode ChainCoverages(int[][] backtrack, int[][] input, int[][] lookahead, params (int Sequence, int Lookup)[] records)
    {
        var node = new OpenTypeNode().U16(3, backtrack.Length);
        Coverages(node, backtrack);
        node.U16(input.Length);
        Coverages(node, input);
        node.U16(lookahead.Length);
        Coverages(node, lookahead);
        node.U16(records.Length);
        Records(node, records);
        return node;
    }

    /// <summary>Extension substitution format 1 around a subtable of <paramref name="type"/>.</summary>
    public static OpenTypeNode Extension(int type, OpenTypeNode subtable) => new OpenTypeNode().U16(1, type).Offset32(subtable);

    /// <summary>Reverse chaining contextual single substitution format 1.</summary>
    public static OpenTypeNode ReverseChain(IReadOnlyDictionary<int, int> map, int[][] backtrack, int[][] lookahead)
    {
        int[] keys = [.. map.Keys.Order()];
        var node = new OpenTypeNode().U16(1).Offset(Coverage(keys)).U16(backtrack.Length);
        Coverages(node, backtrack);
        node.U16(lookahead.Length);
        Coverages(node, lookahead);
        return node.U16(keys.Length).U16([.. keys.Select(key => map[key])]);
    }

    /// <summary>A GDEF table, version 1.2: glyph classes, mark attachment classes and mark glyph sets.</summary>
    public static byte[] Gdef(IReadOnlyDictionary<int, int> glyphClasses, IReadOnlyDictionary<int, int>? markAttachClasses = null, params int[][] markGlyphSets)
    {
        var sets = new OpenTypeNode().U16(1, markGlyphSets.Length);
        foreach (int[] set in markGlyphSets)
        {
            sets.Offset32(Coverage(set));
        }

        return new OpenTypeNode().U16(1, 2).Offset(ClassDef(glyphClasses)).Offset(null).Offset(null)
            .Offset(markAttachClasses is null ? null : ClassDef(markAttachClasses)).Offset(markGlyphSets.Length == 0 ? null : sets).Build();
    }

    private static OpenTypeNode LanguageSystem(int[] features) => new OpenTypeNode().U16(0, 0xFFFF, features.Length).U16(features);

    private static OpenTypeNode Sets(int format, IReadOnlyDictionary<int, int[]> map, Func<int[], OpenTypeNode> set)
    {
        int[] keys = [.. map.Keys.Order()];
        var node = new OpenTypeNode().U16(format).Offset(Coverage(keys)).U16(keys.Length);
        foreach (int key in keys)
        {
            node.Offset(set(map[key]));
        }

        return node;
    }

    private static void Coverages(OpenTypeNode node, int[][] sequence)
    {
        foreach (int[] glyphs in sequence)
        {
            node.Offset(Coverage(glyphs));
        }
    }

    private static void Records(OpenTypeNode node, (int Sequence, int Lookup)[] records)
    {
        foreach ((int sequence, int lookup) in records)
        {
            node.U16(sequence, lookup);
        }
    }
}
