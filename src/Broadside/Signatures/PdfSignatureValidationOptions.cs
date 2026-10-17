using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// What signature validation trusts and uses: the certificates a signer's chain must reach, other certificates that may complete a
/// chain, the time at which certificates must be valid, revocation data, and whether revocation data may be fetched.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 ("the signer's certificate shall be determined and verified by the signature handler to match with any of the
/// validation parameters") and §12.8.3.3.1 ("the policy of how to establish trusted identity lists ... is up to the validation
/// signature handler"); RFC 5280 §6.1 for the trust anchors and the validation time; §12.8.3.4.6 to §12.8.3.4.8 for revocation
/// (RFC 5280 §6.3, RFC 6960). Nothing is trusted by default and nothing is fetched: the operating system's certificate store is not
/// consulted, no certificate is downloaded, and revocation is checked with the data the signatures, the document security store and these
/// options carry until <see cref="OnlineRevocationClient"/> is set.
/// </remarks>
public sealed class PdfSignatureValidationOptions
{
    private IReadOnlyList<X509Certificate2> _trustAnchors = [];
    private IReadOnlyList<X509Certificate2> _extraCertificates = [];
    private IReadOnlyList<byte[]> _ocspResponses = [];
    private IReadOnlyList<byte[]> _certificateRevocationLists = [];

    /// <summary>
    /// Gets or sets the trust anchors: a signer's certificate chain is trusted when it reaches one of them, whether a root or an
    /// intermediate. Empty by default, so no chain is trusted until anchors are given.
    /// </summary>
    /// <remarks>RFC 5280 §6.1.1 (d): an anchor's own validity and constraints are not checked; it is trusted as given.</remarks>
    public IReadOnlyList<X509Certificate2> TrustAnchors
    {
        get => _trustAnchors;
        set => _trustAnchors = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets other certificates that may complete a chain the signature does not carry in full (intermediates from a
    /// document security store or a local cache). They are not trusted. Empty by default.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.3.1 (the chain in the CMS object) and §12.8.4.3 (the document security store).</remarks>
    public IReadOnlyList<X509Certificate2> ExtraCertificates
    {
        get => _extraCertificates;
        set => _extraCertificates = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets the time at which every certificate of a chain must be within its validity period; <see langword="null"/> (the
    /// default) for the time validation runs.
    /// </summary>
    /// <remarks>
    /// RFC 5280 §6.1.1 (b) and §4.1.2.5. This is the current time of ISO 32000-2 §12.8.3.4.7: a time-stamping unit's path is validated
    /// at it, and a signer's path too unless a valid signature time-stamp token gives an earlier time (§12.8.3.4.8, see
    /// <see cref="PdfSignatureValidation.ValidationTime"/>).
    /// </remarks>
    public DateTimeOffset? ValidationTime { get; set; }

    /// <summary>
    /// Gets or sets DER-encoded OCSP responses (<c>OCSPResponse</c>, RFC 6960 §4.2.1) to check certificates against, besides those the
    /// signatures and the document security store carry. Empty by default.
    /// </summary>
    /// <remarks>RFC 6960 §4.2; ISO 32000-2 §12.8.3.4.6. A response counts only when the certificate's issuer, or a responder it
    /// delegated to (RFC 6960 §4.2.2.2), signed it.</remarks>
    public IReadOnlyList<byte[]> OcspResponses
    {
        get => _ocspResponses;
        set => _ocspResponses = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets DER-encoded certificate revocation lists (RFC 5280 §5) to check certificates against, besides those the signatures
    /// and the document security store carry. Empty by default.
    /// </summary>
    /// <remarks>RFC 5280 §5 and §6.3; ISO 32000-2 §12.8.3.4.6. A list counts only when the certificate's issuer signed it.</remarks>
    public IReadOnlyList<byte[]> CertificateRevocationLists
    {
        get => _certificateRevocationLists;
        set => _certificateRevocationLists = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets a value indicating whether a certificate whose revocation status no CRL or OCSP response establishes makes the
    /// signature <see cref="PdfSignatureStatus.Indeterminate"/> (<see cref="PdfSignatureProblemKind.RevocationUnknown"/>). Off by
    /// default: missing revocation data is shown in <see cref="PdfSignatureValidation.Revocations"/> but not reported as a problem.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.4.5 d) and §12.8.3.4.6 ("the revocation status of every certificate ... shall be checked").</remarks>
    public bool RequireRevocationStatus { get; set; }

    /// <summary>
    /// Gets or sets the client revocation data is fetched through when the document and these options hold none that applies: an OCSP
    /// request to the responder a certificate's authority information access names (RFC 6960 Appendix A.1, HTTP POST), else the CRL at
    /// its CRL distribution point (RFC 5280 §4.2.1.13). <see langword="null"/> (the default) fetches nothing.
    /// </summary>
    /// <remarks>
    /// RFC 6960 Appendix A; RFC 5280 §4.2.1.13 and §4.2.2.1. The addresses come from the certificates in the document, so opting in lets
    /// a document make the client send requests to hosts it names (only <c>http</c> and <c>https</c>, at most 32 per validation). The
    /// synchronous <see cref="PdfDocument.ValidateSignatures"/> sends with <see cref="HttpClient.Send(HttpRequestMessage)"/>, which the
    /// browser does not support; use <see cref="PdfDocument.ValidateSignaturesAsync"/> there.
    /// </remarks>
    public HttpClient? OnlineRevocationClient { get; set; }

    /// <summary>Sets <see cref="TrustAnchors"/>.</summary>
    /// <param name="trustAnchors">The certificates to trust.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.3.1; RFC 5280 §6.1.1 (d).</remarks>
    public PdfSignatureValidationOptions WithTrustAnchors(params IEnumerable<X509Certificate2> trustAnchors)
    {
        TrustAnchors = [.. trustAnchors];
        return this;
    }

    /// <summary>Sets <see cref="ExtraCertificates"/>.</summary>
    /// <param name="extraCertificates">Certificates that may complete a chain.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.3.1.</remarks>
    public PdfSignatureValidationOptions WithExtraCertificates(params IEnumerable<X509Certificate2> extraCertificates)
    {
        ExtraCertificates = [.. extraCertificates];
        return this;
    }

    /// <summary>Sets <see cref="ValidationTime"/>.</summary>
    /// <param name="validationTime">The time, or <see langword="null"/> for the time validation runs.</param>
    /// <returns>These options.</returns>
    /// <remarks>RFC 5280 §6.1.1 (b).</remarks>
    public PdfSignatureValidationOptions WithValidationTime(DateTimeOffset? validationTime)
    {
        ValidationTime = validationTime;
        return this;
    }

    /// <summary>Sets <see cref="OcspResponses"/>.</summary>
    /// <param name="ocspResponses">DER-encoded OCSP responses.</param>
    /// <returns>These options.</returns>
    /// <remarks>RFC 6960 §4.2.1.</remarks>
    public PdfSignatureValidationOptions WithOcspResponses(params IEnumerable<byte[]> ocspResponses)
    {
        OcspResponses = [.. ocspResponses];
        return this;
    }

    /// <summary>Sets <see cref="CertificateRevocationLists"/>.</summary>
    /// <param name="certificateRevocationLists">DER-encoded certificate revocation lists.</param>
    /// <returns>These options.</returns>
    /// <remarks>RFC 5280 §5.</remarks>
    public PdfSignatureValidationOptions WithCertificateRevocationLists(params IEnumerable<byte[]> certificateRevocationLists)
    {
        CertificateRevocationLists = [.. certificateRevocationLists];
        return this;
    }

    /// <summary>Sets <see cref="RequireRevocationStatus"/>.</summary>
    /// <param name="requireRevocationStatus">Whether a certificate without an established revocation status is a problem.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.4.6.</remarks>
    public PdfSignatureValidationOptions WithRequireRevocationStatus(bool requireRevocationStatus)
    {
        RequireRevocationStatus = requireRevocationStatus;
        return this;
    }

    /// <summary>Sets <see cref="OnlineRevocationClient"/>: revocation data is fetched through <paramref name="httpClient"/>.</summary>
    /// <param name="httpClient">The client, or <see langword="null"/> to fetch nothing.</param>
    /// <returns>These options.</returns>
    /// <remarks>RFC 6960 Appendix A.1; RFC 5280 §4.2.1.13.</remarks>
    public PdfSignatureValidationOptions UseOnlineRevocation(HttpClient? httpClient)
    {
        OnlineRevocationClient = httpClient;
        return this;
    }
}
