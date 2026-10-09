using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// A counting processor run over the corpus pages through the public seam (<see cref="PdfPage.ProcessContent(ContentProcessor)"/>)
/// sees the events each file's content stream describes. ISO 32000-2 §7.8.2, §8.2.
/// </summary>
public class CorpusContentTests
{
    public static TheoryData<string> FilterFiles => new()
    {
        "flate-stream.pdf",
        "lzw-stream.pdf",
        "ascii85-stream.pdf",
        "asciihex-stream.pdf",
        "runlength-stream.pdf",
        "filter-chain.pdf",
    };

    [Fact]
    public void A_page_without_content_begins_and_ends_a_run_and_nothing_else()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        var processor = new RecordingProcessor();

        document.Pages[0].ProcessContent(processor);

        Assert.Equal(["BeginRun Page", "EndRun"], processor.Lines);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(FilterFiles))]
    public void A_filtered_content_stream_is_decoded_and_interpreted(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        var processor = new RecordingProcessor();

        document.Pages[0].ProcessContent(processor);

        Assert.Equal(1, processor.Count("BeginRun"));
        Assert.Equal(1, processor.Count("EndRun"));
        Assert.Equal(1, processor.Count("BeginText"));
        Assert.Equal(1, processor.Count("EndText"));
        Assert.Equal(5, processor.Count("Operator"));
        Assert.Equal(["Op BT", "Op /F1 24 Tf", "Op 72 700 Td"], processor.Lines.Where(line => line.StartsWith("Op", StringComparison.Ordinal)).Take(3));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Every_page_of_a_well_formed_file_interprets_without_a_warning(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        foreach (PdfPage page in document.Pages)
        {
            var processor = new RecordingProcessor();
            page.ProcessContent(processor);
            Assert.Equal(1, processor.Count("BeginRun"));
            Assert.Equal(1, processor.Count("EndRun"));
        }

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
    }
}
