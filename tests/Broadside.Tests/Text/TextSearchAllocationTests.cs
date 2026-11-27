using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// A literal search folds the query into a pooled buffer and matches the page's cached folded text (issue #623): once the page has
/// been searched, searching it again allocates nothing but the results.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TextSearchAllocationTests
{
    [Fact]
    public void A_literal_search_of_an_extracted_page_with_no_match_allocates_nothing()
    {
        TextPage page = ExtractTenThousandGlyphs(out PdfDocument document);
        using (document)
        {
            var query = new TextQuery("Not on the page");
            IReadOnlyList<TextMatch>? found = null;

            long allocated = Allocations.Measure(() => found = page.Find(query));

            Assert.Empty(found!);
            Assert.Equal(0, allocated);
        }
    }

    [Fact]
    public void A_literal_search_of_an_extracted_page_allocates_only_the_result_list()
    {
        TextPage page = ExtractTenThousandGlyphs(out PdfDocument document);
        using (document)
        {
            var query = new TextQuery("broadside");
            IReadOnlyList<TextMatch>? found = null;

            long allocated = Allocations.Measure(() => found = page.Find(query));

            // Per match: its text ("Broadside", 9 characters) and its quad array (one quad of 64 bytes), plus the list growing by
            // doubling. Nothing is allocated per character of the page.
            int count = found!.Count;
            Assert.True(count >= 200, $"Only {count} matches.");
            long perMatch = (24 + (2 * 9) + 8) + (24 + 64);
            long list = 32 + (2 * count * 80) + 1024;
            Assert.True(allocated <= (count * perMatch) + list, $"{allocated} bytes for {count} matches.");
        }
    }

    private static TextPage ExtractTenThousandGlyphs(out PdfDocument document)
    {
        string lines = System.Text.Encoding.ASCII.GetString(ContentSamples.TextHeavy(10_000));
        document = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, lines, ContentPdf.Helvetica));
        TextPage page = document.Pages[0].ExtractText();
        Assert.True(page.Glyphs.Count >= 10_000, $"Only {page.Glyphs.Count} glyphs.");
        return page;
    }
}
