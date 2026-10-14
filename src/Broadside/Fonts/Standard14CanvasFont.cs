using System.Text;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>
/// A non-embedded Standard 14 font a canvas writes text with: the twelve Latin fonts in <c>WinAnsiEncoding</c>, Symbol and
/// ZapfDingbats in their built-in encodings, measured and kerned from the AFM metrics in the core.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.6.2.1 (Table 109), §9.6.2.2, §9.8 (Table 120) and Annex D.2, D.5, D.6. The font dictionary carries
/// <c>FirstChar</c>, <c>LastChar</c>, <c>Widths</c> and an indirect <c>FontDescriptor</c> (without a font file): optional for the
/// Standard 14 fonts before PDF 2.0, required in 2.0, and "all present or all absent" in every version, so one dictionary is valid
/// whatever version the document is saved as. A character maps to the lowest code whose glyph name the Adobe Glyph List gives it, so
/// the text re-reads as written (§9.10.2); U+00A0 and U+00AD are written as the codes Annex D gives the space and the hyphen a
/// second time (0xA0, 0xAD), which re-read as U+0020 and U+002D.
/// </remarks>
internal sealed class Standard14CanvasFont : PdfCanvasFont
{
    private static readonly Dictionary<int, byte>[] CodesByEncoding = new Dictionary<int, byte>[3];

    private readonly Standard14Font _font;
    private readonly BuiltInEncoding _encoding;
    private readonly Dictionary<int, byte> _codes;

    private Standard14CanvasFont(PdfDocument document, CosReference reference, Standard14Font font)
        : base(document, reference)
    {
        _font = font;
        _encoding = EncodingOf(font);
        _codes = Codes(_encoding);
    }

    /// <inheritdoc/>
    public override string Name => Standard14Data.PostScriptName(_font);

    /// <inheritdoc/>
    public override double Ascent => Standard14Data.Metric(_font, Standard14Metric.Ascent);

    /// <inheritdoc/>
    public override double Descent => Standard14Data.Metric(_font, Standard14Metric.Descent);

    /// <inheritdoc/>
    public override double CapHeight => Standard14Data.Metric(_font, Standard14Metric.CapHeight);

    /// <inheritdoc/>
    public override double XHeight => Standard14Data.Metric(_font, Standard14Metric.XHeight);

    /// <inheritdoc/>
    public override PdfRectangle FontBBox => Standard14Data.FontBBox(_font);

    /// <inheritdoc/>
    internal override int CodeLength => 1;

    /// <summary>Returns the document's canvas font for <paramref name="font"/>, adding its font dictionary and descriptor the first time.</summary>
    public static Standard14CanvasFont Get(PdfDocument document, Standard14Font font)
    {
        CosReference reference = document.Drawing.GetOrAdd("Font|Standard14|" + Standard14Data.PostScriptName(font), () => CreateDictionary(document, font));
        return new Standard14CanvasFont(document, reference, font);
    }

    /// <inheritdoc/>
    internal override bool TryGetGlyph(int scalar, out CanvasGlyph glyph)
    {
        if (_codes.TryGetValue(scalar, out byte code))
        {
            int name = GlyphNameTable.Table(_encoding)[code];
            short width = Standard14Data.WidthOf(_font, name);
            if (width >= 0)
            {
                glyph = new CanvasGlyph(code, width, name);
                return true;
            }
        }

        glyph = default;
        return false;
    }

    /// <inheritdoc/>
    internal override int GetKerning(in CanvasGlyph left, in CanvasGlyph right) => Standard14Data.Kerning(_font, left.Key, right.Key);

    /// <inheritdoc/>
    internal override string DescribeUnencodable(Rune character, int index)
    {
        string encoding = _encoding == BuiltInEncoding.WinAnsi ? "WinAnsiEncoding" : $"the built-in encoding of {Name}";
        return $"The character {Describe(character)} at index {index} is not in {encoding}, so the non-embedded Standard 14 font {Name} cannot show it "
            + "(ISO 32000-2 §9.6.2.2, Annex D). Embed a font program that has a glyph for it (an embedded TrueType font) and show the text in that font instead.";
    }

    private static BuiltInEncoding EncodingOf(Standard14Font font) => font switch
    {
        Standard14Font.Symbol => BuiltInEncoding.Symbol,
        Standard14Font.ZapfDingbats => BuiltInEncoding.ZapfDingbats,
        _ => BuiltInEncoding.WinAnsi,
    };

    /// <summary>Unicode scalar value to code for one encoding, through the Adobe Glyph List (the ITC Zapf Dingbats list for ZapfDingbats); built once.</summary>
    private static Dictionary<int, byte> Codes(BuiltInEncoding encoding)
    {
        int slot = encoding switch
        {
            BuiltInEncoding.Symbol => 1,
            BuiltInEncoding.ZapfDingbats => 2,
            _ => 0,
        };
        if (Volatile.Read(ref CodesByEncoding[slot]) is { } known)
        {
            return known;
        }

        var codes = new Dictionary<int, byte>();
        ReadOnlySpan<short> table = GlyphNameTable.Table(encoding);
        for (int code = 0; code < 256; code++)
        {
            if (table[code] >= 0 && AdobeGlyphList.TryGetScalar(GlyphNameTable.Names[table[code]], encoding == BuiltInEncoding.ZapfDingbats, out int scalar))
            {
                codes.TryAdd(scalar, (byte)code);
            }
        }

        if (encoding == BuiltInEncoding.WinAnsi)
        {
            // Annex D.2 notes 5 and 6: the space and the hyphen are encoded a second time at 0xA0 and 0xAD.
            codes.TryAdd(0x00A0, 0xA0);
            codes.TryAdd(0x00AD, 0xAD);
        }

        Volatile.Write(ref CodesByEncoding[slot], codes);
        return codes;
    }

    /// <summary>The font dictionary (with its widths) and its descriptor, both new; the descriptor is added as an indirect object.</summary>
    private static CosDictionary CreateDictionary(PdfDocument document, Standard14Font font)
    {
        BuiltInEncoding encoding = EncodingOf(font);
        ReadOnlySpan<short> table = GlyphNameTable.Table(encoding);
        int first = 32;
        int last = 255;
        while (first < last && table[first] < 0)
        {
            first++;
        }

        while (last > first && table[last] < 0)
        {
            last--;
        }

        var widths = new CosArray();
        for (int code = first; code <= last; code++)
        {
            widths.Add(new CosInteger(table[code] >= 0 && Standard14Data.WidthOf(font, table[code]) is var width and >= 0 ? width : 0));
        }

        var name = new CosName(Standard14Data.PostScriptName(font));
        var descriptor = new CosDictionary
        {
            [KnownNames.Type] = FontNames.FontDescriptor,
            [FontNames.FontName] = name,
            [FontNames.Flags] = new CosInteger((long)Standard14Data.Flags(font)),
            [FontNames.FontBBox] = DrawingCache.Rectangle(Standard14Data.FontBBox(font)),
            [FontNames.ItalicAngle] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.ItalicAngle)),
            [FontNames.Ascent] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.Ascent)),
            [FontNames.Descent] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.Descent)),
            [FontNames.CapHeight] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.CapHeight)),
            [FontNames.XHeight] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.XHeight)),
            [FontNames.StemV] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.StemV)),
            [FontNames.StemH] = DrawingCache.Number(Standard14Data.Metric(font, Standard14Metric.StemH)),
        };

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = FontNames.Type1,
            [FontNames.BaseFont] = name,
        };
        if (encoding == BuiltInEncoding.WinAnsi)
        {
            dictionary[FontNames.Encoding] = FontNames.WinAnsiEncoding;
        }

        dictionary[FontNames.FirstChar] = new CosInteger(first);
        dictionary[FontNames.LastChar] = new CosInteger(last);
        dictionary[FontNames.Widths] = widths;
        dictionary[FontNames.FontDescriptor] = document.AddObject(descriptor);
        return dictionary;
    }
}
