using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Text state (ISO 32000-2 §9.3), text objects and positioning (§9.4): one glyph event per character code, placed by the text
/// matrix and advanced by the displacement formula of §9.4.4 (p.326). Expected positions are worked out by hand from the
/// spec's formulas and the AFM widths of Helvetica (thousandths of text space).
/// </summary>
public sealed class TextTests
{
    private const int Precision = 9;

    [Fact]
    public void Each_character_code_of_text_standard14_is_one_glyph_placed_by_the_helvetica_widths()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var recorder = new GlyphRecorder();

        document.Pages[0].ProcessContent(recorder);

        // "Hello, Broadside" in Helvetica (Helvetica.afm): H e l l o , space B r o a d s i d e.
        int[] widths = [722, 556, 222, 222, 556, 278, 278, 667, 333, 556, 556, 556, 500, 222, 556, 556];
        Assert.Equal(16, recorder.Glyphs.Count);
        Assert.Equal("Hello, Broadside"u8.ToArray().Select(b => (uint)b), recorder.Glyphs.Select(glyph => glyph.Code));
        Assert.Equal(new Matrix(24, 0, 0, 24, 72, 700), recorder.Glyphs[0].TextMatrix);
        double x = 72;
        for (int index = 0; index < widths.Length; index++)
        {
            RecordedGlyph glyph = recorder.Glyphs[index];
            Assert.Equal(widths[index] / 1000.0, glyph.Width, Precision);
            Assert.Equal(x, glyph.DeviceOrigin.X, Precision);
            Assert.Equal(700, glyph.DeviceOrigin.Y, Precision);
            Assert.Equal(widths[index] * 24 / 1000.0, glyph.AdvanceX, Precision);
            Assert.Equal("Helvetica", glyph.Font?.BaseFont);
            x += widths[index] * 24 / 1000.0;
        }

        Assert.Equal(234.72, recorder.Glyphs[^1].DeviceOrigin.X, Precision);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_embedded_truetype_font_advances_by_its_widths_array()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        var recorder = new GlyphRecorder();

        document.Pages[0].ProcessContent(recorder);

        // Widths [800 400] for codes 72 (H) and 73 (I).
        Assert.Equal([72u, 73u], recorder.Glyphs.Select(glyph => glyph.Code));
        Assert.Equal([0.8, 0.4], recorder.Glyphs.Select(glyph => glyph.Width));
        Assert.Equal(72, recorder.Glyphs[0].DeviceOrigin.X, Precision);
        Assert.Equal(91.2, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.IsType<PdfTrueTypeFont>(recorder.Glyphs[0].Font);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Character_spacing_word_spacing_and_horizontal_scaling_follow_the_displacement_formula()
    {
        // tx = ((w0 - Tj/1000) * Tfs + Tc + Tw) * Th with Tfs 10, Tc 2, Tw 3 (space only), Th 0.5.
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F1 10 Tf 2 Tc 3 Tw 50 Tz 100 200 Td (a b) Tj ET");

        Assert.Equal(3, recorder.Glyphs.Count);
        Assert.Equal(new Matrix(5, 0, 0, 10, 100, 200), recorder.Glyphs[0].TextMatrix);
        Assert.Equal(3.78, recorder.Glyphs[0].AdvanceX, Precision);
        Assert.Equal(103.78, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(3.89, recorder.Glyphs[1].AdvanceX, Precision);
        Assert.Equal(107.67, recorder.Glyphs[2].DeviceOrigin.X, Precision);
        Assert.Equal([false, true, false], recorder.Glyphs.Select(glyph => glyph.WordSpacingApplied));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Each_glyph_reports_its_origin_and_advance_in_device_space()
    {
        // Tfs 10, Tc 2, Th 0.5, Trise 5; Tm rotates 90 degrees to (100, 200); CTM [2 0 0 3 10 20]. "a" and "b" are 556 wide.
        // Origin: Tm maps (0, Trise) to (95, 200), the CTM to (200, 620). Advance tx = (0.556 * 10 + 2) * 0.5 = 3.78 along text x,
        // which Tm turns to (0, 3.78) and the CTM to (0, 11.34).
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("2 0 0 3 10 20 cm BT /F1 10 Tf 2 Tc 50 Tz 5 Ts 0 1 -1 0 100 200 Tm (ab) Tj ET");

        Assert.Equal(2, recorder.Glyphs.Count);
        RecordedGlyph first = recorder.Glyphs[0];
        Assert.Equal(200, first.DeviceOrigin.X, Precision);
        Assert.Equal(620, first.DeviceOrigin.Y, Precision);
        Assert.Equal(0, first.DeviceAdvanceX, Precision);
        Assert.Equal(11.34, first.DeviceAdvanceY, Precision);
        Assert.Equal(200, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(631.34, recorder.Glyphs[1].DeviceOrigin.Y, Precision);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Leading_next_line_quote_operators_td_and_tm_position_lines()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run(
            "BT /F1 10 Tf 0 0 Td 12 TL (a) ' 1 2 (b) \" 5 -20 TD (c) Tj T* (d) Tj 2 0 0 2 50 60 Tm (e) Tj ET");

        Assert.Equal(5, recorder.Glyphs.Count);
        Assert.Equal((0, -12), Point(recorder.Glyphs[0]));
        Assert.Equal((0, -24), Point(recorder.Glyphs[1]));
        Assert.Equal((5, -44), Point(recorder.Glyphs[2]));
        Assert.Equal((5, -64), Point(recorder.Glyphs[3]));
        Assert.Equal(new Matrix(20, 0, 0, 20, 50, 60), recorder.Glyphs[4].TextMatrix);

        // " set Tw 1 and Tc 2 for the rest of the text object: (0.5 * 10 + 2) * 1 for "c" (width 500).
        Assert.Equal(7, recorder.Glyphs[2].AdvanceX, Precision);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Tj_numbers_move_the_next_string_and_are_reported_on_its_first_glyph()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F1 10 Tf [(a) -500 (b) 250 100 (c)] TJ ET");

        Assert.Equal(0, recorder.Glyphs[0].DeviceOrigin.X, Precision);
        Assert.Equal(0, recorder.Glyphs[0].Adjustment);
        Assert.Equal(10.56, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(-500, recorder.Glyphs[1].Adjustment);

        // b advances 5.56; then 350 thousandths of 10 back: 16.12 - 3.5.
        Assert.Equal(12.62, recorder.Glyphs[2].DeviceOrigin.X, Precision);
        Assert.Equal(350, recorder.Glyphs[2].Adjustment);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Text_rise_shifts_the_text_matrix_and_the_ctm_is_reported_beside_it()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("2 0 0 2 10 10 cm BT /F1 10 Tf 3 Ts (a) Tj ET");

        Assert.Equal(new Matrix(10, 0, 0, 10, 0, 3), recorder.Glyphs[0].TextMatrix);
        Assert.Equal(new Matrix(2, 0, 0, 2, 10, 10), recorder.Glyphs[0].Ctm);
        Assert.Equal(16, recorder.Glyphs[0].DeviceOrigin.Y, Precision);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Word_spacing_is_not_applied_to_two_byte_codes_of_a_composite_font()
    {
        string type0 = "<< /Type /Font /Subtype /Type0 /BaseFont /Composite /Encoding /Identity-H /DescendantFonts [6 0 R] >>";
        string cidFont = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Composite "
            + "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 1000 >>";
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/Font << /F2 5 0 R >>", "BT /F2 10 Tf 5 Tw <00200020> Tj ET", type0, cidFont));
        var recorder = new GlyphRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal([32u, 32u], recorder.Glyphs.Select(glyph => glyph.Code));
        Assert.Equal([2, 2], recorder.Glyphs.Select(glyph => glyph.CodeLength));
        Assert.Equal([10.0, 10.0], recorder.Glyphs.Select(glyph => glyph.AdvanceX));
        Assert.All(recorder.Glyphs, glyph => Assert.False(glyph.WordSpacingApplied));
    }

    [Theory]
    [InlineData(0, TextRenderingMode.Fill)]
    [InlineData(1, TextRenderingMode.Stroke)]
    [InlineData(2, TextRenderingMode.FillStroke)]
    [InlineData(3, TextRenderingMode.Invisible)]
    public void Rendering_modes_without_clipping_are_reported_on_every_glyph(int mode, TextRenderingMode expected)
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run($"BT /F1 10 Tf {mode} Tr (ab) Tj ET");

        Assert.Equal([expected, expected], recorder.Glyphs.Select(glyph => glyph.Mode));
        Assert.Equal(["BT", "Glyph", "Glyph", "ET"], recorder.Order);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(4, TextRenderingMode.FillClip)]
    [InlineData(5, TextRenderingMode.StrokeClip)]
    [InlineData(6, TextRenderingMode.FillStrokeClip)]
    [InlineData(7, TextRenderingMode.Clip)]
    public void Clipping_rendering_modes_add_the_glyphs_to_one_text_clip_at_et(int mode, TextRenderingMode expected)
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run($"BT /F1 10 Tf {mode} Tr (ab) Tj 0 Tr (c) Tj {mode} Tr (d) Tj ET");

        Assert.Equal([expected, expected, TextRenderingMode.Fill, expected], recorder.Glyphs.Select(glyph => glyph.Mode));
        Assert.Equal(["BT", "Glyph", "Glyph", "Glyph", "Glyph", "ET", "Clip Text"], recorder.Order);
        (ClipKind kind, FillRule rule, int count, uint[] codes) = Assert.Single(recorder.Clips);
        Assert.Equal(ClipKind.Text, kind);
        Assert.Equal(FillRule.NonZero, rule);
        Assert.Equal(3, count);
        Assert.Equal(Encoding.ASCII.GetBytes("abd").Select(b => (uint)b), codes);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_text_clip_narrows_the_state_clip_until_q_restores_it()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            ContentPdf.HelveticaResources,
            "q BT /F1 10 Tf 7 Tr (a) Tj ET 0 0 m 1 1 l S Q 0 0 m 1 1 l S",
            ContentPdf.Helvetica));
        var handles = new List<int>();
        var recorder = new ClipHandleRecorder(handles);

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(2, handles.Count);
        Assert.NotEqual(0, handles[0]);
        Assert.Equal(0, handles[1]);
        Assert.Equal(ClipKind.Text, recorder.Kinds[0]);
    }

    [Fact]
    public void Text_shown_before_any_tf_uses_helvetica_metrics_at_size_zero_with_a_diagnostic()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT 1 Tc (ab) Tj ET");

        Assert.Equal(2, recorder.Glyphs.Count);
        Assert.Equal("Helvetica", recorder.Glyphs[0].Font?.BaseFont);
        Assert.Equal(0.556, recorder.Glyphs[0].Width, Precision);

        // Size 0: only Tc advances.
        Assert.Equal(1, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(["ContentFontMissing"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void A_font_name_missing_from_the_resources_keeps_the_size_and_falls_back_to_helvetica()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F9 10 Tf (a) Tj (b) Tj ET");

        Assert.Equal(5.56, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(["ContentFontMissing"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Text_shown_outside_a_text_object_is_positioned_from_the_identity()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F1 10 Tf 50 50 Td ET (a) Tj (b) Tj");

        Assert.Equal(0, recorder.Glyphs[0].DeviceOrigin.X, Precision);
        Assert.Equal(5.56, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Equal(["ContentOperatorOutOfContext"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Q_does_not_restore_the_text_matrix()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F1 10 Tf q (a) Tj Q (b) Tj ET");

        Assert.Equal(5.56, recorder.Glyphs[1].DeviceOrigin.X, Precision);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_tj_element_that_is_neither_a_string_nor_a_number_is_skipped_with_a_diagnostic()
    {
        (GlyphRecorder recorder, var diagnostics) = GlyphRecorder.Run("BT /F1 10 Tf [(a) /Bad (b)] TJ ET");

        Assert.Equal(2, recorder.Glyphs.Count);
        Assert.Equal(["ContentOperandType"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Text_state_is_part_of_the_graphics_state()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            ContentPdf.HelveticaResources,
            "BT /F1 12 Tf 1 Tc 2 Tw 80 Tz 14 TL 3 Tr 4 Ts ET 0 0 m 1 1 l S",
            ContentPdf.Helvetica));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder);

        GraphicsState state = Assert.Single(recorder.States);
        Assert.Equal("Helvetica", state.Font?.BaseFont);
        Assert.Equal(12, state.FontSize);
        Assert.Equal(1, state.CharacterSpacing);
        Assert.Equal(2, state.WordSpacing);
        Assert.Equal(0.8, state.HorizontalScaling);
        Assert.Equal(14, state.Leading);
        Assert.Equal(TextRenderingMode.Invisible, state.TextRenderingMode);
        Assert.Equal(4, state.TextRise);
    }

    [Fact]
    public void Vertical_writing_places_each_glyph_by_its_position_vector_and_advances_by_w1()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-identity-v.pdf"));
        var recorder = new GlyphRecorder();

        document.Pages[0].ProcessContent(recorder);

        // README row: 24 pt at (300, 700); CID 0x21 w1y -1200 v (400, 880); 0x22 -900 (200, 880); 0x23 -1100 (500, 900).
        // Origin = pen - v * 24 / 1000; pen y 700, 671.2, 649.6.
        Assert.Equal([0x21u, 0x22u, 0x23u], recorder.Glyphs.Select(glyph => glyph.Code));
        Assert.Equal((290.4, 678.88), Point(recorder.Glyphs[0]));
        Assert.Equal((295.2, 650.08), Point(recorder.Glyphs[1]));
        Assert.Equal((288, 628), Point(recorder.Glyphs[2]));
        Assert.Equal(-28.8, recorder.Glyphs[0].AdvanceY, Precision);
        Assert.Equal(0, recorder.Glyphs[0].AdvanceX);
        Assert.Equal(-28.8, recorder.Glyphs[0].DeviceAdvanceY, Precision);
        Assert.Equal(0, recorder.Glyphs[0].DeviceAdvanceX, Precision);
    }

    [Fact]
    public void A_type3_glyph_advances_through_its_font_matrix_and_its_description_runs_in_glyph_space_when_entered()
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [500] >>";
        string procedure = ContentPdf.Stream("500 0 d0 0 0 100 100 re f");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/Font << /T3 5 0 R >>", "BT /T3 10 Tf (aa) Tj ET", font, procedure));
        var entering = new Type3Recorder(enter: true);
        var skipping = new Type3Recorder(enter: false);

        document.Pages[0].ProcessContent(entering);
        document.Pages[0].ProcessContent(skipping);

        Assert.Equal(["Glyph 97 x=0", "Paint ctm=[0.01 0 0 0.01 0 0] Type3Glyph", "End", "Glyph 97 x=5", "Paint ctm=[0.01 0 0 0.01 5 0] Type3Glyph", "End"], entering.Lines);
        Assert.Equal(["Glyph 97 x=0", "Glyph 97 x=5"], skipping.Lines);
        Assert.Empty(document.Diagnostics);
    }

    private static (double X, double Y) Point(RecordedGlyph glyph) => (Math.Round(glyph.DeviceOrigin.X, Precision), Math.Round(glyph.DeviceOrigin.Y, Precision));

    private sealed class Type3Recorder(bool enter) : ContentProcessor
    {
        public List<string> Lines { get; } = [];

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Type3GlyphContent;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) =>
            Lines.Add($"Glyph {glyph.CharacterCode} x={RecordingProcessor.Number(Math.Round(glyph.TextMatrix.E, Precision))}");

        public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => enter ? ContentVisit.Enter : ContentVisit.Skip;

        public override void EndType3Glyph(in GlyphEvent glyph, ContentContext context) => Lines.Add("End");

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            Matrix ctm = context.State.Ctm;
            var rounded = new Matrix(Math.Round(ctm.A, Precision), ctm.B, ctm.C, Math.Round(ctm.D, Precision), Math.Round(ctm.E, Precision), ctm.F);
            Lines.Add($"Paint ctm={RecordingProcessor.Matrix(rounded)} {context.RunKind}");
        }
    }

    private sealed class ClipHandleRecorder(List<int> handles) : ContentProcessor
    {
        public List<ClipKind> Kinds { get; } = [];

        public override ContentEvents Events => ContentEvents.Paths | ContentEvents.Clips;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            handles.Add(context.State.ClipHandle);
            Kinds.Add(context.GetClip(context.State.ClipHandle).Kind);
        }
    }
}
