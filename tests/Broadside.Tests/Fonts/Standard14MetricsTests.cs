using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The Standard 14 metrics in the core against the Adobe Core 14 AFM files they were generated from, read here independently
/// (Adobe Technical Note #5004 §8, <c>C</c>, <c>WX</c> and <c>N</c> of each <c>StartCharMetrics</c> line). Every glyph of every
/// font is reached through a <c>Differences</c> array, so the whole table is checked through the public font model.
/// ISO 32000-2 §9.6.2.2, §9.6.5.1, Annex D.2, D.5, D.6.
/// </summary>
public class Standard14MetricsTests
{
    public static TheoryData<string, Standard14Font> Fonts => new()
    {
        { "Courier", Standard14Font.Courier },
        { "Courier-Bold", Standard14Font.CourierBold },
        { "Courier-Oblique", Standard14Font.CourierOblique },
        { "Courier-BoldOblique", Standard14Font.CourierBoldOblique },
        { "Helvetica", Standard14Font.Helvetica },
        { "Helvetica-Bold", Standard14Font.HelveticaBold },
        { "Helvetica-Oblique", Standard14Font.HelveticaOblique },
        { "Helvetica-BoldOblique", Standard14Font.HelveticaBoldOblique },
        { "Times-Roman", Standard14Font.TimesRoman },
        { "Times-Bold", Standard14Font.TimesBold },
        { "Times-Italic", Standard14Font.TimesItalic },
        { "Times-BoldItalic", Standard14Font.TimesBoldItalic },
        { "Symbol", Standard14Font.Symbol },
        { "ZapfDingbats", Standard14Font.ZapfDingbats },
    };

    [Theory]
    [MemberData(nameof(Fonts))]
    public void Every_glyph_of_every_font_has_its_AFM_width(string fontName, Standard14Font expectedFont)
    {
        AfmFile afm = AfmFile.Read(fontName);
        foreach (AfmGlyph[] chunk in afm.Glyphs.Chunk(255))
        {
            string differences = string.Join(' ', chunk.Select(glyph => "/" + glyph.Name));
            using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{fontName} /Encoding << /Differences [1 {differences}] >> >>");
            PdfSimpleFont font = FontPdf.SimpleFont(document);

            Assert.Equal(expectedFont, font.Standard14);
            for (int index = 0; index < chunk.Length; index++)
            {
                byte code = (byte)(index + 1);
                Assert.Equal(chunk[index].Name, font.GetGlyphName(code));
                Assert.Equal(chunk[index].Width, font.GetWidth(code));
            }

            Assert.Empty(document.Diagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void Without_an_encoding_each_code_has_the_glyph_the_AFM_file_encodes_and_other_codes_are_notdef(string fontName, Standard14Font expectedFont)
    {
        AfmFile afm = AfmFile.Read(fontName);
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{fontName} >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);
        double notDefWidth = fontName.StartsWith("Courier", StringComparison.Ordinal) ? 600 : 250;

        Assert.Equal(expectedFont, font.Standard14);
        for (int code = 0; code < 256; code++)
        {
            AfmGlyph? glyph = afm.Glyphs.SingleOrDefault(candidate => candidate.Code == code);
            Assert.Equal(glyph?.Name ?? ".notdef", font.GetGlyphName((byte)code));
            Assert.Equal(glyph?.Width ?? notDefWidth, font.GetWidth((byte)code));
        }

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void A_font_without_a_descriptor_gets_one_synthesized_from_its_AFM_file(string fontName, Standard14Font expectedFont)
    {
        AfmFile afm = AfmFile.Read(fontName);
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{fontName} >>");
        PdfFontDescriptor descriptor = FontPdf.Font(document).Descriptor!;

        Assert.True(descriptor.IsSynthesized);
        Assert.Null(descriptor.Dictionary);
        Assert.Equal(fontName, descriptor.FontName);
        double[] box = afm.Numbers("FontBBox");
        Assert.Equal(new PdfRectangle(box[0], box[1], box[2], box[3]), descriptor.FontBBox);
        Assert.Equal(afm.Numbers("ItalicAngle")[0], descriptor.ItalicAngle);
        Assert.Equal(afm.Numbers("StdVW")[0], descriptor.StemV);
        Assert.Equal(afm.Numbers("StdHW")[0], descriptor.StemH);
        bool symbolic = expectedFont is Standard14Font.Symbol or Standard14Font.ZapfDingbats;
        Assert.Equal(symbolic ? box[3] : afm.Numbers("Ascender")[0], descriptor.Ascent);
        Assert.Equal(symbolic ? box[1] : afm.Numbers("Descender")[0], descriptor.Descent);
        Assert.Equal(symbolic ? box[3] : afm.Numbers("CapHeight")[0], descriptor.CapHeight);
        Assert.Equal(symbolic ? 0 : afm.Numbers("XHeight")[0], descriptor.XHeight);
        Assert.Equal(0, descriptor.MissingWidth);
        Assert.Null(descriptor.FontFile);
    }

    [Fact]
    public void Synthesized_flags_follow_the_AFM_files()
    {
        // Courier 33 (FixedPitch, Nonsymbolic), Helvetica 32, Times 34 (Serif), +64 for a slanted font, Symbol and ZapfDingbats 4.
        (string Name, PdfFontFlags Flags)[] expected =
        [
            ("Courier", (PdfFontFlags)33), ("Courier-BoldOblique", (PdfFontFlags)97), ("Helvetica", (PdfFontFlags)32),
            ("Helvetica-Oblique", (PdfFontFlags)96), ("Times-Bold", (PdfFontFlags)34), ("Times-Italic", (PdfFontFlags)98),
            ("Symbol", PdfFontFlags.Symbolic), ("ZapfDingbats", PdfFontFlags.Symbolic),
        ];
        foreach ((string name, PdfFontFlags flags) in expected)
        {
            using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{name} >>");
            Assert.Equal(flags, FontPdf.Font(document).Descriptor!.Flags);
        }
    }

    private sealed record AfmGlyph(int Code, double Width, string Name);

    private sealed class AfmFile
    {
        private readonly Dictionary<string, string> _keys = [];

        public List<AfmGlyph> Glyphs { get; } = [];

        public static AfmFile Read(string fontName)
        {
            string path = Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside", "Fonts", "Data", "Afm", fontName + ".afm");
            var file = new AfmFile();
            bool inMetrics = false;
            foreach (string raw in File.ReadLines(path, Encoding.ASCII))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string key = line.Split(' ', 2)[0];
                if (key == "StartCharMetrics")
                {
                    inMetrics = true;
                }
                else if (key == "EndCharMetrics")
                {
                    inMetrics = false;
                }
                else if (inMetrics)
                {
                    Dictionary<string, string> fields = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(field => field.Split(' ', 2))
                        .GroupBy(parts => parts[0])
                        .ToDictionary(group => group.Key, group => group.First()[1]);
                    file.Glyphs.Add(new AfmGlyph(
                        int.Parse(fields["C"], CultureInfo.InvariantCulture),
                        double.Parse(fields["WX"], CultureInfo.InvariantCulture),
                        fields["N"]));
                }
                else
                {
                    file._keys.TryAdd(key, line.Length > key.Length ? line[(key.Length + 1)..] : string.Empty);
                }
            }

            return file;
        }

        public double[] Numbers(string key) =>
            [.. _keys[key].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture))];
    }
}
