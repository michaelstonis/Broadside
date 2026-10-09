using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Incremental updates: revisions, their byte ranges and the newest copy of each object. ISO 32000-2 §7.5.6, §7.5.4, H.7.</summary>
public class IncrementalUpdateTests
{
    [Fact]
    public void An_updated_file_has_one_revision_per_update_and_the_first_is_the_original_file()
    {
        // incremental-update.pdf is empty-page.pdf with one update appended (tests/Corpus/README.md).
        long original = Corpus.Bytes("empty-page.pdf").LongLength;
        long updated = Corpus.Bytes("incremental-update.pdf").LongLength;

        using PdfDocument document = PdfDocument.Open(Corpus.Path("incremental-update.pdf"));

        Assert.Collection(
            document.Revisions,
            first => Assert.Equal((0, original), (first.Index, first.Length)),
            second => Assert.Equal((1, updated), (second.Index, second.Length)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_hybrid_file_has_its_main_section_and_its_update_section_as_two_revisions()
    {
        // §7.5.8.4: the update section's XRefStm stream is stored after the main section's %%EOF, so it belongs to the update.
        byte[] file = Corpus.Bytes("hybrid-xref.pdf");
        int mainEnd = file.AsSpan().IndexOf("%%EOF\n"u8) + "%%EOF\n"u8.Length;

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal([mainEnd, file.Length], document.Revisions.Select(revision => (int)revision.Length));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Deleted_objects_read_as_null_and_a_reused_number_resolves_only_with_its_new_generation()
    {
        // The stages of ISO 32000-2 H.7: change an annotation, delete two, reuse one deleted number with generation 1.
        byte[][] saves = UpdatingExample();

        using PdfDocument document = PdfDocument.Open(saves[^1]);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(5, 0)));
        Assert.Equal("New Text #1", Contents(document, new CosReference(5, 1)));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(6, 0)));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(6, 1)));
        Assert.Equal("Third", Contents(document, new CosReference(7, 0)));
        CosArray annotations = Assert.IsType<CosArray>(document.Resolve(new CosReference(4, 0)));
        Assert.Equal([new CosReference(5, 1), new CosReference(7, 0)], annotations.Cast<CosReference>());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Each_revision_is_the_file_as_it_was_after_that_save()
    {
        byte[][] saves = UpdatingExample();

        using PdfDocument document = PdfDocument.Open(saves[^1]);

        Assert.Equal(saves.Select(save => save.LongLength), document.Revisions.Select(revision => revision.Length));
        using PdfDocument afterFirstUpdate = PdfDocument.Open(saves[^1].AsMemory(0, (int)document.Revisions[1].Length));
        Assert.Equal("Changed", Contents(afterFirstUpdate, new CosReference(5, 0)));
        Assert.Equal("Second", Contents(afterFirstUpdate, new CosReference(6, 0)));
        Assert.Empty(afterFirstUpdate.Diagnostics);
    }

    [Fact]
    public void Each_revision_has_its_own_trailer()
    {
        byte[][] saves = UpdatingExample();

        using PdfDocument document = PdfDocument.Open(saves[^1]);

        Assert.False(document.Revisions[0].Trailer.ContainsKey(new CosName("Prev")));
        Assert.All(document.Revisions.Skip(1), revision => Assert.IsType<CosInteger>(revision.Trailer[new CosName("Prev")]));
        Assert.Equal(new CosInteger(8), document.Revisions[^1].Trailer[new CosName("Size")]);
        Assert.Equal(new CosInteger(7), document.Revisions[0].Trailer[new CosName("Size")]);
    }

    [Fact]
    public void An_update_trailer_that_drops_root_takes_it_from_the_older_trailer_with_a_diagnostic()
    {
        byte[] file = TestPdf.AppendUpdate(Corpus.Bytes("empty-page.pdf"), "/Size 4", (3, 0, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "TrailerEntryFromOlderRevision");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Root", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_update_trailer_that_drops_root_throws_in_strict_mode()
    {
        byte[] file = TestPdf.AppendUpdate(Corpus.Bytes("empty-page.pdf"), "/Size 4", (3, 0, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"));

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(file, new PdfOptions().UseStrict()));

        Assert.Equal("TrailerEntryFromOlderRevision", error.Diagnostic.Code);
    }

    [Fact]
    public void An_earlier_revision_without_its_end_of_file_marker_ends_after_its_startxref_offset_with_a_diagnostic()
    {
        string original = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf"));
        string truncated = original[..original.LastIndexOf("%%EOF", StringComparison.Ordinal)];
        byte[] file = TestPdf.AppendUpdate(Encoding.Latin1.GetBytes(truncated), "/Size 4 /Root 1 0 R", (3, 0, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal([truncated.TrimEnd().Length, file.Length], document.Revisions.Select(revision => (int)revision.Length));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("EndOfFileMarkerMissing", diagnostic.Code);
        Assert.Equal(truncated.TrimEnd().Length, diagnostic.Offset);
    }

    private static string Contents(PdfDocument document, CosReference annotation)
    {
        CosDictionary dictionary = Assert.IsType<CosDictionary>(document.Resolve(annotation));
        return Assert.IsType<CosString>(dictionary[new CosName("Contents")]).DecodeText();
    }

    /// <summary>The original file and the file after each of three updates, after ISO 32000-2 H.7.</summary>
    private static byte[][] UpdatingExample()
    {
        byte[] original = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Annots 4 0 R >>",
            "[5 0 R 6 0 R]",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (First) >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (Second) >>");
        byte[] changed = TestPdf.AppendUpdate(
            original,
            "/Size 7 /Root 1 0 R",
            (5, 0, "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (Changed) >>"));
        byte[] deleted = TestPdf.AppendUpdate(changed, "/Size 7 /Root 1 0 R", (4, 0, "[]"), (5, 1, null), (6, 1, null));
        byte[] reused = TestPdf.AppendUpdate(
            deleted,
            "/Size 8 /Root 1 0 R",
            (4, 0, "[5 1 R 7 0 R]"),
            (5, 1, "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (New Text #1) >>"),
            (7, 0, "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (Third) >>"));
        return [original, changed, deleted, reused];
    }
}
