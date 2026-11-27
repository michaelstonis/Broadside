using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Headers and footers on existing pages (issue #627): pagination artifacts with their subtype, attached edge and bounding box
/// (ISO 32000-2 §14.8.2.2.2, Table 363), the PDF 2.0 <c>PageNum</c> subtype only where the version allows it.
/// </summary>
public sealed class HeaderFooterStampTests
{
    [Fact]
    public void Header_and_footer_texts_are_pagination_artifacts_with_subtype_edge_and_box()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);

        document.AddHeaderFooter(new PdfHeaderFooter(font, 10) { HeaderLeft = "Acme", FooterRight = "Confidential" });

        List<CosDictionary> artifacts = Artifacts(document.Pages[0]);
        Assert.Equal(2, artifacts.Count);
        CosDictionary header = artifacts[0];
        Assert.Equal(new CosName("Pagination"), header[KnownNames.Type]);
        Assert.Equal(new CosName("Header"), header[new CosName("Subtype")]);
        Assert.Equal(new CosName("Top"), Assert.Single(Assert.IsType<CosArray>(header[new CosName("Attached")])));
        double[] box = [.. Assert.IsType<CosArray>(header[new CosName("BBox")]).Select(value => ((CosNumber)value).ToDouble())];
        Assert.Equal(36, box[0], 6);
        Assert.Equal(792 - 36, box[3], 6);
        CosDictionary footer = artifacts[1];
        Assert.Equal(new CosName("Footer"), footer[new CosName("Subtype")]);
        Assert.Equal(new CosName("Bottom"), Assert.Single(Assert.IsType<CosArray>(footer[new CosName("Attached")])));
        double[] footerBox = [.. Assert.IsType<CosArray>(footer[new CosName("BBox")]).Select(value => ((CosNumber)value).ToDouble())];
        Assert.Equal(612 - 36, footerBox[2], 6);
        Assert.Equal(36, footerBox[1], 6);
    }

    [Fact]
    public void A_page_number_is_a_PageNum_artifact_on_a_pdf_2_0_document()
    {
        using PdfDocument document = PdfDocument.Create();
        Assert.Equal(new PdfVersion(2, 0), document.Version);

        document.AddHeaderFooter(new PdfHeaderFooter(document.GetStandard14Font(Standard14Font.Helvetica), 9) { FooterCenter = "Page {page} of {pages}" });

        CosDictionary artifact = Assert.Single(Artifacts(document.Pages[0]));
        Assert.Equal(new CosName("PageNum"), artifact[new CosName("Subtype")]);
        Assert.Contains("Page 1 of 1", document.Pages[0].ExtractText(new Broadside.Text.TextExtractionOptions().WithArtifacts()).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_number_is_a_Footer_artifact_on_a_pdf_1_7_document()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-labels.pdf"));
        Assert.Equal(new PdfVersion(1, 7), document.Version);

        document.AddHeaderFooter(new PdfHeaderFooter(document.GetStandard14Font(Standard14Font.Helvetica), 9) { FooterCenter = "{label}" });

        foreach (PdfPage page in document.Pages)
        {
            CosDictionary artifact = Assert.Single(Artifacts(page));
            Assert.Equal(new CosName("Footer"), artifact[new CosName("Subtype")]);
        }

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
    }

    [Fact]
    public void Texts_follow_the_page_rotation_and_the_range()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage turned = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        turned.Rotation = 90;

        document.AddHeaderFooter(new PdfHeaderFooter(document.GetStandard14Font(Standard14Font.Helvetica), 9) { HeaderCenter = "Title" }, 1..);

        Assert.Empty(Artifacts(document.Pages[0]));
        double[] box = [.. Assert.IsType<CosArray>(Assert.Single(Artifacts(turned))[new CosName("BBox")]).Select(value => ((CosNumber)value).ToDouble())];

        // Viewed rotated a quarter clockwise, the top of the page is the user-space edge x = 0... the header hugs x = 36.
        Assert.Equal(36, box[0], 6);
    }

    [Fact]
    public void A_font_of_another_document_is_refused_before_any_page_changes()
    {
        using PdfDocument other = PdfDocument.Create();
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.AddHeaderFooter(new PdfHeaderFooter(other.GetStandard14Font(Standard14Font.Helvetica), 9) { HeaderLeft = "x" }));
        Assert.False(document.Pages[0].Dictionary.ContainsKey(KnownNames.Contents));
    }

    /// <summary>The property lists of the page's <c>/Artifact</c> marked-content sequences, in content order.</summary>
    internal static List<CosDictionary> Artifacts(PdfPage page)
    {
        var recorder = new ArtifactRecorder();
        page.ProcessContent(recorder);
        return recorder.Lists;
    }

    private sealed class ArtifactRecorder : ContentProcessor
    {
        public List<CosDictionary> Lists { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            if (Encoding.ASCII.GetString(mc.Tag) != "Artifact")
            {
                return;
            }

            if (mc.Properties is { } named)
            {
                Lists.Add(named);
            }
            else if (mc.InlineProperties.Kind == ContentOperandKind.Dictionary && mc.InlineProperties.ToCosObject() is CosDictionary inline)
            {
                Lists.Add(inline);
            }
        }
    }
}
