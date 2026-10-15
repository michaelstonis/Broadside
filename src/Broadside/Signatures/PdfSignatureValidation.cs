using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;

namespace Broadside.Signatures;

/// <summary>The outcome of validating one signature.</summary>
/// <remarks>
/// ISO 32000-2 §12.8.1: "if the verification fails, the signature shall be considered invalid". The three outcomes follow ETSI
/// EN 319 102-1 §5.1.3 (TOTAL-PASSED, TOTAL-FAILED, INDETERMINATE).
/// </remarks>
public enum PdfSignatureStatus
{
    /// <summary>The byte range, the digest and the signature value check out, and the signer's chain reaches a trust anchor.</summary>
    Valid,

    /// <summary>The signed bytes or the signature value do not check out: the document or the signature was changed or forged.</summary>
    Invalid,

    /// <summary>
    /// Nothing shows the signature is broken, but it cannot be accepted either: the signer is not trusted, a certificate is outside its
    /// validity period, or an algorithm or format is not supported.
    /// </summary>
    Indeterminate,
}

/// <summary>A reason a signature is not <see cref="PdfSignatureStatus.Valid"/>.</summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 and §12.8.3; RFC 5652 §5.4 and §5.6; RFC 5280 §6.1. The kinds before <see cref="SubFilterUnsupported"/> make a
/// signature <see cref="PdfSignatureStatus.Invalid"/>; the others make it <see cref="PdfSignatureStatus.Indeterminate"/>.
/// </remarks>
public enum PdfSignatureProblemKind
{
    /// <summary>The signature dictionary has no <c>ByteRange</c> array of integers (§12.8.1, Table 255).</summary>
    ByteRangeMissing,

    /// <summary>
    /// The <c>ByteRange</c> is not ascending, non-overlapping pairs of non-negative offset and length within the file (§12.8.1,
    /// Table 255).
    /// </summary>
    ByteRangeInvalid,

    /// <summary>
    /// The byte ranges leave out more of the file than the signature value: they do not start at the first byte, or leave a second
    /// gap (§12.8.1: the range "should be the entire PDF file ... excluding the signature value itself").
    /// </summary>
    ByteRangeGap,

    /// <summary>The signature dictionary has no <c>Contents</c> string (§12.8.1, Table 255).</summary>
    ContentsMissing,

    /// <summary>
    /// The bytes the byte ranges leave out are not this signature dictionary's <c>Contents</c> as one hexadecimal string
    /// (§12.8.3.3.1: it "shall fit precisely in the space between the ranges").
    /// </summary>
    ContentsOutsideGap,

    /// <summary>The signature value is not a CMS <c>SignedData</c> this validation can decode (§12.8.3.3.1; RFC 5652 §5).</summary>
    SignatureEncodingInvalid,

    /// <summary>The digest of the signed bytes is not the one the signature carries: the bytes changed after signing (§12.8.1).</summary>
    DigestMismatch,

    /// <summary>The CMS signed attributes lack the message digest or name another content type (RFC 5652 §5.3, §11.1, §11.2).</summary>
    SignedAttributesInvalid,

    /// <summary>The signature value does not verify with the signer certificate's public key (RFC 5652 §5.6).</summary>
    SignatureValueInvalid,

    /// <summary>
    /// The byte ranges do not end at the end of a revision of the file: at a <c>%%EOF</c> marker, and the end-of-line marker after it,
    /// that terminates a revision's trailer, so what was signed cannot be compared with what follows (§12.8.1: the range ends "to the
    /// end of the %%EOF comment, possibly followed by an optional EOL marker, terminating the incremental update that adds the digital
    /// signature dictionary"; §7.5.6).
    /// </summary>
    SignedRevisionUnknown,

    /// <summary>
    /// An incremental update after the signature changed the document in a way the permissions in effect do not allow: those of the
    /// certification signature (§12.8.2.2, Table 257), narrowed by signed field locks (§12.7.5.5, Table 236). See
    /// <see cref="PdfSignatureValidation.Modifications"/>.
    /// </summary>
    ModificationNotPermitted,

    /// <summary>
    /// An incremental update after the signature changed a form field the signature locks: its field's lock dictionary or its FieldMDP
    /// transform parameters name it (§12.7.5.5, Table 236; §12.8.2.4, Table 259).
    /// </summary>
    LockedFieldModified,

    /// <summary>The <c>SubFilter</c> names an encoding this validation does not implement (§12.8.3.1, Table 260).</summary>
    SubFilterUnsupported,

    /// <summary>A digest or signature algorithm is not one this validation implements on this platform (§12.8.3.1, Table 260).</summary>
    AlgorithmUnsupported,

    /// <summary>The CMS object does not carry the certificate its signer information names (§12.8.3.3.1).</summary>
    SignerCertificateMissing,

    /// <summary>A certificate of the chain expired before the validation time (RFC 5280 §6.1.3 (a)(2)).</summary>
    CertificateExpired,

    /// <summary>A certificate of the chain is not valid until after the validation time (RFC 5280 §6.1.3 (a)(2)).</summary>
    CertificateNotYetValid,

    /// <summary>No issuer of a certificate of the chain was found among the signature's, the extra and the trusted certificates.</summary>
    ChainIncomplete,

    /// <summary>The chain ends at a self-signed certificate that is not a trust anchor.</summary>
    ChainUntrusted,

    /// <summary>A certificate that issued another is not a certification authority, or may not sign certificates (RFC 5280 §6.1.4 (k), (n)).</summary>
    IssuerNotCertificateAuthority,

    /// <summary>The signer certificate's key usage allows neither digital signatures nor non-repudiation (RFC 5280 §4.2.1.3).</summary>
    KeyUsageNotPermitted,

    /// <summary>The chain is longer than an issuer's path length constraint allows (RFC 5280 §6.1.4 (l), (m)).</summary>
    PathLengthExceeded,
}

/// <summary>One reason a signature is not valid, with what was found.</summary>
/// <remarks>ISO 32000-2 §12.8.1.</remarks>
public sealed class PdfSignatureProblem
{
    internal PdfSignatureProblem(PdfSignatureProblemKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    /// <summary>Gets the reason.</summary>
    /// <remarks>ISO 32000-2 §12.8.1.</remarks>
    public PdfSignatureProblemKind Kind { get; }

    /// <summary>Gets what was found, for a person to read.</summary>
    /// <remarks>ISO 32000-2 §12.8.1.</remarks>
    public string Message { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>
/// The validation of one signature of a document: its status, the reasons for it, and what was read on the way (the byte range, the
/// digest algorithm, the signer certificate and the certificate path that was built).
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 (verification: the signer's certificate is determined and verified, the digest recomputed and compared) and
/// §12.8.3 (Table 260, CMS signatures), and modification detection (§12.8.2.2.2, §12.8.2.4): what the incremental updates after the
/// signed revision changed, and whether the permissions in effect and the signature's field locks allow it. Revocation and timestamps
/// are not part of this validation.
/// </remarks>
public sealed class PdfSignatureValidation
{
    internal PdfSignatureValidation(
        PdfSignatureField field,
        string? subFilter,
        IReadOnlyList<long> byteRange,
        bool coversWholeFile,
        HashAlgorithmName? digestAlgorithm,
        X509Certificate2? signerCertificate,
        IReadOnlyList<X509Certificate2> certificatePath,
        IReadOnlyList<PdfSignatureProblem> problems,
        int? signedRevision = null,
        PdfCertificationPermissions? certificationPermissions = null,
        IReadOnlyList<PdfModification>? modifications = null)
    {
        Field = field;
        FieldName = field.FullyQualifiedName;
        SubFilter = subFilter;
        ByteRange = byteRange;
        CoversWholeFile = coversWholeFile;
        DigestAlgorithm = digestAlgorithm;
        SignerCertificate = signerCertificate;
        CertificatePath = certificatePath;
        Problems = problems;
        SignedRevision = signedRevision;
        CertificationPermissions = certificationPermissions;
        Modifications = modifications ?? [];
        Status = problems.Count == 0 ? PdfSignatureStatus.Valid
            : problems.Any(static problem => problem.Kind < PdfSignatureProblemKind.SubFilterUnsupported) ? PdfSignatureStatus.Invalid
            : PdfSignatureStatus.Indeterminate;
    }

    /// <summary>Gets the signature field whose value the signature dictionary is.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public PdfSignatureField Field { get; }

    /// <summary>Gets the field's fully qualified name.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2.</remarks>
    public string FieldName { get; }

    /// <summary>Gets the outcome.</summary>
    /// <remarks>ISO 32000-2 §12.8.1.</remarks>
    public PdfSignatureStatus Status { get; }

    /// <summary>Gets the reasons the signature is not valid; empty when it is.</summary>
    /// <remarks>ISO 32000-2 §12.8.1.</remarks>
    public IReadOnlyList<PdfSignatureProblem> Problems { get; }

    /// <summary>Gets the signature dictionary's <c>SubFilter</c>, or <see langword="null"/> when it has none.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255, and §12.8.3.1, Table 260.</remarks>
    public string? SubFilter { get; }

    /// <summary>Gets the <c>ByteRange</c> as read: pairs of offset from the first byte of the file and length; empty when unreadable.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public IReadOnlyList<long> ByteRange { get; }

    /// <summary>
    /// Gets a value indicating whether the byte ranges reach the end of the file (only white space after them). When they do not,
    /// later incremental updates follow the signed revision (§12.8.1, NOTE 1); what they changed is not examined here.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1 and §7.5.6.</remarks>
    public bool CoversWholeFile { get; }

    /// <summary>Gets the digest algorithm of the signer information, or <see langword="null"/> when it was not read.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.1, Table 260; RFC 5652 §5.3.</remarks>
    public HashAlgorithmName? DigestAlgorithm { get; }

    /// <summary>Gets the signer's certificate, or <see langword="null"/> when the signature does not carry it.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.3.1: "at minimum the CMS object shall include the signer's X.509 signing certificate".</remarks>
    public X509Certificate2? SignerCertificate { get; }

    /// <summary>
    /// Gets the certificate path that was built: the signer's certificate first, then each issuer, ending at a trust anchor when the
    /// chain is trusted; empty when there is no signer certificate.
    /// </summary>
    /// <remarks>RFC 5280 §6.1.</remarks>
    public IReadOnlyList<X509Certificate2> CertificatePath { get; }

    /// <summary>
    /// Gets the index in <see cref="PdfDocument.Revisions"/> of the revision the signature covers: the one whose end the byte ranges
    /// end at; <see langword="null"/> when they end elsewhere or could not be read.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255, and §7.5.6.</remarks>
    public int? SignedRevision { get; }

    /// <summary>
    /// Gets the changes a certification signature permits after it (its DocMDP transform parameters' <c>P</c>), or
    /// <see langword="null"/> for an approval signature or a document timestamp. Only the document's first signature certifies: a
    /// DocMDP reference in a later one is ignored (§12.8.1).
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1 and §12.8.2.2, Tables 256 and 257.</remarks>
    public PdfCertificationPermissions? CertificationPermissions { get; }

    /// <summary>
    /// Gets what the incremental updates after the signed revision changed, in revision order, each with whether the permissions in
    /// effect and the signature's field locks allow it; empty when nothing follows the signed revision or it is unknown.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.2.2.2 and §12.8.2.4.</remarks>
    public IReadOnlyList<PdfModification> Modifications { get; }

    /// <summary>Returns this validation with the outcome of modification detection added.</summary>
    internal PdfSignatureValidation WithModifications(
        int? signedRevision,
        PdfCertificationPermissions? certificationPermissions,
        IReadOnlyList<PdfModification> modifications,
        IReadOnlyList<PdfSignatureProblem> problems) =>
        new(Field, SubFilter, ByteRange, CoversWholeFile, DigestAlgorithm, SignerCertificate, CertificatePath, [.. Problems, .. problems], signedRevision, certificationPermissions, modifications);
}
