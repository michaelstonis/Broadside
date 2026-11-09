using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Objects the encryption dictionary's entries refer to (issue #398). The reader resolves those entries before it installs
/// decryption, so it reads them undecrypted. That is right for the encryption dictionary (ISO 32000-2 §7.6.2: its values are not
/// encrypted), but another object reached that way is an ordinary object of the document. It must load again, decrypted, for
/// everything else, and an object stream decoded before decryption must not stay decoded.
/// </summary>
/// <remarks>
/// The files are <c>encrypted-aes-256.pdf</c> (revision 6, empty user password) with objects added. With AES-256 every object is
/// encrypted with the file encryption key itself (Algorithm 1.A), which <c>tests/Corpus/generate.py</c> derives from
/// <c>fixed_bytes("encrypted-aes-256:file-key", 32)</c>.
/// </remarks>
public sealed partial class EncryptionDictionaryReferenceTests
{
    private const string Id = "/ID [<a36aa81ec7ab1e690c924cd73611dff4> <a36aa81ec7ab1e690c924cd73611dff4>]";
    private const string Label = "Shared label";

    private static readonly CosName LabelKey = new("Label");

    [Fact]
    public void A_dictionary_the_encryption_dictionary_and_the_document_share_reads_decrypted_for_the_document()
    {
        using PdfDocument document = PdfDocument.Open(SharedCryptFilters());

        Assert.Equal(5, document.Security!.Version);
        Assert.Equal(Label, LabelOf(document));
        Assert.Contains("AES-256 (R6)", ContentOf(document), StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_save_that_rewrites_a_shared_dictionary_writes_its_strings_once_encrypted(bool incremental)
    {
        using PdfDocument source = PdfDocument.Open(SharedCryptFilters());
        ((CosDictionary)source.Resolve(new CosReference(7, 0)))[new CosName("Note")] = new CosString("changed"u8);
        using var output = new MemoryStream();

        source.Save(output, new PdfSaveOptions().WithIncrementalUpdate(incremental));

        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        Assert.Equal(5, reopened.Security!.Version);
        Assert.Equal(Label, LabelOf(reopened));
        Assert.Equal("changed", Encoding.Latin1.GetString(((CosString)((CosDictionary)reopened.Resolve(new CosReference(7, 0)))[new CosName("Note")]).Bytes));
        Assert.Contains("AES-256 (R6)", ContentOf(reopened), StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_that_refers_into_an_object_stream_leaves_the_object_stream_to_be_decoded_decrypted()
    {
        // V is object 7, in encrypted object stream 8 with the catalog. Before decryption is installed the stream cannot be decoded,
        // so V reads as absent (R 6 implies V 5); the catalog and V read from the stream decoded once decryption is installed.
        using PdfDocument document = PdfDocument.Open(VersionInObjectStream());

        Assert.Equal(5, document.Security!.Version);
        Assert.Equal(new CosInteger(5), document.Resolve(new CosReference(7, 0)));
        Assert.Single(document.Pages);
        Assert.Contains("AES-256 (R6)", ContentOf(document), StringComparison.Ordinal);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "RootMissing" or "CatalogNotFound");
    }

    /// <summary>
    /// The corpus file whose crypt filter <c>StdCF</c> is object 7, a dictionary that also holds an encrypted string and that the
    /// catalog refers to.
    /// </summary>
    private static byte[] SharedCryptFilters()
    {
        string[] objects = CorpusObjects();
        objects[0] = "<< /Type /Catalog /Pages 2 0 R /Shared 7 0 R >>";
        objects[5] = CryptFiltersPattern().Replace(objects[5], "/CF << /StdCF 7 0 R >>");
        Assert.Contains("/StdCF 7 0 R", objects[5], StringComparison.Ordinal);
        string shared = $"<< /CFM /AESV3 /AuthEvent /DocOpen /Length 32 /Label <{Convert.ToHexString(Encrypt(Encoding.Latin1.GetBytes(Label), 7))}> >>";
        return new TestPdf { Header = "%PDF-2.0", TrailerEntries = $"/Encrypt 6 0 R {Id}" }.Build([.. objects, shared]);
    }

    /// <summary>
    /// The corpus file with its catalog (1) and an integer 5 (7) in object stream 8, encrypted, the encryption dictionary's
    /// <c>V</c> referring to 7, and a cross-reference stream (9).
    /// </summary>
    private static byte[] VersionInObjectStream()
    {
        string[] objects = CorpusObjects();
        string encryption = objects[5].Replace("/V 5", "/V 7 0 R", StringComparison.Ordinal);
        Assert.Contains("/V 7 0 R", encryption, StringComparison.Ordinal);

        string[] members = [objects[0], "5"];
        var data = new StringBuilder();
        var header = new StringBuilder();
        int[] numbers = [1, 7];
        for (int index = 0; index < members.Length; index++)
        {
            header.Append(CultureInfo.InvariantCulture, $"{numbers[index]} {data.Length} ");
            data.Append(members[index]).Append(' ');
        }

        byte[] encrypted = Encrypt(Encoding.Latin1.GetBytes(header.ToString() + data), 8);
        string objectStream = $"<< /Type /ObjStm /N 2 /First {header.Length} /Length {encrypted.Length} >>\nstream\n{Encoding.Latin1.GetString(encrypted)}\nendstream";

        var text = new StringBuilder("%PDF-2.0\n%âãÏÓ\n");
        var offsets = new Dictionary<int, int>();
        void Add(int number, string body)
        {
            offsets[number] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n");
        }

        for (int number = 2; number <= 5; number++)
        {
            Add(number, objects[number - 1]);
        }

        Add(6, encryption);
        Add(8, objectStream);
        int xref = text.Length;
        var rows = new List<byte>();
        void Row(byte type, int field, int index)
        {
            rows.Add(type);
            rows.AddRange([(byte)(field >> 24), (byte)(field >> 16), (byte)(field >> 8), (byte)field]);
            rows.AddRange([(byte)(index >> 8), (byte)index]);
        }

        Row(0, 0, 0xFFFF);
        Row(2, 8, 0);
        for (int number = 2; number <= 6; number++)
        {
            Row(1, offsets[number], 0);
        }

        Row(2, 8, 1);
        Row(1, offsets[8], 0);
        Row(1, xref, 0);
        text.Append(CultureInfo.InvariantCulture, $"9 0 obj\n<< /Type /XRef /Size 10 /W [1 4 2] /Root 1 0 R /Encrypt 6 0 R {Id} /Length {rows.Count} >>\nstream\n");
        text.Append(Encoding.Latin1.GetString([.. rows]));
        text.Append(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>The IV followed by AES-256-CBC with PKCS#5 padding under the file encryption key (§7.6.3.1, Algorithm 1.A).</summary>
    private static byte[] Encrypt(byte[] plaintext, int objectNumber)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData("broadside-corpus:encrypted-aes-256:file-key:0"u8);
        byte[] iv = SHA256.HashData(Encoding.ASCII.GetBytes($"iv {objectNumber}"))[..16];
        return [.. iv, .. aes.EncryptCbc(plaintext, iv)];
    }

    /// <summary>Objects 1 to 6 of <c>encrypted-aes-256.pdf</c>: catalog, page tree, page, its encrypted content stream, font and encryption dictionary.</summary>
    private static string[] CorpusObjects()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-256.pdf"));
        return [.. ObjectPattern().Matches(text).Select(match => match.Groups[2].Value).Take(6)];
    }

    private static string LabelOf(PdfDocument document) =>
        Encoding.Latin1.GetString(((CosString)((CosDictionary)document.Resolve(new CosReference(7, 0)))[LabelKey]).Bytes);

    private static string ContentOf(PdfDocument document) =>
        Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")])).Span);

    [GeneratedRegex(@"(\d+) 0 obj\n(.*?)\nendobj", RegexOptions.Singleline)]
    private static partial Regex ObjectPattern();

    [GeneratedRegex(@"/CF << /StdCF << [^>]* >> >>")]
    private static partial Regex CryptFiltersPattern();
}
