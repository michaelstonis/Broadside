using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// <c>text-cidcff-predefined-cmap.pdf</c> through the core alone: the CID-keyed CFF program draws its glyphs with each Font DICT's
/// Private DICT and the concatenated FontMatrix, and the predefined CMap it names (90ms-RKSJ-H) is reported as unavailable while
/// its codespace still splits the string. With the CMaps package, see the package's tests. ISO 32000-2 §9.7.4.2, §9.7.5.2, §9.9.
/// </summary>
public sealed class CidCffCorpusTests
{
    private const string File = "text-cidcff-predefined-cmap.pdf";

    [Fact]
    public void The_CID_keyed_program_maps_each_CID_through_its_charset_to_the_outline_its_font_dictionary_draws()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(File));
        PdfCidFont font = Assert.IsType<PdfCidFontType0>(CompositeFontCorpusTests.Font(document).DescendantFont);
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(font.Program);

        Assert.Equal([1, 2, 3, 0], new[] { 264, 3284, 3722, 1 }.Select(font.GetGlyphId));
        Assert.Equal(Matrix.CreateScale(0.001, 0.001), program.FontMatrix);
        Assert.Equal("M 50,0 L 450,0 L 450,700 L 50,700 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 100,0 L 900,0 L 900,800 L 100,800 Z M 200,100 L 200,700 L 800,700 L 800,100 Z", OutlineText.Of(program, 2));
        Assert.Equal("M 100,0 L 900,0 L 900,800 L 100,800 Z", OutlineText.Of(program, 3));
        Assert.Equal([500.0, 1000.0, 1000.0], new[] { 1, 2, 3 }.Select(gid => program.GetMetrics(gid).AdvanceWidth));
        Assert.Equal([500.0, 1000.0, 1000.0], new[] { 264, 3284, 3722 }.Select(font.GetWidth));
    }

    [Fact]
    public void Without_the_CMaps_package_the_predefined_CMap_is_named_and_the_codes_still_split_by_its_codespace()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(File));
        Assert.Empty(document.Diagnostics);

        CidGlyph[] glyphs = CompositeFontCorpusTests.Glyphs(CompositeFontCorpusTests.Font(document), [0x41, 0x93, 0xFA, 0x96, 0x7B]);

        Assert.Equal([(1, 0, 0), (2, 0, 0), (2, 0, 0)], glyphs.Select(glyph => (glyph.Code.Length, glyph.Cid, glyph.GlyphId)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("CMapUnavailable", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains("90ms-RKSJ-H", diagnostic.Message, StringComparison.Ordinal);
    }
}
