using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Extraction allocates nothing per glyph beyond its output (CLAUDE.md, "Code conventions"): an extractor reused for a page
/// allocates nothing once its buffers have grown. <c>TextExtractionBenchmarks</c> in <c>bench/Broadside.Benchmarks</c> is the
/// measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TextExtractionAllocationTests
{
    private const int WarmUp = 50;

    [Fact]
    public void Extracting_ten_thousand_glyphs_again_into_a_reused_extractor_allocates_nothing()
    {
        string lines = System.Text.Encoding.ASCII.GetString(ContentSamples.TextHeavy(10_000));
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, $"{lines} /Artifact BMC {lines} EMC", ContentPdf.Helvetica));
        var extractor = new TextExtractor();

        long allocated = Measure(document.Pages[0], extractor);

        Assert.True(extractor.Glyphs.Length >= 20_000, $"Only {extractor.Glyphs.Length} glyphs.");
        Assert.Equal(0, allocated);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_ActualText_replacement_allocates_its_string_once_per_sequence_whatever_the_number_of_glyphs_it_covers()
    {
        string many = System.Text.Encoding.ASCII.GetString(ContentSamples.TextHeavy(10_000));
        const string few = "BT /F1 10 Tf (ab) Tj ET";
        using PdfDocument small = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, $"/Span << /ActualText (replacement) >> BDC {few} EMC", ContentPdf.Helvetica));
        using PdfDocument large = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, $"/Span << /ActualText (replacement) >> BDC {many} EMC", ContentPdf.Helvetica));
        var extractor = new TextExtractor();

        long forFew = Measure(small.Pages[0], extractor);
        long forMany = Measure(large.Pages[0], extractor);

        Assert.True(extractor.Glyphs.Length >= 10_000, $"Only {extractor.Glyphs.Length} glyphs.");
        Assert.Equal("replacement", extractor.Text.ToString());
        Assert.True(forFew > 0, "The replacement string is expected to be allocated.");
        Assert.Equal(forFew, forMany);
    }

    private static long Measure(PdfPage page, TextExtractor extractor) => Allocations.Measure(
        () =>
        {
            extractor.Clear();
            page.ProcessContent(extractor);
        },
        WarmUp);
}
