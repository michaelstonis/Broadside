using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// OCR text layers (issue #649): invisible text in a glyphless Type 0 font over each word's box, so extraction and search find the
/// words of a scan. ISO 32000-2 §9.3.6, §9.3.4, §9.4.2, §9.7.6, §9.10.3, §14.8.2.4 and §14.7.5.
/// </summary>
public sealed class TextLayerTests
{
    private const double Tolerance = 0.5;

    [Fact]
    public void Extraction_returns_the_ocr_words_in_order_with_quads_over_their_boxes()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];

        // A 1224 by 1584 pixel scan of the 612 by 792 page: two pixels per point.
        var ocr = new OcrPage(1224, 1584,
        [
            new OcrLine([Word("Invoice", 200, 200, 420, 250), Word("number", 440, 200, 640, 250), Word("42", 660, 200, 720, 250)]),
            new OcrLine([Word("Total:", 200, 300, 360, 340), Word("€1,234.00", 380, 300, 620, 344)]),
        ]);
        page.AddTextLayer(ocr);

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();

        Assert.Equal(["Invoice", "number", "42", "Total:", "€1,234.00"], Words(text));
        Assert.Equal("Invoice number 42\nTotal: €1,234.00", text.PlainText.Trim().ReplaceLineEndings("\n"));
        Assert.All(text.Glyphs, glyph => Assert.True(glyph.Flags.HasFlag(TextGlyphFlags.Invisible)));
        AssertBox(text.Words[0].Quad, 100, 792 - 125, 210, 792 - 100);
        AssertBox(text.Words[1].Quad, 220, 792 - 125, 320, 792 - 100);
        AssertBox(text.Words[4].Quad, 190, 792 - 172, 310, 792 - 150);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void The_layer_is_one_invisible_text_object_per_line_after_the_existing_content_in_its_own_saved_state()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
        }

        document.Pages[0].AddTextLayer(new OcrPage(612, 792, [new OcrLine([Word("one", 10, 10, 40, 20)]), new OcrLine([Word("two", 10, 30, 40, 40)])]));

        string content = Content(document, document.Pages[0]);
        int layer = content.IndexOf("3 Tr", StringComparison.Ordinal);
        Assert.True(content.IndexOf("re", StringComparison.Ordinal) < layer);
        Assert.Equal(2, Count(content, "BT"));
        Assert.Equal(2, Count(content, "3 Tr"));
        Assert.Contains(" Tz", content, StringComparison.Ordinal);
        Assert.EndsWith("Q", content.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_glyphless_font_is_a_type0_identity_h_font_written_once_per_document_with_a_tounicode_map()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages[0].AddTextLayer(new OcrPage(612, 792, [new OcrLine([Word("a", 10, 10, 20, 20)])]));
        document.Pages[1].AddTextLayer(new OcrPage(612, 792, [new OcrLine([Word("b", 10, 10, 20, 20)])]));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        PdfFont first = Assert.Single(Fonts(reopened, reopened.Pages[0]));
        PdfFont second = Assert.Single(Fonts(reopened, reopened.Pages[1]));
        PdfType0Font type0 = Assert.IsType<PdfType0Font>(first);
        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal("GlyphLessFont", type0.BaseFont);
        PdfCidFont cidFont = type0.DescendantFont!;
        Assert.Equal(PdfCidFontType.CidFontType2, cidFont.CidFontType);
        Assert.True(cidFont.IsEmbedded);
        Assert.Equal(2, cidFont.Program!.GlyphCount);
        Assert.Equal(500, cidFont.DefaultWidth);
        Assert.Equal([1, 1, 1], new[] { cidFont.GetGlyphId(0), cidFont.GetGlyphId('a'), cidFont.GetGlyphId(0xFFFF) });
        Assert.Equal("a", reopened.Pages[0].ExtractText().Text);
        Assert.Equal("b", reopened.Pages[1].ExtractText().Text);
    }

    [Fact]
    public void Any_script_extracts_through_the_tounicode_map_and_characters_outside_the_bmp_through_actual_text()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].AddTextLayer(new OcrPage(612, 792,
        [
            new OcrLine([Word("Ωμέγα", 10, 10, 80, 30), Word("日本語", 90, 10, 150, 30)]),
            new OcrLine([Word("שלום", 10, 40, 60, 60)], Language: "he"),
            new OcrLine([Word("𝔄𝔅", 10, 70, 60, 90)]),
        ]));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));

        Assert.Equal(["Ωμέγα", "日本語", "שלום", "𝔄𝔅"], Words(reopened.Pages[0].ExtractText()));
    }

    [Fact]
    public void Empty_whitespace_zero_width_and_low_confidence_words_are_skipped()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].AddTextLayer(
            new OcrPage(612, 792,
            [
                new OcrLine([Word("kept", 10, 10, 50, 30), Word("", 60, 10, 80, 30), Word("  ", 90, 10, 100, 30), Word("flat", 110, 10, 110, 30), Word("thin", 120, 20, 160, 20)]),
                new OcrLine([new OcrWord("unsure", new PdfRectangle(10, 40, 60, 60), 0.3), new OcrWord("sure", new PdfRectangle(70, 40, 110, 60), 0.9)]),
                new OcrLine([]),
            ]),
            new TextLayerOptions().WithMinConfidence(0.5));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));

        Assert.Equal(["kept", "sure"], Words(reopened.Pages[0].ExtractText()));
    }

    [Fact]
    public void A_layer_with_no_word_to_write_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        string before = Content(document, document.Pages[0]);

        document.Pages[0].AddTextLayer(new OcrPage(100, 100, [new OcrLine([Word(" ", 1, 1, 5, 5)])]));

        Assert.Equal(before, Content(document, document.Pages[0]));
        Assert.Null(document.GlyphlessFont);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void On_a_rotated_page_the_words_land_on_their_boxes_as_the_page_is_displayed(int rotation)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        page.Rotation = rotation;
        bool turned = rotation is 90 or 270;
        double width = turned ? 792 : 612;
        double height = turned ? 612 : 792;

        // One pixel per point of the displayed page; the word at (100, 50)-(200, 80) from the displayed top-left.
        page.AddTextLayer(new OcrPage(width, height, [new OcrLine([Word("rotated", 100, 50, 200, 80)])]));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(["rotated"], Words(text));

        // The displayed box (u, v) with v up from the displayed bottom, turned into default user space by the page's rotation.
        (double u0, double u1, double v0, double v1) = (100, 200, height - 80, height - 50);
        (double left, double bottom, double right, double top) = rotation switch
        {
            90 => (612 - v1, u0, 612 - v0, u1),
            180 => (612 - u1, 792 - v1, 612 - u0, 792 - v0),
            _ => (v0, 792 - u1, v1, 792 - u0),
        };
        AssertBox(text.Words[0].Quad, left, bottom, right, top);
    }

    [Fact]
    public void A_rotated_line_runs_along_its_angle_and_fills_its_axis_aligned_box()
    {
        using PdfDocument document = PdfDocument.Create();

        // Text running upward: the box is tall and narrow, 20 wide (the text height) and 120 tall (its length).
        document.Pages[0].AddTextLayer(new OcrPage(612, 792, [new OcrLine([Word("upward", 300, 200, 320, 320)], BaselineAngle: 90)]));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(["upward"], Words(text));
        Quad quad = text.Words[0].Quad;
        AssertBox(quad, 300, 792 - 320, 320, 792 - 200);
        Assert.True(quad.BottomRight.Y - quad.BottomLeft.Y > 100, "the baseline runs up the page");
    }

    [Fact]
    public void A_target_maps_the_image_onto_a_part_of_the_page()
    {
        using PdfDocument document = PdfDocument.Create();
        var target = new PdfRectangle(100, 100, 300, 200);

        document.Pages[0].AddTextLayer(new OcrPage(400, 200, [new OcrLine([Word("inset", 0, 0, 200, 100)])]), new TextLayerOptions().WithTarget(target));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        AssertBox(reopened.Pages[0].ExtractText().Words[0].Quad, 100, 150, 200, 200);
    }

    [Fact]
    public void Tagging_makes_each_line_a_paragraph_with_its_language_under_a_document_element()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].AddTextLayer(
            new OcrPage(612, 792, [new OcrLine([Word("Hello", 10, 10, 60, 30)], Language: "en"), new OcrLine([Word("Hallo", 10, 40, 60, 60)], Language: "de")]),
            new TextLayerOptions().WithTagging(true));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        Structure.PdfStructureElement root = Assert.Single(reopened.StructureTree!.Children);
        Assert.Equal("Document", root.StructureType?.Value);
        Structure.PdfStructureElement[] lines = [.. root.Children.OfType<Structure.PdfStructureElement>()];
        Assert.Equal(["P", "P"], lines.Select(static child => child.StructureType?.Value));
        Assert.Equal(["en", "de"], lines.Select(static child => child.Language));
        Assert.True(reopened.MarkInfo.Marked);
        Assert.Equal(["Hello", "Hallo"], Words(reopened.Pages[0].ExtractText()));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Untagged_layers_leave_the_document_without_a_structure_tree()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].AddTextLayer(new OcrPage(612, 792, [new OcrLine([Word("plain", 10, 10, 60, 30)])]));

        Assert.Null(document.StructureTree);
    }

    [Fact]
    public void The_options_are_immutable_and_check_their_values()
    {
        var defaults = new TextLayerOptions();
        TextLayerOptions changed = defaults.WithMinConfidence(0.8).WithTarget(new PdfRectangle(0, 0, 10, 10)).WithTagging(true);

        Assert.Equal((0, null, false), (defaults.MinConfidence, defaults.Target, defaults.Tag));
        Assert.Equal((0.8, new PdfRectangle(0, 0, 10, 10), true), (changed.MinConfidence, changed.Target, changed.Tag));
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.WithMinConfidence(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.WithMinConfidence(double.NaN));
        Assert.Throws<ArgumentException>(() => defaults.WithTarget(new PdfRectangle(0, 0, 0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrPage(0, 10, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrPage(10, double.PositiveInfinity, []));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Create().Pages[0].AddTextLayer(null!));
    }

    internal static OcrWord Word(string text, double left, double top, double right, double bottom) =>
        new(text, new PdfRectangle(left, top, right, bottom));

    internal static string[] Words(TextPage text) => [.. text.Words.Select(word => text.GetWordText(word).ToString())];

    private static void AssertBox(Quad quad, double left, double bottom, double right, double top)
    {
        PathPoint[] corners = [quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft];
        Assert.Equal(left, corners.Min(static point => point.X), Tolerance);
        Assert.Equal(right, corners.Max(static point => point.X), Tolerance);
        Assert.Equal(bottom, corners.Min(static point => point.Y), Tolerance);
        Assert.Equal(top, corners.Max(static point => point.Y), Tolerance);
    }

    private static PdfFont[] Fonts(PdfDocument document, PdfPage page) =>
        document.Resolve(page.Resources?.GetValueOrDefault(new CosName("Font"))) is CosDictionary fonts
            ? [.. fonts.Select(entry => page.GetFont(entry.Key.Value)!)]
            : [];

    private static string Content(PdfDocument document, PdfPage page)
    {
        var text = new StringBuilder();
        switch (document.Resolve(page.Dictionary.GetValueOrDefault(KnownNames.Contents)))
        {
            case CosStream stream:
                text.Append(Encoding.Latin1.GetString(document.DecodeStream(stream).Span));
                break;
            case CosArray array:
                foreach (CosObject part in array)
                {
                    text.Append(Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(part)).Span)).Append('\n');
                }

                break;
        }

        return text.ToString();
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
