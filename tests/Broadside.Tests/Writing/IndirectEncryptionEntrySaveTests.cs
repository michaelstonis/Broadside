using System.Text;
using System.Text.RegularExpressions;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// An encryption dictionary whose entries are indirect objects (issue #390, found by the <c>save</c> fuzz target: a mutation of
/// <c>encrypted-rc4-40-r3.pdf</c> whose <c>V</c> became <c>2 0 R</c>, the page tree root). The values of the entries of Table 20
/// shall not be encrypted and the dictionary's strings shall be direct objects (ISO 32000-2 §7.6.2); a reader resolves the entries
/// before it can decrypt anything. A save that keeps the document's encryption writes the dictionary direct, so neither string
/// encryption nor an encrypted object stream (§7.5.7) reaches what the entries hold.
/// </summary>
public sealed partial class IndirectEncryptionEntrySaveTests
{
    private const string Owner = "<61840a4e66456bd408feb9a45fc05fce6d8f57574f1350bf0362c1836240898c>";
    private const string User = "<445327c9b9d60d77f21ed152b53f7be900000000000000000000000000000000>";
    private const string Id = "/ID [<86c5c91d9639a345f3f9a33da432c3b1> <86c5c91d9639a345f3f9a33da432c3b1>]";

    private static readonly CosName Root = new("Root");
    private static readonly CosName Encrypt = new("Encrypt");

    /// <summary>Every cross-reference layout, with the numbers kept and renumbered.</summary>
    public static TheoryData<PdfCrossReferenceLayout, PdfObjectNumbering> LayoutsAndNumberings
    {
        get
        {
            var data = new TheoryData<PdfCrossReferenceLayout, PdfObjectNumbering>();
            foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
            {
                foreach (PdfObjectNumbering numbering in Enum.GetValues<PdfObjectNumbering>())
                {
                    data.Add(layout, numbering);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LayoutsAndNumberings))]
    public void A_kept_encryption_dictionary_whose_entries_are_indirect_is_written_direct_and_reopens(PdfCrossReferenceLayout layout, PdfObjectNumbering numbering)
    {
        // Filter, Length, O and V are indirect objects 7 to 10. Saved as they were, O's string would be encrypted with its object's
        // key and V's integer would go in an encrypted object stream; the file would open with neither password nor key.
        byte[] file = Rc4File($"<< /Filter 7 0 R /R 3 /Length 8 0 R /P -4 /O 9 0 R /U {User} /V 10 0 R >>", "/Standard", "40", Owner, "2");
        using PdfDocument source = PdfDocument.Open(file);
        Assert.Equal(2, source.Security!.Version);
        Assert.Empty(source.Diagnostics);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithObjectNumbering(numbering));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(source.Security.Version, reopened.Security!.Version);
        Assert.Equal(source.Security.Revision, reopened.Security.Revision);
        Assert.Equal(source.Security.KeyLength, reopened.Security.KeyLength);
        AssertSameDocument(source, reopened);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Code != "EncryptionDeprecated");
        AssertDirect(reopened.Security.EncryptionDictionary);
    }

    [Theory]
    [MemberData(nameof(LayoutsAndNumberings))]
    public void An_encryption_dictionary_whose_v_refers_to_the_page_tree_saves_a_file_that_reopens(PdfCrossReferenceLayout layout, PdfObjectNumbering numbering)
    {
        // The fuzz finding: V is the page tree root, which the handler reads as no V (a warning). Kept as a reference, the save put
        // the page tree root in an encrypted object stream with the catalog; the reader, resolving V before it can decrypt, read the
        // object stream as undecodable and found no catalog.
        byte[] file = Rc4File($"<< /Filter /Standard /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 2 0 R >>");
        using PdfDocument source = PdfDocument.Open(file);
        Assert.Equal(2, source.Security!.Version);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithObjectNumbering(numbering));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.Equal(source.Security.KeyLength, reopened.Security!.KeyLength);
        AssertSameDocument(source, reopened);
        AssertDirect(reopened.Security.EncryptionDictionary);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void A_kept_encryption_dictionary_whose_entries_are_indirect_saves_linearized(PdfCrossReferenceLayout layout)
    {
        byte[] file = Rc4File($"<< /Filter 7 0 R /R 3 /Length 8 0 R /P -4 /O 9 0 R /U {User} /V 2 0 R >>", "/Standard", "40", Owner);
        using PdfDocument source = PdfDocument.Open(file);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithLinearize(true));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.IsLinearized);
        AssertSameDocument(source, reopened);
        AssertDirect(reopened.Security!.EncryptionDictionary);
    }

    [Fact]
    public void An_incremental_update_keeps_an_encryption_dictionary_with_indirect_entries_as_it_is()
    {
        // The update writes only what changed; the dictionary and the objects its entries refer to stay as they were read.
        byte[] file = Rc4File($"<< /Filter 7 0 R /R 3 /Length 8 0 R /P -4 /O 9 0 R /U {User} /V 2 0 R >>", "/Standard", "40", Owner);
        using PdfDocument source = PdfDocument.Open(file);
        source.Catalog[new CosName("Lang")] = new CosString("en"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithIncrementalUpdate(true));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Pages);
        Assert.Equal("en"u8.ToArray(), ((CosString)reopened.Catalog[new CosName("Lang")]).Bytes.ToArray());
        AssertSameDocument(source, reopened);
    }

    [Fact]
    public void An_oversized_entry_before_the_handlers_entries_does_not_take_them_out_of_the_saved_dictionary()
    {
        // Review finding: Extra (no handler reads it) refers to 4096 integers, and V to an array, which the handler reads as no V.
        string integers = string.Join(' ', Enumerable.Repeat("0", 4096));
        byte[] file = Rc4File($"<< /Extra 7 0 R /Filter /Standard /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 8 0 R >>", $"[{integers}]", "[1 2]");
        using PdfDocument source = PdfDocument.Open(file);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams));

        using PdfDocument reopened = PdfDocument.Open(saved);
        AssertSameDocument(source, reopened);
        CosDictionary written = reopened.Security!.EncryptionDictionary;
        Assert.False(written.ContainsKey(new CosName("Extra")));
        Assert.Equal(2L, Assert.IsType<CosInteger>(written[new CosName("V")]).Value);
        foreach (string key in (string[])["Filter", "R", "Length", "P", "O", "U"])
        {
            Assert.True(written.ContainsKey(new CosName(key)), key);
        }
    }

    [Theory]
    [MemberData(nameof(LayoutsAndNumberings))]
    public void A_confidential_string_is_never_written_in_the_clear(PdfCrossReferenceLayout layout, PdfObjectNumbering numbering)
    {
        // Review finding: V refers to the page tree, whose page and annotation hold strings the save encrypts. Nothing of the
        // document may reach the encryption dictionary, which is written unencrypted.
        byte[] file = Rc4File($"<< /Filter /Standard /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 2 0 R >>");
        using PdfDocument source = PdfDocument.Open(file);
        PdfPage page = Assert.Single(source.Pages);
        page.Dictionary[new CosName("Secret")] = new CosString("confidential-page"u8);
        page.Dictionary[new CosName("Annots")] = new CosArray([
            new CosDictionary
            {
                [new CosName("Subtype")] = new CosName("Text"),
                [new CosName("Rect")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]),
                [new CosName("Contents")] = new CosString("confidential-note"u8),
            },
        ]);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithObjectNumbering(numbering).WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal(-1, saved.AsSpan().IndexOf("confidential"u8));
        using PdfDocument reopened = PdfDocument.Open(saved);
        CosDictionary reopenedPage = Assert.Single(reopened.Pages).Dictionary;
        Assert.Equal("confidential-page"u8.ToArray(), ((CosString)reopenedPage[new CosName("Secret")]).Bytes.ToArray());
        AssertDirect(reopened.Security!.EncryptionDictionary);
    }

    [Fact]
    public void A_reference_to_a_reference_reads_as_the_reader_read_it()
    {
        // Review finding: Filter refers to object 7, whose value is the reference 8 0 R to /Vendor. The reader resolves once, sees
        // no name, and takes the standard handler because R is present; the save must not write /Vendor.
        byte[] file = Rc4File($"<< /Filter 7 0 R /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 2 >>", "8 0 R", "/Vendor");
        using PdfDocument source = PdfDocument.Open(file);
        Assert.Equal(new CosName("Standard"), source.Security!.Handler);

        byte[] saved = Save(source, new PdfSaveOptions());

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new CosName("Standard"), reopened.Security!.Handler);
        Assert.Equal(new CosName("Standard"), reopened.Security.EncryptionDictionary[new CosName("Filter")]);
        AssertSameDocument(source, reopened);
    }

    [Fact]
    public void Shared_arrays_an_entry_refers_to_do_not_amplify_the_saved_dictionary()
    {
        // Review finding: 64 references to an array of 64 references to an array of 64 integers would copy 262144 values.
        string level2 = string.Join(' ', Enumerable.Repeat("0", 64));
        string level1 = string.Join(' ', Enumerable.Repeat("9 0 R", 64));
        string level0 = string.Join(' ', Enumerable.Repeat("8 0 R", 64));
        byte[] file = Rc4File($"<< /Filter /Standard /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 2 /Extra 7 0 R >>", $"[{level0}]", $"[{level1}]", $"[{level2}]");
        using PdfDocument source = PdfDocument.Open(file);

        byte[] saved = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None));

        Assert.True(saved.Length < file.Length + 2048, $"The saved file is {saved.Length} bytes; the source {file.Length}.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        AssertSameDocument(source, reopened);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void An_encryption_dictionary_the_page_tree_also_uses_keeps_its_content_in_a_save_that_keeps_the_encryption(PdfCrossReferenceLayout layout)
    {
        // Review finding: the catalog's Pages is the encryption dictionary, which reads as the one page, with Contents 4 0 R.
        byte[] file = new TestPdf { Header = "%PDF-1.4", TrailerEntries = $"/Encrypt 6 0 R {Id}" }.Build(
        [
            "<< /Type /Catalog /Pages 6 0 R >>",
            .. CorpusObjects()[1..5],
            $"<< /Filter /Standard /R 3 /Length 40 /P -4 /O {Owner} /U {User} /V 2 /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
        ]);
        using PdfDocument source = PdfDocument.Open(file);
        Assert.Equal(new CosReference(6, 0), Assert.Single(source.Pages).Reference);
        string[] contents = DecodedContents(source);
        Assert.NotEqual(string.Empty, Assert.Single(contents));

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(contents, DecodedContents(reopened));
        Assert.True(reopened.IsEncrypted);
    }

    [Fact]
    public void A_direct_encryption_dictionary_in_the_trailer_with_references_is_written_as_a_new_direct_object()
    {
        byte[] file = new TestPdf { Header = "%PDF-1.4", TrailerEntries = $"/Encrypt << /Filter /Standard /R 3 /Length 40 /P -4 /O 6 0 R /U {User} /V 2 >> {Id}" }
            .Build([.. CorpusObjects(), Owner]);
        using PdfDocument source = PdfDocument.Open(file);
        Assert.True(source.IsEncrypted);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams));

        using PdfDocument reopened = PdfDocument.Open(saved);
        AssertSameDocument(source, reopened);
        Assert.IsType<CosReference>(reopened.Trailer[Encrypt]);
        AssertDirect(reopened.Security!.EncryptionDictionary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_document_whose_encryption_dictionary_has_indirect_entries_saves_with_new_encryption_or_none(bool remove)
    {
        byte[] file = Rc4File($"<< /Filter 7 0 R /R 3 /Length 8 0 R /P -4 /O 9 0 R /U {User} /V 10 0 R >>", "/Standard", "40", Owner, "2");
        using PdfDocument source = PdfDocument.Open(file);
        PdfSaveOptions options = new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams);
        options = remove ? options.WithRemoveEncryption(true) : options.WithEncryption(new PdfEncryptionOptions(null, "owner"));

        byte[] saved = Save(source, options);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(!remove, reopened.IsEncrypted);
        AssertSameDocument(source, reopened);
    }

    /// <summary>
    /// <c>encrypted-rc4-40-r3.pdf</c> (empty user password) with its objects 1 to 5 as they are, object 6 the encryption dictionary
    /// <paramref name="encryption"/> and <paramref name="extra"/> objects from 7 on.
    /// </summary>
    private static byte[] Rc4File(string encryption, params string[] extra) =>
        new TestPdf { Header = "%PDF-1.4", TrailerEntries = $"/Encrypt 6 0 R {Id}" }.Build([.. CorpusObjects(), encryption, .. extra]);

    /// <summary>Objects 1 to 5 of <c>encrypted-rc4-40-r3.pdf</c>: catalog, page tree, page, its encrypted content stream and font.</summary>
    private static string[] CorpusObjects()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-rc4-40-r3.pdf"));
        return [.. ObjectPattern().Matches(text).Select(match => match.Groups[2].Value).Take(5)];
    }

    /// <summary>No entry of the dictionary, nor of a dictionary or array inside it, is a reference.</summary>
    private static void AssertDirect(CosObject value)
    {
        switch (value)
        {
            case CosReference reference:
                Assert.Fail($"The encryption dictionary holds the reference {reference.ObjectNumber} {reference.Generation} R.");
                break;
            case CosDictionary dictionary:
                foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                {
                    AssertDirect(entry.Value);
                }

                break;
            case CosArray array:
                foreach (CosObject item in array)
                {
                    AssertDirect(item);
                }

                break;
        }
    }

    /// <summary>The decoded content of every page.</summary>
    private static string[] DecodedContents(PdfDocument document) =>
        [.. document.Pages.Select(page => document.Resolve(page.Dictionary[new CosName("Contents")]) is CosStream stream
            ? Encoding.Latin1.GetString(document.DecodeStream(stream).Span)
            : string.Empty)];

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>The catalog graphs are equal: every string and every stream's data (decrypted) the same.</summary>
    private static void AssertSameDocument(PdfDocument expected, PdfDocument actual) =>
        CosGraph.AssertIsomorphic(expected, expected.Trailer[Root], actual, actual.Trailer[Root]);

    [GeneratedRegex(@"(\d+) 0 obj\n(.*?)\nendobj", RegexOptions.Singleline)]
    private static partial Regex ObjectPattern();
}
