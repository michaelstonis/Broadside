using System.Text;
using Broadside.Diagnostics;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Reading ALTO 2.0 to 4.4 (issue #649): pages, lines and words in any ALTO namespace, with confidences, rotations and languages;
/// damaged words are skipped with a diagnostic and XML that is not well-formed throws. The ABBYY-style sample is the corpus file
/// <c>ocr-page.alto.xml</c>.
/// </summary>
public sealed class AltoParserTests
{
    /// <summary>The shape Tesseract writes (<c>tesseract scan.png out alto</c>, ALTO 3), with a rotated block.</summary>
    internal const string Tesseract = """
        <?xml version="1.0" encoding="UTF-8"?>
        <alto xmlns="http://www.loc.gov/standards/alto/ns-v3#" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="http://www.loc.gov/standards/alto/ns-v3# http://www.loc.gov/alto/v3/alto-3-0.xsd">
          <Description>
            <MeasurementUnit>pixel</MeasurementUnit>
            <sourceImageInformation><fileName>scan.png</fileName></sourceImageInformation>
            <OCRProcessing ID="OCR_0"><ocrProcessingStep><processingSoftware><softwareName>tesseract 5.3.0</softwareName></processingSoftware></ocrProcessingStep></OCRProcessing>
          </Description>
          <Layout>
            <Page WIDTH="1700" HEIGHT="2200" PHYSICAL_IMG_NR="0" ID="page_0">
              <PrintSpace HPOS="0" VPOS="0" WIDTH="1700" HEIGHT="2200">
                <ComposedBlock ID="cblock_0" HPOS="100" VPOS="100" WIDTH="900" HEIGHT="400">
                  <TextBlock ID="block_0" HPOS="100" VPOS="100" WIDTH="900" HEIGHT="60">
                    <TextLine ID="line_0" HPOS="100" VPOS="100" WIDTH="900" HEIGHT="60">
                      <String ID="string_0" HPOS="100" VPOS="100" WIDTH="300" HEIGHT="60" WC="0.96" CONTENT="Hello"/><SP WIDTH="20" VPOS="100" HPOS="400"/>
                      <String ID="string_1" HPOS="420" VPOS="100" WIDTH="300" HEIGHT="60" WC="0.95" CONTENT="world"/>
                    </TextLine>
                  </TextBlock>
                  <TextBlock ID="block_1" HPOS="1500" VPOS="200" WIDTH="60" HEIGHT="400" ROTATION="90">
                    <TextLine ID="line_1" HPOS="1500" VPOS="200" WIDTH="60" HEIGHT="400">
                      <String ID="string_2" HPOS="1500" VPOS="200" WIDTH="60" HEIGHT="400" WC="0.90" CONTENT="Sideways"/>
                    </TextLine>
                  </TextBlock>
                </ComposedBlock>
              </PrintSpace>
            </Page>
          </Layout>
        </alto>
        """;

    [Fact]
    public void An_abbyy_document_reads_as_pages_lines_and_words()
    {
        OcrPage page = Assert.Single(OcrPage.FromAlto(new MemoryStream(Broadside.TestSupport.Corpus.Bytes("ocr-page.alto.xml"))));

        Assert.Equal((2550, 3300), (page.Width, page.Height));
        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(["Rechnung", "Nr.", "17&4"], page.Lines[0].Words.Select(static word => word.Text));
        Assert.Equal(new PdfRectangle(200, 200, 440, 250), page.Lines[0].Words[0].Bounds);
        Assert.Equal([0.97, 0.81, 1], page.Lines[0].Words.Select(static word => word.Confidence));
        Assert.Equal("de", page.Lines[0].Language);
        Assert.Equal("en", page.Lines[1].Language);
        Assert.Empty(page.Diagnostics);
    }

    [Fact]
    public void A_tesseract_document_reads_with_the_rotation_of_its_blocks()
    {
        OcrPage page = Assert.Single(Read(Tesseract));

        Assert.Equal((1700, 2200), (page.Width, page.Height));
        Assert.Equal(["Hello", "world"], page.Lines[0].Words.Select(static word => word.Text));
        Assert.Equal(0, page.Lines[0].BaselineAngle);
        Assert.Equal(90, page.Lines[1].BaselineAngle);
        Assert.Equal(0, page.Lines[1].Words[0].Angle);
        Assert.Null(page.Lines[0].Language);
    }

    [Theory]
    [InlineData("http://www.loc.gov/standards/alto/ns-v4#")]
    [InlineData("")]
    public void Alto_4_and_alto_without_a_namespace_read_alike(string ns)
    {
        string alto = $"""
            <alto xmlns="{ns}"><Layout><Page WIDTH="100" HEIGHT="50"><PrintSpace><TextBlock><TextLine>
            <String CONTENT="four" HPOS="1" VPOS="2" WIDTH="30" HEIGHT="10" ROTATION="15"/>
            </TextLine></TextBlock></PrintSpace></Page></Layout></alto>
            """;

        OcrWord word = Assert.Single(Assert.Single(Assert.Single(Read(alto)).Lines).Words);

        Assert.Equal(("four", new PdfRectangle(1, 2, 31, 12), 15.0), (word.Text, word.Bounds, word.Angle));
    }

    [Fact]
    public void Damaged_coordinates_skip_the_word_and_a_bad_confidence_or_page_size_is_repaired_with_diagnostics()
    {
        const string alto = """
            <alto><Layout><Page WIDTH="wide"><TextBlock><TextLine>
            <String CONTENT="nohpos" VPOS="2" WIDTH="30" HEIGHT="10"/>
            <String CONTENT="nan" HPOS="NaN" VPOS="2" WIDTH="30" HEIGHT="10"/>
            <String CONTENT="negative" HPOS="1" VPOS="2" WIDTH="-30" HEIGHT="10"/>
            <String CONTENT="kept" HPOS="40" VPOS="2" WIDTH="30" HEIGHT="10" WC="1.7"/>
            </TextLine></TextBlock></Page></Layout></alto>
            """;

        OcrPage page = Assert.Single(Read(alto));

        OcrWord word = Assert.Single(Assert.Single(page.Lines).Words);
        Assert.Equal(("kept", 1.0), (word.Text, word.Confidence));
        Assert.Equal((70, 12), (page.Width, page.Height));
        Assert.Equal(3, page.Diagnostics.Count(static diagnostic => diagnostic.Kind == TextDiagnostics.OcrBoundsInvalid));
        Assert.Single(page.Diagnostics, static diagnostic => diagnostic.Kind == TextDiagnostics.OcrConfidenceInvalid);
        Assert.Single(page.Diagnostics, static diagnostic => diagnostic.Kind == TextDiagnostics.OcrPageSizeInvalid);
    }

    [Fact]
    public void Xml_that_is_not_well_formed_throws_a_format_exception_and_a_dtd_is_not_processed()
    {
        Assert.Throws<FormatException>(() => Read("<alto><Layout><Page></Layout>"));
        Assert.Throws<FormatException>(() => Read("not xml"));
        Assert.Throws<ArgumentNullException>(() => OcrPage.FromAlto(null!));

        const string entities = """
            <!DOCTYPE alto [<!ENTITY a "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <alto><Layout><Page WIDTH="10" HEIGHT="10"><TextLine><String CONTENT="&b;" HPOS="0" VPOS="0" WIDTH="5" HEIGHT="5"/></TextLine></Page></Layout></alto>
            """;
        Assert.Throws<FormatException>(() => Read(entities));
    }

    [Fact]
    public void A_document_without_pages_reads_as_none()
    {
        Assert.Empty(Read("<alto><Description/></alto>"));
    }

    [Fact]
    public void An_abbyy_page_becomes_a_text_layer_that_extracts_its_words()
    {
        using PdfDocument document = PdfDocument.Create();

        document.Pages[0].AddTextLayer(Assert.Single(OcrPage.FromAlto(new MemoryStream(Broadside.TestSupport.Corpus.Bytes("ocr-page.alto.xml")))));

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        Assert.Equal(["Rechnung", "Nr.", "17&4", "Invoice"], TextLayerTests.Words(reopened.Pages[0].ExtractText()));
    }

    private static IReadOnlyList<OcrPage> Read(string alto) => OcrPage.FromAlto(new MemoryStream(Encoding.UTF8.GetBytes(alto)));
}
