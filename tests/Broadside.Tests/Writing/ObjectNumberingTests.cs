using System.Globalization;
using System.Text;
using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>Numbers of new indirect objects (ISO 32000-2 §7.3.10, §7.5.4): never one a reference in the file already names.</summary>
public sealed class ObjectNumberingTests
{
    // A repaired file (no cross-reference table) whose page tree names a page 4 0 R that does not exist, so it reads as 1 page.
    private static readonly byte[] RepairedWithDanglingKid = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n"
        + "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
        + "2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>\nendobj\n"
        + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n"
        + "trailer\n<< /Root 1 0 R >>\n%%EOF\n");

    // A file with an intact cross-reference table (Size 4) whose page tree names 6 0 R, above Size.
    private static readonly byte[] DanglingAboveSize = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n"
        + "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
        + "2 0 obj\n<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>\nendobj\n"
        + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n"
        + "xref\n0 4\n0000000000 65535 f\r\n0000000009 00000 n\r\n0000000058 00000 n\r\n0000000121 00000 n\r\n"
        + "trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n192\n%%EOF\n");

    // A one-page file whose catalog names 4 0 R, just above the highest object number 3; Size 4, so 4 0 R is null (§7.3.10).
    private static readonly string[] DanglingJustAboveHighest =
    [
        "<< /Type /Catalog /Pages 2 0 R /Dangling 4 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
    ];

    public static TheoryData<bool> Layouts => new() { false, true };

    public static TheoryData<PdfCrossReferenceLayout, PdfObjectNumbering> StreamLayouts => new()
    {
        { PdfCrossReferenceLayout.StreamWithObjectStreams, PdfObjectNumbering.Automatic },
        { PdfCrossReferenceLayout.StreamWithObjectStreams, PdfObjectNumbering.Compact },
        { PdfCrossReferenceLayout.Stream, PdfObjectNumbering.Automatic },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void An_added_object_does_not_take_the_number_of_a_dangling_reference(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(RepairedWithDanglingKid);
        Assert.Single(document.Pages);

        CosReference added = document.AddObject(new CosDictionary { [new CosName("Type")] = new CosName("Page") });

        Assert.NotEqual(4, added.ObjectNumber);
        using PdfDocument reopened = PdfDocument.Open(Save(document, incremental));
        Assert.Single(reopened.Pages);
    }

    [Fact]
    public void A_dangling_reference_above_Size_is_skipped_and_later_objects_number_on_from_it()
    {
        using PdfDocument document = PdfDocument.Open(DanglingAboveSize);
        Assert.Empty(document.Diagnostics);
        Assert.Single(document.Pages);

        int[] numbers = [.. Enumerable.Range(0, 4).Select(_ => document.AddObject(new CosDictionary { [new CosName("Type")] = new CosName("Page") }).ObjectNumber)];

        Assert.Equal([4, 5, 7, 8], numbers);
        using PdfDocument reopened = PdfDocument.Open(Save(document, incremental: false));
        Assert.Single(reopened.Pages);
    }

    [Fact]
    public void A_new_encryption_dictionary_takes_the_number_the_next_added_object_would()
    {
        // One allocator: the encryption dictionary a save adds is numbered as AddObject would number it, above the dangling 6 0 R.
        using PdfDocument document = PdfDocument.Open(DanglingAboveSize);
        document.AddObject(new CosDictionary());
        document.AddObject(new CosDictionary());

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false).WithEncryption(new PdfEncryptionOptions(null, "owner")));

        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        Assert.Single(reopened.Pages);
        Assert.Equal(7, ((CosReference)reopened.Trailer[new CosName("Encrypt")]).ObjectNumber);
    }

    [Theory]
    [MemberData(nameof(StreamLayouts))]
    public void Saved_object_streams_and_the_cross_reference_stream_do_not_take_the_number_of_a_dangling_reference(
        PdfCrossReferenceLayout layout,
        PdfObjectNumbering numbering)
    {
        // ISO 32000-2 §7.3.10: 4 0 R names no object, so it is null; the streams a save adds (§7.5.7, §7.5.8) must not come to be it.
        using PdfDocument document = PdfDocument.Open(WithCrossReferenceTable(DanglingJustAboveHighest));
        Assert.Empty(document.Diagnostics);

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false).WithCrossReferenceLayout(layout).WithObjectNumbering(numbering));

        AssertDanglingIsNull(output.ToArray());
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_file_with_a_cross_reference_stream_saves_without_its_streams_taking_a_dangling_number(bool incremental)
    {
        // Objects 1-3 and the cross-reference stream 4 (Size 5); the catalog names 5 0 R, just above the highest number. An update
        // takes the file's form, so it writes a cross-reference stream too (§7.5.8.4).
        using PdfDocument document = PdfDocument.Open(WithCrossReferenceStream(
        [
            "<< /Type /Catalog /Pages 2 0 R /Dangling 5 0 R >>",
            DanglingJustAboveHighest[1],
            DanglingJustAboveHighest[2],
        ]));
        Assert.Empty(document.Diagnostics);
        document.Catalog[new CosName("Lang")] = new CosString("en"u8);

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions()
            .WithUpdateModificationDate(false)
            .WithIncrementalUpdate(incremental)
            .WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams));

        AssertDanglingIsNull(output.ToArray());
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table, false)]
    [InlineData(PdfCrossReferenceLayout.Stream, false)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams, false)]
    [InlineData(PdfCrossReferenceLayout.Table, true)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams, true)]
    public void A_linearized_save_numbers_its_own_streams_and_writes_a_dangling_reference_as_null(PdfCrossReferenceLayout layout, bool streamSource)
    {
        // Annex F: a linearized file renumbers every object by part and numbers its object streams, cross-reference streams,
        // linearization dictionary and hint stream itself, in that renumbered space; the dangling reference is written as null
        // (§7.3.10), so none of them can come to be it, without the document's allocator.
        using PdfDocument document = PdfDocument.Open(streamSource
            ? WithCrossReferenceStream(["<< /Type /Catalog /Pages 2 0 R /Dangling 5 0 R >>", DanglingJustAboveHighest[1], DanglingJustAboveHighest[2]])
            : WithCrossReferenceTable(DanglingJustAboveHighest));
        Assert.Empty(document.Diagnostics);

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false).WithCrossReferenceLayout(layout).WithLinearize(true));
        byte[] saved = output.ToArray();

        AssertDanglingIsNull(saved);
        using (PdfDocument reopened = PdfDocument.Open(saved))
        {
            Assert.True(reopened.IsLinearized);
        }

        (int exitCode, string report) = ExternalTool.Run("qpdf", saved, "--check-linearization");
        Assert.True(exitCode == 0 && report.Contains("no linearization errors", StringComparison.Ordinal), report);
    }

    /// <summary>Reopens a saved file: one page, the catalog's <c>Dangling</c> entry still null or gone, and qpdf finds nothing wrong.</summary>
    private static void AssertDanglingIsNull(byte[] file)
    {
        using (PdfDocument reopened = PdfDocument.Open(file))
        {
            Assert.Empty(reopened.Diagnostics);
            Assert.Single(reopened.Pages);
            // A renumbered file writes the reference as null, and a dictionary entry whose value is null is no entry (§7.3.7).
            Assert.IsType<CosNull>(reopened.Resolve(reopened.Catalog.TryGetValue(new CosName("Dangling"), out CosObject? value) ? value : null));
        }

        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>A file of the given objects, numbered from 1, with a classic cross-reference table (§7.5.4) whose Size is one more.</summary>
    private static byte[] WithCrossReferenceTable(string[] objects)
    {
        (StringBuilder file, List<int> offsets) = Body(objects);
        int xref = file.Length;
        file.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f\r\n");
        foreach (int offset in offsets)
        {
            file.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n\r\n");
        }

        file.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(file.ToString());
    }

    /// <summary>A file of the given objects, numbered from 1, then an unfiltered cross-reference stream (§7.5.8) that lists them and itself.</summary>
    private static byte[] WithCrossReferenceStream(string[] objects)
    {
        (StringBuilder file, List<int> offsets) = Body(objects);
        int number = objects.Length + 1;
        int xref = file.Length;
        offsets.Add(xref);
        var rows = new List<byte> { 0, 0, 0, 0xFF };
        foreach (int offset in offsets)
        {
            rows.AddRange([1, (byte)(offset >> 8), (byte)offset, 0]);
        }

        file.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Type /XRef /Size {number + 1} /W [1 2 1] /Root 1 0 R /Length {rows.Count} >>\nstream\n");
        byte[] head = Encoding.Latin1.GetBytes(file.ToString());
        byte[] tail = Encoding.Latin1.GetBytes(FormattableString.Invariant($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n"));
        return [.. head, .. rows, .. tail];
    }

    private static (StringBuilder File, List<int> Offsets) Body(string[] objects)
    {
        var file = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(file.Length);
            file.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        return (file, offsets);
    }

    private static byte[] Save(PdfDocument document, bool incremental)
    {
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(incremental));
        return output.ToArray();
    }
}
