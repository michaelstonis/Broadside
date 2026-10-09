using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Linearized files: detection, the parameter dictionary, the first-page objects and the hint tables. ISO 32000-2 Annex F. Expected
/// values come from <c>qpdf --show-linearization tests/Corpus/linearized.pdf</c> (qpdf 12.4.2), recorded in the corpus README.
/// </summary>
public class LinearizationTests
{
    [Fact]
    public void A_linearized_file_is_detected_and_exposes_its_parameter_dictionary()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("linearized.pdf"));

        Assert.True(document.IsLinearized);
        PdfLinearization linearization = Assert.IsType<PdfLinearization>(document.Linearization);
        Assert.Equal(new CosReference(5, 0), linearization.Reference);
        Assert.Same(document.Resolve(new CosReference(5, 0)), linearization.Dictionary);
        Assert.Equal(new CosInteger(1627), linearization.Dictionary[new CosName("L")]);
        Assert.Equal(1627, linearization.FileLength);
        Assert.Equal(2, linearization.PageCount);
        Assert.Equal(8, linearization.FirstPageObjectNumber);
        Assert.Equal(0, linearization.FirstPageNumber);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_first_page_objects_are_those_in_the_first_page_cross_reference_section()
    {
        // F.3.4: the linearization dictionary (5), the catalog (6), the primary hint stream (7) and the first page's objects (8-12).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("linearized.pdf"));

        IReadOnlyList<CosReference> objects = document.Linearization!.FirstPageObjects;

        Assert.Equal(Enumerable.Range(5, 8).Select(number => new CosReference(number, 0)), objects);
    }

    [Fact]
    public void A_linearized_file_is_one_revision()
    {
        byte[] file = Corpus.Bytes("linearized.pdf");

        using PdfDocument document = PdfDocument.Open(file);

        PdfRevision revision = Assert.Single(document.Revisions);
        Assert.Equal(file.LongLength, revision.Length);
        Assert.Equal(new CosReference(6, 0), revision.Trailer[new CosName("Root")]);
        Assert.Equal(2, document.Pages.Count);
    }

    [Fact]
    public void The_page_offset_hint_table_locates_each_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("linearized.pdf"));

        PdfLinearizationHints hints = Assert.IsType<PdfLinearizationHints>(document.Linearization!.Hints);

        Assert.Collection(
            hints.Pages,
            first =>
            {
                Assert.Equal((8, 5, 720L, 359L), (first.FirstObjectNumber, first.ObjectCount, first.Offset, first.Length));
                Assert.Empty(first.SharedObjects);
            },
            second =>
            {
                Assert.Equal((1, 2, 1079L, 184L), (second.FirstObjectNumber, second.ObjectCount, second.Offset, second.Length));
                Assert.Equal([2, 3, 4], second.SharedObjects);
            });
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_shared_object_hint_table_locates_each_group()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("linearized.pdf"));

        PdfLinearizationHints hints = document.Linearization!.Hints!;

        Assert.Equal(
            [(8, 1, 720L, 98L), (9, 1, 818L, 86L), (10, 1, 904L, 98L), (11, 1, 1002L, 32L), (12, 1, 1034L, 45L)],
            hints.SharedObjects.Select(group => (group.FirstObjectNumber, group.ObjectCount, group.Offset, group.Length)));
    }

    [Fact]
    public void Hint_offsets_count_from_byte_0_of_the_file_when_bytes_precede_the_header()
    {
        byte[] file = [.. "Junk\n"u8, .. Corpus.Bytes("linearized.pdf")];

        using PdfDocument document = PdfDocument.Open(file);

        Assert.True(document.IsLinearized);
        Assert.Equal([725L, 1084L], document.Linearization!.Hints!.Pages.Select(page => page.Offset));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_file_that_is_not_linearized_reports_false()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));

        Assert.False(document.IsLinearized);
        Assert.Null(document.Linearization);
    }

    [Fact]
    public void An_update_appended_to_a_linearized_file_makes_it_an_ordinary_two_revision_file()
    {
        // Table F.1, L: a length mismatch means the file shall be treated as an ordinary file; G.7: hints may be stale.
        byte[] original = Corpus.Bytes("linearized.pdf");
        string text = Encoding.Latin1.GetString(original);
        int id = text.IndexOf("/ID ", StringComparison.Ordinal);
        string idEntry = text[id..text.IndexOf(">>", id, StringComparison.Ordinal)];
        byte[] file = TestPdf.AppendUpdate(original, $"/Size 13 /Root 6 0 R {idEntry}", (11, 0, "[0 0 595 842]"));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.False(document.IsLinearized);
        Assert.NotNull(document.Linearization);
        Assert.Equal([original.LongLength, file.LongLength], document.Revisions.Select(revision => revision.Length));
        Assert.All(document.Pages, page => Assert.Equal(new PdfRectangle(0, 0, 595, 842), page.MediaBox));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_malformed_linearization_dictionary_is_ignored_with_a_diagnostic()
    {
        byte[] file = Replace(Corpus.Bytes("linearized.pdf"), "/N 2 ", "/N() ");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.False(document.IsLinearized);
        Assert.Null(document.Linearization);
        Assert.Equal(2, document.Pages.Count);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("LinearizationDictionaryInvalid", diagnostic.Code);
        Assert.Equal(new CosReference(5, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void A_malformed_hint_table_leaves_the_file_linearized_without_hints_and_with_a_diagnostic()
    {
        byte[] file = Replace(Corpus.Bytes("linearized.pdf"), "/S 44 ", "/S 99 ");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.True(document.IsLinearized);
        Assert.Null(document.Linearization!.Hints);
        Assert.Equal("LinearizationHintsInvalid", Assert.Single(document.Diagnostics).Code);
    }

    private static byte[] Replace(byte[] file, string oldValue, string newValue)
    {
        string text = Encoding.Latin1.GetString(file);
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(text.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
