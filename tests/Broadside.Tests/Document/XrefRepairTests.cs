using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The repairs of the file structure that used to be silent: an entry beyond the trailer's Size, a comment or a malformed line inside
/// a cross-reference table, an XRefStm offset that misses its stream, and a last startxref that is damaged. Each repair records a
/// diagnostic (ADR 0005); <see cref="BrokenCorpusTests"/> asserts the exact diagnostics and the strict-mode exception over the same
/// corpus files. ISO 32000-2 §7.5.4, §7.5.5, §7.5.6, §7.5.8.
/// </summary>
public sealed class XrefRepairTests
{
    [Fact]
    public void An_entry_numbered_Size_or_more_is_ignored_and_the_object_reads_as_null()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("xref-entry-beyond-size.pdf"));

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Equal(["XrefEntryBeyondSize"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(DiagnosticSeverity.Warning, document.Diagnostics[0].Severity);
    }

    [Fact]
    public void A_comment_inside_the_table_is_skipped_and_every_entry_is_read()
    {
        using PdfDocument intact = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("xref-comment.pdf"));

        foreach (int number in new[] { 1, 2, 3 })
        {
            Assert.Equal(intact.Resolve(new CosReference(number, 0)), document.Resolve(new CosReference(number, 0)));
        }

        Assert.Equal(["XrefCommentIgnored"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_line_that_is_not_an_entry_is_skipped_and_the_entries_after_it_are_read()
    {
        using PdfDocument intact = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("xref-line-malformed.pdf"));

        foreach (int number in new[] { 1, 2, 3 })
        {
            Assert.Equal(intact.Resolve(new CosReference(number, 0)), document.Resolve(new CosReference(number, 0)));
        }

        Assert.Equal(["XrefLineMalformed"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void An_XRefStm_offset_into_the_stream_reads_the_stream_nearest_to_it()
    {
        using PdfDocument intact = PdfDocument.Open(Corpus.Path("hybrid-xref.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("xrefstm-offset-wrong.pdf"));

        // Object 4 (/Info) lives in an object stream only the XRefStm stream lists.
        CosObject expected = intact.Resolve(new CosReference(4, 0));
        Assert.IsType<CosDictionary>(expected);
        Assert.Equal(expected, document.Resolve(new CosReference(4, 0)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("TrailerXRefStmInvalid", DiagnosticSeverity.Warning), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_damaged_last_startxref_reads_the_newest_revision_not_the_previous_one()
    {
        using PdfDocument intact = PdfDocument.Open(Corpus.Path("incremental-update.pdf"));
        using PdfDocument original = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("startxref-last-damaged.pdf"));

        Assert.Equal(DocumentProjection.Of(intact).Pages, DocumentProjection.Of(document).Pages);
        Assert.Equal(intact.Resolve(new CosReference(3, 0)), document.Resolve(new CosReference(3, 0)));
        Assert.NotEqual(DocumentProjection.Of(original).Pages, DocumentProjection.Of(document).Pages);
        Assert.Equal("StartxrefLastDamaged", document.Diagnostics[0].Code);
    }

    [Theory]
    [InlineData("xref-entry-beyond-size.pdf")]
    [InlineData("xref-comment.pdf")]
    [InlineData("xref-line-malformed.pdf")]
    [InlineData("xrefstm-offset-wrong.pdf")]
    [InlineData("startxref-last-damaged.pdf")]
    public void Strict_mode_throws_for_each_repair(string fileName)
    {
        _ = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStrict()));
    }
}
