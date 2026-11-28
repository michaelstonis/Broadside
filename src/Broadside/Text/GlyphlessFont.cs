using System.Globalization;
using System.IO.Compression;
using System.Text;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Text;

/// <summary>
/// The font of OCR text layers (issue #649): a Type 0 font with <c>Identity-H</c> whose two-byte codes are UTF-16 code units, over a
/// CIDFontType2 whose embedded TrueType program (<see cref="GlyphlessFontData"/>) has one blank glyph that every CID maps to, and a
/// <c>ToUnicode</c> CMap that maps every code back to its code unit. It shows any script without a real font, and its objects are
/// written once per document, a few hundred bytes.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.4 (CIDFontType2, <c>DW</c>, <c>CIDToGIDMap</c>), §9.7.5.2 (<c>Identity-H</c>), §9.7.6 (Type 0 fonts), §9.8
/// (font descriptor), §9.9 (<c>FontFile2</c>) and §9.10.3 (<c>ToUnicode</c>). Every glyph is 500 units wide (<c>DW</c> and the
/// program's <c>hmtx</c> agree, ISO 19005-2 §6.2.11.5); the writer stretches each word with <c>Tz</c> to its box. The blank glyph is
/// glyph 1, not .notdef, so PDF/A-2 and later accept the text (ISO 19005-2 §6.2.11.8). A character outside the BMP is shown as
/// the code of U+FFFD; the canvas then adds <c>ActualText</c> with the real text (§14.9.4), because <see cref="OnShown"/> says the
/// <c>ToUnicode</c> map does not give it back.
/// </para>
/// </remarks>
internal sealed class GlyphlessFont : PdfCanvasFont
{
    /// <summary>The font's name, as <c>BaseFont</c> and <c>FontName</c>.</summary>
    public const string FontName = "GlyphLessFont";

    /// <summary>The width of every glyph, in glyph space units.</summary>
    public const double GlyphWidth = 500;

    private const int Replacement = 0xFFFD;

    private GlyphlessFont(PdfDocument document, CosReference reference)
        : base(document, reference)
    {
    }

    /// <inheritdoc/>
    public override string Name => FontName;

    /// <inheritdoc/>
    public override double Ascent => 800;

    /// <inheritdoc/>
    public override double Descent => -200;

    /// <inheritdoc/>
    public override double CapHeight => 800;

    /// <inheritdoc/>
    public override double XHeight => 500;

    /// <inheritdoc/>
    public override PdfRectangle FontBBox => new(0, -200, 500, 800);

    /// <inheritdoc/>
    internal override int CodeLength => 2;

    /// <summary>Returns the document's glyphless font, adding its objects the first time.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The font.</returns>
    public static GlyphlessFont For(PdfDocument document)
    {
        if (document.GlyphlessFont is { } existing && document.Resolve(existing.Reference) is CosDictionary)
        {
            return existing;
        }

        byte[] program = GlyphlessFontData.Program.ToArray();
        CosReference fontFile = document.AddObject(new CosStream(new CosDictionary { [FontNames.Length1] = new CosInteger(program.Length) }, program));
        CosReference descriptor = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = new CosName("FontDescriptor"),
            [FontNames.FontName] = new CosName(FontName),
            [FontNames.Flags] = new CosInteger(1 | 4), // FixedPitch, Symbolic (§9.8.2, Table 121)
            [FontNames.FontBBox] = new CosArray([new CosInteger(0), new CosInteger(-200), new CosInteger(500), new CosInteger(800)]),
            [FontNames.ItalicAngle] = new CosInteger(0),
            [FontNames.Ascent] = new CosInteger(800),
            [FontNames.Descent] = new CosInteger(-200),
            [FontNames.CapHeight] = new CosInteger(800),
            [FontNames.StemV] = new CosInteger(80),
            [FontNames.FontFile2] = fontFile,
        });
        CosReference cidToGid = document.AddObject(new CosStream(
            new CosDictionary { [FilterNames.Filter] = FilterNames.FlateDecode },
            CidToGidMap()));
        CosReference cidFont = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = CompositeFontNames.CidFontType2,
            [FontNames.BaseFont] = new CosName(FontName),
            [CompositeFontNames.CidSystemInfo] = new CosDictionary
            {
                [CompositeFontNames.Registry] = new CosString("Adobe"u8),
                [CompositeFontNames.Ordering] = new CosString("Identity"u8),
                [CompositeFontNames.Supplement] = new CosInteger(0),
            },
            [FontNames.FontDescriptor] = descriptor,
            [CompositeFontNames.DW] = new CosInteger((int)GlyphWidth),
            [CompositeFontNames.CidToGidMap] = cidToGid,
        });
        CosReference toUnicode = document.AddObject(new CosStream(new CosDictionary(), ToUnicodeCMap()));
        CosReference font = document.AddObject(new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = FontNames.Type0,
            [FontNames.BaseFont] = new CosName(FontName),
            [FontNames.Encoding] = new CosName("Identity-H"),
            [CompositeFontNames.DescendantFonts] = new CosArray([cidFont]),
            [CompositeFontNames.ToUnicode] = toUnicode,
        });
        document.RequireFeature(Writing.VersionedFeatures.EmbeddedTrueTypeFont);
        var created = new GlyphlessFont(document, font);
        document.GlyphlessFont = created;
        return created;
    }

    /// <inheritdoc/>
    internal override bool TryGetGlyph(int scalar, out CanvasGlyph glyph)
    {
        int code = scalar is >= 0 and <= 0xFFFF and not (>= 0xD800 and <= 0xDFFF) ? scalar : Replacement;
        glyph = new CanvasGlyph((uint)code, GlyphWidth, 1, scalar);
        return true;
    }

    /// <inheritdoc/>
    internal override bool OnShown(in CanvasGlyph glyph, ReadOnlySpan<char> text) => glyph.Code == glyph.Scalar;

    /// <inheritdoc/>
    internal override string DescribeUnencodable(Rune character, int index) =>
        $"The character {Describe(character)} at index {index} cannot be shown in the glyphless font.";

    /// <summary>The <c>CIDToGIDMap</c> data, Flate-encoded: every CID from 0 to 65535 to glyph 1 (§9.7.4.1, Table 115).</summary>
    private static byte[] CidToGidMap()
    {
        var map = new byte[65536 * 2];
        for (int cid = 0; cid < 65536; cid++)
        {
            map[(cid * 2) + 1] = 1;
        }

        using var encoded = new MemoryStream();
        using (var zlib = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(map);
        }

        return encoded.ToArray();
    }

    /// <summary>
    /// A <c>ToUnicode</c> CMap mapping every two-byte code to the code unit it is (§9.10.3, Adobe TN 5411): one <c>bfrange</c> per
    /// high byte, so no range crosses a last-byte boundary; the surrogate codes D800 to DFFF are never written and not mapped.
    /// </summary>
    private static byte[] ToUnicodeCMap()
    {
        var ranges = new List<int>();
        for (int high = 0; high < 256; high++)
        {
            if (high is < 0xD8 or > 0xDF)
            {
                ranges.Add(high);
            }
        }

        var cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        for (int start = 0; start < ranges.Count; start += 100)
        {
            int count = Math.Min(100, ranges.Count - start);
            cmap.Append(CultureInfo.InvariantCulture, $"{count} beginbfrange\n");
            for (int index = start; index < start + count; index++)
            {
                cmap.Append(CultureInfo.InvariantCulture, $"<{ranges[index]:X2}00> <{ranges[index]:X2}FF> <{ranges[index]:X2}00>\n");
            }

            cmap.Append("endbfrange\n");
        }

        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(cmap.ToString());
    }
}
