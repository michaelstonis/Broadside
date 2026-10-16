using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// TrueType programs embedded through <see cref="PdfDocument.EmbedTrueTypeFont(ReadOnlyMemory{byte}, int)"/> and shown on a canvas:
/// saved as a Type 0 font (CIDFontType2, <c>Identity-H</c>) whose <c>FontFile2</c> is a renumbered subset of the drawn glyphs with
/// their components, a subset tag, a <c>W</c> array and a <c>ToUnicode</c> CMap. ISO 32000-2 §9.7.4–§9.7.6, §9.8, §9.9 and §9.10.3.
/// </summary>
public sealed partial class TrueTypeEmbeddingTests
{
    /// <summary>Twenty distinct characters, each its own glyph in Liberation Sans.</summary>
    private const string Twenty = "ABCDEFGHIJabcdefghij";

    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    [Fact]
    public void Twenty_glyphs_of_liberation_sans_embed_as_a_subset_under_12_KB_that_re_parses_with_the_original_outlines_and_widths()
    {
        byte[] saved = Draw(document => document.EmbedTrueTypeFont(LiberationSans), Twenty);

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(Twenty, text.Text);
        var font = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font);
        Assert.Matches(SubsetName(), font.BaseFont);
        Assert.Equal("Identity-H", font.Encoding.Name);
        PdfCidFont descendant = font.DescendantFont!;
        Assert.Equal(PdfCidFontType.CidFontType2, descendant.CidFontType);
        Assert.Equal(font.BaseFont, descendant.BaseFont);
        Assert.Equal(font.BaseFont, descendant.Descriptor!.FontName);

        // The FontFile2 program re-parses through the TrueType parser (#50): .notdef and the twenty glyphs, nothing else.
        byte[] program = reopened.DecodeStream(descendant.Descriptor.FontFile2!).ToArray();
        Assert.InRange(program.Length, 1, 12_000);
        Assert.Equal(program.Length, ((Objects.CosInteger)descendant.Descriptor.FontFile2!.Dictionary[new Objects.CosName("Length1")]).Value);
        FontProgram subset = new TrueTypeFontProgramParser().Parse(program, new FontProgramContext())!;
        Assert.Equal(FontProgramFormat.TrueType, subset.Format);
        Assert.Equal(21, subset.GlyphCount);

        FontProgram original = Original();
        foreach (TextGlyph glyph in text.Glyphs)
        {
            int originalGlyph = UnicodeGlyph(original, text.GetGlyphText(glyph));
            int subsetGlyph = descendant.GetGlyphId((int)glyph.CharacterCode);
            Assert.Equal(Outline(original, originalGlyph), Outline(subset, subsetGlyph));
            Assert.Equal(original.GetMetrics(originalGlyph).AdvanceWidth, subset.GetMetrics(subsetGlyph).AdvanceWidth);
            Assert.Equal(Math.Round(original.GetMetrics(originalGlyph).AdvanceWidth * 1000 / 2048, 3), descendant.GetWidth((int)glyph.CharacterCode), 3);
        }

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void Cyrillic_greek_and_accented_text_re_reads_through_to_unicode_with_composite_glyphs_drawn_from_their_kept_components()
    {
        // é, Й and ά are composite glyphs in Liberation Sans: their components must be in the subset too.
        string[] lines = ["Привет, мир! Йё", "Ωμέγα ά", "Café crème"];
        byte[] saved = Draw(document => document.EmbedTrueTypeFont(LiberationSans), lines);

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(string.Concat(lines).Replace(" ", string.Empty, StringComparison.Ordinal), text.Text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal));
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram original = Original();
        int shown = 0;
        var distinct = new HashSet<int>();
        foreach (TextGlyph glyph in text.Glyphs)
        {
            int originalGlyph = UnicodeGlyph(original, text.GetGlyphText(glyph));
            Assert.Equal(Outline(original, originalGlyph), Outline(descendant.Program!, descendant.GetGlyphId((int)glyph.CharacterCode)));
            shown += distinct.Add(originalGlyph) ? 1 : 0;
        }

        // .notdef, the shown glyphs and the components of the composite ones.
        Assert.True(descendant.Program!.GlyphCount > shown + 1, $"{descendant.Program.GlyphCount} glyphs for {shown} shown: the composites' components are missing.");
        Assert.InRange(descendant.Program.GlyphCount, shown + 2, shown + 12);

        // poppler: the file checks, the font is an embedded subset with a ToUnicode CMap, the text extracts and the page has ink.
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Matches(@"[A-Z]{6}\+LiberationSans\s+CID TrueType\s+Identity-H\s+yes\s+yes\s+yes", output);
        Assert.Equal(lines, PopplerText(saved).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.True(DarkPixels(saved) > 500);
    }

    [Fact]
    public void Subsetting_writes_the_same_bytes_on_every_run()
    {
        byte[] first = Draw(document => document.EmbedTrueTypeFont(LiberationSans), Twenty, "Привет, мир!");
        byte[] second = Draw(document => document.EmbedTrueTypeFont(LiberationSans), Twenty, "Привет, мир!");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Every_canvas_and_page_shares_one_font_that_grows_across_incremental_saves()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        Assert.Same(font.Font.Dictionary, document.EmbedTrueTypeFont(LiberationSans).Font.Dictionary);
        Assert.Equal("LiberationSans", font.Name);
        Show(document.Pages[0], document.EmbedTrueTypeFont(LiberationSans), "Hello", 700);
        byte[] first = SaveIncremental(document);
        string firstName = font.Font.BaseFont!;

        PdfPage second = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        Show(second, font, "Жук", 700);
        Show(document.Pages[0], font, "Жаба", 650);
        byte[] saved = SaveIncremental(document);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("HelloЖаба", reopened.Pages[0].ExtractText().Text);
        Assert.Equal("Жук", reopened.Pages[1].ExtractText().Text);
        PdfFont shared = Assert.Single(reopened.Pages[0].ExtractText().Glyphs.Concat(reopened.Pages[1].ExtractText().Glyphs).Select(glyph => glyph.Font!).DistinctBy(font => font.Reference));
        Assert.Matches(SubsetName(), shared.BaseFont);
        Assert.NotEqual(firstName, shared.BaseFont);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);

        // The first update's subset still shows its text in the file it was saved as.
        using PdfDocument earlier = PdfDocument.Open(first);
        Assert.Equal("Hello", earlier.Pages[0].ExtractText().Text);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    private static byte[] SaveIncremental(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithUpdateModificationDate(false).WithIncrementalUpdate(true));
        return stream.ToArray();
    }

    [Fact]
    public void A_font_found_through_the_font_resolvers_embeds_and_none_is_found_without_them()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStandard14Fonts());
        PdfCanvasFont? font = document.EmbedTrueTypeFont(new FontQuery("Helvetica-Bold") { FontType = PdfFontType.TrueType, Standard14 = Standard14Font.HelveticaBold });
        Assert.NotNull(font);
        Assert.Equal("LiberationSans-Bold", font.Name);
        Show(document.Pages[0], font, "Bold Ж");
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal("Bold Ж", reopened.Pages[0].ExtractText().Text);

        using PdfDocument bare = PdfDocument.Create(new PdfOptions().UseSystemFontResolver(null));
        Assert.Null(bare.EmbedTrueTypeFont(new FontQuery("NoSuchFont-Regular") { FontType = PdfFontType.TrueType }));
    }

    [Fact]
    public void Text_shown_before_a_save_extracts_and_draws_from_the_document_in_memory()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        Show(document.Pages[0], font, "Ωμέγα é");

        TextPage text = document.Pages[0].ExtractText();
        Assert.Equal("Ωμέγα é", text.Text);
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram original = Original();
        foreach (TextGlyph glyph in text.Glyphs)
        {
            if (text.GetGlyphText(glyph) is not " ")
            {
                Assert.Equal(Outline(original, UnicodeGlyph(original, text.GetGlyphText(glyph))), Outline(descendant.Program!, descendant.GetGlyphId((int)glyph.CharacterCode)));
            }
        }
    }

    [Fact]
    public void A_character_the_program_has_no_glyph_for_is_refused_by_name_and_nothing_is_written()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        Assert.False(font.CanEncode("日本"));
        Assert.True(font.CanEncode("Ωμέγα"));
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 12);
        ArgumentException refused = Assert.Throws<ArgumentException>(() => canvas.ShowText("ab日"));
        Assert.Contains("'日' (U+65E5) at index 2", refused.Message, StringComparison.Ordinal);
        Assert.Contains("LiberationSans", refused.Message, StringComparison.Ordinal);
    }

    private static void Show(PdfPage page, PdfCanvasFont font, string text, double y = 600)
    {
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 18);
        canvas.MoveText(72, y);
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

    /// <summary>The Liberation Sans program parsed on its own, the independent reference for outlines and widths.</summary>
    private static FontProgram Original() => new TrueTypeFontProgramParser().Parse(LiberationSans, new FontProgramContext())!;

    /// <summary>The glyph the program's Unicode cmap gives the text's first character.</summary>
    private static int UnicodeGlyph(FontProgram program, ReadOnlySpan<char> text)
    {
        FontCharacterMap map = program.CharacterMaps.First(map => map is { PlatformId: 3, EncodingId: 1 });
        return map.GetGlyphId(Rune.GetRuneAt(text.ToString(), 0).Value);
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
