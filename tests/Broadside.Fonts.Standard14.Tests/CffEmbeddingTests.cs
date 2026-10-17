using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Fonts.Cff;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// CFF and OpenType-CFF programs embedded through <see cref="PdfDocument.EmbedFont(ReadOnlyMemory{byte}, int)"/> (issue #151): saved
/// as a Type 0 font over a CIDFontType0 whose <c>FontFile3</c> (<c>CIDFontType0C</c>) is a CID-keyed subset of the shown glyphs,
/// their subroutines inlined. The programs are the subroutine-heavy <see cref="CffTestFonts"/> made
/// from Liberation Sans. ISO 32000-2 §9.7.4.2, §9.9 and §9.9.2; Adobe Technical Notes #5176 and #5177.
/// </summary>
public sealed partial class CffEmbeddingTests
{
    private const string Twenty = "ABCDEFGHIJabcdefghij";

    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);
    private static readonly Lazy<byte[]> OpenTypeFont = new(() => CffTestFonts.OpenType(LiberationSans));
    private static readonly Lazy<byte[]> CidKeyedOpenTypeFont = new(() => CffTestFonts.OpenType(LiberationSans, cidKeyed: true));
    private static readonly Lazy<byte[]> BareFont = new(() => CffTestFonts.Cff(LiberationSans));
    private static readonly Lazy<byte[]> AccentFont = new(() => CffTestFonts.Cff(LiberationSans, accent: true));

    [Fact]
    public void An_opentype_cff_subset_of_twenty_glyphs_re_parses_through_the_cff_parser_with_the_original_outlines_and_widths()
    {
        byte[] saved = Draw(document => document.EmbedFont(OpenTypeFont.Value), Twenty);

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(Twenty, text.Text);
        var font = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font);
        Assert.Matches(SubsetName(), font.BaseFont);
        Assert.Equal("Identity-H", font.Encoding.Name);
        PdfCidFont descendant = font.DescendantFont!;
        Assert.Equal(PdfCidFontType.CidFontType0, descendant.CidFontType);
        Assert.Null(descendant.Descriptor!.FontFile2);
        CosStream fontFile = descendant.Descriptor.FontFile3!;
        Assert.Equal(new CosName("CIDFontType0C"), fontFile.Dictionary[new CosName("Subtype")]);

        // FontFile3 re-parses through the CFF parser (#51): .notdef and the twenty glyphs, a sliver of the whole program.
        byte[] program = reopened.DecodeStream(fontFile).ToArray();
        Assert.InRange(program.Length, 1, OpenTypeFont.Value.Length / 20);
        FontProgram subset = new CffFontProgramParser().Parse(program, new FontProgramContext())!;
        Assert.Equal(FontProgramFormat.Cff, subset.Format);
        Assert.Equal(21, subset.GlyphCount);
        AssertEveryGlyphDraws(subset);
        AssertSameGlyphs(text, descendant, subset, Parse(OpenTypeFont.Value));
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_cid_keyed_font_keeps_the_font_dicts_and_local_subroutines_of_both_halves_its_glyphs_come_from()
    {
        // The test font's glyphs below half its glyph count use Font DICT 0, the rest Font DICT 1, each with its own subroutines.
        FontProgram original = Parse(CidKeyedOpenTypeFont.Value);
        string late = string.Concat(Enumerable.Range(0x1F00, 0x100).Select(scalar => (char)scalar).Where(character => char.IsLetter(character) && UnicodeGlyph(original, character.ToString()) >= original.GlyphCount / 2).Take(8));
        Assert.Equal(8, late.Length);
        byte[] saved = Draw(document => document.EmbedFont(CidKeyedOpenTypeFont.Value), "Hello", late);

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram subset = new CffFontProgramParser().Parse(reopened.DecodeStream(descendant.Descriptor!.FontFile3!).ToArray(), new FontProgramContext())!;
        AssertSameGlyphs(text, descendant, subset, original);
        AssertPopplerDraws(saved, ["Hello", late], "CID Type 0C");
    }

    [Fact]
    public void A_bare_cff_program_embeds_as_cidfonttype0c_with_characters_mapped_through_its_glyph_names()
    {
        byte[] saved = Draw(document => document.EmbedFont(BareFont.Value), Twenty, "Hello, world");

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        Assert.Equal(PdfCidFontType.CidFontType0, descendant.CidFontType);
        CosStream fontFile = descendant.Descriptor!.FontFile3!;
        Assert.Equal(new CosName("CIDFontType0C"), fontFile.Dictionary[new CosName("Subtype")]);
        byte[] program = reopened.DecodeStream(fontFile).ToArray();
        FontProgram subset = new CffFontProgramParser().Parse(program, new FontProgramContext())!;
        Assert.Equal(FontProgramFormat.Cff, subset.Format);
        AssertSameGlyphs(text, descendant, subset, Parse(BareFont.Value));
        AssertEveryGlyphDraws(subset);
        AssertPopplerDraws(saved, [Twenty, "Hello, world"], "CID Type 0C");
    }

    [Fact]
    public void An_endchar_accent_keeps_its_components_and_the_glyph_ids_so_it_draws_as_in_the_original()
    {
        byte[] saved = Draw(document => document.EmbedFont(AccentFont.Value), "Áxyz");

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram original = Parse(AccentFont.Value);
        FontProgram subset = new CffFontProgramParser().Parse(reopened.DecodeStream(descendant.Descriptor!.FontFile3!).ToArray(), new FontProgramContext())!;

        // Name-keyed with its glyph ids: the accent's StandardEncoding codes still name A and acute, which are kept.
        Assert.Equal(original.GlyphCount, subset.GlyphCount);
        AssertSameGlyphs(text, descendant, subset, original);
        int aacute = UnicodeGlyph(original, "Á");
        Assert.NotEqual(Outline(original, UnicodeGlyph(original, "A")), Outline(original, aacute));
        foreach (string component in (string[])["A", "´"])
        {
            int glyph = UnicodeGlyph(original, component);
            Assert.Equal(Outline(original, glyph), Outline(subset, glyph));
        }

        AssertPopplerDraws(saved, ["Áxyz"], "CID Type 0C");
    }

    [Fact]
    public void Embedding_is_deterministic_and_poppler_draws_and_extracts_the_opentype_subset()
    {
        byte[] first = Draw(document => document.EmbedFont(OpenTypeFont.Value), Twenty, "Привет, мир! Ωμέγα");
        byte[] second = Draw(document => document.EmbedFont(OpenTypeFont.Value), Twenty, "Привет, мир! Ωμέγα");
        Assert.Equal(first, second);
        AssertPopplerDraws(first, [Twenty, "Привет, мир! Ωμέγα"], "CID Type 0C");
    }

    [Fact]
    public void Before_a_save_the_document_holds_the_whole_program_and_reads_its_text_and_outlines()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedFont(OpenTypeFont.Value);
        Show(document.Pages[0], font, Twenty);

        TextPage text = document.Pages[0].ExtractText();
        Assert.Equal(Twenty, text.Text);
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram original = Parse(OpenTypeFont.Value);
        Assert.Equal(original.GlyphCount, descendant.Program!.GlyphCount);
        AssertSameGlyphs(text, descendant, descendant.Program, original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Embedding_raises_an_older_document_to_pdf_1_3_for_cidfonttype0c(bool openType)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.2"));
        PdfCanvasFont font = document.EmbedFont(openType ? OpenTypeFont.Value : BareFont.Value);
        Show(document.Pages[0], font, "Raised");

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal(new PdfVersion(1, 3), reopened.Version);
        Assert.Equal("Raised", reopened.Pages[0].ExtractText().Text);
    }

    [Fact]
    public void Embed_true_type_font_still_refuses_cff_outlines_and_points_at_embed_font()
    {
        using PdfDocument document = PdfDocument.Create();
        ArgumentException refused = Assert.Throws<ArgumentException>(() => document.EmbedTrueTypeFont(OpenTypeFont.Value));
        Assert.Contains("EmbedFont", refused.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => document.EmbedTrueTypeFont(BareFont.Value));
    }

    /// <summary>Every glyph shown: its CID is its original glyph id, and the subset's glyph for the CID has its outline and width.</summary>
    private static void AssertSameGlyphs(TextPage text, PdfCidFont descendant, FontProgram subset, FontProgram original)
    {
        foreach (TextGlyph glyph in text.Glyphs)
        {
            int originalGlyph = UnicodeGlyph(original, text.GetGlyphText(glyph).ToString());
            Assert.Equal(originalGlyph, (int)glyph.CharacterCode);
            int subsetGlyph = descendant.GetGlyphId((int)glyph.CharacterCode);
            Assert.Equal(Outline(original, originalGlyph), Outline(subset, subsetGlyph));
            Assert.Equal(original.GetMetrics(originalGlyph).AdvanceWidth, subset.GetMetrics(subsetGlyph).AdvanceWidth);
            Assert.Equal(Math.Round(original.GetMetrics(originalGlyph).AdvanceWidth * 1000 / 2048, 3), descendant.GetWidth((int)glyph.CharacterCode), 3);
        }
    }

    /// <summary>Every glyph of the subset interprets: no subroutine call it makes dangles.</summary>
    private static void AssertEveryGlyphDraws(FontProgram subset)
    {
        for (int glyph = 0; glyph < subset.GlyphCount; glyph++)
        {
            _ = Outline(subset, glyph);
        }
    }

    /// <summary>qpdf checks the file; poppler lists the font as an embedded subset with ToUnicode, extracts the lines and draws ink.</summary>
    private static void AssertPopplerDraws(byte[] saved, string[] lines, string type)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Matches($@"[A-Z]{{6}}\+\S+\s+{type}\s+Identity-H\s+yes\s+yes\s+yes", output);
        Assert.Equal(lines, PopplerText(saved).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.True(DarkPixels(saved) > 100, "poppler drew no glyphs");
    }

    private static FontProgram Parse(byte[] program) => new CffFontProgramParser().Parse(program, new FontProgramContext())!;

    private static void Show(PdfPage page, PdfCanvasFont font, string text)
    {
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 18);
        canvas.MoveText(72, 600);
        canvas.ShowText(text);
        canvas.EndText();
    }

    /// <summary>poppler's text of page 1 (<c>pdftotext</c>), which reads the font's ToUnicode CMap.</summary>
    private static string PopplerText(byte[] file)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string pdf = Path.Combine(directory, "in.pdf");
            string text = Path.Combine(directory, "out.txt");
            File.WriteAllBytes(pdf, file);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pdftotext", ["-enc", "UTF-8", "-f", "1", "-l", "1", pdf, text]) { UseShellExecute = false })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return File.ReadAllText(text, Encoding.UTF8).Replace("\f", string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The pixels darker than mid-grey poppler draws on page 1 at 72 dpi.</summary>
    private static int DarkPixels(byte[] file)
    {
        (int exitCode, byte[] image, _) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-gray", "-f", "1", "-l", "1");
        Assert.Equal(0, exitCode);
        int headerFields = 0;
        int offset = 0;
        while (headerFields < 4)
        {
            while (char.IsWhiteSpace((char)image[offset]))
            {
                offset++;
            }

            while (!char.IsWhiteSpace((char)image[offset]))
            {
                offset++;
            }

            headerFields++;
        }

        return image.AsSpan(offset + 1).ToArray().Count(value => value < 128);
    }

    /// <summary>The glyph the program's Unicode map gives the text's first character.</summary>
    private static int UnicodeGlyph(FontProgram program, string text)
    {
        int scalar = Rune.GetRuneAt(text, 0).Value;
        if (program.CharacterMaps.FirstOrDefault(map => map is { PlatformId: 3, EncodingId: 1 }) is { } map)
        {
            return map.GetGlyphId(scalar);
        }

        // A bare CFF program: the glyph named after the character, as the test fonts name them.
        string name = scalar switch
        {
            >= 'A' and <= 'Z' or >= 'a' and <= 'z' => ((char)scalar).ToString(),
            0xB4 => "acute",
            0xC1 => "Aacute",
            _ => $"uni{scalar:X4}",
        };
        return program.TryGetGlyphId(name, out int glyph) ? glyph : 0;
    }

    /// <summary>A glyph's outline as text: every verb and point, so two outlines compare equal only when they draw the same.</summary>
    private static string Outline(FontProgram program, int glyph)
    {
        var outline = new GlyphOutline();
        GlyphOutlineStatus status = program.GetOutline(glyph, outline);
        Assert.True(status is GlyphOutlineStatus.Complete or GlyphOutlineStatus.Empty, $"glyph {glyph}: {status}");
        var description = new StringBuilder().Append(status).Append(' ');
        foreach (PathVerb verb in outline.Path.Verbs)
        {
            description.Append(verb).Append(' ');
        }

        foreach (PathPoint point in outline.Path.Points)
        {
            description.Append(System.Globalization.CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R} ");
        }

        return description.ToString();
    }

    /// <summary>Creates a document, shows each line on page 1 in the font <paramref name="embed"/> returns, and saves it.</summary>
    private static byte[] Draw(Func<PdfDocument, PdfCanvasFont> embed, params string[] lines)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = embed(document);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 24);
            canvas.MoveText(72, 700);
            canvas.SetTextLeading(30);
            foreach (string line in lines)
            {
                canvas.ShowText(line);
                canvas.MoveToNextLine();
            }

            canvas.EndText();
        }

        return Saved(document);
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithUpdateModificationDate(false));
        return stream.ToArray();
    }

    [GeneratedRegex(@"^[A-Z]{6}\+LiberationSans$")]
    private static partial Regex SubsetName();
}
