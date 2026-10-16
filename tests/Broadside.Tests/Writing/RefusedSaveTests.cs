using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// A save that strict mode refuses changes nothing in the document (issue #300): every check that can refuse a save runs before
/// the save dates the document (#162), completes font subsets (#285) or declares extensions and versions in the catalog (#141).
/// ADR 0005; ISO 32000-2 §7.5.2, §7.6.2 (Table 20), §14.3.4.
/// </summary>
public sealed class RefusedSaveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.FromHours(-5));

    private const string LaterExtension = "/Extensions << /XYZ_ << /BaseVersion /1.6 /ExtensionLevel 1 >> >>";

    private static PdfOptions Strict => new PdfOptions().UseStrict().UseTimeProvider(new FixedTimeProvider(Now));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_refused_version_raise_leaves_a_changed_document_as_it_was(bool incremental)
    {
        // PDF 1.4 without an Info dictionary: dating would add one (#162); the extension needs PDF 1.6 (#141).
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", LaterExtension, header: "%PDF-1.4"), Strict);
        document.Pages[0].Rotation = 90;

        AssertRefusedSaveChangesNothing(document, new PdfSaveOptions().WithIncrementalUpdate(incremental), "VersionRaised");
        Assert.Null(document.CatalogVersion);
        Assert.Null(document.Information);
    }

    [Fact]
    public void A_refused_version_raise_of_an_info_and_xmp_document_leaves_both_undated()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"), Strict);
        document.Pages[0].Rotation = 90;
        PdfDate? modified = document.Information?.ModificationDate;

        AssertRefusedSaveChangesNothing(document, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")), "VersionRaised");
        Assert.Equal(modified, document.Information?.ModificationDate);
    }

    [Fact]
    public void After_a_refused_save_the_next_save_writes_what_a_save_without_the_refusal_writes()
    {
        // A clock that moves on each time it is read: a refused save that read it would date the next save differently.
        byte[] file = Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4");
        using PdfDocument refused = PdfDocument.Open(file, new PdfOptions().UseStrict().UseTimeProvider(new TickingTimeProvider(Now)));
        using PdfDocument direct = PdfDocument.Open(file, new PdfOptions().UseStrict().UseTimeProvider(new TickingTimeProvider(Now)));
        refused.Pages[0].Rotation = 90;
        direct.Pages[0].Rotation = 90;

        Assert.Throws<DiagnosticException>(() => refused.Save(new MemoryStream(), new PdfSaveOptions().WithCrossReferenceLayout(PdfCrossReferenceLayout.Stream)));

        Assert.Equal(Save(direct, new PdfSaveOptions()), Save(refused, new PdfSaveOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_save_refused_for_a_destination_to_a_removed_page_leaves_the_document_as_it_was(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"), Strict);
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.RemoveAt(0);

        AssertRefusedSaveChangesNothing(document, new PdfSaveOptions().WithIncrementalUpdate(incremental), "DestinationPageRemoved");
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4)]
    [InlineData(PdfEncryptionAlgorithm.Aes128)]
    public void A_save_refused_for_deprecated_new_encryption_leaves_the_document_as_it_was(PdfEncryptionAlgorithm algorithm)
    {
        // empty-page.pdf is PDF 1.7, which RC4 (1.4) and AES-128 (1.6) need no raise for: deprecation is the only refusal.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"), Strict);
        document.Pages[0].Rotation = 180;

        AssertRefusedSaveChangesNothing(
            document,
            new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm)),
            "EncryptionDeprecated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_save_refused_for_kept_deprecated_encryption_leaves_the_document_as_it_was(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-rc4-128.pdf"), Strict);
        document.Pages[0].Rotation = 90;

        AssertRefusedSaveChangesNothing(document, new PdfSaveOptions().WithIncrementalUpdate(incremental), "EncryptionDeprecated");
    }

    [Fact]
    public void A_refused_first_save_of_a_new_document_keeps_its_identifiers_and_adds_no_creation_date()
    {
        using PdfDocument document = PdfDocument.Create(Strict);

        AssertRefusedSaveChangesNothing(
            document,
            new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Rc4)),
            "EncryptionDeprecated");
        Assert.NotNull(document.FileIdentifier);
    }

    /// <summary>
    /// Saves <paramref name="document"/>, which strict mode must refuse with <paramref name="code"/>, and asserts that nothing was
    /// written and the document's COS graph is exactly what it was before the save.
    /// </summary>
    private static void AssertRefusedSaveChangesNothing(PdfDocument document, PdfSaveOptions options, string code)
    {
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => document.Save(output, options));

        Assert.Equal(code, refused.Diagnostic.Code);
        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }

    /// <summary>A clock that always reads <paramref name="now"/>, in a zone with its offset.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Fixed", now.Offset, "Fixed", "Fixed");

        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    /// <summary>A clock that starts at <paramref name="start"/> and moves on a minute each time it is read.</summary>
    private sealed class TickingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private int _reads;

        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Fixed", start.Offset, "Fixed", "Fixed");

        public override DateTimeOffset GetUtcNow() => start.ToUniversalTime().AddMinutes(_reads++);
    }
}
