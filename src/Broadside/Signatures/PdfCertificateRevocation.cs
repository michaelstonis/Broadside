using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>What the revocation data that was found says about one certificate.</summary>
/// <remarks>RFC 6960 §2.2 (good, revoked, unknown); RFC 5280 §5.3 and §6.3.</remarks>
public enum PdfRevocationStatus
{
    /// <summary>No CRL or OCSP response that applies at the validation time was found, or none was fetched.</summary>
    NotChecked,

    /// <summary>A CRL or OCSP response the issuer signed shows the certificate was not revoked at the validation time.</summary>
    Good,

    /// <summary>A CRL or OCSP response the issuer signed lists the certificate as revoked (see <see cref="PdfCertificateRevocation.RevocationTime"/>).</summary>
    Revoked,

    /// <summary>An OCSP responder answered that it does not know the certificate (RFC 6960 §2.2).</summary>
    Unknown,
}

/// <summary>The kind of revocation data a status comes from.</summary>
/// <remarks>RFC 5280 §5 (CRL); RFC 6960 (OCSP).</remarks>
public enum PdfRevocationSource
{
    /// <summary>No revocation data applied.</summary>
    None,

    /// <summary>An OCSP response (RFC 6960).</summary>
    Ocsp,

    /// <summary>A certificate revocation list (RFC 5280 §5).</summary>
    Crl,
}

/// <summary>
/// The revocation status of one certificate of a certificate path, and where it comes from: the signature (a CMS <c>crls</c> field or the
/// <c>adbe-revocationInfoArchival</c> attribute), the document security store, the data the caller gave, or a fetch through the
/// client the caller opted in with.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.3.3.2, §12.8.3.4.6 to §12.8.3.4.8 and §12.8.4.3; RFC 5280 §5 and §6.3 (CRLs); RFC 6960 (OCSP).
/// </remarks>
public sealed class PdfCertificateRevocation
{
    internal PdfCertificateRevocation(
        X509Certificate2 certificate,
        PdfRevocationStatus status,
        PdfRevocationSource source,
        DateTimeOffset? revocationTime,
        X509RevocationReason? reason,
        bool fetched)
    {
        Certificate = certificate;
        Status = status;
        Source = source;
        RevocationTime = revocationTime;
        Reason = reason;
        Fetched = fetched;
    }

    /// <summary>Gets the certificate.</summary>
    /// <remarks>RFC 5280 §6.1.</remarks>
    public X509Certificate2 Certificate { get; }

    /// <summary>Gets the status.</summary>
    /// <remarks>RFC 6960 §2.2.</remarks>
    public PdfRevocationStatus Status { get; }

    /// <summary>Gets the kind of data the status comes from; <see cref="PdfRevocationSource.None"/> when not checked.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.4.6.</remarks>
    public PdfRevocationSource Source { get; }

    /// <summary>
    /// Gets when the certificate was revoked, for <see cref="PdfRevocationStatus.Revoked"/>. A revocation after the validation time does
    /// not make the signature indeterminate.
    /// </summary>
    /// <remarks>RFC 5280 §5.3 (revocationDate); RFC 6960 §4.2.1 (revocationTime).</remarks>
    public DateTimeOffset? RevocationTime { get; }

    /// <summary>Gets the revocation reason, when the data gives one.</summary>
    /// <remarks>RFC 5280 §5.3.1; ISO 32000-2 §12.8.3.4.8.</remarks>
    public X509RevocationReason? Reason { get; }

    /// <summary>
    /// Gets a value indicating whether the data was fetched online through
    /// <see cref="PdfSignatureValidationOptions.OnlineRevocationClient"/> rather than found in the document or the options.
    /// </summary>
    /// <remarks>RFC 6960 Appendix A.1; RFC 5280 §4.2.1.13.</remarks>
    public bool Fetched { get; }
}
