using System.IO.Compression;
using System.Text;
using Broadside.Content;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// A page's decoded content is cached per document (issue #90, ISO 32000-2 §7.8.2): a later run reuses it, and any edit of what it
/// was decoded from (the stream's data, its dictionary, the <c>Contents</c> array or what the page's <c>Contents</c> names) is seen
/// by the next run. The allocation half (a second run neither reads nor decodes) is in <see cref="ContentAllocationTests"/>.
/// </summary>
public sealed class ContentCacheTests
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Filter = new("Filter");

    [Fact]
    public void Replacing_the_data_of_the_page_content_stream_is_seen_by_the_next_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("flate-stream.pdf"));
        PdfPage page = document.Pages[0];
        CosStream stream = ContentStream(document, page);
        List<string> before = Run(page);

        stream.EncodedData = Deflate("0 0 m 10 10 l S");

        Assert.NotEqual(["Op 0 0 m", "Op 10 10 l", "Op S"], before);
        Assert.Equal(["Op 0 0 m", "Op 10 10 l", "Op S"], Run(page));
    }

    [Fact]
    public void Changing_the_filters_of_the_page_content_stream_is_seen_by_the_next_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("3020302032203220726520660A>"));
        PdfPage page = document.Pages[0];
        CosStream stream = ContentStream(document, page);
        _ = Run(page);

        stream.Dictionary[Filter] = new CosName("ASCIIHexDecode");

        Assert.Equal(["Op 0 0 2 2 re", "Op f"], Run(page));
    }

    [Fact]
    public void Pointing_the_page_at_another_content_stream_is_seen_by_the_next_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m 1 1 l S"));
        PdfPage page = document.Pages[0];
        _ = Run(page);

        page.Dictionary[Contents] = new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes("q Q"));

        Assert.Equal(["Op q", "Op Q"], Run(page));
    }

    [Fact]
    public void Adding_a_stream_to_the_contents_array_is_seen_by_the_next_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m", "1 1 l S"));
        PdfPage page = document.Pages[0];
        Assert.Equal(["Op 0 0 m", "Op 1 1 l", "Op S"], Run(page));
        var array = (CosArray)document.Resolve(page.Dictionary[Contents]);

        array.Add(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes("q Q")));

        Assert.Equal(["Op 0 0 m", "Op 1 1 l", "Op S", "Op q", "Op Q"], Run(page));
    }

    [Fact]
    public void Replacing_the_data_of_a_part_of_the_contents_array_is_seen_by_the_next_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m", "1 1 l S"));
        PdfPage page = document.Pages[0];
        _ = Run(page);
        var array = (CosArray)document.Resolve(page.Dictionary[Contents]);

        ((CosStream)document.Resolve(array[1])).EncodedData = Encoding.ASCII.GetBytes("2 2 l s");

        Assert.Equal(["Op 0 0 m", "Op 2 2 l", "Op s"], Run(page));
    }

    [Fact]
    public void A_contents_array_element_that_is_not_a_stream_is_reported_on_every_run()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents [4 0 R 5 0 R] >>",
            "<< /Length 3 >>\nstream\nq Q\nendstream",
            "(not a stream)");

        Assert.Equal(["ContentStreamInvalid"], RunStrict(file, runs: 1));
        Assert.Equal(["ContentStreamInvalid", "ContentStreamInvalid"], RunStrict(file, runs: 2));
    }

    /// <summary>
    /// Runs of the same page started together on a cold cache, many times over: every run sees the events a single run sees.
    /// </summary>
    [Theory]
    [InlineData("filter-chain.pdf")]
    [InlineData("pattern-in-form.pdf")]
    [InlineData("text-type1-embedded.pdf")]
    public void Concurrent_runs_of_a_page_see_the_events_of_a_single_run(string file)
    {
        byte[] bytes = Corpus.Bytes(file);
        List<string> expected;
        using (PdfDocument single = PdfDocument.Open(bytes))
        {
            expected = RunAll(single.Pages[0]);
        }

        int threads = Math.Max(4, Environment.ProcessorCount);
        for (int round = 0; round < 20; round++)
        {
            using PdfDocument document = PdfDocument.Open(bytes);
            PdfPage page = document.Pages[0];
            using var start = new Barrier(threads);
            var results = new List<string>[threads];

            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, index =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                results[index] = RunAll(page);
            });

            Assert.All(results, result => Assert.Equal(expected, result));
        }
    }

    private static List<string> RunAll(PdfPage page)
    {
        var processor = new RecordingProcessor(ContentEvents.All);
        page.ProcessContent(processor);
        return processor.Lines;
    }

    /// <summary>Runs the page of <paramref name="file"/> <paramref name="runs"/> times in strict mode and returns the code each run threw.</summary>
    private static List<string> RunStrict(byte[] file, int runs)
    {
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());
        var codes = new List<string>();
        for (int run = 0; run < runs; run++)
        {
            codes.Add(Assert.Throws<Diagnostics.DiagnosticException>(() => document.Pages[0].ProcessContent(new RecordingProcessor())).Diagnostic.Code);
        }

        return codes;
    }

    private static List<string> Run(PdfPage page)
    {
        var processor = new RecordingProcessor(ContentEvents.Operators);
        page.ProcessContent(processor);
        return processor.Body;
    }

    private static CosStream ContentStream(PdfDocument document, PdfPage page) => (CosStream)document.Resolve(page.Dictionary[Contents]);

    private static byte[] Deflate(string content)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
        {
            zlib.Write(Encoding.ASCII.GetBytes(content));
        }

        return output.ToArray();
    }
}
