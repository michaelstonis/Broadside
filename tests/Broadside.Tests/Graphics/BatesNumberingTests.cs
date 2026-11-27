using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Bates numbering of existing pages (issue #646): a zero-padded counter that carries across documents, stamped as a
/// <c>Pagination</c> artifact with the PDF 2.0 <c>Bates</c> subtype or, below 2.0, <c>Footer</c> (ISO 32000-2 §14.8.2.2.2, Table 363),
/// placed in the page's box as viewed (§14.11.2).
/// </summary>
public sealed class BatesNumberingTests
{
    private static readonly CosName Subtype = new("Subtype");

    [Fact]
    public void Numbers_are_zero_padded_between_the_prefix_and_the_suffix()
    {
        using PdfDocument document = PdfDocument.Create();
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9);

        Assert.Equal("000001", bates.Format(1));
        Assert.Equal(1, bates.NextNumber);
        Assert.Equal("ACME000123-C", bates.WithPrefix("ACME").WithSuffix("-C").Format(123));
        Assert.Equal("ACME999999-C", bates.Format(999_999));
        Assert.Equal("ACME7-C", bates.WithDigits(1).Format(7));
        Assert.Equal("ACME999999999999999999-C", bates.WithDigits(18).Format(999_999_999_999_999_999));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.WithDigits(3).Format(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.Format(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.WithDigits(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.WithDigits(19));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.WithNextNumber(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => bates.WithMargin(-1));
        Assert.Throws<ArgumentNullException>(() => bates.WithPrefix(null!));
    }

    [Fact]
    public void The_counter_carries_across_three_documents()
    {
        using PdfDocument source = PdfDocument.Create();
        var bates = new PdfBatesNumbering(source.GetStandard14Font(Standard14Font.Courier), 9).WithPrefix("ACME").WithNextNumber(41);
        var ranges = new List<PdfBatesRange>();
        var texts = new List<string>();

        foreach (int pageCount in new[] { 2, 3, 1 })
        {
            ranges.Add(Number(pageCount, bates, texts));
        }

        Assert.Equal(
            [new(41, 42, "ACME000041", "ACME000042"), new(43, 45, "ACME000043", "ACME000045"), new(46, 46, "ACME000046", "ACME000046")],
            ranges);
        Assert.Equal(["ACME000041", "ACME000042", "ACME000043", "ACME000044", "ACME000045", "ACME000046"], texts);
        Assert.Equal(47, bates.NextNumber);
    }

    [Fact]
    public void A_range_numbers_only_its_pages_and_shares_one_font_resource()
    {
        using PdfDocument document = PdfDocument.Create();
        for (int index = 0; index < 3; index++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        }

        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9);
        PdfBatesRange range = document.AddBatesNumbers(bates, 1..3);

        Assert.Equal(new PdfBatesRange(1, 2, "000001", "000002"), range);
        Assert.Empty(HeaderFooterStampTests.Artifacts(document.Pages[0]));
        Assert.Empty(HeaderFooterStampTests.Artifacts(document.Pages[3]));
        Assert.Single(document.Pages.SelectMany(FontResources).Distinct());
    }

    [Fact]
    public void Overflow_throws_naming_the_number_and_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9).WithDigits(2).WithNextNumber(98);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => document.AddBatesNumbers(bates));

        Assert.Contains("100", refused.Message, StringComparison.Ordinal);
        Assert.Equal(98, bates.NextNumber);
        Assert.All(document.Pages, page => Assert.False(page.Dictionary.ContainsKey(KnownNames.Contents)));
    }

    [Fact]
    public void Overflow_near_the_largest_long_is_refused_without_wrapping()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9).WithDigits(18).WithNextNumber(999_999_999_999_999_999);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => document.AddBatesNumbers(bates));

        Assert.Contains("1000000000000000000", refused.Message, StringComparison.Ordinal);
        Assert.Equal(999_999_999_999_999_999, bates.NextNumber);
    }

    [Fact]
    public void An_empty_range_numbers_nothing_and_leaves_the_counter()
    {
        using PdfDocument document = PdfDocument.Create();
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9).WithNextNumber(10);

        PdfBatesRange range = document.AddBatesNumbers(bates, 0..0);

        Assert.Equal(10, range.FirstNumber);
        Assert.Equal(9, range.LastNumber);
        Assert.Equal(string.Empty, range.First);
        Assert.Equal(10, bates.NextNumber);
        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));
    }

    [Fact]
    public void A_prefix_the_font_cannot_show_is_refused_before_any_change()
    {
        using PdfDocument document = PdfDocument.Create();
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9).WithPrefix("証拠");

        ArgumentException refused = Assert.Throws<ArgumentException>(() => document.AddBatesNumbers(bates));

        Assert.Equal("numbering", refused.ParamName);
        Assert.Equal(1, bates.NextNumber);
        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));
    }

    [Fact]
    public void An_embedded_font_of_another_document_is_refused_and_a_standard_14_font_is_reused()
    {
        using PdfDocument other = PdfDocument.Create();
        using PdfDocument document = PdfDocument.Create();
        var bates = new PdfBatesNumbering(other.EmbedTrueTypeFont(TextSamples.FontProgram), 9);

        Assert.Throws<ArgumentException>(() => document.AddBatesNumbers(bates));
        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));

        bates.Font = document.EmbedTrueTypeFont(TextSamples.FontProgram);
        Assert.Equal("000001", document.AddBatesNumbers(bates).First);
        Assert.Single(HeaderFooterStampTests.Artifacts(document.Pages[0]));
    }

    [Fact]
    public void The_number_is_a_Bates_artifact_attached_to_the_bottom_on_a_pdf_2_0_document()
    {
        using PdfDocument document = PdfDocument.Create();
        Assert.Equal(new PdfVersion(2, 0), document.Version);

        document.AddBatesNumbers(new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9).WithPrefix("ACME"));

        CosDictionary artifact = Assert.Single(HeaderFooterStampTests.Artifacts(document.Pages[0]));
        Assert.Equal(new CosName("Pagination"), artifact[KnownNames.Type]);
        Assert.Equal(new CosName("Bates"), artifact[Subtype]);
        Assert.Equal(new CosName("Bottom"), Assert.Single(Assert.IsType<CosArray>(artifact[new CosName("Attached")])));
        double[] box = Box(artifact);
        Assert.Equal(612 - 18, box[2], 6);
        Assert.Equal(18, box[1], 6);
    }

    [Fact]
    public void The_number_is_a_Footer_artifact_on_a_pdf_1_7_document_and_the_version_stays()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-labels.pdf"));
        Assert.Equal(new PdfVersion(1, 7), document.Version);
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 9)
            .WithVerticalAlignment(PdfStampAlignment.Start);

        PdfBatesRange range = document.AddBatesNumbers(bates);

        Assert.Equal(new PdfBatesRange(1, 12, "000001", "000012"), range);
        foreach (PdfPage page in document.Pages)
        {
            CosDictionary artifact = Assert.Single(HeaderFooterStampTests.Artifacts(page));
            Assert.Equal(new CosName("Footer"), artifact[Subtype]);
            Assert.Equal(new CosName("Top"), Assert.Single(Assert.IsType<CosArray>(artifact[new CosName("Attached")])));
        }

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Numbers_on_rotated_pages_sit_bottom_right_as_viewed(int pageIndex)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        PdfPage page = document.Pages[pageIndex];
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.Helvetica), 6).WithMargin(4);

        document.AddBatesNumbers(bates);

        // The artifact's box, seen through the page's rotation, hugs the bottom-right corner of the displayed crop box.
        Matrix toDisplayed = PageFormImporter.FormMatrix(page.CropBox, page.Rotation, 1);
        double[] box = Box(Assert.Single(HeaderFooterStampTests.Artifacts(page)));
        PathPoint a = toDisplayed.Transform(box[0], box[1]);
        PathPoint b = toDisplayed.Transform(box[2], box[3]);
        double displayedWidth = page.Rotation is 90 or 270 ? page.CropBox.Height : page.CropBox.Width;
        Assert.Equal(displayedWidth - 4, Math.Max(a.X, b.X), 6);
        Assert.Equal(4, Math.Min(a.Y, b.Y), 6);

        // Upright as viewed: the text reads left to right on screen.
        string text = page.ExtractText(new TextExtractionOptions().WithArtifacts()).Text;
        Assert.Contains(bates.Format(pageIndex + 1), text, StringComparison.Ordinal);
    }

    /// <summary>Numbers a new document of <paramref name="pageCount"/> pages and collects the numbers read back from its saved bytes.</summary>
    private static PdfBatesRange Number(int pageCount, PdfBatesNumbering bates, List<string> texts)
    {
        using PdfDocument document = PdfDocument.Create();
        for (int index = 1; index < pageCount; index++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        }

        PdfBatesRange range = document.AddBatesNumbers(bates);
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        texts.AddRange(reopened.Pages.Select(page => page.ExtractText(new TextExtractionOptions().WithArtifacts()).Text.Trim()));
        Assert.Empty(reopened.Diagnostics);
        return range;
    }

    private static double[] Box(CosDictionary artifact) =>
        [.. Assert.IsType<CosArray>(artifact[new CosName("BBox")]).Select(value => ((CosNumber)value).ToDouble())];

    private static IEnumerable<CosObject> FontResources(PdfPage page) =>
        page.Document.Resolve(page.Resources?.GetValueOrDefault(new CosName("Font"))) is CosDictionary entries ? entries.Select(entry => entry.Value) : [];
}
