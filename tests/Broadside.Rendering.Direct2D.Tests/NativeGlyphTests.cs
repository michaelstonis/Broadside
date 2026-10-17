using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Glyph runs of TrueType and OpenType programs are drawn by DirectWrite from a font face loaded from the embedded bytes, created
/// once per program per renderer; every other program, and every program when outlines are forced, draws from its outlines; an sfnt
/// program DirectWrite cannot use falls back to its outlines with a diagnostic (issue #131, open decision 7; ISO 32000-2 §9.9).
/// </summary>
public sealed class NativeGlyphTests
{
    [Theory]
    [InlineData("text-truetype-embedded.pdf", 1)]
    [InlineData("text-truetype-composite.pdf", 1)]
    [InlineData("text-opentype-cff-embedded.pdf", 1)]
    [InlineData("text-cid-identity-h.pdf", 1)]
    [InlineData("text-type1-embedded.pdf", 0)]
    [InlineData("text-cff-embedded.pdf", 0)]
    [InlineData("text-type3.pdf", 0)]
    public void A_font_face_is_created_once_per_sfnt_program_per_renderer(string fileName, int faces)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using (renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions()))
        {
        }

        using (renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions()))
        {
        }

        Assert.Equal(faces, renderer.FontFacesCreated);
        Assert.DoesNotContain(renderer.Diagnostics, diagnostic => diagnostic.Code == "BackendNativeGlyphsUnavailable");
    }

    [Fact]
    public void Native_glyphs_are_drawn_by_DirectWrite_not_from_the_outlines()
    {
        using PdfDocument document = GoldenCorpus.Open("text-truetype-embedded.pdf");
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap native = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());
        using Bitmap outlines = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.False(native.Pixels.SequenceEqual(outlines.Pixels));
    }

    [Theory]
    [InlineData("text-type1-embedded.pdf")]
    [InlineData("text-cff-embedded.pdf")]
    public void Glyphs_of_other_programs_are_drawn_from_their_outlines(string fileName)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap native = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());
        using Bitmap outlines = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.True(native.Pixels.SequenceEqual(outlines.Pixels));
        Assert.Empty(renderer.Diagnostics);
    }

    [Fact]
    public void Forcing_outlines_creates_no_font_face()
    {
        using PdfDocument document = GoldenCorpus.Open("text-truetype-embedded.pdf");
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using (renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true)))
        {
        }

        Assert.Equal(0, renderer.FontFacesCreated);
        Assert.Empty(renderer.Diagnostics);
    }

    [Fact]
    public void A_TrueType_program_that_needed_a_repair_draws_from_its_outlines_with_a_diagnostic()
    {
        // The program of text-truetype-embedded.pdf with a Length1 longer than the program (ISO 32000-2 §9.9 Table 125): the reader
        // repairs it, so DirectWrite never sees it and its glyphs are drawn from their outlines.
        using PdfDocument corpus = GoldenCorpus.Open("text-truetype-embedded.pdf");
        byte[] program = corpus.DecodeStream(corpus.Pages[0].GetFont("F1")!.Descriptor!.FontFile2!).ToArray();
        using PdfDocument document = PdfDocument.Open(DamagedTrueTypePage(program));
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap native = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());
        Diagnostic diagnostic = Assert.Single(renderer.Diagnostics);
        using Bitmap outlines = renderer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.Equal("BackendNativeGlyphsUnavailable", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.True(native.Pixels.SequenceEqual(outlines.Pixels));
        Assert.Contains(native.Pixels.ToArray(), value => value != 255);
    }

    private static byte[] DamagedTrueTypePage(byte[] program)
    {
        string font = "<< /Type /Font /Subtype /TrueType /BaseFont /Damaged /FirstChar 72 /LastChar 73 /Widths [800 400] /FontDescriptor 6 0 R >>";
        const string descriptor = "<< /Type /FontDescriptor /FontName /Damaged /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 100 /FontFile2 7 0 R >>";
        string stream = ContentSamples.Stream(Encoding.Latin1.GetString(program), string.Create(CultureInfo.InvariantCulture, $"/Length1 {program.Length + 100}"));
        return ContentSamples.OnePage("BT /F1 72 Tf 72 600 Td (HIH) Tj ET"u8.ToArray(), "/Font << /F1 5 0 R >>", font, descriptor, stream);
    }
}
