using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Long-term validation (ISO 32000-2 §12.8.4.3, §12.8.4.4 and §12.8.5.3; ETSI EN 319 142-1 B-LT and B-LTA) through
/// <see cref="PdfDocument.AddValidationData"/> and <see cref="PdfDocument.ValidateSignatures"/>: the certificates and revocation data
/// every signature needs go into a document security store in an incremental update, a document timestamp then protects it, and the
/// document validates offline, later, once certificates have expired. The certification authority, its OCSP responder and the
/// time-stamping authorities are <see cref="TestRevocationAuthority"/> and <see cref="TestTimestampAuthority"/>, inside the test
/// process: nothing goes to the network.
/// </summary>
public sealed class LongTermValidationTests
{
    private static readonly CosName Dss = new("DSS");

    [Fact]
    public void Validation_data_added_to_a_b_t_signature_validates_it_offline()
    {
        using Pki pki = new();
        byte[] signed = pki.Sign(Corpus.Bytes("empty-page.pdf"));

        byte[] lt = pki.AddValidationData(signed);

        using PdfDocument document = PdfDocument.Open(lt);
        PdfSignatureValidation validation = Assert.Single(document.ValidateSignatures(pki.Offline()));
        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Empty(validation.Problems);
        PdfCertificateRevocation signer = Assert.Single(validation.Revocations);
        Assert.Equal((PdfRevocationStatus.Good, PdfRevocationSource.Ocsp, false), (signer.Status, signer.Source, signer.Fetched));
        PdfTimestampValidation timestamp = Assert.IsType<PdfTimestampValidation>(validation.Timestamp);
        Assert.Equal(PdfSignatureStatus.Valid, timestamp.Status);
        Assert.Equal(PdfRevocationStatus.Good, Assert.Single(timestamp.Revocations).Status);
        Assert.Equal(1, pki.Network.Requests.Count(static request => request.StartsWith("POST", StringComparison.Ordinal)));

        // The update is a document security store only, which every permission allows (§12.8.2.2.2, Table 257).
        Assert.All(validation.Modifications, static modification => Assert.True(modification.IsPermitted));
        Assert.Contains(validation.Modifications, static modification => modification.Kind == PdfModificationKind.DocumentSecurityStore);
        using PdfDocument before = PdfDocument.Open(signed);
        Assert.Equal(before.Revisions.Count + 1, document.Revisions.Count);
    }

    [Fact]
    public void The_security_store_holds_the_chains_the_revocation_data_and_a_vri_entry_per_signature()
    {
        using Pki pki = new();
        byte[] signed = pki.Sign(Corpus.Bytes("empty-page.pdf"));

        using PdfDocument document = PdfDocument.Open(pki.AddValidationData(signed));

        var store = Assert.IsType<CosDictionary>(document.Resolve(document.Catalog[Dss]));
        string[] certificates = [.. Streams(document, store, "Certs").Select(static data => X509CertificateLoader.LoadCertificate(data).Thumbprint)];
        Assert.Equal(
            new[] { pki.Signer.Thumbprint, pki.Root.Thumbprint, TestTimestampAuthority.Unit.Thumbprint, TestTimestampAuthority.Root.Thumbprint }.Order(),
            certificates.Order());
        Assert.Equal(2, Streams(document, store, "OCSPs").Count);
        Assert.Empty(Streams(document, store, "CRLs"));

        // §12.8.4.3, Table 261: the key is the SHA-1 of the whole Contents string, zero padding included.
        var signature = (CosDictionary)document.Resolve(document.AcroForm!.TerminalFields.OfType<PdfSignatureField>().Single().SignatureDictionary);
#pragma warning disable CA5350 // Table 261 names SHA-1 for the key.
        string key = Convert.ToHexString(SHA1.HashData(((CosString)signature[KnownNames.Contents]).Bytes));
#pragma warning restore CA5350
        var vri = Assert.IsType<CosDictionary>(document.Resolve(store[new CosName("VRI")]));
        var entry = Assert.IsType<CosDictionary>(document.Resolve(vri[new CosName(key)]));
        Assert.Equal(4, Assert.IsType<CosArray>(document.Resolve(entry[new CosName("Cert")])).Count);
        Assert.Equal(2, Assert.IsType<CosArray>(document.Resolve(entry[new CosName("OCSP")])).Count);
        Assert.Equal(new PdfVersion(2, 0), document.Version);
    }

    [Fact]
    public void A_b_lta_document_validates_offline_after_the_signer_and_its_timestamp_unit_expired()
    {
        using Pki pki = new(signerNotAfter: DateTimeOffset.UtcNow.AddHours(1));
        using X509Certificate2 archiveUnit = TestTimestampAuthority.IssueUnit(TestTimestampAuthority.Root, notAfter: DateTimeOffset.UtcNow.AddMonths(11));
        using var archive = new TestTimestampAuthority { Certificate = archiveUnit };
        using var archiveHttp = new HttpClient(archive, disposeHandler: false);
        byte[] lt = pki.AddValidationData(pki.Sign(Corpus.Bytes("empty-page.pdf")));
        using var lta = new MemoryStream();
        using (PdfDocument document = PdfDocument.Open(lt))
        {
            document.Timestamp(lta, new PdfTimestampClient(archiveHttp, TestTimestampAuthority.Address));
        }

        // Seven months on, the signer (one hour) and the signature's time-stamping unit (six months) have expired.
        DateTimeOffset later = DateTimeOffset.UtcNow.AddMonths(7);
        using PdfDocument reopened = PdfDocument.Open(lta.ToArray());
        PdfSignatureValidationOptions options = pki.Offline()
            .WithValidationTime(later)
            .WithOcspResponses(pki.TimestampRevocation.Ocsp(archiveUnit, nextUpdate: later.AddDays(1)));
        IReadOnlyList<PdfSignatureValidation> validations = reopened.ValidateSignatures(options);

        Assert.Equal(2, validations.Count);
        PdfSignatureValidation archived = validations[1];
        Assert.Equal("ETSI.RFC3161", archived.SubFilter);
        Assert.Equal(PdfSignatureStatus.Valid, archived.Status);
        Assert.Equal(later, archived.ValidationTime);

        // §12.8.5.3: the document timestamp proves the signature, its token and the store existed at its time, so they are
        // validated then, with the revocation data the store holds.
        PdfSignatureValidation signature = validations[0];
        Assert.Equal(PdfSignatureStatus.Valid, signature.Status);
        Assert.Empty(signature.Problems);
        Assert.Equal(PdfSignatureStatus.Valid, signature.Timestamp?.Status);
        Assert.True(signature.ValidationTime < archived.Timestamp!.Time);
        Assert.Equal(PdfRevocationStatus.Good, Assert.Single(signature.Revocations).Status);
        Assert.All(signature.Modifications, static modification => Assert.True(modification.IsPermitted));
    }

    [Fact]
    public void Without_the_archive_timestamp_the_expired_signature_is_indeterminate()
    {
        using Pki pki = new(signerNotAfter: DateTimeOffset.UtcNow.AddHours(1));
        byte[] lt = pki.AddValidationData(pki.Sign(Corpus.Bytes("empty-page.pdf")));

        using PdfDocument document = PdfDocument.Open(lt);
        PdfSignatureValidation validation = Assert.Single(document.ValidateSignatures(pki.Offline().WithValidationTime(DateTimeOffset.UtcNow.AddMonths(7))));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Contains(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.CertificateExpired);
    }

    [Fact]
    public void A_certified_document_keeps_its_signature_valid_through_the_store_and_the_archive_timestamp()
    {
        using Pki pki = new();
        byte[] certified = pki.Sign(Corpus.Bytes("empty-page.pdf"), options => options.WithCertification(PdfCertificationPermissions.NoChanges));
        byte[] lta = pki.Archive(pki.AddValidationData(certified));

        using PdfDocument document = PdfDocument.Open(lta);
        IReadOnlyList<PdfSignatureValidation> validations = document.ValidateSignatures(pki.Offline());

        Assert.Equal([PdfSignatureStatus.Valid, PdfSignatureStatus.Valid], validations.Select(static validation => validation.Status));
        Assert.Equal(PdfCertificationPermissions.NoChanges, validations[0].CertificationPermissions);
        Assert.All(validations[0].Modifications, static modification => Assert.True(modification.IsPermitted, modification.Description));
        Assert.Contains(validations[0].Modifications, static modification => modification.Kind == PdfModificationKind.DocumentTimestamp);

        // poppler verifies the signature; it does not know ETSI.RFC3161 document timestamps ("Signature Type: unknown", not
        // verified, a non-zero exit), so only the signature counts.
        (_, string report) = ExternalTool.Run("pdfsig", lta);
        Assert.Equal(1, Pdfsig.ValidSignatures(report));
        Assert.Contains("Signature #2", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Earlier_signatures_still_verify_after_the_store_and_pdfsig_agrees()
    {
        using Pki pki = new();
        byte[] twice = pki.Sign(pki.Sign(Corpus.Bytes("empty-page.pdf")));

        byte[] lt = pki.AddValidationData(twice);

        using PdfDocument document = PdfDocument.Open(lt);
        IReadOnlyList<PdfSignatureValidation> validations = document.ValidateSignatures(pki.Offline());
        Assert.Equal([PdfSignatureStatus.Valid, PdfSignatureStatus.Valid], validations.Select(static validation => validation.Status));
        var store = (CosDictionary)document.Resolve(document.Catalog[Dss]);
        Assert.Equal(2, ((CosDictionary)document.Resolve(store[new CosName("VRI")])).Count);
        Assert.Equal(2, Pdfsig.ValidSignatures(Pdfsig.Run(lt)));
    }

    [Fact]
    public void A_second_update_extends_the_store_without_repeating_what_it_holds()
    {
        using Pki pki = new();
        byte[] once = pki.AddValidationData(pki.Sign(Corpus.Bytes("empty-page.pdf")));

        byte[] archived = pki.Archive(once);
        byte[] renewed = pki.AddValidationData(archived, options => options.WithOcspResponses(pki.TimestampRevocation.Ocsp(TestTimestampAuthority.Unit)));

        using PdfDocument document = PdfDocument.Open(renewed);
        var store = (CosDictionary)document.Resolve(document.Catalog[Dss]);
        Assert.Equal(4, Streams(document, store, "Certs").Count);
        Assert.Equal(2, ((CosDictionary)document.Resolve(store[new CosName("VRI")])).Count);
        Assert.Equal([PdfSignatureStatus.Valid, PdfSignatureStatus.Valid], document.ValidateSignatures(pki.Offline()).Select(static validation => validation.Status));
    }

    [Fact]
    public async Task Validation_data_is_added_asynchronously_and_fetched_only_through_the_injected_client()
    {
        using Pki pki = new();
        byte[] signed = pki.Sign(Corpus.Bytes("empty-page.pdf"));
        using PdfDocument offline = PdfDocument.Open(signed);
        using var withoutClient = new MemoryStream();
        await offline.AddValidationDataAsync(withoutClient, pki.Offline().WithOcspResponses(pki.TimestampRevocation.Ocsp(TestTimestampAuthority.Unit)), TestContext.Current.CancellationToken);
        Assert.Empty(pki.Network.Requests);

        using PdfDocument document = PdfDocument.Open(signed);
        using var output = new MemoryStream();
        await document.AddValidationDataAsync(output, pki.Collecting(), TestContext.Current.CancellationToken);

        Assert.NotEmpty(pki.Network.Requests);
        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        Assert.Equal(PdfSignatureStatus.Valid, Assert.Single(await reopened.ValidateSignaturesAsync(pki.Offline(), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public void A_document_without_signatures_is_refused_and_left_as_it_was()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => document.AddValidationData(output));

        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void Strict_mode_refuses_a_security_store_in_a_pdf_1_7_document_before_fetching_anything()
    {
        using Pki pki = new();
        byte[] signed = pki.Sign(Corpus.Bytes("empty-page.pdf"), static options => options.WithSubFilter(PdfSignatureSubFilter.AdobePkcs7Detached));
        using PdfDocument document = PdfDocument.Open(signed, new PdfOptions().UseStrict());
        Assert.True(document.Version < new PdfVersion(2, 0));
        string before = CosSnapshot.Of(document);
        int requests = pki.Network.Requests.Count;
        using var output = new MemoryStream();

        Diagnostics.DiagnosticException exception = Assert.Throws<Diagnostics.DiagnosticException>(() => document.AddValidationData(output, pki.Collecting()));

        Assert.Contains("document security store", exception.Message, StringComparison.Ordinal);
        Assert.Equal(requests, pki.Network.Requests.Count);
        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    private static List<byte[]> Streams(PdfDocument document, CosDictionary store, string key) =>
        store.TryGetValue(new CosName(key), out CosObject? value) && document.Resolve(value) is CosArray array
            ? [.. array.Select(item => document.DecodeStream((CosStream)document.Resolve(item)).ToArray())]
            : [];

    /// <summary>
    /// A signer under a root whose OCSP responder (<see cref="Network"/>) answers through an injected client, and the default
    /// time-stamping authority, whose unit's status <see cref="TimestampRevocation"/> gives.
    /// </summary>
    private sealed class Pki : IDisposable
    {
        private readonly HttpClient _networkClient;
        private readonly TestTimestampAuthority _authority = new();
        private readonly HttpClient _authorityClient;

        public Pki(DateTimeOffset? signerNotAfter = null)
        {
            Root = ValidationCertificates.Authority("CN=Broadside LTV root");
            Signer = ValidationCertificates.IssueRsaSigner(Root, "CN=Broadside LTV signer", revocationAddresses: true, notAfter: signerNotAfter);
            Network = new TestRevocationAuthority(Root);
            TimestampRevocation = new TestRevocationAuthority(TestTimestampAuthority.Root);
            _networkClient = new HttpClient(Network, disposeHandler: false);
            _authorityClient = new HttpClient(_authority, disposeHandler: false);
        }

        public X509Certificate2 Root { get; }

        public X509Certificate2 Signer { get; }

        public TestRevocationAuthority Network { get; }

        public TestRevocationAuthority TimestampRevocation { get; }

        /// <summary>A B-T signature over <paramref name="file"/>, in a new field.</summary>
        public byte[] Sign(byte[] file, Func<PdfSignatureOptions, PdfSignatureOptions>? configure = null)
        {
            using PdfDocument document = PdfDocument.Open(file);
            using var output = new MemoryStream();
            PdfSignatureOptions options = new PdfSignatureOptions()
                .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached)
                .WithTimestampClient(new PdfTimestampClient(_authorityClient, TestTimestampAuthority.Address));
            document.Sign(output, new PdfCertificateSigner(Signer, [Root]), configure?.Invoke(options) ?? options);
            return output.ToArray();
        }

        /// <summary>The file with a document timestamp from the default authority.</summary>
        public byte[] Archive(byte[] file)
        {
            using PdfDocument document = PdfDocument.Open(file);
            using var output = new MemoryStream();
            document.Timestamp(output, new PdfTimestampClient(_authorityClient, TestTimestampAuthority.Address));
            return output.ToArray();
        }

        /// <summary>The file with the validation data of its signatures, fetched through the injected client.</summary>
        public byte[] AddValidationData(byte[] file, Func<PdfSignatureValidationOptions, PdfSignatureValidationOptions>? configure = null)
        {
            using PdfDocument document = PdfDocument.Open(file);
            using var output = new MemoryStream();
            PdfSignatureValidationOptions options = Collecting();
            document.AddValidationData(output, configure?.Invoke(options) ?? options);
            return output.ToArray();
        }

        /// <summary>Trusts both roots, gives the unit's status and fetches the signer's.</summary>
        public PdfSignatureValidationOptions Collecting() =>
            new PdfSignatureValidationOptions()
                .WithTrustAnchors(Root, TestTimestampAuthority.Root)
                .WithOcspResponses(TimestampRevocation.Ocsp(TestTimestampAuthority.Unit))
                .UseOnlineRevocation(_networkClient);

        /// <summary>Trusts both roots and requires every status, with nothing fetched.</summary>
        public PdfSignatureValidationOptions Offline() =>
            new PdfSignatureValidationOptions().WithTrustAnchors(Root, TestTimestampAuthority.Root).WithRequireRevocationStatus(true);

        public void Dispose()
        {
            _networkClient.Dispose();
            _authorityClient.Dispose();
            _authority.Dispose();
            Network.Dispose();
            TimestampRevocation.Dispose();
            Signer.Dispose();
            Root.Dispose();
        }
    }
}
