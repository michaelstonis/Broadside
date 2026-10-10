using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Glyph outlines, metrics and glyph selection of the embedded Type 1 corpus files, through the public document API. Expected
/// outlines are the coordinates of the charstrings <c>minimal_type1()</c> in <c>tests/Corpus/generate.py</c> writes (worked out by
/// hand from the Type 1 Font Format command definitions). ISO 32000-2 §9.6.2, §9.6.5.2, §9.9.
/// </summary>
public sealed class Type1CorpusTests
{
    /// <summary>Glyph name and its outline: one glyph per charstring feature.</summary>
    public static TheoryData<string, string> Outlines => new()
    {
        // hsbw, hints, rmoveto, hlineto and vlineto.
        { "H", "M 100,0 L 300,0 L 300,300 L 500,300 L 500,0 L 700,0 L 700,700 L 500,700 L 500,400 L 300,400 L 300,700 L 100,700 Z" },

        // callsubr nested two deep (Subrs 4 calls Subrs 6), return.
        { "I", "M 100,0 L 300,0 L 300,700 L 100,700 Z" },

        // vhcurveto, hvcurveto and rrcurveto.
        { "O", "M 50,350 C 50,157 207,0 400,0 C 593,0 750,157 750,350 C 750,543 593,700 400,700 C 207,700 50,543 50,350 Z" },

        // Flex (Type 1 Font Format §8.3, the example of Figure 8e): Subrs 0-2, OtherSubrs 0-2, pop and setcurrentpoint.
        { "F", "M 100,-10 C 115,-10 125,0 150,0 C 175,0 185,-10 200,-10 L 200,90 L 100,90 Z" },

        // Hint replacement (§8.2): 5 1 3 callothersubr pop callsubr runs Subrs 5, which only hints; dotsection.
        { "E", "M 100,0 L 500,0 L 500,100 L 200,100 L 200,600 L 500,600 L 500,700 L 100,700 Z" },

        // closepath leaves the current point at the last point (§6.4), so the second rmoveto is relative to (50, 700).
        { "T", "M 50,600 L 450,600 L 450,700 L 50,700 Z M 200,0 L 300,0 L 300,600 L 200,600 Z" },
        { "A", "M 20,0 L 220,0 L 370,500 L 520,0 L 720,0 L 470,700 L 270,700 Z" },

        // sbw.
        { "acute", "M 200,600 L 300,600 L 400,700 L 300,700 Z" },

        // seac 200 150 150 65 194: A, then acute moved by (sbx + adx − asb, ady) = (20 + 150 − 200, 150) (TN 5015 §6 errata).
        { "Aacute", "M 20,0 L 220,0 L 370,500 L 520,0 L 720,0 L 470,700 L 270,700 Z M 170,750 L 270,750 L 370,850 L 270,850 Z" },
    };

    /// <summary>The well-formed file and the three layouts producers embed besides it.</summary>
    public static TheoryData<string> Files => new(["text-type1-embedded.pdf", "text-type1-pfb.pdf", "text-type1-hex-eexec.pdf", "text-type1-bad-lengths.pdf"]);

    [Theory]
    [MemberData(nameof(Outlines))]
    public void Each_glyph_of_the_embedded_Type_1_program_has_its_cubic_outline(string glyphName, string expected)
    {
        using PdfDocument document = Open("text-type1-embedded.pdf");
        FontProgram program = Font(document).Program!;

        Assert.True(program.TryGetGlyphId(glyphName, out int glyph));
        Assert.Equal(expected, OutlineText.Of(program, glyph));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_program_exposes_its_glyph_names_font_matrix_bounding_box_and_name()
    {
        using PdfDocument document = Open("text-type1-embedded.pdf");
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(Font(document).Program);

        Assert.Equal(FontProgramFormat.Type1, program.Format);
        Assert.Equal(10, program.GlyphCount);
        Assert.Equal(
            [".notdef", "H", "I", "O", "F", "E", "T", "A", "acute", "Aacute"],
            Enumerable.Range(0, program.GlyphCount).Select(program.GetGlyphName).ToArray());
        Assert.Equal("[0.001 0 0 0.001 0 0]", program.FontMatrix.ToString());
        Assert.Equal(new PdfRectangle(0, -10, 750, 850), program.FontBBox);
        Assert.Equal("BroadsideT1", program.PostScriptName);
        Assert.Equal("Empty", OutlineText.Of(program, 0));
        Assert.False(program.TryGetGlyphId("B", out int missing));
        Assert.Equal(0, missing);
        Assert.Null(program.GetGlyphName(10));
        Assert.Equal("Invalid", OutlineText.Of(program, 10));
    }

    [Fact]
    public void Advances_and_side_bearings_come_from_hsbw_and_sbw_including_a_width_computed_with_div()
    {
        using PdfDocument document = Open("text-type1-embedded.pdf");
        FontProgram program = Font(document).Program!;

        Assert.Equal(new GlyphMetrics(500, 0), program.GetMetrics(0));
        Assert.Equal(new GlyphMetrics(800, 100), program.GetMetrics(Glyph(program, "H")));
        Assert.Equal(new GlyphMetrics(500, 50), program.GetMetrics(Glyph(program, "T"))); // 50 500000 1000 div hsbw
        Assert.Equal(new GlyphMetrics(400, 200), program.GetMetrics(Glyph(program, "acute"))); // sbw
        Assert.Equal(new GlyphMetrics(740, 20), program.GetMetrics(Glyph(program, "Aacute"))); // the composite's own hsbw
        Assert.Equal(default, program.GetMetrics(-1));
    }

    [Fact]
    public void Without_an_Encoding_entry_the_font_uses_the_programs_built_in_encoding()
    {
        // ISO 32000-2 §9.6.5.2: StandardEncoding would map code 49 to "one" and code 193 to "grave"; the program maps them to H and Aacute.
        using PdfDocument document = Open("text-type1-embedded.pdf");
        PdfType1Font font = Font(document);
        IReadOnlyList<string> encoding = font.Program!.BuiltInEncoding!;

        Assert.Equal(256, encoding.Count);
        Assert.Equal(["H", "H", "I", "Aacute", "acute", ".notdef"], [encoding[49], encoding[72], encoding[73], encoding[193], encoding[194], encoding[50]]);
        Assert.Equal("H", font.GetGlyphName(49));
        Assert.Equal("Aacute", font.GetGlyphName(193));
        Assert.Equal(".notdef", font.GetGlyphName((byte)'B'));
        Assert.Equal(
            [1, 1, 2, 3, 4, 5, 6, 7, 9],
            "1HIOFETAÁ".Select(code => font.GetGlyphId((byte)code)).ToArray());
        Assert.Equal(0, font.GetGlyphId((byte)'B'));
        Assert.Equal(800, font.GetWidth(49));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_layout_yields_the_same_glyphs(string name)
    {
        using PdfDocument reference = Open("text-type1-embedded.pdf");
        using PdfDocument document = Open(name);
        FontProgram expected = Font(reference).Program!;
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(Font(document).Program);

        Assert.Equal(expected.GlyphCount, program.GlyphCount);
        for (int glyph = 0; glyph < program.GlyphCount; glyph++)
        {
            Assert.Equal(expected.GetGlyphName(glyph), program.GetGlyphName(glyph));
            Assert.Equal(OutlineText.Of(expected, glyph), OutlineText.Of(program, glyph));
            Assert.Equal(expected.GetMetrics(glyph), program.GetMetrics(glyph));
        }

        Assert.Equal(expected.BuiltInEncoding, program.BuiltInEncoding);
    }

    /// <summary>The broken layouts and the diagnostics their repair records on the font file stream (object 7).</summary>
    public static TheoryData<string, string[]> Repairs => new()
    {
        { "text-type1-embedded.pdf", [] },
        { "text-type1-pfb.pdf", ["FontType1PfbWrapped"] },
        { "text-type1-hex-eexec.pdf", ["FontType1HexEexec"] },
        { "text-type1-bad-lengths.pdf", ["FontType1Length1Repaired", "FontType1Length2Repaired"] },
    };

    [Theory]
    [MemberData(nameof(Repairs))]
    public void Each_layout_repair_is_recorded_once_on_the_font_file_stream(string name, string[] codes)
    {
        using PdfDocument document = Open(name);
        Assert.Empty(document.Diagnostics);

        _ = Font(document).Program;

        Assert.Equal(codes, document.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        Assert.All(document.Diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal(7, diagnostic.ObjectReference?.ObjectNumber);
        });
    }

    [Theory]
    [MemberData(nameof(Repairs))]
    public void Strict_mode_reads_the_well_formed_file_and_throws_on_the_first_repair(string name, string[] codes)
    {
        using PdfDocument document = Open(name, new PdfOptions().UseStrict());
        PdfType1Font font = Font(document);
        if (codes.Length == 0)
        {
            var outline = new GlyphOutline();
            for (int glyph = 0; glyph < font.Program!.GlyphCount; glyph++)
            {
                Assert.NotEqual(GlyphOutlineStatus.Invalid, font.Program.GetOutline(glyph, outline));
            }

            Assert.Empty(document.Diagnostics);
            return;
        }

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => font.Program);
        Assert.Equal(codes[0], exception.Diagnostic.Code);
    }

    internal static PdfDocument Open(string name, PdfOptions? options = null) =>
        PdfDocument.Open(Path.Combine(Corpus.Directory, name), options ?? new PdfOptions());

    internal static PdfType1Font Font(PdfDocument document) =>
        Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

    private static int Glyph(FontProgram program, string name) => program.TryGetGlyphId(name, out int glyph) ? glyph : -1;
}
