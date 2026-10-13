using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Text in the display list (ISO 32000-2 §9.4.3, §9.3.6, §9.2.4; ADR 0006): shown glyphs reach the backend as glyph runs carrying
/// the font program, glyph ids, text matrices and interned outlines, painted as the text rendering mode says.
/// </summary>
public sealed class DisplayListTextTests
{
    [Fact]
    public void Glyphs_of_an_embedded_truetype_font_reach_the_backend_as_one_run_with_their_ids_positions_and_outlines()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // BT /F1 24 Tf 72 700 Td (HI) Tj ET: H is glyph 1 (width 800, so I starts 19.2 points later), I is glyph 2.
        Assert.Equal(
            [$"glyphs Fill TrueType 1 {new Matrix(24, 0, 0, 24, 72, 700)} 2 {new Matrix(24, 0, 0, 24, 72 + (800 * 24 / 1000.0), 700)} | fill DeviceGray 0"],
            recorder.Operations);

        // The program's glyph space is 1000 units per em, so a glyph maps to user space by its text matrix scaled by 1/1000.
        Assert.Equal(new Matrix(0.024, 0, 0, 0.024, 72, 700), recorder.Glyphs[0].Transform);
        Assert.Equal("M 100 0 L 100 700 L 300 700 L 300 0 Z", recorder.Glyphs[1].Outline);
        Assert.StartsWith("M 100 0 L 100 700 L 300 700 L 300 400", recorder.Glyphs[0].Outline, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text-truetype-composite.pdf", "TrueType")]
    [InlineData("text-cff-embedded.pdf", "Cff")]
    [InlineData("text-opentype-cff-embedded.pdf", "OpenType")]
    [InlineData("text-type1-embedded.pdf", "Type1")]
    [InlineData("text-cid-identity-h.pdf", "TrueType")]
    [InlineData("text-cid-identity-v.pdf", "TrueType")]
    public void Glyphs_of_every_embedded_font_format_carry_their_program_and_outlines(string fileName, string format)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.All(recorder.Operations, line => Assert.StartsWith("glyphs Fill " + format + " ", line, StringComparison.Ordinal));
        Assert.Contains(recorder.Glyphs, glyph => glyph.Outline.Length > 0);
        Assert.All(recorder.Glyphs, glyph => Assert.Equal(format, glyph.Program?.Format.ToString()));
    }

    [Fact]
    public void A_composite_font_shows_the_glyph_each_cid_selects_and_cid_0s_glyph_for_a_cid_without_one()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-identity-h.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // <000100030002> at 24 pt: CIDs 1, 3, 2 select glyphs 1, 0 (past the program's three glyphs) and 2, advancing 19.2 and 14.4.
        Assert.Equal([1, 0, 2], recorder.Glyphs.Select(glyph => glyph.GlyphId));
        Assert.Equal([72, 91.2, 105.6], recorder.Glyphs.Select(glyph => Math.Round(glyph.Transform.E, 9)));
    }

    [Fact]
    public void A_vertical_composite_font_places_each_glyph_at_the_pen_minus_its_position_vector()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-identity-v.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // Pen at (300, 700), 671.2, 649.6 (w1y -1200 and -900 at 24 pt); position vectors (400, 880), (200, 880), (500, 900).
        Assert.Equal(
            [(290.4, 678.88), (295.2, 650.08), (288, 628)],
            recorder.Glyphs.Select(glyph => (Math.Round(glyph.Transform.E, 9), Math.Round(glyph.Transform.F, 9))));
    }

    [Fact]
    public void Each_rendering_mode_fills_strokes_both_or_neither_and_invisible_text_still_advances()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // 48 pt: H advances 0.8 x 48 and I 0.4 x 48; lines at y 230, 170, 110 and 50 from x 40.
        const string fill = " | fill DeviceRgb 0.2 0.4 0.8";
        const string stroke = " | stroke 4 Butt Round 10 [] 0 DeviceRgb 0.8 0.1 0.1";
        Assert.Equal(
            [
                $"glyphs Fill TrueType 1 {At(40, 230)} 2 {At(40 + (0.8 * 48), 230)}{fill}",
                $"glyphs Stroke TrueType 1 {At(40, 170)} 2 {At(40 + (0.8 * 48), 170)}{stroke}",
                $"glyphs FillStroke TrueType 1 {At(40, 110)} 2 {At(40 + (0.8 * 48), 110)}{fill}{stroke}",
                $"glyphs Invisible TrueType 1 {At(40, 50)} 2 {At(40 + (0.8 * 48), 50)}",
                $"glyphs Fill TrueType 2 {At(40 + (0.8 * 48) + (0.4 * 48), 50)}{fill}",
            ],
            recorder.Operations);

        static Matrix At(double x, double y) => new(48, 0, 0, 48, x, y);
    }

    [Fact]
    public void A_glyph_outline_is_read_from_the_program_once_per_list_however_often_it_is_shown()
    {
        var parser = new CountingParser();
        using PdfDocument document = new PdfEngine(new PdfOptions().UseFontProgramParser(parser)).Open(Corpus.Bytes("text-render-modes.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // Nine glyphs shown, two distinct (H and I).
        Assert.Equal(9, recorder.Glyphs.Count);
        Assert.Equal(2, parser.Outlines);
    }

    /// <summary>Parses TrueType programs and counts the outlines read from them.</summary>
    private sealed class CountingParser : IFontProgramParser
    {
        private readonly Broadside.Fonts.TrueType.TrueTypeFontProgramParser _inner = new();

        public int Outlines { get; set; }

        public IReadOnlyList<FontProgramFormat> Formats => _inner.Formats;

        public bool CanParse(ReadOnlySpan<byte> data) => _inner.CanParse(data);

        public FontProgram? Parse(ReadOnlyMemory<byte> data, FontProgramContext context) =>
            _inner.Parse(data, context) is { } program ? new CountingProgram(program, this) : null;
    }

    /// <summary>A program that counts the outlines read from it and defers everything to the parsed one.</summary>
    private sealed class CountingProgram(FontProgram inner, CountingParser parser) : FontProgram
    {
        public override FontProgramFormat Format => inner.Format;

        public override int GlyphCount => inner.GlyphCount;

        public override Matrix FontMatrix => inner.FontMatrix;

        public override IReadOnlyList<FontCharacterMap> CharacterMaps => inner.CharacterMaps;

        public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
        {
            parser.Outlines++;
            return inner.GetOutline(glyphId, outline);
        }

        public override GlyphMetrics GetMetrics(int glyphId) => inner.GetMetrics(glyphId);

        public override bool TryGetGlyphId(string glyphName, out int glyphId) => inner.TryGetGlyphId(glyphName, out glyphId);

        public override string? GetGlyphName(int glyphId) => inner.GetGlyphName(glyphId);
    }
}
