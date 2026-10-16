using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Signing a document: a signature dictionary in a signature field, an incremental update whose byte ranges cover the whole file but
/// the signature value, and a detached CMS signature over them. ISO 32000-2 §12.8.1, §12.8.3.3. Verified independently with the
/// platform PKCS package (<see cref="SignedCms"/>), OpenSSL's CMS verification and poppler's <c>pdfsig</c>; the certificates are
/// made in the test, so nothing needs the network or a real certificate.
/// </summary>
public sealed class ByteRangeSigningTests
{
    private static readonly System.Buffers.SearchValues<byte> HexDigits = System.Buffers.SearchValues.Create("0123456789ABCDEFabcdef"u8);

    [Fact]
    public void A_signed_corpus_file_verifies_with_openssl_over_its_byte_ranges()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] file = Corpus.Bytes("empty-page.pdf");
        using PdfDocument document = PdfDocument.Open(file);

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
    }

    [Fact]
    public void A_signed_corpus_file_verifies_with_pdfsig()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithReason("Approved").WithLocation("Lisbon"));

        string output = Pdfsig.Run(signed);
        Assert.Equal(1, Pdfsig.ValidSignatures(output));
        Assert.Contains("Signer Certificate Common Name: Broadside signer", output, StringComparison.Ordinal);
        Assert.Contains("Signature Type: adbe.pkcs7.detached", output, StringComparison.Ordinal);
        Assert.Contains("Total document signed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_byte_ranges_cover_the_whole_file_except_the_signature_value()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] file = Corpus.Bytes("empty-page.pdf");
        using PdfDocument document = PdfDocument.Open(file);

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        Assert.True(signed.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        SignedFile signature = SignedFile.Last(signed);
        Assert.Equal(4, signature.ByteRange.Length);
        Assert.Equal(0, signature.ByteRange[0]);
        long gapStart = signature.ByteRange[1];
        long gapEnd = signature.ByteRange[2];
        Assert.Equal(signed.Length, gapEnd + signature.ByteRange[3]);
        Assert.Equal((byte)'<', signed[gapStart]);
        Assert.Equal((byte)'>', signed[gapEnd - 1]);
        Assert.True(signed.AsSpan((int)gapStart + 1, (int)(gapEnd - gapStart - 2)).IndexOfAnyExcept(HexDigits) < 0, "The gap holds more than the signature value.");
        Assert.Equal(2 * signature.Contents.Length, gapEnd - gapStart - 2);
        Assert.EndsWith("%%EOF\n", System.Text.Encoding.ASCII.GetString(signed, signed.Length - 6, 6), StringComparison.Ordinal);
    }

    [Fact]
    public void The_signature_verifies_with_the_platform_pkcs_package_and_names_the_signer()
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = TestCertificates.Chain("CN=Broadside chained signer");
        using (authority)
        using (leaf)
        {
            using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

            byte[] signed = Sign(document, new PdfCertificateSigner(leaf, [authority]), new PdfSignatureOptions().WithDigestAlgorithm(HashAlgorithmName.SHA512));

            SignedFile signature = SignedFile.Last(signed);
            var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
            cms.Decode(signature.Cms);
            cms.CheckSignature(verifySignatureOnly: true);
            SignerInfo signer = Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
            Assert.Equal(leaf.Thumbprint, signer.Certificate!.Thumbprint);
            Assert.Equal("2.16.840.1.101.3.4.2.3", signer.DigestAlgorithm.Value);
            Assert.Equal(2, cms.Certificates.Count);
            OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), authority);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signing_twice_adds_two_revisions_and_the_first_signature_still_verifies(bool reopenBetween)
    {
        using X509Certificate2 first = TestCertificates.SelfSigned("CN=First signer");
        using X509Certificate2 second = TestCertificates.SelfSigned("CN=Second signer");
        byte[] file = Corpus.Bytes("xref-stream.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        int revisions = document.Revisions.Count;

        byte[] once = Sign(document, new PdfCertificateSigner(first));
        using PdfDocument reopened = PdfDocument.Open(once);
        byte[] twice = Sign(reopenBetween ? reopened : document, new PdfCertificateSigner(second));

        Assert.True(twice.AsSpan(0, once.Length).SequenceEqual(once), "The first signed revision changed.");
        using (PdfDocument signedTwice = PdfDocument.Open(twice))
        {
            Assert.Equal(revisions + 2, signedTwice.Revisions.Count);
        }

        IReadOnlyList<SignedFile> signatures = SignedFile.All(twice);
        Assert.Equal(["Signature1", "Signature2"], signatures.Select(signature => signature.FieldName));
        Assert.Equal(once.Length, signatures[0].ByteRange[2] + signatures[0].ByteRange[3]);
        Assert.Equal(twice.Length, signatures[1].ByteRange[2] + signatures[1].ByteRange[3]);
        OpenSsl.VerifyCms(signatures[0].Cms, signatures[0].CoveredBytes(), first);
        OpenSsl.VerifyCms(signatures[1].Cms, signatures[1].CoveredBytes(), second);
        Assert.Equal(2, Pdfsig.ValidSignatures(Pdfsig.Run(twice)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_external_signer_that_receives_only_the_digest_produces_a_valid_signature(bool asynchronous)
    {
        using X509Certificate2 withKey = TestCertificates.SelfSigned("CN=Remote signer");
        using RSA key = withKey.GetRSAPrivateKey()!;
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
        var signer = new DigestOnlySigner(publicOnly, key);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = asynchronous ? await SignThroughSignAsync(document, signer) : Sign(document, signer);

        (byte[] digest, HashAlgorithmName algorithm, bool wasAsync) = Assert.Single(signer.Requests);
        Assert.Equal(asynchronous, wasAsync);
        Assert.Equal(HashAlgorithmName.SHA256, algorithm);
        Assert.Equal(32, digest.Length);
        SignedFile signature = SignedFile.Last(signed);
        var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
        cms.Decode(signature.Cms);
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.True(key.VerifyHash(digest, cms.SignerInfos[0].GetSignature(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), publicOnly);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    /// <summary>Corpus files of each cross-reference form, and <c>Created</c> for a document made with <see cref="PdfDocument.Create()"/>.</summary>
    public static TheoryData<string> FileLayouts => new() { "incremental-update.pdf", "hybrid-xref.pdf", "linearized.pdf", "object-stream.pdf", "no-xref.pdf", "Created" };

    [Theory]
    [MemberData(nameof(FileLayouts))]
    public void Every_file_layout_signs_to_a_file_that_qpdf_checks_and_pdfsig_verifies(string fileName)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = fileName == "Created" ? PdfDocument.Create() : PdfDocument.Open(Corpus.Bytes(fileName));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        SignedFile signature = SignedFile.Last(signed);
        var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
        cms.Decode(signature.Cms);
        cms.CheckSignature(verifySignatureOnly: true);
        (int exitCode, string output) = ExternalTool.Run("qpdf", signed, "--check");
        Assert.True(exitCode == 0, output);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    [Fact]
    public void An_existing_unsigned_signature_field_is_signed_in_place()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        int fields = document.AcroForm!.TerminalFields.Count;

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName("sig"));

        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(fields, reopened.AcroForm!.TerminalFields.Count);
        var field = Assert.IsType<PdfSignatureField>(reopened.AcroForm.FindField("sig"));
        Assert.True(field.IsSigned);
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, reopened.AcroForm.SignatureFlags);
        Assert.Equal("sig", SignedFile.Last(signed).FieldName);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    [Fact]
    public void A_new_field_is_an_invisible_widget_on_the_first_page_with_the_signature_properties()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        Assert.Null(document.AcroForm);
        var options = new PdfSignatureOptions()
            .WithFieldName("Approval")
            .WithReason("Approved")
            .WithLocation("Lisboa, Portugal")
            .WithContactInfo("+351 21 000 0000")
            .WithSignerName("Zoë Signer")
            .WithSigningTime(new DateTimeOffset(2026, 10, 12, 9, 30, 15, TimeSpan.FromHours(1)));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), options);

        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Empty(reopened.Diagnostics);
        var field = Assert.IsType<PdfSignatureField>(Assert.Single(reopened.AcroForm!.TerminalFields));
        Assert.Equal("Approval", field.FullyQualifiedName);
        Assert.False(field.IsVisible);
        Assert.Equal(PdfSignatureFlags.SignaturesExist | PdfSignatureFlags.AppendOnly, reopened.AcroForm.SignatureFlags);
        var widget = Assert.Single(field.Widgets);
        Assert.Same(reopened.Pages[0].Dictionary, widget.Page?.Dictionary);
        Assert.Contains(reopened.Pages[0].Annotations, annotation => ReferenceEquals(annotation.Dictionary, widget.Dictionary));
        CosDictionary signature = field.SignatureDictionary!;
        Assert.Equal("Sig", ((CosName)signature[new CosName("Type")]).Value);
        Assert.Equal("Adobe.PPKLite", ((CosName)signature[new CosName("Filter")]).Value);
        Assert.Equal("adbe.pkcs7.detached", ((CosName)signature[new CosName("SubFilter")]).Value);
        Assert.Equal("Approved", Text(signature, "Reason"));
        Assert.Equal("Lisboa, Portugal", Text(signature, "Location"));
        Assert.Equal("+351 21 000 0000", Text(signature, "ContactInfo"));
        Assert.Equal("Zoë Signer", Text(signature, "Name"));
        Assert.Equal("D:20261012093015+01'00'", Text(signature, "M"));
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));

        static string Text(CosDictionary dictionary, string key) => ((CosString)dictionary[new CosName(key)]).DecodeText();
    }

    [Theory]
    [InlineData("sig")]
    [InlineData("name")]
    public void Signing_a_signed_field_or_a_field_of_another_type_throws_and_changes_nothing(string fieldName)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed;
        using (PdfDocument original = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf")))
        {
            signed = Sign(original, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName("sig"));
        }

        using PdfDocument document = PdfDocument.Open(signed);
        int revisions = document.Revisions.Count;
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName(fieldName)));

        Assert.Equal(0, output.Length);
        Assert.Equal(revisions, document.Revisions.Count);
        Assert.Single(document.AcroForm!.TerminalFields.OfType<PdfSignatureField>(), field => field.IsSigned);
    }

    [Fact]
    public void What_byte_range_signing_does_not_write_yet_is_refused_before_anything_changes()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using X509Certificate2 edwards = TestCertificates.WithKeyAlgorithm("CN=Ed25519 signer", "1.3.101.112", new byte[32], certificate);
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => document.Sign(output, new FailingSigner(edwards)));
        Assert.Throws<ArgumentException>(() => new PdfCertificateSigner(publicOnly));
        Assert.Throws<NotSupportedException>(() => new PdfSignatureOptions().WithDigestAlgorithm(HashAlgorithmName.SHA1));
        Assert.Throws<ArgumentException>(() => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName("a.b")));
        Assert.Throws<ArgumentException>(() => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName(string.Empty)));

        Assert.Equal(0, output.Length);
        Assert.Null(document.AcroForm);
    }

    [Fact]
    public void A_signature_covers_the_modification_date_and_the_raised_version_its_update_writes()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));
        document.AddEmbeddedFile("note.txt", "Signed with an attachment."u8);

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        // Issues #141 and #162: the save inside signing raises the version (UF needs PDF 1.7) in the catalog and dates the
        // modification; both are in the signed update, so the byte ranges cover them.
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
        using (PdfDocument reopened = PdfDocument.Open(signed))
        {
            Assert.Equal(new PdfVersion(1, 7), reopened.Version);
            Assert.NotNull(reopened.Information?.ModificationDate);
            Assert.Single(reopened.EmbeddedFiles);
        }

        SignedFile signature = SignedFile.Last(signed);
        var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
        cms.Decode(signature.Cms);
        cms.CheckSignature(verifySignatureOnly: true);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        (int exitCode, string output) = ExternalTool.Run("qpdf", signed, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Theory]
    [InlineData("empty-page.pdf", false)]
    [InlineData("metadata-xmp.pdf", false)]
    [InlineData("info-dictionary.pdf", true)]
    public async Task A_signer_that_fails_leaves_every_object_of_the_document_as_it_was(string file, bool ecdsa)
    {
        // Issue #306: the save inside signing changes the document before the signer is asked. empty-page.pdf has no Info, so the
        // dating adds one and points the trailer at it; metadata-xmp.pdf has Info and an XMP packet, both dated; an ECDSA signature
        // raises info-dictionary.pdf (PDF 1.7) to 2.0, which declares the ISO_ extension and the catalog Version and moves the
        // deprecated Info entries to a new XMP packet (#301). All of it is undone when the signer throws.
        using X509Certificate2 certificate = ecdsa
            ? TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.NamedCurves.nistP256)
            : TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Assert.Throws<CryptographicException>(() => document.Sign(output, new FailingSigner(certificate)));
        Assert.Equal(before, CosSnapshot.Of(document));

        await Assert.ThrowsAsync<CryptographicException>(
            () => document.SignAsync(output, new FailingSigner(certificate), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(before, CosSnapshot.Of(document));
        Assert.Equal(0, output.Length);

        // Nothing the failed signatures changed comes back with a later save.
        using var update = new MemoryStream();
        await document.SaveAsync(update, new PdfSaveOptions().WithIncrementalUpdate(true), TestContext.Current.CancellationToken);
        using PdfDocument reopened = PdfDocument.Open(update.ToArray());
        Assert.Null(reopened.AcroForm);
        Assert.Empty(reopened.Extensions);
        Assert.Equal(document.Version, reopened.Version);
    }

    [Fact]
    public void A_signature_after_a_failed_one_keeps_the_modification_date_set_before_both_in_the_form_of_the_raised_version()
    {
        // A date set explicitly is what the signed update writes (#162), in the PDF 2.0 form (no terminating apostrophe) once the
        // ECDSA signature raises the PDF 1.7 document to 2.0 (#291). The failed signature must not use up either.
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.NamedCurves.nistP256);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"));
        var date = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));
        document.Properties.ModificationDate = date;
        Assert.Throws<CryptographicException>(() => document.Sign(new MemoryStream(), new FailingSigner(certificate)));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        var info = (CosDictionary)reopened.Resolve(reopened.Trailer[new CosName("Info")]);
        Assert.Equal("D:20250102030405+01'00", ((CosString)reopened.Resolve(info[new CosName("ModDate")])).DecodeText());
    }

    private static async Task<byte[]> SignThroughSignAsync(PdfDocument document, IPdfSigner signer)
    {
        using var output = new MemoryStream();
        await document.SignAsync(output, signer, cancellationToken: TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    [Fact]
    public void Signing_a_linearized_file_appends_an_update_that_leaves_it_unlinearized()
    {
        // A signature is an incremental update (§12.8.1), which cannot be linearized (Annex F, F.1, G.7): the original bytes are
        // kept, the update is recorded as dropping the linearization, and the signature still verifies.
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] file = Corpus.Bytes("linearized.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        Assert.True(document.IsLinearized);

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        Assert.True(signed.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "LinearizationDropped");
        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(2, reopened.Pages.Count);
    }

    private static byte[] Sign(PdfDocument document, IPdfSigner signer, PdfSignatureOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Sign(output, signer, options);
        return output.ToArray();
    }

    /// <summary>A signer whose device refuses to sign.</summary>
    private sealed class FailingSigner(X509Certificate2 certificate) : IPdfSigner
    {
        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) => throw new CryptographicException("The token is locked.");
    }

    /// <summary>A signer whose key is not in its certificate, standing in for an HSM or a remote service: it only ever gets a digest.</summary>
    private sealed class DigestOnlySigner(X509Certificate2 certificate, RSA key) : IPdfSigner
    {
        public List<(byte[] Digest, HashAlgorithmName Algorithm, bool Asynchronous)> Requests { get; } = [];

        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm)
        {
            Requests.Add((digest.ToArray(), digestAlgorithm, false));
            return key.SignHash(digest.ToArray(), digestAlgorithm, RSASignaturePadding.Pkcs1);
        }

        public async ValueTask<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, HashAlgorithmName digestAlgorithm, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Requests.Add((digest.ToArray(), digestAlgorithm, true));
            return key.SignHash(digest.ToArray(), digestAlgorithm, RSASignaturePadding.Pkcs1);
        }
    }
}
