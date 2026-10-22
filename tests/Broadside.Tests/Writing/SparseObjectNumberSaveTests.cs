using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving a file whose object numbers are huge or sparse (issue #371): what the cross-reference information costs is bounded by the
/// objects written, never by the highest number. A full save lists one subsection from 0 (ISO 32000-2 §7.5.4) and renumbers a
/// numbering it cannot list that way; an incremental update lists only what it writes, in subsections (§7.5.4, §7.5.6) or
/// <c>Index</c> pairs (§7.5.8.2). A number past what a save may write is a recorded <c>ObjectNumberOutOfRange</c> diagnostic.
/// </summary>
public sealed class SparseObjectNumberSaveTests
{
    /// <summary>The highest object number a save keeps (ISO 32000-1 Annex C, Table C.1).</summary>
    private const int Limit = 8_388_607;

    private const string OutOfRange = "ObjectNumberOutOfRange";

    public static TheoryData<PdfCrossReferenceLayout> Layouts => new()
    {
        PdfCrossReferenceLayout.Table,
        PdfCrossReferenceLayout.Stream,
        PdfCrossReferenceLayout.StreamWithObjectStreams,
    };

    /// <summary>The minimized input of the save fuzz target's finding (issue #371), which the smoke run replays first.</summary>
    internal static byte[] FuzzSeed() =>
        File.ReadAllBytes(Path.Combine(Corpus.Directory, "..", "Broadside.Fuzz", "Seeds", "save", "huge-unreachable-object-number.pdf"));

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_damaged_file_whose_scan_found_a_huge_unreachable_object_saves_compactly(PdfCrossReferenceLayout layout)
    {
        // The scan of the damaged file reads "2000000015 0 obj"; nothing reaches it, so garbage collection frees its number. Listing
        // every number up to a freed one took gigabytes and overflowed the size of the cross-reference stream's rows.
        byte[] file = FuzzSeed();
        using PdfDocument document = PdfDocument.Open(file);

        byte[] saved = Save(document, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithRemoveUnreachableObjects(true));

        Assert.True(saved.Length < 4096, $"{saved.Length} bytes.");
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, item => item.Code == OutOfRange);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Single(reopened.Pages);
        Assert.True(((CosInteger)reopened.Trailer[new CosName("Size")]).Value < 10);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void A_dangling_reference_to_a_huge_number_keeps_the_numbering_and_the_streams_below_it(PdfCrossReferenceLayout layout)
    {
        // The streams a save adds skip only the numbers references name, from just above the highest number written (§7.3.10,
        // issue #295): a reference to 2,000,000,000 neither numbers them above it nor makes the save renumber.
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, catalogEntries: "/Dangling 2000000000 0 R"));
        Assert.Empty(document.Diagnostics);

        byte[] saved = Save(document, new PdfSaveOptions().WithCrossReferenceLayout(layout));

        Assert.True(saved.Length < 4096, $"{saved.Length} bytes.");
        Assert.Empty(document.Diagnostics);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Single(reopened.Pages);
        Assert.True(((CosInteger)reopened.Trailer[new CosName("Size")]).Value < 10);
        Assert.IsType<CosNull>(reopened.Resolve(reopened.Catalog.TryGetValue(new CosName("Dangling"), out CosObject? value) ? value : null));
    }

    [Fact]
    public void In_strict_mode_a_save_that_would_renumber_a_number_above_the_limit_throws_and_writes_nothing()
    {
        int number = Limit + 1;
        byte[] file = WithUpdate(number, "<< /Kept true >>", $"/Extra {number} 0 R");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false)));

        Assert.Equal(OutOfRange, exception.Diagnostic.Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void In_strict_mode_a_save_asked_to_renumber_keeps_going()
    {
        int number = Limit + 1;
        using PdfDocument document = PdfDocument.Open(WithUpdate(number, "<< /Kept true >>", $"/Extra {number} 0 R"), new PdfOptions().UseStrict());

        byte[] saved = Save(document, new PdfSaveOptions().WithObjectNumbering(PdfObjectNumbering.Compact));

        Assert.DoesNotContain(document.Diagnostics, item => item.Code == OutOfRange);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(CosObject.DeepEquals(new CosDictionary { [new CosName("Kept")] = CosBoolean.True }, reopened.Resolve(reopened.Catalog[new CosName("Extra")])));
    }

    [Fact]
    public void An_update_of_a_huge_number_lists_it_in_a_subsection_of_its_own()
    {
        // §7.5.4, §7.5.6: an update cannot renumber, so the classic table lists object 0, then the changed object and the added one
        // (numbered just above it) in one subsection: never every number below Size.
        const int number = 2_000_000_015;
        byte[] file = WithUpdate(number, "<< /Value 1 >>", $"/Extra {number} 0 R");
        using PdfDocument document = PdfDocument.Open(file);
        ((CosDictionary)document.Resolve(new CosReference(number, 0)))[new CosName("Value")] = new CosInteger(2);
        CosReference added = document.AddObject(new CosInteger(7));

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        string update = Encoding.Latin1.GetString(saved, file.Length, saved.Length - file.Length);
        Assert.True(update.Length < 1024, update);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"\n{number} 2\n"), update, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"/Size {added.ObjectNumber + 1}"), update, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(2L, ((CosInteger)((CosDictionary)reopened.Resolve(new CosReference(number, 0)))[new CosName("Value")]).Value);
        Assert.Equal(7L, ((CosInteger)reopened.Resolve(added)).Value);
    }

    [Fact]
    public void An_update_of_a_huge_number_in_a_cross_reference_stream_lists_it_in_index_pairs()
    {
        // §7.5.8.2, Table 17: the update's cross-reference stream lists object 0, the changed object and itself (numbered above the
        // old stream) as Index pairs.
        const int number = 2_000_000_015;
        byte[] file = WithCrossReferenceStream(number);
        using PdfDocument document = PdfDocument.Open(file);
        Assert.Empty(document.Diagnostics);
        ((CosDictionary)document.Resolve(new CosReference(number, 0)))[new CosName("Value")] = new CosInteger(2);

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(saved.Length - file.Length < 1024, $"The update is {saved.Length - file.Length} bytes.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(2L, ((CosInteger)((CosDictionary)reopened.Resolve(new CosReference(number, 0)))[new CosName("Value")]).Value);
        CosStream section = Assert.IsType<CosStream>(reopened.Resolve(new CosReference((int)((CosInteger)reopened.Trailer[new CosName("Size")]).Value - 1, 0)));
        CosArray index = (CosArray)section.Dictionary[new CosName("Index")];
        Assert.Equal([0L, 1L, number, 1L, number + 2L, 1L], index.Select(item => ((CosInteger)item).Value));
    }

    [Theory]
    [InlineData(PdfReadingMode.Lenient)]
    [InlineData(PdfReadingMode.Strict)]
    public void An_update_of_a_file_that_holds_the_largest_number_is_refused_before_anything_is_written(PdfReadingMode mode)
    {
        // Annex C, Table C.1: integers such as object numbers are expected to fit in 32 bits. An update cannot renumber, and its
        // Size, one more than the highest number, would not fit.
        byte[] file = WithUpdate(int.MaxValue, "<< /Value 1 >>", $"/Extra {int.MaxValue} 0 R");
        using PdfDocument document = PdfDocument.Open(file, mode == PdfReadingMode.Strict ? new PdfOptions().UseStrict() : new PdfOptions());
        document.Catalog[new CosName("Lang")] = new CosString("en"u8);
        using var output = new MemoryStream();

        Exception? exception = Record.Exception(() => document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true)));

        Assert.IsType(mode == PdfReadingMode.Strict ? typeof(DiagnosticException) : typeof(NotSupportedException), exception);
        Assert.Contains(document.Diagnostics, item => item.Code == OutOfRange && item.Severity == DiagnosticSeverity.Warning);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_full_save_of_a_file_that_holds_the_largest_number_renumbers_it(PdfCrossReferenceLayout layout)
    {
        byte[] file = WithUpdate(int.MaxValue, "<< /Value 1 >>", $"/Extra {int.MaxValue} 0 R");
        using PdfDocument document = PdfDocument.Open(file);
        using var output = new MemoryStream();

        // Default options: the save dates the document, which needs an information dictionary although no number is left above.
        document.Save(output, new PdfSaveOptions().WithCrossReferenceLayout(layout));

        byte[] saved = output.ToArray();
        Assert.True(saved.Length < 4096, $"{saved.Length} bytes.");
        Assert.Contains(document.Diagnostics, item => item.Code == OutOfRange && item.Severity == DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.True(CosObject.DeepEquals(new CosDictionary { [new CosName("Value")] = new CosInteger(1) }, reopened.Resolve(reopened.Catalog[new CosName("Extra")])));
    }

    [Fact]
    public void New_encryption_of_a_file_that_holds_the_largest_number_renumbers_rather_than_running_out_of_numbers()
    {
        // The new encryption dictionary takes the number the next added object would (§7.6), and none is left above 2147483647.
        byte[] file = WithUpdate(int.MaxValue, "<< /Value 1 >>", $"/Extra {int.MaxValue} 0 R");
        using PdfDocument document = PdfDocument.Open(file);

        byte[] saved = Save(document, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions(null, "owner")));

        Assert.Contains(document.Diagnostics, item => item.Code == OutOfRange && item.Severity == DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Diagnostics);
        Assert.NotNull(reopened.Security);
        Assert.True(CosObject.DeepEquals(new CosDictionary { [new CosName("Value")] = new CosInteger(1) }, reopened.Resolve(reopened.Catalog[new CosName("Extra")])));
    }

    /// <summary>A one-page file and an update adding object <paramref name="number"/>, which the catalog names through <paramref name="catalogEntries"/>.</summary>
    private static byte[] WithUpdate(int number, string body, string catalogEntries) =>
        Document.TestPdf.AppendUpdate(
            Document.TestPdf.OnePage(string.Empty, catalogEntries),
            string.Create(CultureInfo.InvariantCulture, $"/Size {(long)number + 1} /Root 1 0 R"),
            (number, 0, body));

    /// <summary>
    /// A one-page file whose catalog names object <paramref name="number"/>, listed with objects 1 to 3 and the cross-reference stream
    /// itself (numbered one above it) by an unfiltered cross-reference stream with <c>Index [0 4 number 2]</c> (§7.5.8).
    /// </summary>
    private static byte[] WithCrossReferenceStream(int number)
    {
        string[] objects =
        [
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /Catalog /Pages 2 0 R /Extra {number} 0 R >>"),
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        ];
        var file = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        foreach ((string body, int objectNumber) in objects.Select((body, index) => (body, index + 1)).Append(("<< /Value 1 >>", number)))
        {
            offsets.Add(file.Length);
            file.Append(CultureInfo.InvariantCulture, $"{objectNumber} 0 obj\n{body}\nendobj\n");
        }

        int xref = file.Length;
        offsets.Add(xref);
        var rows = new List<byte> { 0, 0, 0, 0xFF };
        foreach (int offset in offsets)
        {
            rows.AddRange([1, (byte)(offset >> 8), (byte)offset, 0]);
        }

        file.Append(CultureInfo.InvariantCulture, $"{(long)number + 1} 0 obj\n<< /Type /XRef /Size {(long)number + 2} /Index [0 4 {number} 2] /W [1 2 1] /Root 1 0 R /Length {rows.Count} >>\nstream\n");
        byte[] head = Encoding.Latin1.GetBytes(file.ToString());
        byte[] tail = Encoding.Latin1.GetBytes(FormattableString.Invariant($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));
        return [.. head, .. rows, .. tail];
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options.WithUpdateModificationDate(false));
        return output.ToArray();
    }
}

/// <summary>Saving a damaged file of a few hundred bytes with a huge object number allocates kilobytes, not gigabytes (issue #371).</summary>
[Collection(Document.HeavyTestCollection.Name)]
public sealed class SparseObjectNumberAllocationTests
{
    [Theory]
    [MemberData(nameof(SparseObjectNumberSaveTests.Layouts), MemberType = typeof(SparseObjectNumberSaveTests))]
    public void Saving_the_fuzz_finding_allocates_less_than_a_few_megabytes(PdfCrossReferenceLayout layout)
    {
        byte[] file = SparseObjectNumberSaveTests.FuzzSeed();
        PdfSaveOptions options = new PdfSaveOptions().WithCrossReferenceLayout(layout).WithRemoveUnreachableObjects(true).WithUpdateModificationDate(false);
        using var output = new MemoryStream();
        void OpenAndSave()
        {
            output.SetLength(0);
            using PdfDocument document = PdfDocument.Open(file);
            document.Save(output, options);
        }

        long allocated = Allocations.Measure(OpenAndSave, warmUpCalls: 3);

        Assert.True(file.Length < 700);
        Assert.InRange(allocated, 1, 4L << 20);
    }
}
