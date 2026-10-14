using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// A file whose trailer has an <c>Encrypt</c> entry that does not reach an encryption dictionary (issue #283, found by the
/// <c>flatten</c> fuzz target: a mutation that broke the encryption dictionary's object header). It reads as not encrypted with an
/// <c>EncryptDictionaryInvalid</c> error, and saves the way an unencrypted document does. ISO 32000-2 §7.6.1, §7.6.2; ADR 0005.
/// </summary>
public sealed class DamagedEncryptionSaveTests
{
    private const string Id = "/ID [<8b05a813faaa844eeb06329838ba6583> <8b05a813faaa844eeb06329838ba6583>]";

    private static readonly CosName Encrypt = new("Encrypt");

    /// <summary>The trailer's <c>Encrypt</c> entry, damaged three ways: a reference to no object, a reference to a non-dictionary, a direct non-dictionary.</summary>
    public static TheoryData<string> DamagedEntries => new()
    {
        "/Encrypt 9 0 R",
        "/Encrypt 4 0 R",
        "/Encrypt 6",
    };

    [Theory]
    [MemberData(nameof(DamagedEntries))]
    public void A_damaged_encrypt_entry_reads_as_not_encrypted_and_records_an_error(string entry)
    {
        using PdfDocument document = PdfDocument.Open(Damaged(entry));

        Assert.False(document.IsEncrypted);
        Assert.Null(document.Security);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "EncryptDictionaryInvalid");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Single(document.Pages);
    }

    [Theory]
    [MemberData(nameof(DamagedEntries))]
    public void A_damaged_encrypt_entry_is_refused_in_strict_mode(string entry)
    {
        var exception = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Damaged(entry), new PdfOptions().UseStrict()));

        Assert.Equal("EncryptDictionaryInvalid", exception.Diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(DamagedEntries))]
    public void A_document_read_as_not_encrypted_saves_as_a_complete_file_without_the_damaged_entry(string entry)
    {
        using PdfDocument document = PdfDocument.Open(Damaged(entry));

        byte[] saved = Save(document, new PdfSaveOptions());

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.False(reopened.IsEncrypted);
        Assert.False(reopened.Trailer.ContainsKey(Encrypt));
        Assert.Single(reopened.Pages);
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(DamagedEntries))]
    public void A_document_read_as_not_encrypted_saves_incrementally_and_reads_back_the_same_way(string entry)
    {
        byte[] original = Damaged(entry);
        using PdfDocument document = PdfDocument.Open(original);
        document.Catalog[new CosName("Lang")] = new CosString("en"u8);

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.False(reopened.IsEncrypted);
        Assert.Equal("en", ((CosString)reopened.Catalog[new CosName("Lang")]).DecodeText());
        Assert.Single(reopened.Pages);
        Assert.Contains(reopened.Diagnostics, d => d.Code == "EncryptDictionaryInvalid");
    }

    [Theory]
    [MemberData(nameof(DamagedEntries))]
    public void Removing_encryption_from_a_document_read_as_not_encrypted_drops_the_damaged_entry_in_either_layout(string entry)
    {
        byte[] original = Damaged(entry);
        using PdfDocument document = PdfDocument.Open(original);

        byte[] complete = Save(document, new PdfSaveOptions().WithRemoveEncryption(true));
        byte[] incremental = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true).WithRemoveEncryption(true));

        Assert.True(incremental.AsSpan(0, original.Length).SequenceEqual(original));
        foreach (byte[] saved in new[] { complete, incremental })
        {
            using PdfDocument reopened = PdfDocument.Open(saved);
            Assert.False(reopened.IsEncrypted);
            Assert.False(reopened.Trailer.ContainsKey(Encrypt));
            Assert.Single(reopened.Pages);
            Assert.DoesNotContain(reopened.Diagnostics, d => d.Code == "EncryptDictionaryInvalid");
        }
    }

    /// <summary>Each save that replaces or drops the encryption dictionary, with and without garbage collection and renumbering.</summary>
    public static TheoryData<int> ReplacingSaves => [.. Enumerable.Range(0, 8)];

    [Theory]
    [MemberData(nameof(ReplacingSaves))]
    public void An_encryption_dictionary_the_catalog_also_refers_to_stays_in_a_save_that_replaces_or_drops_it(int save)
    {
        // Found by the save fuzz target: one byte turned the catalog's Pages 2 0 R into 6 0 R, the trailer's Encrypt. The document
        // reads with the encryption dictionary as its only page, so a save that drops the old dictionary must keep that object.
        byte[] file = Corpus.Bytes("encrypted-owner-key-variant.pdf");
        int pages = file.AsSpan().IndexOf("/Pages 2 0 R"u8);
        file[pages + "/Pages ".Length] = (byte)'6';
        using PdfDocument document = PdfDocument.Open(file);
        Assert.True(document.IsEncrypted);
        Assert.Equal(new CosReference(6, 0), Assert.Single(document.Pages).Reference);

        byte[] saved = Save(document, ReplacingSave(save));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.Equal(save >= 4, reopened.IsEncrypted);
    }

    [Theory]
    [MemberData(nameof(ReplacingSaves))]
    public void A_damaged_encrypt_entry_to_an_object_the_document_uses_does_not_take_that_object_out_of_a_save(int save)
    {
        // The trailer's Encrypt entry reaches the integer the catalog's Answer entry holds: read as not encrypted, and every save drops
        // the entry but keeps the object.
        byte[] file = new TestPdf { TrailerEntries = $"/Encrypt 4 0 R {Id}" }.Build(
            "<< /Type /Catalog /Pages 2 0 R /Answer 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> >>",
            "42");
        using PdfDocument document = PdfDocument.Open(file);
        Assert.False(document.IsEncrypted);

        byte[] saved = Save(document, ReplacingSave(save));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.Equal(42L, ((CosInteger)reopened.Resolve(reopened.Catalog[new CosName("Answer")])).Value);
        Assert.Equal(save >= 4, reopened.IsEncrypted);
    }

    /// <summary>Saves 0-3 remove encryption and 4-7 replace it, each with and without garbage collection and compact numbering.</summary>
    private static PdfSaveOptions ReplacingSave(int save)
    {
        PdfSaveOptions options = new PdfSaveOptions()
            .WithRemoveUnreachableObjects(save % 2 == 1)
            .WithObjectNumbering(save / 2 % 2 == 1 ? PdfObjectNumbering.Compact : PdfObjectNumbering.Automatic);
        return save < 4 ? options.WithRemoveEncryption(true) : options.WithEncryption(new PdfEncryptionOptions(null, "owner"));
    }

    /// <summary>One page, object 4 an integer, and the damaged <c>Encrypt</c> entry in the trailer.</summary>
    private static byte[] Damaged(string entry) =>
        new TestPdf { TrailerEntries = $"{entry} {Id}" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << >> >>",
            "42");

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}
