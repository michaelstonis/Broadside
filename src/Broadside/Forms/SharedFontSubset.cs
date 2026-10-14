using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// One TrueType program embedded once per document as a subset Type 0 font (CIDFontType2, <c>Identity-H</c>, glyph ids as CIDs) that
/// every appearance needing the program shares, grown as fields need more glyphs (issue #285).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7 (Type 0 fonts, CIDFontType2, <c>W</c>, <c>CIDToGIDMap</c>), §9.8 (descriptor, <c>FontFile2</c>), §9.9.2 (subset
/// fonts and their tag) and §9.10.3 (<c>ToUnicode</c>). The font's five objects are added once, through
/// <see cref="PdfDocument.AddObject"/>, with the subset of the first text's glyphs; references to them never change.
/// </para>
/// <para>
/// Growing keeps the document readable at once: the glyph map, <c>W</c>, <c>ToUnicode</c> and the subset tag are rewritten, and the
/// <c>FontFile2</c> stream holds the whole program (every glyph keeps its id) until <see cref="Complete"/>, which a save runs before it
/// plans, reduces it to the subset of the glyphs used. Internal and minimal (issue #167); issue #150 owns general TrueType embedding.
/// </para>
/// </remarks>
internal sealed class SharedFontSubset
{
    private readonly SfntFile _sfnt;
    private readonly FontProgram _program;
    private readonly FontCharacterMap _cmap;
    private readonly double _scale;
    private readonly Dictionary<int, int> _glyphs;
    private readonly Dictionary<int, double> _widths = [];
    private readonly CosReference _fontFile;
    private readonly CosReference _descriptor;
    private readonly CosReference _cidFont;
    private readonly CosReference _toUnicode;
    private byte[]? _wholeProgram;
    private bool _pending;

    private SharedFontSubset(PdfDocument document, SfntFile sfnt, FontProgram program, FontCharacterMap cmap, byte[] subset, Dictionary<int, int> glyphs)
    {
        _sfnt = sfnt;
        _program = program;
        _cmap = cmap;
        _scale = program.FontMatrix.A * 1000;
        _glyphs = glyphs;
        foreach (int glyph in glyphs.Values)
        {
            _widths[glyph] = Width(glyph);
        }

        string name = SubsetName();
        double ascent = Math.Round(program.Ascender * _scale);
        double descent = Math.Round(program.Descender * _scale);
        Ascent = ascent > 0 ? ascent : 800;
        Descent = descent < 0 ? descent : -200;
        PdfRectangle box = program.FontBBox;
        var bbox = new CosArray([Number(Math.Round(box.Left * _scale)), Number(Math.Round(box.Bottom * _scale)), Number(Math.Round(box.Right * _scale)), Number(Math.Round(box.Top * _scale))]);

        _fontFile = document.AddObject(new CosStream(new CosDictionary { [FontNames.Length1] = new CosInteger(subset.Length) }, subset));
        _descriptor = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = new CosName("FontDescriptor"),
            [FontNames.FontName] = new CosName(name),
            [FontNames.Flags] = new CosInteger(32),
            [FontNames.FontBBox] = bbox,
            [FontNames.ItalicAngle] = new CosInteger(0),
            [FontNames.Ascent] = Number(ascent),
            [FontNames.Descent] = Number(descent),
            [FontNames.CapHeight] = Number(ascent),
            [FontNames.StemV] = new CosInteger(80),
            [FontNames.FontFile2] = _fontFile,
        });

        _cidFont = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = CompositeFontNames.CidFontType2,
            [FontNames.BaseFont] = new CosName(name),
            [CompositeFontNames.CidSystemInfo] = new CosDictionary
            {
                [CompositeFontNames.Registry] = new CosString("Adobe"u8),
                [CompositeFontNames.Ordering] = new CosString("Identity"u8),
                [CompositeFontNames.Supplement] = new CosInteger(0),
            },
            [FontNames.FontDescriptor] = _descriptor,
            [CompositeFontNames.DW] = new CosInteger(1000),
            [CompositeFontNames.W] = WidthArray(),
            [CompositeFontNames.CidToGidMap] = CompositeFontNames.Identity,
        });

        _toUnicode = document.AddObject(new CosStream(new CosDictionary(), ToUnicodeCMap()));
        Font = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = FontNames.Type0,
            [FontNames.BaseFont] = new CosName(name),
            [FontNames.Encoding] = new CosName("Identity-H"),
            [CompositeFontNames.DescendantFonts] = new CosArray([_cidFont]),
            [CompositeFontNames.ToUnicode] = _toUnicode,
        });
    }

    /// <summary>Gets the reference of the Type 0 font dictionary appearances name in their <c>Font</c> resources.</summary>
    public CosReference Font { get; }

    /// <summary>Gets the ascent above the baseline, in thousandths of the font size.</summary>
    public double Ascent { get; }

    /// <summary>Gets the descent below the baseline (negative), in thousandths of the font size.</summary>
    public double Descent { get; }

    /// <summary>
    /// Finds the glyph of every character of <paramref name="text"/> in <paramref name="cmap"/>: the character-to-glyph map, or
    /// <see langword="null"/> with the first character the program has no glyph for.
    /// </summary>
    public static Dictionary<int, int>? Cover(FontCharacterMap cmap, int glyphCount, IReadOnlyList<string> text, out string? uncovered)
    {
        uncovered = null;
        var glyphs = new Dictionary<int, int>();
        foreach (string part in text)
        {
            foreach (Rune rune in part.EnumerateRunes())
            {
                int glyph = cmap.GetGlyphId(rune.Value);
                if (glyph <= 0 || glyph >= glyphCount)
                {
                    uncovered ??= rune.ToString();
                }
                else
                {
                    glyphs[rune.Value] = glyph;
                }
            }
        }

        return uncovered is null ? glyphs : null;
    }

    /// <summary>Adds the subset of <paramref name="glyphs"/> to the document as a new shared font; <see langword="null"/> when the program cannot be subset.</summary>
    public static SharedFontSubset? Create(PdfDocument document, SfntFile sfnt, FontProgram program, FontCharacterMap cmap, Dictionary<int, int> glyphs) =>
        TrueTypeSubsetter.Subset(sfnt, glyphs.Values) is { } subset ? new SharedFontSubset(document, sfnt, program, cmap, subset, glyphs) : null;

    /// <summary>Whether the font's objects are still the document's: nobody removed or replaced them.</summary>
    public bool IsIntact(PdfDocument document) =>
        document.Resolve(Font) is CosDictionary && document.Resolve(_cidFont) is CosDictionary && document.Resolve(_descriptor) is CosDictionary
        && document.Resolve(_fontFile) is CosStream && document.Resolve(_toUnicode) is CosStream;

    /// <summary>Whether the font has a glyph for <paramref name="scalar"/>.</summary>
    public bool Has(int scalar) => _glyphs.ContainsKey(scalar);

    /// <summary>The glyph id (and CID) that shows <paramref name="scalar"/>; the font must have it (<see cref="Has"/>).</summary>
    public int GlyphOf(int scalar) => _glyphs[scalar];

    /// <summary>The advance of <paramref name="glyph"/> in thousandths of the font size, as the <c>W</c> array gives it.</summary>
    public double WidthOf(int glyph) => _widths[glyph];

    /// <summary>Finds the glyphs of <paramref name="text"/> in this font's program; <see langword="null"/> with the first character it lacks.</summary>
    public Dictionary<int, int>? Cover(IReadOnlyList<string> text, out string? uncovered) => Cover(_cmap, _program.GlyphCount, text, out uncovered);

    /// <summary>
    /// Grows the font by the glyphs of <paramref name="glyphs"/> it does not have yet: the glyph map, <c>W</c>, <c>ToUnicode</c> and
    /// the subset tag are rewritten, and the program is embedded whole until <see cref="Complete"/> subsets it again.
    /// </summary>
    public void Include(PdfDocument document, Dictionary<int, int> glyphs)
    {
        bool grew = false;
        foreach ((int scalar, int glyph) in glyphs)
        {
            if (_glyphs.TryAdd(scalar, glyph))
            {
                grew = true;
                _widths.TryAdd(glyph, Width(glyph));
            }
        }

        if (!grew)
        {
            return;
        }

        var name = new CosName(SubsetName());
        var font = (CosDictionary)document.Resolve(Font);
        var cidFont = (CosDictionary)document.Resolve(_cidFont);
        font[FontNames.BaseFont] = name;
        cidFont[FontNames.BaseFont] = name;
        cidFont[CompositeFontNames.W] = WidthArray();
        ((CosDictionary)document.Resolve(_descriptor))[FontNames.FontName] = name;
        SetData((CosStream)document.Resolve(_toUnicode), ToUnicodeCMap());

        _wholeProgram ??= TrueTypeSubsetter.Subset(_sfnt, Enumerable.Range(0, _program.GlyphCount)) ?? [];
        if (_wholeProgram.Length > 0)
        {
            SetProgram((CosStream)document.Resolve(_fontFile), _wholeProgram);
            _pending = true;
        }
    }

    /// <summary>Reduces the embedded program to the subset of the glyphs used, when it grew since it was last subset (§9.9.2).</summary>
    /// <returns>Whether the program stream changed.</returns>
    public bool Complete(PdfDocument document)
    {
        if (!_pending || !IsIntact(document))
        {
            return false;
        }

        _pending = false;
        if (TrueTypeSubsetter.Subset(_sfnt, _widths.Keys) is not { } subset)
        {
            return false;
        }

        SetProgram((CosStream)document.Resolve(_fontFile), subset);
        return true;
    }

    private static void SetProgram(CosStream stream, byte[] program)
    {
        stream.Dictionary[FontNames.Length1] = new CosInteger(program.Length);
        SetData(stream, program);
    }

    /// <summary>Replaces a stream's data with unfiltered bytes; a save compresses the stream again (#142).</summary>
    private static void SetData(CosStream stream, byte[] data)
    {
        foreach (CosName key in (ReadOnlySpan<CosName>)[Filters.FilterNames.Filter, Filters.FilterNames.DecodeParms])
        {
            _ = stream.Dictionary.Remove(key);
        }

        stream.EncodedData = data;
    }

    private double Width(int glyph) => Math.Round(_program.GetMetrics(glyph).AdvanceWidth * _scale, 3);

    private string SubsetName() => SubsetTag([.. _widths.Keys.Order()]) + "+" + PostScriptName(_program);

    /// <summary>The <c>W</c> array: one run per range of consecutive glyph ids (§9.7.4.3).</summary>
    private CosArray WidthArray()
    {
        int[] used = [.. _widths.Keys.Order()];
        var widthArray = new CosArray();
        for (int index = 0; index < used.Length;)
        {
            int first = index;
            var run = new CosArray();
            do
            {
                run.Add(Number(_widths[used[index]]));
                index++;
            }
            while (index < used.Length && used[index] == used[index - 1] + 1);

            widthArray.Add(new CosInteger(used[first]));
            widthArray.Add(run);
        }

        return widthArray;
    }

    private static CosNumber Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < int.MaxValue ? new CosInteger((long)value) : new CosReal(value);

    /// <summary>Six capital letters derived from the glyph set, so the same subset gets the same tag (§9.9.2).</summary>
    private static string SubsetTag(int[] glyphs)
    {
        byte[] bytes = new byte[glyphs.Length * 4];
        for (int index = 0; index < glyphs.Length; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(index * 4), glyphs[index]);
        }

        byte[] hash = SHA256.HashData(bytes);
        var tag = new StringBuilder(6);
        for (int index = 0; index < 6; index++)
        {
            tag.Append((char)('A' + (hash[index] % 26)));
        }

        return tag.ToString();
    }

    private static string PostScriptName(FontProgram program)
    {
        var name = new StringBuilder();
        foreach (char character in program.PostScriptName ?? "Font")
        {
            if (character is > ' ' and < (char)127 and not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%' or '#'))
            {
                name.Append(character);
            }
        }

        return name.Length == 0 ? "Font" : name.ToString();
    }

    /// <summary>A ToUnicode CMap mapping each two-byte glyph id to its character (§9.10.3, Adobe TN 5411).</summary>
    private byte[] ToUnicodeCMap()
    {
        var byGlyph = new SortedDictionary<int, int>();
        foreach ((int scalar, int glyph) in _glyphs.OrderBy(pair => pair.Key))
        {
            byGlyph.TryAdd(glyph, scalar);
        }

        var cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        KeyValuePair<int, int>[] entries = [.. byGlyph];
        for (int start = 0; start < entries.Length; start += 100)
        {
            int count = Math.Min(100, entries.Length - start);
            cmap.Append(CultureInfo.InvariantCulture, $"{count} beginbfchar\n");
            for (int index = start; index < start + count; index++)
            {
                cmap.Append(CultureInfo.InvariantCulture, $"<{entries[index].Key:X4}> <");
                foreach (char unit in char.ConvertFromUtf32(entries[index].Value))
                {
                    cmap.Append(CultureInfo.InvariantCulture, $"{(int)unit:X4}");
                }

                cmap.Append(">\n");
            }

            cmap.Append("endbfchar\n");
        }

        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(cmap.ToString());
    }
}

/// <summary>
/// A document's shared font subsets, one per resolved program (its bytes and face), completed before every save (issue #285).
/// </summary>
/// <remarks>ISO 32000-2 §9.9.2. Used only by mutations (filling fields) and saves, which are single-threaded.</remarks>
internal sealed class SharedFontSubsets
{
    private readonly Dictionary<(ReadOnlyMemory<byte> Data, int FaceIndex), SharedFontSubset> _subsets = [];

    /// <summary>The intact shared subset of a program, or <see langword="null"/> when the document has none (or it was removed).</summary>
    public SharedFontSubset? Find(PdfDocument document, FontResolution resolution)
    {
        var key = (resolution.Data, resolution.FaceIndex);
        if (_subsets.TryGetValue(key, out SharedFontSubset? subset) && !subset.IsIntact(document))
        {
            _ = _subsets.Remove(key);
            return null;
        }

        return subset;
    }

    /// <summary>Records <paramref name="subset"/> as the program's shared subset.</summary>
    public void Add(FontResolution resolution, SharedFontSubset subset) => _subsets[(resolution.Data, resolution.FaceIndex)] = subset;

    /// <summary>Subsets every font that grew since its program was last subset (<see cref="SharedFontSubset.Complete"/>).</summary>
    /// <returns>Whether any program stream changed.</returns>
    public bool Complete(PdfDocument document)
    {
        bool changed = false;
        foreach (SharedFontSubset subset in _subsets.Values)
        {
            changed |= subset.Complete(document);
        }

        return changed;
    }
}
