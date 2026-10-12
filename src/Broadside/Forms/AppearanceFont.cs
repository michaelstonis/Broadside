using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>A font an appearance stream shows variable text with: how text encodes in it, how wide it is, and its vertical extent.</summary>
/// <remarks>ISO 32000-2 §12.7.4.3 (the default appearance font) and §9.2.4 (glyph widths in thousandths of text space).</remarks>
internal abstract class AppearanceFont
{
    /// <summary>Gets the font object the appearance's <c>Font</c> resources hold (a reference or a dictionary).</summary>
    public abstract CosObject FontObject { get; }

    /// <summary>Gets the ascent above the baseline, in thousandths of the font size.</summary>
    public abstract double Ascent { get; }

    /// <summary>Gets the descent below the baseline (negative), in thousandths of the font size.</summary>
    public abstract double Descent { get; }

    /// <summary>Gets a value indicating whether the shown string is written as hexadecimal (two-byte codes).</summary>
    public virtual bool IsHexadecimal => false;

    /// <summary>Whether every character of <paramref name="text"/> has a code and a glyph in the font.</summary>
    public abstract bool CanEncode(string text);

    /// <summary>The string that shows <paramref name="text"/>; every character must be encodable (<see cref="CanEncode"/>).</summary>
    public abstract byte[] Encode(string text);

    /// <summary>The width of <paramref name="text"/> in thousandths of the font size (§9.2.4).</summary>
    public abstract double Measure(string text);

    /// <summary>Reads the ascent and descent of a font from its descriptor, else its bounding box, else 800 and -200.</summary>
    private protected static (double Ascent, double Descent) Extent(PdfFont font)
    {
        PdfFontDescriptor? descriptor = font.Descriptor;
        double ascent = descriptor?.Ascent ?? 0;
        double descent = descriptor?.Descent ?? 0;
        if (ascent <= 0 && descriptor is not null)
        {
            ascent = descriptor.FontBBox.Top;
            descent = descriptor.FontBBox.Bottom;
        }

        if (ascent <= 0)
        {
            return (800, -200);
        }

        return (ascent, descent > 0 ? -descent : descent);
    }
}

/// <summary>A simple font of the default resources (Type 1, TrueType): one byte per character, through its encoding's glyph names.</summary>
/// <remarks>ISO 32000-2 §9.6.5 (character encoding) and Annex D; a character maps to the lowest code whose glyph name stands for it.</remarks>
internal sealed class SimpleAppearanceFont : AppearanceFont
{
    private readonly PdfSimpleFont _font;
    private readonly Dictionary<int, byte> _codes = [];

    public SimpleAppearanceFont(PdfSimpleFont font, CosObject fontObject)
    {
        _font = font;
        FontObject = fontObject;
        bool zapf = font.Standard14 == Standard14Font.ZapfDingbats;
        for (int code = 0; code < 256; code++)
        {
            string? name = font.GetGlyphName((byte)code);
            if (!string.IsNullOrEmpty(name) && name != ".notdef" && AdobeGlyphList.TryGetScalar(name, zapf, out int scalar))
            {
                _codes.TryAdd(scalar, (byte)code);
            }
        }

        (Ascent, Descent) = Extent(font);
    }

    public override CosObject FontObject { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override bool CanEncode(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!_codes.ContainsKey(rune.Value))
            {
                return false;
            }
        }

        return true;
    }

    public override byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            bytes.Add(_codes[rune.Value]);
        }

        return [.. bytes];
    }

    public override double Measure(string text)
    {
        double width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += _font.GetWidth(_codes[rune.Value]);
        }

        return width;
    }
}

/// <summary>
/// A TrueType program found by the engine's font resolvers, embedded as a subset Type 0 font (CIDFontType2, <c>Identity-H</c>, glyph
/// ids as CIDs) with a <c>W</c> array and a <c>ToUnicode</c> CMap: for text the default appearance font cannot encode.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7 (Type 0 fonts, CIDFontType2, <c>W</c>, <c>CIDToGIDMap</c>), §9.8 (descriptor, <c>FontFile2</c>), §9.9.2 (subset
/// tag) and §9.10.3 (<c>ToUnicode</c>). Internal and minimal (issue #167); issue #150 owns general TrueType embedding.
/// </remarks>
internal sealed class EmbeddedAppearanceFont : AppearanceFont
{
    private readonly Dictionary<int, int> _glyphs;
    private readonly Dictionary<int, double> _widths;

    private EmbeddedAppearanceFont(CosReference reference, Dictionary<int, int> glyphs, Dictionary<int, double> widths, double ascent, double descent)
    {
        FontObject = reference;
        _glyphs = glyphs;
        _widths = widths;
        Ascent = ascent;
        Descent = descent;
    }

    public override CosObject FontObject { get; }

    public override double Ascent { get; }

    public override double Descent { get; }

    public override bool IsHexadecimal => true;

    /// <summary>
    /// Finds a TrueType program for <paramref name="text"/> through the document's font resolvers (one like <paramref name="like"/>,
    /// then Helvetica's stand-in), and adds the subset font to the document.
    /// </summary>
    /// <param name="document">The document the font is added to.</param>
    /// <param name="like">The default appearance font, whose name and style choose the program; or <see langword="null"/>.</param>
    /// <param name="text">Every text the font will show.</param>
    /// <param name="missing">When no program has glyphs for all of it, the first character none has; else <see langword="null"/>.</param>
    /// <returns>The font, or <see langword="null"/> when no resolver found a TrueType program covering the text.</returns>
    public static EmbeddedAppearanceFont? Create(PdfDocument document, PdfFont? like, IReadOnlyList<string> text, out string? missing)
    {
        missing = null;
        var queries = new List<FontQuery>();
        Standard14Font? standard14 = (like as PdfSimpleFont)?.Standard14;
        if (like?.BaseFont is { Length: > 0 } baseFont)
        {
            queries.Add(new FontQuery(baseFont) { FontType = PdfFontType.TrueType, Standard14 = standard14 });
        }

        if (standard14 != Standard14Font.Helvetica)
        {
            queries.Add(new FontQuery("Helvetica") { FontType = PdfFontType.TrueType, Standard14 = Standard14Font.Helvetica });
        }

        foreach (FontQuery query in queries)
        {
            if (document.FontResolvers.ResolveFont(query) is not { } resolution
                || SfntFile.Open(resolution.Data, new FontProgramContext { FaceIndex = resolution.FaceIndex }) is not { } sfnt
                || !sfnt.Has(SfntFile.Tag("glyf"))
                || document.GetSubstituteProgram(resolution, reference: null) is not { } program
                || PickCharacterMap(program) is not { } cmap)
            {
                continue;
            }

            var glyphs = new Dictionary<int, int>();
            string? uncovered = null;
            foreach (string part in text)
            {
                foreach (Rune rune in part.EnumerateRunes())
                {
                    int glyph = cmap.GetGlyphId(rune.Value);
                    if (glyph <= 0 || glyph >= program.GlyphCount)
                    {
                        uncovered ??= rune.ToString();
                    }
                    else
                    {
                        glyphs[rune.Value] = glyph;
                    }
                }
            }

            if (uncovered is not null)
            {
                missing ??= uncovered;
                continue;
            }

            if (TrueTypeSubsetter.Subset(sfnt, glyphs.Values) is not { } subset)
            {
                continue;
            }

            missing = null;
            return Embed(document, program, subset, glyphs);
        }

        return null;
    }

    public override bool CanEncode(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!_glyphs.ContainsKey(rune.Value))
            {
                return false;
            }
        }

        return true;
    }

    public override byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length * 2);
        foreach (Rune rune in text.EnumerateRunes())
        {
            int glyph = _glyphs[rune.Value];
            bytes.Add((byte)(glyph >> 8));
            bytes.Add((byte)glyph);
        }

        return [.. bytes];
    }

    public override double Measure(string text)
    {
        double width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += _widths[_glyphs[rune.Value]];
        }

        return width;
    }

    /// <summary>The Unicode subtable: (3, 10), else (3, 1), else (0, any).</summary>
    private static FontCharacterMap? PickCharacterMap(FontProgram program)
    {
        FontCharacterMap? best = null;
        int rank = 0;
        foreach (FontCharacterMap map in program.CharacterMaps)
        {
            int candidate = (map.PlatformId, map.EncodingId) switch
            {
                (3, 10) => 3,
                (3, 1) => 2,
                (0, _) => 1,
                _ => 0,
            };
            if (candidate > rank)
            {
                (best, rank) = (map, candidate);
            }
        }

        return best;
    }

    /// <summary>Adds the Type 0 font, its CIDFont, descriptor, program and ToUnicode CMap to the document.</summary>
    private static EmbeddedAppearanceFont Embed(PdfDocument document, FontProgram program, byte[] subset, Dictionary<int, int> glyphs)
    {
        double scale = program.FontMatrix.A * 1000;
        var widths = new Dictionary<int, double>();
        foreach (int glyph in glyphs.Values)
        {
            widths[glyph] = Math.Round(program.GetMetrics(glyph).AdvanceWidth * scale, 3);
        }

        int[] used = [.. widths.Keys.Order()];
        string name = SubsetTag(used) + "+" + PostScriptName(program);
        double ascent = Math.Round(program.Ascender * scale);
        double descent = Math.Round(program.Descender * scale);
        PdfRectangle box = program.FontBBox;
        var bbox = new CosArray([Number(Math.Round(box.Left * scale)), Number(Math.Round(box.Bottom * scale)), Number(Math.Round(box.Right * scale)), Number(Math.Round(box.Top * scale))]);

        CosReference fontFile = document.AddObject(new CosStream(new CosDictionary { [FontNames.Length1] = new CosInteger(subset.Length) }, subset));
        CosReference descriptor = document.AddObject(new CosDictionary
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
            [FontNames.FontFile2] = fontFile,
        });

        var widthArray = new CosArray();
        for (int index = 0; index < used.Length;)
        {
            int first = index;
            var run = new CosArray();
            do
            {
                run.Add(Number(widths[used[index]]));
                index++;
            }
            while (index < used.Length && used[index] == used[index - 1] + 1);

            widthArray.Add(new CosInteger(used[first]));
            widthArray.Add(run);
        }

        CosReference cidFont = document.AddObject(new CosDictionary
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
            [FontNames.FontDescriptor] = descriptor,
            [CompositeFontNames.DW] = new CosInteger(1000),
            [CompositeFontNames.W] = widthArray,
            [CompositeFontNames.CidToGidMap] = CompositeFontNames.Identity,
        });

        CosReference toUnicode = document.AddObject(new CosStream(new CosDictionary(), ToUnicodeCMap(glyphs)));
        CosReference font = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = FontNames.Type0,
            [FontNames.BaseFont] = new CosName(name),
            [FontNames.Encoding] = new CosName("Identity-H"),
            [CompositeFontNames.DescendantFonts] = new CosArray([cidFont]),
            [CompositeFontNames.ToUnicode] = toUnicode,
        });

        return new EmbeddedAppearanceFont(font, glyphs, widths, ascent > 0 ? ascent : 800, descent < 0 ? descent : -200);
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
    private static byte[] ToUnicodeCMap(Dictionary<int, int> glyphs)
    {
        var byGlyph = new SortedDictionary<int, int>();
        foreach ((int scalar, int glyph) in glyphs.OrderBy(pair => pair.Key))
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
