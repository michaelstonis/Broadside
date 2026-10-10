using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Type 0 fonts over CIDFontType2 descendants in the composite corpus files, through the public document API. Expected values are
/// the ones <c>tests/Corpus/generate.py</c> writes and the corpus README lists. ISO 32000-2 §9.7.4, §9.7.5, §9.7.6.
/// </summary>
public sealed class CompositeFontCorpusTests
{
    [Fact]
    public void Identity_H_with_an_identity_glyph_map_selects_glyphs_by_CID_and_widths_from_W_and_DW()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-identity-h.pdf");
        PdfType0Font font = Font(document);

        CidGlyph[] glyphs = Glyphs(font, [0x00, 0x01, 0x00, 0x03, 0x00, 0x02]);

        Assert.Same(CMap.IdentityH, font.Encoding);
        Assert.Equal(WritingMode.Horizontal, font.WritingMode);
        Assert.Equal([1, 3, 2], glyphs.Select(glyph => glyph.Cid));
        Assert.Equal([1, 0, 2], glyphs.Select(glyph => glyph.GlyphId));
        Assert.Equal([2, 2, 2], glyphs.Select(glyph => glyph.Code.Length));
        Assert.Equal([19.2, 14.4, 9.6], glyphs.Select(glyph => Math.Round(glyph.Width / 1000 * 24, 6)));
        Assert.All(glyphs, glyph => Assert.False(glyph.AppliesWordSpacing));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_descendant_CIDFont_exposes_its_dictionary_entries_and_TrueType_outlines()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-identity-h.pdf");
        PdfCidFont descendant = Assert.IsType<PdfCidFontType2>(Font(document).DescendantFont);

        Assert.Equal(PdfCidFontType.CidFontType2, descendant.CidFontType);
        Assert.Equal("BroadsideMinimal", descendant.BaseFont);
        Assert.Equal(new CidSystemInfo("Adobe", "Identity", 0), descendant.SystemInfo);
        Assert.True(descendant.IsEmbedded);
        Assert.Equal("BroadsideMinimal", descendant.Descriptor?.FontName);
        Assert.Equal(600, descendant.DefaultWidth);
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(descendant.Program);
        Assert.Equal(
            "M 100,0 L 100,700 L 300,700 L 300,400 L 500,400 L 500,700 L 700,700 L 700,0 L 500,0 L 500,300 L 300,300 L 300,0 Z",
            OutlineText.Of(program, descendant.GetGlyphId(1)));
        Assert.Equal("M 100,0 L 100,700 L 300,700 L 300,0 Z", OutlineText.Of(program, descendant.GetGlyphId(2)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Identity_V_takes_glyph_ids_from_the_map_stream_and_vertical_metrics_from_W2_and_DW2()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-identity-v.pdf");
        PdfType0Font font = Font(document);

        CidGlyph[] glyphs = Glyphs(font, [0x00, 0x21, 0x00, 0x22, 0x00, 0x23]);

        Assert.Same(CMap.IdentityV, font.Encoding);
        Assert.Equal(WritingMode.Vertical, font.WritingMode);
        Assert.Equal([0x21, 0x22, 0x23], glyphs.Select(glyph => glyph.Cid));
        Assert.Equal([1, 2, 0], glyphs.Select(glyph => glyph.GlyphId));
        Assert.Equal(
            [new CidVerticalMetrics(-1200, 400, 880), new CidVerticalMetrics(-900, 200, 880), new CidVerticalMetrics(-1100, 500, 900)],
            glyphs.Select(glyph => glyph.VerticalMetrics));

        // Pen positions at 24 pt from 300 700 (ty = w1y / 1000 * Tfs; Th never applies) and glyph origins offset by -v.
        double y = 700;
        var pen = new List<double>();
        foreach (CidGlyph glyph in glyphs)
        {
            pen.Add(Math.Round(y, 6));
            y += glyph.VerticalMetrics.VerticalAdvance / 1000 * 24;
        }

        Assert.Equal([700, 671.2, 649.6], pen);
        Assert.Equal(
            [(-9.6, -21.12), (-4.8, -21.12), (-12.0, -21.6)],
            glyphs.Select(glyph => (Math.Round(-glyph.VerticalMetrics.PositionX / 1000 * 24, 6), Math.Round(-glyph.VerticalMetrics.PositionY / 1000 * 24, 6))));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_embedded_CMap_using_another_reads_mixed_length_codes_and_maps_through_both()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-embedded-cmap.pdf");
        PdfType0Font font = Font(document);
        CMap cmap = font.Encoding;

        CidGlyph[] glyphs = Glyphs(font, "HI"u8.ToArray());
        glyphs = [.. glyphs, .. Glyphs(font, [0x81, 0x41, 0x81, 0x40])];

        Assert.Equal("Broadside-Child-H", cmap.Name);
        Assert.Equal("Broadside-Parent-H", cmap.Parent?.Name);
        Assert.Equal(new CidSystemInfo("Adobe", "Identity", 0), cmap.SystemInfo);
        Assert.Equal([1, 1, 2, 2], glyphs.Select(glyph => glyph.Code.Length));
        Assert.Equal([1, 2, 2, 1], glyphs.Select(glyph => glyph.Cid));
        Assert.Equal([1, 2, 2, 1], glyphs.Select(glyph => glyph.GlyphId));
        Assert.Equal([800, 400, 400, 800], glyphs.Select(glyph => glyph.Width));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_code_in_no_codespace_range_byte_by_byte_takes_two_bytes_and_shows_CID_0_with_one_diagnostic()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-embedded-cmap.pdf");
        PdfType0Font font = Font(document);

        CidGlyph[] glyphs = [.. Glyphs(font, [0x82, 0x10]), .. Glyphs(font, [0x82, 0x10])];

        Assert.All(glyphs, glyph => Assert.Equal(new CharacterCode(0x8210, 2, IsValid: false), glyph.Code));
        Assert.All(glyphs, glyph => Assert.Equal((0, 0, 1000.0), (glyph.Cid, glyph.GlyphId, glyph.Width)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("CMapCodeInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(font.Reference, diagnostic.ObjectReference);
    }

    [Fact]
    public void Strict_mode_throws_for_the_invalid_code()
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-cid-embedded-cmap.pdf", new PdfOptions().UseStrict());
        PdfType0Font font = Font(document);

        Assert.Equal(1, font.ReadGlyph("H"u8).Cid);
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => font.ReadGlyph([0x82, 0x10]));
        Assert.Equal("CMapCodeInvalid", error.Diagnostic.Code);
    }

    /// <summary>Gets font F1 of the file's page as a Type 0 font.</summary>
    internal static PdfType0Font Font(PdfDocument document) => Assert.IsType<PdfType0Font>(Assert.Single(document.Pages).GetFont("F1"));

    /// <summary>Reads every glyph of a shown string.</summary>
    internal static CidGlyph[] Glyphs(PdfType0Font font, ReadOnlySpan<byte> text)
    {
        var glyphs = new List<CidGlyph>();
        while (!text.IsEmpty)
        {
            CidGlyph glyph = font.ReadGlyph(text);
            glyphs.Add(glyph);
            text = text[glyph.Code.Length..];
        }

        return [.. glyphs];
    }
}
