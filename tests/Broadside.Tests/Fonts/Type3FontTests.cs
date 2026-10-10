using System.Globalization;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Type 3 fonts (ISO 32000-2 §9.6.4): each shown glyph runs its <c>CharProcs</c> procedure through the content interpreter in glyph
/// space (FontMatrix × T<sub>rm</sub>), with the font's own resources; <c>d1</c> glyphs take their colour from the text's graphics
/// state (§8.6.8), <c>d0</c> glyphs set their own.
/// </summary>
public class Type3FontTests
{
    private const int Precision = 6;

    /// <summary>
    /// Per the README row of text-type3.pdf: glyphs a, b and c of each font at x = 72, 96 and 120 (24 pt, 1000 glyph units per em
    /// in T3a, 100 in T3b); the page fill colour is blue.
    /// </summary>
    private static readonly string[] FirstLine =
    [
        "Glyph 97 ctm=[0.024 0 0 0.024 72 700]",
        "Fill 72,700,90,718 rgb=0,0,1",
        "Form Fm0",
        "Fill 74.4,702.4,79.2,707.2 rgb=0,0,1",
        "EndForm",
        "End 97",
        "Glyph 98 ctm=[0.024 0 0 0.024 96 700]",
        "Fill 96,700,114,718 rgb=1,0,0",
        "End 98",
        "Glyph 99 ctm=[0.024 0 0 0.024 120 700]",
        "Mask 120,700,138,718 rgb=0,0,1",
        "End 99",
    ];

    [Fact]
    public void Each_glyph_runs_its_procedure_in_glyph_space_through_the_font_matrix()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // T3a's FontMatrix is 0.001, T3b's 0.01 with every number divided by 10: the same device geometry, 50 points lower.
        string[] secondLine =
        [
            "Glyph 97 ctm=[0.24 0 0 0.24 72 650]",
            "Fill 72,650,90,668 rgb=0,0,1",
            "Form Fm0",
            "Fill 74.4,652.4,79.2,657.2 rgb=0,0,1",
            "EndForm",
            "End 97",
            "Glyph 98 ctm=[0.24 0 0 0.24 96 650]",
            "Fill 96,650,114,668 rgb=1,0,0",
            "End 98",
            "Glyph 99 ctm=[0.24 0 0 0.24 120 650]",
            "Mask 120,650,138,668 rgb=0,0,1",
            "End 99",
        ];
        Assert.Equal([.. FirstLine, .. secondLine], recorder.Lines);
    }

    [Fact]
    public void A_d1_glyph_ignores_colour_operators_and_non_mask_images_in_every_stream_it_invokes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // The square's 0 1 0 rg and its form's 1 0 0 rg are ignored (blue, the text's colour); the bitmap's DeviceGray image is not
        // painted; the d0 triangle's 1 0 0 rg is kept. After each glyph the page's colour is back.
        Assert.DoesNotContain(recorder.Lines, line => line.Contains("rgb=0,1,0", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Lines, line => line.StartsWith("Image", StringComparison.Ordinal));
        Assert.Equal(2, recorder.Lines.Count(line => line.Contains("rgb=1,0,0", StringComparison.Ordinal)));
        Assert.Equal([("ContentColorOperatorIgnored", DiagnosticSeverity.Information)], document.Diagnostics.Select(static d => (d.Code, d.Severity)).Distinct());
    }

    [Fact]
    public void Glyph_procedures_resolve_names_in_the_font_resources_not_the_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // The page's own /Fm0 fills the whole page (0,0 to 612,792): it never appears.
        Assert.Equal(2, recorder.Lines.Count(line => line == "Form Fm0"));
        Assert.DoesNotContain(recorder.Lines, line => line.StartsWith("Fill 0,0,612,792", StringComparison.Ordinal));
    }

    [Fact]
    public void A_font_without_resources_resolves_names_in_the_resources_of_the_form_that_shows_the_glyph()
    {
        string form = ContentPdf.Stream("BT /T3 10 Tf (a) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /Font << /T3 6 0 R >> /XObject << /Inner 8 0 R >> >>");
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 0 0] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 7 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] >>";
        string procedure = ContentPdf.Stream("1000 0 d0 /Inner Do");
        string inner = ContentPdf.Stream("0 0 500 500 re f", "/Type /XObject /Subtype /Form /BBox [0 0 1000 1000]");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/XObject << /Fm1 5 0 R >>", "/Fm1 Do", form, font, procedure, inner));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // ISO 32000-2 Table 110: without Resources, the glyph's names resolve where the font is used (here the form's resources).
        Assert.Equal(["Form Fm1", "Glyph 97 ctm=[0.01 0 0 0.01 0 0]", "Form Inner", "Fill 0,0,5,5 rgb=0", "EndForm", "End 97", "EndForm"], recorder.Lines);
        Assert.Equal(new PdfRectangle(0, 0, 0, 0), Assert.IsType<PdfType3Font>(document.GetFont(new Broadside.Objects.CosReference(6, 0))).FontBBox);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_malformed_font_matrix_is_the_default_and_a_font_without_char_procs_paints_nothing()
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [1 2] "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [500] >>";
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/Font << /T3 5 0 R >>", "BT /T3 10 Tf (aa) Tj ET", font));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(["Glyph 97 ctm=[0.01 0 0 0.01 0 0]", "End 97", "Glyph 97 ctm=[0.01 0 0 0.01 5 0]", "End 97"], recorder.Lines);
        Assert.Equal(["FontType3FontMatrixInvalid", "FontType3CharProcsInvalid"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void The_font_view_reads_the_matrix_box_resources_and_procedures_of_the_font_dictionary()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        PdfType3Font a = Assert.IsType<PdfType3Font>(document.Pages[0].GetFont("T3a"));
        PdfType3Font b = Assert.IsType<PdfType3Font>(document.Pages[0].GetFont("T3b"));

        Assert.Equal(new Matrix(0.001, 0, 0, 0.001, 0, 0), a.FontMatrix);
        Assert.Equal(new Matrix(0.01, 0, 0, 0.01, 0, 0), b.FontMatrix);
        Assert.Equal(new PdfRectangle(0, 0, 75, 75), b.FontBBox);
        Assert.Equal(["square", "triangle", "bitmap"], new[] { a.GetGlyphName(97), a.GetGlyphName(98), a.GetGlyphName(99) });
        Assert.Equal(100, b.GetWidth(98));
        Assert.NotNull(a.Resources);
        Assert.NotNull(a.GetCharProc(97));
        Assert.Null(a.GetCharProc(100));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Text_rendering_modes_3_and_7_run_no_procedure_and_type3_glyphs_add_no_text_clip()
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] >>";
        string procedure = ContentPdf.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/Font << /T3 5 0 R >>", "BT /T3 10 Tf 3 Tr (a) Tj 7 Tr (a) Tj 5 Tr (a) Tj ET", font, procedure));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(
        [
            "Glyph 97 ctm=[0.01 0 0 0.01 0 0]",
            "Glyph 97 ctm=[0.01 0 0 0.01 10 0]",
            "Glyph 97 ctm=[0.01 0 0 0.01 20 0]",
            "Fill 20,0,30,10 rgb=0",
            "End 97",
        ],
            recorder.Lines);
        Assert.DoesNotContain(recorder.Lines, line => line.StartsWith("Clip", StringComparison.Ordinal));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_glyph_that_shows_itself_through_the_inherited_font_is_refused_and_one_without_d0_or_d1_runs_as_d0()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3-recursive.pdf"));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // Glyph r shows (ar) in the inherited font T3 at 24 pt, in r's glyph space (scale 0.024 x 0.024 = 0.000576): a runs, r is
        // refused (it is already running) but still advances; glyph n has no d0 or d1 and paints anyway.
        Assert.Equal(
        [
            "Glyph 97 ctm=[0.024 0 0 0.024 72 700]",
            "Fill 72,700,96,724 rgb=0",
            "End 97",
            "Glyph 114 ctm=[0.024 0 0 0.024 96 700]",
            "Glyph 97 ctm=[0.000576 0 0 0.000576 96 700]",
            "Fill 96,700,96.576,700.576 rgb=0",
            "End 97",
            "Glyph 114 ctm=[0.000576 0 0 0.000576 96.576 700]",
            "End 114",
            "End 114",
            "Glyph 110 ctm=[0.024 0 0 0.024 120 700]",
            "Fill 120,700,132,712 rgb=0",
            "End 110",
        ],
            recorder.Lines);
        Assert.Equal(["ContentType3GlyphRecursion", "ContentType3GlyphMetricsMissing"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void A_d0_or_d1_that_is_not_the_first_operator_of_a_glyph_description_is_ignored()
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] >>";
        string procedure = ContentPdf.Stream("1000 0 d0 0 0 1000 1000 re f 1000 0 0 0 1000 1000 d1 1 0 0 rg 0 0 10 10 re f");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/Font << /T3 5 0 R >>", "BT /T3 10 Tf (a) Tj ET 1000 0 d0", font, procedure));
        var recorder = new Type3Recorder();

        document.Pages[0].ProcessContent(recorder);

        // The late d1 does not make the glyph uncoloured: its 1 0 0 rg applies. d0 in page content is ignored too: one diagnostic
        // per content stream.
        Assert.Contains("Fill 0,0,0.1,0.1 rgb=1,0,0", recorder.Lines);
        Assert.Equal(["ContentType3GlyphMetricsMisplaced", "ContentType3GlyphMetricsMisplaced"], ContentPdf.Codes(document.Diagnostics));
    }

    /// <summary>Records each Type 3 glyph with its glyph-space CTM, and each paint inside it in device space with the fill colour.</summary>
    private sealed class Type3Recorder : ContentProcessor
    {
        public List<string> Lines { get; } = [];

        public override ContentEvents Events =>
            ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Images | ContentEvents.Forms | ContentEvents.Clips | ContentEvents.Type3GlyphContent;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            if (glyph.Font is not PdfType3Font font)
            {
                return;
            }

            Matrix ctm = font.FontMatrix * glyph.TextMatrix * glyph.Ctm;
            Lines.Add($"Glyph {glyph.CharacterCode} ctm={RecordingProcessor.Matrix(Round(ctm))}");
        }

        public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => ContentVisit.Enter;

        public override void EndType3Glyph(in GlyphEvent glyph, ContentContext context) => Lines.Add($"End {glyph.CharacterCode}");

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            Lines.Add($"Form {System.Text.Encoding.ASCII.GetString(form.ResourceName)}");
            return ContentVisit.Enter;
        }

        public override void EndForm(in FormEvent form, ContentContext context) => Lines.Add("EndForm");

        public override void PaintPath(in PathEvent path, ContentContext context) =>
            Lines.Add($"Fill {DeviceBox(path.Path.Bounds, context.State.Ctm)} rgb={Colour(context)}");

        public override void PaintImage(in ImageEvent image, ContentContext context) =>
            Lines.Add($"{(image.IsStencil ? "Mask" : "Image")} {DeviceBox(new PdfRectangle(0, 0, 1, 1), image.Ctm)} rgb={Colour(context)}");

        public override void IntersectClip(in ClipEvent clip, ContentContext context)
        {
            if (clip.Kind == ClipKind.Text)
            {
                Lines.Add("Clip Text");
            }
        }

        private static string Colour(ContentContext context)
        {
            PdfColor colour = context.State.FillColor;
            return string.Join(",", colour.Components.ToArray().Select(c => RecordingProcessor.Number(Math.Round(c, 3))));
        }

        private static string DeviceBox(PdfRectangle box, Matrix ctm)
        {
            PathPoint a = ctm.Transform(box.Left, box.Bottom);
            PathPoint b = ctm.Transform(box.Right, box.Top);
            return string.Join(",", new[] { Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y) }
                .Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
        }

        private static Matrix Round(Matrix m) =>
            new(Math.Round(m.A, Precision), Math.Round(m.B, Precision), Math.Round(m.C, Precision), Math.Round(m.D, Precision), Math.Round(m.E, Precision), Math.Round(m.F, Precision));
    }
}
