using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broadside.Fonts.Cff;
using Broadside.Fonts.TrueType;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>
/// One TrueType, OpenType or CFF program embedded once per document as a subset Type 0 font (<c>Identity-H</c>, the program's glyph
/// ids as CIDs) that every canvas and appearance showing the program shares, grown as text needs more glyphs (issues #150, #151, #285).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4 (CIDFontType2, <c>W</c>, <c>CIDToGIDMap</c>), §9.7.5.2 (<c>Identity-H</c>), §9.7.6 (Type 0 font dictionary),
/// §9.8 (descriptor, <c>FontFile2</c>), §9.9 (Table 124, Table 125 <c>Length1</c>), §9.9.2 (subset tag) and §9.10.3
/// (<c>ToUnicode</c>). The font's six objects are added once, through <see cref="PdfDocument.AddObject"/>; references to them never
/// change. A CID is the glyph's id in the original program, so text already written never changes; the <c>CIDToGIDMap</c> stream maps
/// it to the glyph's id in the embedded subset, which keeps only the used glyphs and their components (renumbered, §9.7.4.2).
/// </para>
/// <para>
/// A program with CFF outlines (issue #151) is a CIDFontType0 font instead, with five objects (no <c>CIDToGIDMap</c>): its
/// <c>FontFile3</c> is a CID-keyed CFF program (<c>CIDFontType0C</c>, PDF 1.3) whose charset maps each CID, the original glyph id, to
/// the glyph's id in the subset (§9.7.4.2, <see cref="CffSubsetter"/>). An OpenType program's <c>CFF </c> table is embedded that
/// way too, not as an <c>OpenType</c> <c>FontFile3</c>: FreeType reads the glyph index it is given for a CID-keyed CFF font as a
/// CID and maps it through the charset again, so poppler, which maps an OpenType program's CIDs through the charset first, would
/// draw the wrong glyphs from a renumbered subset.
/// </para>
/// <para>
/// Growing keeps the document readable at once: the glyph map, <c>W</c>, <c>ToUnicode</c> and the subset tag are rewritten, and the
/// <c>FontFile2</c> stream holds the whole program with an identity <c>CIDToGIDMap</c> until <see cref="Complete"/>, which a save
/// runs before it plans, reduces it to the subset of the glyphs used. A program whose <c>OS/2</c> <c>fsType</c> forbids subsetting
/// is embedded whole, without a subset tag; one whose licence restricts embedding is refused (<see cref="Create"/>).
/// </para>
/// </remarks>
internal sealed class SharedFontSubset
{
    private const int RestrictedLicence = 0x0002;
    private const int NoSubsetting = 0x0100;

    // The longest text a glyph is mapped to in ToUnicode; a bfchar destination holds at most 512 bytes (Adobe TN 5411).
    private const int MaxMappedText = 64;

    private readonly SfntFile? _sfnt;
    private readonly ReadOnlyMemory<byte> _data;
    private readonly EmbeddedOutlines _outlines;
    private readonly FontProgram _program;
    private readonly FontCharacterMap _cmap;
    private readonly double _scale;
    private readonly bool _subsettable;
    private readonly Dictionary<int, string> _texts = [];
    private readonly Dictionary<int, double> _widths = [];
    private readonly CosReference _fontFile;
    private readonly CosReference _descriptor;
    private readonly CosReference _cidFont;
    private readonly CosReference? _cidToGid;
    private readonly CosReference _toUnicode;
    private SubsetProgram? _wholeProgram;
    private OpenType.OpenTypeLayout? _layout;
    private bool _layoutRead;
    private bool _pending;

    private SharedFontSubset(PdfDocument document, EmbeddedProgram source, FontProgram program, FontCharacterMap cmap, bool subsettable, SubsetProgram subset, Dictionary<int, int> glyphs)
    {
        _sfnt = source.Sfnt;
        _data = source.Data;
        _outlines = source.Outlines;
        SfntFile? sfnt = source.Sfnt;
        _program = program;
        _cmap = cmap;
        _scale = program.FontMatrix.A * 1000;
        _subsettable = subsettable;
        foreach ((int scalar, int glyph) in glyphs.OrderBy(pair => pair.Key))
        {
            _widths[glyph] = WidthOf(glyph);
            _texts.TryAdd(glyph, char.ConvertFromUtf32(scalar));
        }

        PostScriptName = Sanitize(program.PostScriptName);
        (double ascender, double descender) = ReadAscent(program, sfnt);
        double ascent = Math.Round(ascender * _scale);
        double descent = Math.Round(descender * _scale);
        Ascent = ascent > 0 ? ascent : 800;
        Descent = descent < 0 ? descent : -200;
        PdfRectangle box = program.FontBBox;
        FontBBox = new PdfRectangle(Math.Round(box.Left * _scale), Math.Round(box.Bottom * _scale), Math.Round(box.Right * _scale), Math.Round(box.Top * _scale));
        (double capHeight, double xHeight, int weight, bool italic) = ReadOs2(sfnt, _scale);
        CapHeight = capHeight > 0 ? capHeight : Ascent;
        XHeight = Math.Max(xHeight, 0);
        (double italicAngle, bool fixedPitch) = ReadPost(sfnt);

        string name = SubsetName();
        bool trueType = _outlines == EmbeddedOutlines.TrueType;
        _fontFile = document.AddObject(new CosStream(
            trueType
                ? new CosDictionary { [FontNames.Length1] = new CosInteger(subset.Program.Length) }
                : new CosDictionary { [FontNames.Subtype] = new CosName("CIDFontType0C") },
            subset.Program));
        _descriptor = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = new CosName("FontDescriptor"),
            [FontNames.FontName] = new CosName(name),
            [FontNames.Flags] = new CosInteger(32 | (fixedPitch ? 1 : 0) | (italic || italicAngle != 0 ? 64 : 0)),
            [FontNames.FontBBox] = new CosArray([Number(FontBBox.Left), Number(FontBBox.Bottom), Number(FontBBox.Right), Number(FontBBox.Top)]),
            [FontNames.ItalicAngle] = Number(Math.Round(italicAngle, 3)),
            [FontNames.Ascent] = Number(ascent),
            [FontNames.Descent] = Number(descent),
            [FontNames.CapHeight] = Number(CapHeight),
            [FontNames.StemV] = new CosInteger(weight >= 600 ? 120 : 80),
            [trueType ? FontNames.FontFile2 : FontNames.FontFile3] = _fontFile,
        });
        if (XHeight > 0)
        {
            ((CosDictionary)document.Resolve(_descriptor))[new CosName("XHeight")] = Number(XHeight);
        }

        _cidToGid = trueType ? document.AddObject(new CosStream(new CosDictionary(), CidToGidMap(subset))) : null;
        var cidFont = new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = trueType ? CompositeFontNames.CidFontType2 : CompositeFontNames.CidFontType0,
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
        };
        if (_cidToGid is not null)
        {
            cidFont[CompositeFontNames.CidToGidMap] = _cidToGid;
        }

        _cidFont = document.AddObject(cidFont);

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

    /// <summary>Gets the feature (and PDF version) the font needs: its font type and its <c>FontFile2</c> or <c>FontFile3</c> subtype.</summary>
    public Writing.VersionedFeature Feature =>
        _outlines == EmbeddedOutlines.TrueType ? Writing.VersionedFeatures.EmbeddedTrueTypeFont : Writing.VersionedFeatures.EmbeddedCffFont;

    /// <summary>Gets the reference of the Type 0 font dictionary canvases and appearances name in their <c>Font</c> resources.</summary>
    public CosReference Font { get; }

    /// <summary>Gets the program's PostScript name, without a subset tag.</summary>
    public string PostScriptName { get; }

    /// <summary>Gets the ascent above the baseline, in thousandths of the font size.</summary>
    public double Ascent { get; }

    /// <summary>Gets the descent below the baseline (negative), in thousandths of the font size.</summary>
    public double Descent { get; }

    /// <summary>Gets the height of flat capitals, in thousandths of the font size (the ascent when the program does not say).</summary>
    public double CapHeight { get; }

    /// <summary>Gets the height of flat lowercase letters, in thousandths of the font size, or 0 when the program does not say.</summary>
    public double XHeight { get; }

    /// <summary>Gets the program's bounding box, in thousandths of the font size.</summary>
    public PdfRectangle FontBBox { get; }

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

    /// <summary>
    /// Adds the subset of <paramref name="glyphs"/> to the document as a new shared font; <see langword="null"/> with the reason when
    /// the program cannot be embedded (its licence restricts embedding, or it cannot be subset).
    /// </summary>
    public static SharedFontSubset? Create(PdfDocument document, EmbeddedProgram source, FontProgram program, FontCharacterMap cmap, Dictionary<int, int> glyphs, out string? problem)
    {
        SfntFile? sfnt = source.Sfnt;
        int fsType = sfnt is not null && sfnt.TryGetTable(SfntFile.Tag("OS/2"), out ReadOnlyMemory<byte> os2) && os2.Length >= 10 ? BinaryPrimitives.ReadUInt16BigEndian(os2.Span[8..]) : 0;
        if ((fsType & 0x000F) == RestrictedLicence)
        {
            problem = "its OS/2 fsType is 2 (restricted licence embedding): the font's licence does not allow embedding it";
            return null;
        }

        bool subsettable = (fsType & NoSubsetting) == 0;
        if (Subset(source, subsettable ? glyphs.Values : Enumerable.Range(0, program.GlyphCount)) is not { } subset)
        {
            problem = source.Outlines == EmbeddedOutlines.TrueType
                ? "its head, hhea, maxp, loca or glyf table is missing or damaged"
                : "its CFF program is damaged or does not use Type 2 charstrings";
            return null;
        }

        problem = null;
        return new SharedFontSubset(document, source, program, cmap, subsettable, subset, glyphs);
    }

    /// <summary>Whether the font's objects are still the document's: nobody removed or replaced them.</summary>
    public bool IsIntact(PdfDocument document) =>
        document.Resolve(Font) is CosDictionary && document.Resolve(_cidFont) is CosDictionary && document.Resolve(_descriptor) is CosDictionary
        && document.Resolve(_fontFile) is CosStream && document.Resolve(_toUnicode) is CosStream && (_cidToGid is null || document.Resolve(_cidToGid) is CosStream);

    /// <summary>The glyph id the program's Unicode cmap gives <paramref name="scalar"/>, or 0 when it has none.</summary>
    public int GlyphFor(int scalar)
    {
        int glyph = _cmap.GetGlyphId(scalar);
        return glyph > 0 && glyph < _program.GlyphCount ? glyph : 0;
    }

    /// <summary>The advance of <paramref name="glyph"/> in thousandths of the font size, as the <c>W</c> array gives it.</summary>
    public double WidthOf(int glyph) => _widths.TryGetValue(glyph, out double width) ? width : Math.Round(_program.GetMetrics(glyph).AdvanceWidth * _scale, 3);

    /// <summary>Finds the glyphs of <paramref name="text"/> in this font's program; <see langword="null"/> with the first character it lacks.</summary>
    public Dictionary<int, int>? Cover(IReadOnlyList<string> text, out string? uncovered) => Cover(_cmap, _program.GlyphCount, text, out uncovered);

    /// <summary>Gets the number of glyphs of the program.</summary>
    public int GlyphCount => _program.GlyphCount;

    /// <summary>Gets the program's OpenType layout tables (read once), or <see langword="null"/> when it has no <c>GSUB</c> table or is a bare CFF program.</summary>
    public OpenType.OpenTypeLayout? Layout
    {
        get
        {
            if (!Volatile.Read(ref _layoutRead))
            {
                _layout = _sfnt is null ? null : OpenType.OpenTypeLayout.Read(_sfnt, _program.GlyphCount);
                Volatile.Write(ref _layoutRead, true);
            }

            return _layout;
        }
    }

    /// <summary>
    /// Grows the font by one shown glyph and the text it stands for (see <see cref="Include(PdfDocument, Dictionary{int, int})"/>):
    /// a glyph is mapped in <c>ToUnicode</c> to the first non-empty text it is shown for, and keeps that mapping.
    /// </summary>
    /// <returns>Whether <c>ToUnicode</c> maps the glyph to exactly <paramref name="text"/> (false for empty text, or text the mapping cannot hold).</returns>
    public bool Include(PdfDocument document, int glyph, ReadOnlySpan<char> text)
    {
        bool grew = false;
        if (!_widths.ContainsKey(glyph))
        {
            _widths[glyph] = WidthOf(glyph);
            grew = true;
        }

        bool mapped;
        if (_texts.TryGetValue(glyph, out string? existing))
        {
            mapped = text.SequenceEqual(existing);
        }
        else if (text.IsEmpty || text.Length > MaxMappedText)
        {
            mapped = false;
        }
        else
        {
            _texts.Add(glyph, text.ToString());
            mapped = grew = true;
        }

        if (grew)
        {
            Rewrite(document);
        }

        return mapped;
    }

    /// <summary>
    /// Grows the font by the glyphs of <paramref name="glyphs"/> (character to glyph) it does not have yet: the glyph map, <c>W</c>,
    /// <c>ToUnicode</c> and the subset tag are rewritten, and the program is embedded whole until <see cref="Complete"/> subsets it again.
    /// </summary>
    public void Include(PdfDocument document, Dictionary<int, int> glyphs)
    {
        bool grew = false;
        foreach ((int scalar, int glyph) in glyphs.OrderBy(pair => pair.Key))
        {
            grew |= _texts.TryAdd(glyph, char.ConvertFromUtf32(scalar));
            grew |= _widths.TryAdd(glyph, WidthOf(glyph));
        }

        if (grew)
        {
            Rewrite(document);
        }
    }

    private void Rewrite(PdfDocument document)
    {
        if (!IsIntact(document))
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

        _wholeProgram ??= Subset(Source, Enumerable.Range(0, _program.GlyphCount));
        if (_wholeProgram is not null)
        {
            SetProgram((CosStream)document.Resolve(_fontFile), _wholeProgram.Program);
            SetMap(document, _wholeProgram);
            _pending = _subsettable;
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
        CosChangeLog.OnRevert(() => _pending = true);
        if (Subset(Source, _widths.Keys) is not { } subset)
        {
            return false;
        }

        SetProgram((CosStream)document.Resolve(_fontFile), subset.Program);
        SetMap(document, subset);
        return true;
    }

    private EmbeddedProgram Source => new(_outlines, _sfnt, _data);

    /// <summary>Subsets a program to <paramref name="glyphs"/>: a TrueType program, or the CFF program of an OpenType or bare CFF one.</summary>
    private static SubsetProgram? Subset(EmbeddedProgram source, IEnumerable<int> glyphs) => source.Outlines switch
    {
        EmbeddedOutlines.TrueType => TrueTypeSubsetter.Subset(source.Sfnt!, glyphs),
        EmbeddedOutlines.OpenTypeCff => source.Sfnt!.TryGetTable(SfntFile.Tag("CFF "), out ReadOnlyMemory<byte> cff) ? CffSubsetter.Subset(cff, glyphs) : null,
        _ => CffSubsetter.Subset(source.Data, glyphs),
    };

    private void SetProgram(CosStream stream, byte[] program)
    {
        if (_outlines == EmbeddedOutlines.TrueType)
        {
            stream.Dictionary[FontNames.Length1] = new CosInteger(program.Length);
        }

        SetData(stream, program);
    }

    /// <summary>Rewrites the <c>CIDToGIDMap</c> for a TrueType program (a CIDFontType0 program's charset maps CIDs itself).</summary>
    private void SetMap(PdfDocument document, SubsetProgram program)
    {
        if (_cidToGid is not null)
        {
            SetData((CosStream)document.Resolve(_cidToGid), CidToGidMap(program));
        }
    }

    /// <summary>
    /// The ascender and descender in font units: the program's, else (a CFF program, which has none) its OpenType <c>hhea</c>
    /// table's, else its bounding box's top and bottom.
    /// </summary>
    private static (double Ascender, double Descender) ReadAscent(FontProgram program, SfntFile? sfnt)
    {
        if (program.Ascender != 0 || program.Descender != 0)
        {
            return (program.Ascender, program.Descender);
        }

        if (sfnt is not null && sfnt.TryGetTable(SfntFile.Tag("hhea"), out ReadOnlyMemory<byte> hhea) && hhea.Length >= 8)
        {
            return (BinaryPrimitives.ReadInt16BigEndian(hhea.Span[4..]), BinaryPrimitives.ReadInt16BigEndian(hhea.Span[6..]));
        }

        return (program.FontBBox.Top, program.FontBBox.Bottom);
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

    /// <summary>OS/2 <c>sCapHeight</c>, <c>sxHeight</c> (version 2 and later), <c>usWeightClass</c> and the italic selection bit.</summary>
    private static (double CapHeight, double XHeight, int Weight, bool Italic) ReadOs2(SfntFile? sfnt, double scale)
    {
        if (sfnt is null || !sfnt.TryGetTable(SfntFile.Tag("OS/2"), out ReadOnlyMemory<byte> table) || table.Length < 64)
        {
            return (0, 0, 400, false);
        }

        ReadOnlySpan<byte> os2 = table.Span;
        int weight = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]);
        bool italic = (BinaryPrimitives.ReadUInt16BigEndian(os2[62..]) & 1) != 0;
        if (BinaryPrimitives.ReadUInt16BigEndian(os2) < 2 || os2.Length < 90)
        {
            return (0, 0, weight, italic);
        }

        return (Math.Round(BinaryPrimitives.ReadInt16BigEndian(os2[88..]) * scale), Math.Round(BinaryPrimitives.ReadInt16BigEndian(os2[86..]) * scale), weight, italic);
    }

    /// <summary>post <c>italicAngle</c> (16.16 fixed) and <c>isFixedPitch</c>.</summary>
    private static (double ItalicAngle, bool FixedPitch) ReadPost(SfntFile? sfnt)
    {
        if (sfnt is null || !sfnt.TryGetTable(SfntFile.Tag("post"), out ReadOnlyMemory<byte> table) || table.Length < 16)
        {
            return (0, false);
        }

        ReadOnlySpan<byte> post = table.Span;
        return (BinaryPrimitives.ReadInt32BigEndian(post[4..]) / 65536.0, BinaryPrimitives.ReadUInt32BigEndian(post[12..]) != 0);
    }

    /// <summary>The <c>CIDToGIDMap</c> stream data: two bytes per CID up to the largest used, the glyph's id in <paramref name="program"/> (§9.7.4.1, Table 115).</summary>
    private byte[] CidToGidMap(SubsetProgram program)
    {
        int last = _widths.Count == 0 ? 0 : _widths.Keys.Max();
        byte[] map = new byte[(last + 1) * 2];
        foreach (int cid in _widths.Keys)
        {
            BinaryPrimitives.WriteUInt16BigEndian(map.AsSpan(cid * 2), (ushort)program.NewId(cid));
        }

        return map;
    }

    private string SubsetName() => _subsettable ? SubsetTag([.. _widths.Keys.Order()]) + "+" + PostScriptName : PostScriptName;

    /// <summary>The <c>W</c> array: one run per range of consecutive CIDs (§9.7.4.3).</summary>
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

    /// <summary>Six capital letters derived from the glyph set, so the same subset gets the same tag and different ones differ (§9.9.2).</summary>
    private static string SubsetTag(int[] glyphs)
    {
        byte[] bytes = new byte[glyphs.Length * 4];
        for (int index = 0; index < glyphs.Length; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(index * 4), glyphs[index]);
        }

        byte[] hash = SHA256.HashData(bytes);
        var tag = new StringBuilder(6);
        for (int index = 0; index < 6; index++)
        {
            tag.Append((char)('A' + (hash[index] % 26)));
        }

        return tag.ToString();
    }

    /// <summary>The PostScript name without characters a name object or a <c>BaseFont</c> should not carry (§7.3.5, §9.6.2.1).</summary>
    private static string Sanitize(string? postScriptName)
    {
        var name = new StringBuilder();
        foreach (char character in postScriptName ?? "Font")
        {
            if (character is > ' ' and < (char)127 and not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%' or '#' or '+'))
            {
                name.Append(character);
            }
        }

        return name.Length == 0 ? "Font" : name.ToString();
    }

    /// <summary>A ToUnicode CMap mapping each two-byte CID to its text (§9.10.3, Adobe TN 5411): one character, or all the characters of a ligature.</summary>
    private byte[] ToUnicodeCMap()
    {
        var byGlyph = new SortedDictionary<int, string>(_texts);

        var cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        KeyValuePair<int, string>[] entries = [.. byGlyph];
        for (int start = 0; start < entries.Length; start += 100)
        {
            int count = Math.Min(100, entries.Length - start);
            cmap.Append(CultureInfo.InvariantCulture, $"{count} beginbfchar\n");
            for (int index = start; index < start + count; index++)
            {
                cmap.Append(CultureInfo.InvariantCulture, $"<{entries[index].Key:X4}> <");
                foreach (char unit in entries[index].Value)
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

/// <summary>The outlines of an embedded program, which choose its <c>FontFile</c> entry and subtype (ISO 32000-2 §9.9, Tables 124 and 125).</summary>
internal enum EmbeddedOutlines
{
    /// <summary>A TrueType program (<c>glyf</c>): <c>FontFile2</c>, CIDFontType2.</summary>
    TrueType,

    /// <summary>An OpenType program with a <c>CFF </c> table: its CFF program as <c>FontFile3</c> <c>CIDFontType0C</c>, CIDFontType0.</summary>
    OpenTypeCff,

    /// <summary>A bare CFF program: <c>FontFile3</c> <c>CIDFontType0C</c>, CIDFontType0.</summary>
    Cff,
}

/// <summary>A program to embed: its outlines, its table directory (TrueType and OpenType) and its bytes (bare CFF).</summary>
/// <param name="Outlines">The outlines.</param>
/// <param name="Sfnt">The table directory, or <see langword="null"/> for bare CFF.</param>
/// <param name="Data">The program's bytes.</param>
internal readonly record struct EmbeddedProgram(EmbeddedOutlines Outlines, SfntFile? Sfnt, ReadOnlyMemory<byte> Data);

/// <summary>
/// A document's shared font subsets, one per embedded program (its bytes and face), completed before every save (issues #150, #285).
/// </summary>
/// <remarks>ISO 32000-2 §9.9.2. Used only by mutations (drawing text, filling fields) and saves, which are single-threaded.</remarks>
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
