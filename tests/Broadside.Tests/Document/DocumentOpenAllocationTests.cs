using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Opening a document allocates for what the open reads, not for every cache the document could fill later (issue #337). A
/// document has a few dozen lazily filled caches (fonts, CMaps, color spaces, functions, metadata, pages); each one's table cost
/// about 470 bytes when it was created with the document, so opening a one-page file and reading its page sizes allocated about
/// 18 KB, a third of it empty tables. In the heavy collection, like the other allocation tests.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DocumentOpenAllocationTests
{
    [Fact]
    public void Opening_a_document_creates_no_table_for_a_cache_it_does_not_fill()
    {
        byte[] bytes = ContentSamples.OnePage("0 0 m 10 10 l S"u8.ToArray());
        Action open = () =>
        {
            using PdfDocument document = PdfDocument.Open(bytes);
            foreach (PdfPage page in document.Pages)
            {
                _ = page.CropBox;
            }
        };

        long allocated = Allocations.Measure(open);

        Assert.InRange(allocated, 1, 14 * 1024);
    }
}
