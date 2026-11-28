using System.Text;
using Broadside.Diagnostics;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Reading hOCR 1.2 (issue #649): pages, lines and words with their boxes, confidences, angles and languages, from Tesseract's XHTML
/// and from HTML that is not well-formed XML; damaged words are skipped with a diagnostic. The Tesseract-style sample is the
/// corpus file <c>ocr-page.hocr</c>.
/// </summary>
public sealed class HocrParserTests
{
    [Fact]
    public void A_tesseract_document_reads_as_pages_lines_and_words()
    {
        OcrPage page = Assert.Single(OcrPage.FromHocr(new MemoryStream(Broadside.TestSupport.Corpus.Bytes("ocr-page.hocr"))));

        Assert.Equal((2480, 3508), (page.Width, page.Height));
        Assert.Equal(2, page.Lines.Count);
        OcrLine first = page.Lines[0];
        Assert.Equal(["Invoice", "No.", "A&B"], first.Words.Select(static word => word.Text));
        Assert.Equal(new PdfRectangle(300, 400, 520, 450), first.Words[0].Bounds);
        Assert.Equal([0.96, 0.91, 0.42], first.Words.Select(static word => word.Confidence));
        Assert.Equal("en", first.Language);
        Assert.Equal(0, first.BaselineAngle);
        Assert.Equal(90, page.Lines[1].BaselineAngle);
        Assert.Empty(page.Diagnostics);
    }

    [Fact]
    public void Html_that_is_not_well_formed_xml_reads_with_the_tolerant_tag_reader()
    {
        const string html = """
            <!doctype html>
            <html lang=de><head><meta charset=utf-8><title>scan</title>
            <style>.ocrx_word { color: red } </style><script>var x = "<span class='ocrx_word'>";</script></head>
            <body>
            <div class=ocr_page title="bbox 0 0 1000 800">
            <!-- a comment with <span class='ocrx_word'> inside -->
            <span class=ocr_line title="bbox 10 10 500 40">
            <span class="ocrx_word" title="bbox 10 10 100 40; x_wconf 90">Straße<br>
            <span class="ocrx_word" title="bbox 120 10 200 40">&#x263A;&nbsp;x</span>
            <p>
            <span class="ocr_line ocrx_line" title="bbox 10 60 500 90"><span class="ocrx_word" title="bbox 10 60 90 90">zwei</span>
            </div>
            """;

        OcrPage page = Assert.Single(Read(html));

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(["Straße", "\u263A x"], page.Lines[0].Words.Select(static word => word.Text));
        Assert.Equal(["zwei"], page.Lines[1].Words.Select(static word => word.Text));
        Assert.Equal("de", page.Lines[0].Language);
        Assert.Equal(0.9, page.Lines[0].Words[0].Confidence);
        Assert.Equal(1, page.Lines[0].Words[1].Confidence);
    }

    [Fact]
    public void Several_pages_read_in_document_order_and_a_page_offset_moves_its_words()
    {
        const string hocr = """
            <html><body>
            <div class='ocr_page' title='bbox 0 0 100 100'><span class='ocr_line' title='bbox 0 0 50 10'><span class='ocrx_word' title='bbox 0 0 50 10'>one</span></span></div>
            <div class='ocr_page' title='bbox 100 200 300 400'><span class='ocr_line' title='bbox 110 210 150 220'><span class='ocrx_word' title='bbox 110 210 150 220'>two</span></span></div>
            </body></html>
            """;

        IReadOnlyList<OcrPage> pages = Read(hocr);

        Assert.Equal(2, pages.Count);
        Assert.Equal((200, 200), (pages[1].Width, pages[1].Height));
        Assert.Equal(new PdfRectangle(10, 10, 50, 20), pages[1].Lines[0].Words[0].Bounds);
    }

    [Fact]
    public void A_malformed_bbox_skips_the_word_and_a_malformed_confidence_reads_as_one_with_diagnostics()
    {
        const string hocr = """
            <div class='ocr_page' title='bbox 0 0 100 100'>
            <span class='ocr_line'>
            <span class='ocrx_word' title='bbox 1 2 x 4'>broken</span>
            <span class='ocrx_word' title='x_wconf 50'>boxless</span>
            <span class='ocrx_word' title='bbox 10 10 5 20'>backwards</span>
            <span class='ocrx_word' title='bbox 10 10 50 20; x_wconf 250'>kept</span>
            </span></div>
            """;

        OcrPage page = Assert.Single(Read(hocr));

        OcrWord word = Assert.Single(Assert.Single(page.Lines).Words);
        Assert.Equal(("kept", 1.0), (word.Text, word.Confidence));
        Assert.Equal(3, page.Diagnostics.Count(static diagnostic => diagnostic.Kind == TextDiagnostics.OcrBoundsInvalid));
        Assert.Single(page.Diagnostics, static diagnostic => diagnostic.Kind == TextDiagnostics.OcrConfidenceInvalid);
    }

    [Fact]
    public void A_page_without_a_size_takes_the_extent_of_its_words_and_says_so()
    {
        OcrPage page = Assert.Single(Read("<div class='ocr_page'><span class='ocrx_word' title='bbox 10 10 300 50'>alone</span></div>"));

        Assert.Equal((300, 50), (page.Width, page.Height));
        Assert.Equal("alone", Assert.Single(Assert.Single(page.Lines).Words).Text);
        Assert.Single(page.Diagnostics, static diagnostic => diagnostic.Kind == TextDiagnostics.OcrPageSizeInvalid);
    }

    [Fact]
    public void Markup_without_pages_reads_as_none_and_input_without_markup_throws()
    {
        Assert.Empty(Read("<html><body><p>Nothing recognized</p></body></html>"));
        Assert.Throws<FormatException>(() => Read("just some text"));
        Assert.Throws<FormatException>(() => Read(string.Empty));
        Assert.Throws<ArgumentNullException>(() => OcrPage.FromHocr(null!));
    }

    [Fact]
    public void Utf16_input_with_a_byte_order_mark_reads()
    {
        byte[] data = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<div class='ocr_page' title='bbox 0 0 10 10'><span class='ocrx_word' title='bbox 0 0 5 5'>Ж</span></div>")];

        OcrPage page = Assert.Single(OcrPage.FromHocr(new MemoryStream(data)));

        Assert.Equal("Ж", page.Lines[0].Words[0].Text);
    }

    [Fact]
    public void A_tesseract_page_becomes_a_text_layer_that_extracts_its_words()
    {
        using PdfDocument document = PdfDocument.Create();
        OcrPage page = Assert.Single(OcrPage.FromHocr(new MemoryStream(Broadside.TestSupport.Corpus.Bytes("ocr-page.hocr"))));

        document.Pages[0].AddTextLayer(page);

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        Assert.Equal(["Invoice", "No.", "A&B", "vertical"], TextLayerTests.Words(reopened.Pages[0].ExtractText()));
    }

    private static IReadOnlyList<OcrPage> Read(string hocr) => OcrPage.FromHocr(new MemoryStream(Encoding.UTF8.GetBytes(hocr)));
}
