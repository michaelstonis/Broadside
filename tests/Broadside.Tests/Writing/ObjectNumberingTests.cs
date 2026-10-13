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

    public static TheoryData<bool> Layouts => new() { false, true };

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

    private static byte[] Save(PdfDocument document, bool incremental)
    {
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(incremental));
        return output.ToArray();
    }
}
