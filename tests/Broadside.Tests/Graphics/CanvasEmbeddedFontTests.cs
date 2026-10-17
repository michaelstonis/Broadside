using System.Buffers.Binary;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Graphics;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// TrueType programs built in memory, embedded with <see cref="PdfDocument.EmbedTrueTypeFont(ReadOnlyMemory{byte}, int)"/>: nested
/// composite glyphs keep their components (renumbered), the <c>OS/2</c> <c>fsType</c> licence bits are honoured, programs that are
/// not TrueType are refused, and an older document is raised to PDF 1.2. ISO 32000-2 §9.7.4, §9.7.6, §9.9 and §9.9.2.
/// </summary>
public sealed class CanvasEmbeddedFontTests
{
    [Fact]
    public void A_nested_composite_glyph_keeps_every_component_renumbered_and_draws_its_original_outline()
    {
        byte[] program = Program();
        byte[] saved = Show(PdfDocument.Create(), program, "CD");

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(reopened.Pages[0].ExtractText().Glyphs[0].Font).DescendantFont!;
        FontProgram subset = descendant.Program!;
        FontProgram original = new TrueTypeFontProgramParser().Parse(program, new FontProgramContext())!;

        // .notdef, C (7) built from 5 (built from 2) and 4, and D (8): six glyphs of the original nine.
        Assert.Equal(6, subset.GlyphCount);
        Assert.Equal(Outline(original, 7), Outline(subset, descendant.GetGlyphId(7)));
        Assert.Equal(Outline(original, 8), Outline(subset, descendant.GetGlyphId(8)));
        Assert.Equal(Outline(original, 0), Outline(subset, 0));
        Assert.Equal("CD", reopened.Pages[0].ExtractText().Text);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_program_whose_licence_restricts_embedding_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => document.EmbedTrueTypeFont(Program(fsType: 0x0002)));
        Assert.Contains("restricted licence", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_program_that_forbids_subsetting_is_embedded_whole_without_a_subset_tag()
    {
        byte[] saved = Show(PdfDocument.Create(), Program(fsType: 0x0100), "D");

        using PdfDocument reopened = PdfDocument.Open(saved);
        var font = Assert.IsType<PdfType0Font>(reopened.Pages[0].ExtractText().Glyphs[0].Font);
        Assert.Equal("BroadsideTest", font.BaseFont);
        Assert.Equal(9, font.DescendantFont!.Program!.GlyphCount);
    }

    [Fact]
    public void A_program_that_is_not_truetype_is_refused_naming_why()
    {
        using PdfDocument document = PdfDocument.Create();
        var cff = new TrueTypeBuilder { Version = 0x4F54544F };
        cff.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 100, 100));
        cff.Overrides["glyf"] = null;
        cff.Overrides["loca"] = null;

        Assert.Contains("not a TrueType", Assert.Throws<ArgumentException>(() => document.EmbedTrueTypeFont("not a font"u8.ToArray())).Message, StringComparison.Ordinal);
        Assert.Contains("CFF", Assert.Throws<ArgumentException>(() => document.EmbedTrueTypeFont(cff.Build())).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.EmbedTrueTypeFont(Program(), -1));
    }

    [Fact]
    public void Embedding_raises_a_document_below_pdf_1_2()
    {
        PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.1"));
        byte[] saved = Show(document, Program(), "C", out bool raised);
        Assert.True(raised);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 2), reopened.Version);
        Assert.Equal("C", reopened.Pages[0].ExtractText().Text);
    }

    /// <summary>Shows <paramref name="text"/> in <paramref name="program"/> on page 1 of <paramref name="document"/>, saves and disposes it.</summary>
    private static byte[] Show(PdfDocument document, byte[] program, string text) => Show(document, program, text, out _);

    private static byte[] Show(PdfDocument document, byte[] program, string text, out bool raised)
    {
        using (document)
        {
            PdfCanvasFont font = document.EmbedTrueTypeFont(program);
            using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
            {
                canvas.BeginText();
                canvas.SetFont(font, 20);
                canvas.MoveText(72, 700);
                canvas.ShowText(text);
                canvas.EndText();
            }

            using var output = new MemoryStream();
            document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false));
            raised = document.Diagnostics.Any(diagnostic => diagnostic.Code == "VersionRaised" && diagnostic.Message.Contains("embedded TrueType", StringComparison.Ordinal));
            return output.ToArray();
        }
    }

    /// <summary>
    /// Nine glyphs: 0 .notdef; 2 and 4 rectangles; 5 a composite of 2; 7 ('C') a composite of 5 and 4; 8 ('D') a rectangle; 1, 3 and
    /// 6 unused rectangles. With an <c>OS/2</c> table carrying <paramref name="fsType"/>.
    /// </summary>
    private static byte[] Program(int fsType = 0)
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((67, 68, -60)))),
        };
        builder.Glyphs.AddRange(
        [
            TrueTypeBuilder.Rectangle(50, 0, 450, 700),
            TrueTypeBuilder.Rectangle(0, 0, 10, 10),
            TrueTypeBuilder.Rectangle(100, 0, 200, 100),
            TrueTypeBuilder.Rectangle(0, 0, 20, 20),
            TrueTypeBuilder.Rectangle(300, 0, 400, 700),
            TrueTypeBuilder.Composite((110, 20, 210, 120), (0x0003, 2, 10, 20, [])),
            TrueTypeBuilder.Rectangle(0, 0, 30, 30),
            TrueTypeBuilder.Composite((110, 0, 450, 700), (0x0003, 5, 0, 0, []), (0x0003, 4, 50, 0, [])),
            TrueTypeBuilder.Rectangle(60, 0, 500, 500),
        ]);
        byte[] os2 = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(os2, 4);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(4), 400);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(8), (ushort)fsType);
        builder.Overrides["OS/2"] = os2;
        return builder.Build();
    }

    /// <summary>A glyph's outline as text: its status, every verb and every point.</summary>
    private static string Outline(FontProgram program, int glyph)
    {
        var outline = new GlyphOutline();
        var description = new StringBuilder().Append(program.GetOutline(glyph, outline)).Append(' ');
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
}
