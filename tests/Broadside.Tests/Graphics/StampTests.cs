using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Stamps on existing pages (issue #627): ISO 32000-2 §14.8.2.2 (artifacts, Table 363), §8.11.3 (optional content), §12.4.2 (page
/// labels), §14.11.2 (page boundaries) and §8.10.1 (forms).
/// </summary>
public sealed class StampTests
{
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName Font = new("Font");

    [Theory]
    [InlineData(PdfStampAlignment.Start, PdfStampAlignment.Start, 0, 692)]
    [InlineData(PdfStampAlignment.Center, PdfStampAlignment.Center, 256, 346)]
    [InlineData(PdfStampAlignment.End, PdfStampAlignment.End, 512, 0)]
    [InlineData(PdfStampAlignment.Start, PdfStampAlignment.End, 0, 0)]
    [InlineData(PdfStampAlignment.End, PdfStampAlignment.Start, 512, 692)]
    public void Each_alignment_places_the_stamp_in_the_box(PdfStampAlignment horizontal, PdfStampAlignment vertical, double left, double bottom)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject square = Square(document, 100);

        document.Stamp(PdfStamp.FromForm(square).WithHorizontalAlignment(horizontal).WithVerticalAlignment(vertical));

        Assert.Equal(new PdfRectangle(left, bottom, left + 100, bottom + 100), Single(Placements(document.Pages[0])));
    }

    [Fact]
    public void Offsets_move_the_aligned_stamp_right_and_up()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Stamp(PdfStamp.FromForm(Square(document, 100)).WithHorizontalAlignment(PdfStampAlignment.Start)
            .WithVerticalAlignment(PdfStampAlignment.End).WithOffsetX(36).WithOffsetY(18));

        Assert.Equal(new PdfRectangle(36, 18, 136, 118), Single(Placements(document.Pages[0])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void A_stamp_following_the_page_rotation_sits_top_left_as_viewed(int rotation)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 200, 400));
        page.Rotation = rotation;
        PdfFormXObject arrow = Square(document, 20);

        document.Stamp(PdfStamp.FromForm(arrow).WithHorizontalAlignment(PdfStampAlignment.Start).WithVerticalAlignment(PdfStampAlignment.Start), 1..);

        // Where the stamp lands in user space, seen through the page's rotation, is the top-left corner of the displayed page.
        Matrix toDisplayed = PageFormImporter.FormMatrix(page.CropBox, rotation, 1);
        (PdfRectangle placed, Matrix ctm) = Assert.Single(Placements(page));
        PdfRectangle shown = Transform(toDisplayed, placed);
        double displayedHeight = rotation is 90 or 270 ? 200 : 400;
        Assert.Equal(new PdfRectangle(0, displayedHeight - 20, 20, displayedHeight), Rounded(shown));

        // Upright as viewed: the form's x axis points right on screen.
        Matrix viewed = ctm * toDisplayed;
        Assert.Equal(1, Math.Round(viewed.A, 9));
        Assert.Equal(0, Math.Round(viewed.B, 9));
    }

    [Fact]
    public void A_rotated_stamp_turns_counterclockwise_and_its_box_is_centered()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Stamp(PdfStamp.FromForm(Square(document, 100)).WithRotation(90));

        (PdfRectangle placed, Matrix ctm) = Assert.Single(Placements(document.Pages[0]));
        Assert.Equal(new PdfRectangle(256, 346, 356, 446), Rounded(placed));
        Assert.Equal(1, Math.Round(ctm.B, 9));
    }

    [Fact]
    public void Fit_to_page_scales_the_stamp_to_the_box_keeping_its_aspect()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject wide = document.CreateFormXObject(new PdfRectangle(0, 0, 200, 100));
        using (PdfCanvas canvas = wide.CreateCanvas())
        {
            canvas.Rectangle(0, 0, 200, 100);
            canvas.Fill();
        }

        document.Stamp(PdfStamp.FromForm(wide).WithFitToPage());

        Assert.Equal(new PdfRectangle(0, 243, 612, 549), Rounded(Single(Placements(document.Pages[0]))));
    }

    [Fact]
    public void Fit_to_page_skips_a_page_whose_box_has_no_area()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].Dictionary[KnownNames.MediaBox] = new CosArray { new CosInteger(0), new CosInteger(0), new CosInteger(0), new CosInteger(0) };
        CosObject? before = document.Pages[0].Dictionary.GetValueOrDefault(KnownNames.Contents);

        document.Stamp(PdfStamp.FromForm(Square(document, 10)).WithFitToPage().WithBoundary(PdfPageBoundary.MediaBox));

        Assert.Same(before, document.Pages[0].Dictionary.GetValueOrDefault(KnownNames.Contents));
    }

    [Fact]
    public void A_background_stamp_is_drawn_before_the_content_and_a_foreground_one_after()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
        }

        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        document.Stamp(PdfStamp.FromText("UNDER", font, 40).WithLayer(PdfStampLayer.Background));
        document.Stamp(PdfStamp.FromText("OVER", font, 40));

        string content = Content(document, document.Pages[0]);
        int under = content.IndexOf("(UNDER)", StringComparison.Ordinal);
        int own = content.IndexOf("0 0 10 10 re", StringComparison.Ordinal);
        int over = content.IndexOf("(OVER)", StringComparison.Ordinal);
        Assert.True(under >= 0 && under < own && own < over, content);
    }

    [Fact]
    public void Opacity_below_one_sets_the_fill_and_stroke_alpha()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Stamp(PdfStamp.FromText("DRAFT", document.GetStandard14Font(Standard14Font.Helvetica), 60).WithOpacity(0.25));

        CosDictionary states = Assert.IsType<CosDictionary>(document.Resolve(document.Pages[0].Resources![new CosName("ExtGState")]));
        List<CosDictionary> values = [.. states.Select(entry => Assert.IsType<CosDictionary>(document.Resolve(entry.Value)))];
        Assert.Contains(values, state => state.TryGetValue(new CosName("ca"), out CosObject? ca) && ((CosNumber)ca).ToDouble() == 0.25);
        Assert.Contains(values, state => state.TryGetValue(new CosName("CA"), out CosObject? ca) && ((CosNumber)ca).ToDouble() == 0.25);
    }

    [Fact]
    public void One_image_and_one_font_are_shared_by_every_stamped_page()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        PdfImage image = document.CreateImage(new byte[] { 0, 128, 255, 64 }, new PdfImageLayout(2, 2, PdfColor.FromGray(0).ColorSpace), PdfImageCompression.Flate);

        document.Stamp(PdfStamp.FromImage(image).WithScale(10));
        document.Stamp(PdfStamp.FromText("Logo", document.GetStandard14Font(Standard14Font.Helvetica), 10));

        Assert.Single(document.Pages.SelectMany(page => Entries(document, page, XObject)).Distinct());
        Assert.Single(document.Pages.SelectMany(page => Entries(document, page, Font)).Distinct());
    }

    [Fact]
    public void A_page_stamp_draws_one_form_made_from_the_page_on_every_page()
    {
        using PdfDocument letterhead = PdfDocument.Create();
        using (PdfCanvas canvas = letterhead.Pages[0].CreateCanvas())
        {
            canvas.Rectangle(0, 742, 612, 50);
            canvas.Fill();
        }

        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));

        document.Stamp(PdfStamp.FromPage(letterhead.Pages[0]).WithLayer(PdfStampLayer.Background));

        CosObject form = Assert.Single(document.Pages.SelectMany(page => Entries(document, page, XObject)).Distinct());
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Single(Placements(document.Pages[1])));
        Assert.True(PdfFormXObject.Create(document, form)!.IsForm);
    }

    [Fact]
    public void Tokens_expand_with_page_labels_and_the_first_page_number()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-labels.pdf"));
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        PdfPageLabels labels = document.PageLabels!;
        int count = document.Pages.Count;

        document.Stamp(PdfStamp.FromText("{label}|{page}/{pages}", font, 8).WithFirstPageNumber(5));

        for (int index = 0; index < count; index++)
        {
            string text = document.Pages[index].ExtractText(new TextExtractionOptions().WithArtifacts()).Text;
            Assert.Contains($"{labels.GetLabel(index)}|{index + 5}/{count}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_range_stamps_only_its_pages_and_counts_them()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));

        document.Stamp(PdfStamp.FromText("{page} of {pages}", document.GetStandard14Font(Standard14Font.Helvetica), 8), 1..);

        Assert.DoesNotContain("of", Artifacts(document.Pages[0]), StringComparison.Ordinal);
        Assert.Contains("1 of 2", Artifacts(document.Pages[1]), StringComparison.Ordinal);
        Assert.Contains("2 of 2", Artifacts(document.Pages[2]), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_range_does_nothing()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Stamp(PdfStamp.FromText("x", document.GetStandard14Font(Standard14Font.Helvetica), 8), 0..0);

        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));
    }

    [Fact]
    public void Text_the_font_cannot_encode_throws_before_any_page_changes()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));

        // The second page's text holds a character WinAnsi Helvetica has no code for.
        PdfStamp stamp = PdfStamp.FromText("Page {page} 一{pages}", document.GetStandard14Font(Standard14Font.Helvetica), 8);

        Assert.Throws<ArgumentException>(() => document.Stamp(stamp));
        Assert.All(document.Pages, page => Assert.False(page.Dictionary.ContainsKey(KnownNames.Contents)));
    }

    [Fact]
    public void A_stamp_in_an_optional_content_group_can_be_hidden()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfOptionalContentGroup group = document.CreateOptionalContentGroup("Watermark");

        document.Stamp(PdfStamp.FromText("DRAFT", document.GetStandard14Font(Standard14Font.Helvetica), 40).WithOptionalContentGroup(group));

        string content = Content(document, document.Pages[0]);
        Assert.Matches(@"/OC /\w+ BDC\s*/Artifact <<[^>]*/Subtype /Watermark", content);
        Assert.True(document.Resolve(document.Pages[0].Resources![new CosName("Properties")]) is CosDictionary { Count: 1 });
    }

    [Fact]
    public void Stamps_are_pagination_watermark_artifacts_with_a_bounding_box()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Stamp(PdfStamp.FromForm(Square(document, 100)));

        string content = Content(document, document.Pages[0]);
        Assert.Matches(@"/Artifact <<\s*/Type /Pagination /BBox \[256 346 356 446\] /Subtype /Watermark\s*>> BDC", content);
    }

    private static PdfFormXObject Square(PdfDocument document, double size)
    {
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, size, size));
        using PdfCanvas canvas = form.CreateCanvas();
        canvas.Rectangle(0, 0, size, size);
        canvas.Fill();
        return form;
    }

    private static PdfRectangle Single(List<(PdfRectangle Box, Matrix Ctm)> placements) => Rounded(Assert.Single(placements).Box);

    private static PdfRectangle Rounded(PdfRectangle box) =>
        new(Math.Round(box.Left, 6), Math.Round(box.Bottom, 6), Math.Round(box.Right, 6), Math.Round(box.Top, 6));

    private static PdfRectangle Transform(Matrix matrix, PdfRectangle box)
    {
        PathPoint a = matrix.Transform(box.Left, box.Bottom);
        PathPoint b = matrix.Transform(box.Right, box.Top);
        return new PdfRectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    /// <summary>The user-space box and CTM of every form painted on the page.</summary>
    private static List<(PdfRectangle Box, Matrix Ctm)> Placements(PdfPage page)
    {
        var recorder = new FormRecorder();
        page.ProcessContent(recorder);
        return recorder.Forms;
    }

    private static IEnumerable<CosObject> Entries(PdfDocument document, PdfPage page, CosName category) =>
        document.Resolve(page.Resources?.GetValueOrDefault(category)) is CosDictionary entries ? entries.Select(entry => entry.Value) : [];

    private static string Content(PdfDocument document, PdfPage page)
    {
        var text = new StringBuilder();
        switch (document.Resolve(page.Dictionary[KnownNames.Contents]))
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

    private static string Artifacts(PdfPage page) => page.ExtractText(new TextExtractionOptions().WithArtifacts()).Text;

    private sealed class FormRecorder : ContentProcessor
    {
        public List<(PdfRectangle Box, Matrix Ctm)> Forms { get; } = [];

        public override ContentEvents Events => ContentEvents.Forms;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            PdfRectangle box = form.BoundingBox;
            Matrix ctm = form.Matrix * context.State.Ctm;
            Forms.Add((Transform(ctm, box), ctm));
            return ContentVisit.Skip;
        }
    }
}
