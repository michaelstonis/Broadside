using System.Text;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The open-and-walk the real-world corpus gate, the document fuzz target and the open-and-walk benchmark share
/// (<see cref="DocumentWalker"/>) stays proportional to the file, not to the <c>Size</c> a damaged trailer claims (issue #280).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DocumentWalkerTests
{
    /// <summary>
    /// pdf.js bug 1980958 in miniature: three objects, no cross-reference table, no trailer, and one object numbered 2147483647, so
    /// the rebuilt trailer's <c>Size</c> is 2^31 and the walker resolves every number up to <see cref="DocumentWalker.MaxObjectNumber"/>.
    /// </summary>
    private static readonly byte[] HugeObjectNumber = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj <</Type /Catalog /Pages 2 0 R>>\nendobj\n2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>>\nendobj\n"
        + "3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 10 10]>>\nendobj\n\n2147483647 0 obj <</Root 1 0 R>>\nendobj\n");

    /// <summary>
    /// What one claimed number may cost: the <see cref="Broadside.Objects.CosReference"/> the public <c>Resolve</c> needs (24 bytes)
    /// and change. Remembering every free number in the walk's visited set cost 76 bytes each (641 MB for this file), and the large
    /// object heap growth that took is what stretched the walk past the corpus gate's time limit on a loaded machine.
    /// </summary>
    private const long BytesPerClaimedNumber = 32;

    [Fact]
    public void Walking_the_numbers_a_damaged_trailer_claims_keeps_nothing_per_free_number()
    {
        using PdfDocument document = PdfDocument.Open(HugeObjectNumber);
        DocumentWalkResult result = default!;
        Action walk = () => result = DocumentWalker.Walk(document);

        long allocated = Allocations.Measure(walk, warmUpCalls: 0);

        Assert.Equal(new DocumentWalkResult(Pages: 1, Objects: 3, Streams: 0, DecodedBytes: 0), result);
        Assert.InRange(allocated, 0, DocumentWalker.MaxObjectNumber * BytesPerClaimedNumber);
    }
}
