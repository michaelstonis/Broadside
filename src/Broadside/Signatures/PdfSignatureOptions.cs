using System.Security.Cryptography;

namespace Broadside.Signatures;

/// <summary>
/// Options for one signature: the field it goes into, the signature properties it records, the sub-filter and the digest algorithm.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1, Table 255, §12.8.3 and §12.7.5.5. The defaults sign a new invisible signature field named <c>Signature1</c>
/// (or the first such name the form does not use) with <c>adbe.pkcs7.detached</c> and the digest the signer's key calls for
/// (SHA-256 for RSA and P-256), and record the time of signing.
/// </remarks>
public sealed class PdfSignatureOptions
{
    private HashAlgorithmName? _digestAlgorithm;
    private PdfCertificationPermissions? _certification;

    /// <summary>
    /// Gets or sets the fully qualified name of the signature field to sign. An unsigned signature field of that name is signed; when
    /// the form has no field of that name, a new invisible signature field is created under it. <see langword="null"/> (the default)
    /// creates a new field named <c>SignatureN</c>, N the lowest number from 1 the form does not use.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.4.2 (field names) and §12.7.5.5 (signature fields; invisible when the widget's rectangle is empty).</remarks>
    public string? FieldName { get; set; }

    /// <summary>Gets or sets the reason for signing (<c>Reason</c>), or <see langword="null"/> to leave it out.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public string? Reason { get; set; }

    /// <summary>Gets or sets the host name or physical location of the signing (<c>Location</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public string? Location { get; set; }

    /// <summary>Gets or sets how to contact the signer (<c>ContactInfo</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public string? ContactInfo { get; set; }

    /// <summary>
    /// Gets or sets the name of the signer (<c>Name</c>), or <see langword="null"/> to leave it out: Table 255 says to use it "only when
    /// it is not possible to extract the name from the signature", which carries the certificate.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public string? SignerName { get; set; }

    /// <summary>
    /// Gets or sets the time of signing written as the signature dictionary's <c>M</c> entry; <see langword="null"/> (the default)
    /// writes the current time. The time is not verifiable: a timestamp is.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255, and §7.9.4 (dates).</remarks>
    public DateTimeOffset? SigningTime { get; set; }

    /// <summary>
    /// Gets or sets the encoding of the signature (<c>SubFilter</c>): <see cref="PdfSignatureSubFilter.AdobePkcs7Detached"/> (the
    /// default) or a PAdES signature, <see cref="PdfSignatureSubFilter.EtsiCadesDetached"/>, which raises the document to PDF 2.0.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.1, Table 260; §12.8.3.3; §12.8.3.4.</remarks>
    public PdfSignatureSubFilter SubFilter { get; set; } = PdfSignatureSubFilter.AdobePkcs7Detached;

    /// <summary>
    /// Gets or sets the digest algorithm for the byte range digest and the signed attributes: SHA-256, SHA-384, SHA-512 or, as
    /// <see cref="HashAlgorithmName"/>s named <c>SHA3-256</c>, <c>SHA3-384</c> and <c>SHA3-512</c>, the SHA-3 digests.
    /// <see langword="null"/> (the default) picks the one the signer's key calls for: SHA-384 for an ECDSA key on P-384, SHA-512 on
    /// P-521, SHA-256 otherwise.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.8.3.1, Table 260: SHA-256 (PDF 1.6), SHA-384 and SHA-512 (PDF 1.7). ISO/TS 32001 §5.1.4: SHA3-256, SHA3-384
    /// and SHA3-512 (PDF 2.0 with the ISO/TS 32001 developer extension, which signing declares). An ECDSA key takes only the
    /// digests of its curve's size (ISO/TS 32002 §5.1.3, Table 3): signing refuses another.
    /// </remarks>
    /// <exception cref="NotSupportedException">The value is none of the six digests.</exception>
    public HashAlgorithmName? DigestAlgorithm
    {
        get => _digestAlgorithm;
        set
        {
            if (value is { } algorithm)
            {
                CmsSignedData.RequireSupportedDigest(algorithm);
            }

            _digestAlgorithm = value;
        }
    }

    /// <summary>
    /// Gets or sets the changes the document permits after this signature, which makes it a certification signature; <see langword="null"/>
    /// (the default) makes an approval signature. A certification signature shall be the document's first signature.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.8.1 (at most one certification signature; approval signatures follow it), §12.8.2.2 (DocMDP, Table 257) and
    /// §12.8.6 (the catalog's <c>Perms</c> <c>DocMDP</c> entry, Table 263). PDF 1.5.
    /// </remarks>
    public PdfCertificationPermissions? Certification
    {
        get => _certification;
        set
        {
            if (value is { } permissions && !Enum.IsDefined(permissions))
            {
                throw new ArgumentOutOfRangeException(nameof(value), permissions, "The permissions shall be 1, 2 or 3 (Table 257).");
            }

            _certification = value;
        }
    }

    /// <summary>
    /// Gets or sets the fields this signature locks, written as the signature field's lock dictionary; <see langword="null"/> (the
    /// default) writes none. A field that already has a lock dictionary is locked by it whatever this says, and cannot take another.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Tables 235 and 236, and §12.8.2.4 (FieldMDP, Table 259). PDF 1.5; a lock's <c>P</c>, PDF 2.0.</remarks>
    public PdfFieldLockOptions? FieldLock { get; set; }

    /// <summary>Sets <see cref="Certification"/>.</summary>
    /// <param name="permissions">The changes permitted after the certification signature, or <see langword="null"/> for an approval signature.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.2.2, Table 257.</remarks>
    public PdfSignatureOptions WithCertification(PdfCertificationPermissions? permissions)
    {
        Certification = permissions;
        return this;
    }

    /// <summary>Sets <see cref="FieldLock"/>.</summary>
    /// <param name="fieldLock">The fields the signature locks, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236.</remarks>
    public PdfSignatureOptions WithFieldLock(PdfFieldLockOptions? fieldLock)
    {
        FieldLock = fieldLock;
        return this;
    }

    /// <summary>Sets <see cref="FieldName"/>.</summary>
    /// <param name="fieldName">The fully qualified field name, or <see langword="null"/> for a new field.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.2, §12.7.5.5.</remarks>
    public PdfSignatureOptions WithFieldName(string? fieldName)
    {
        FieldName = fieldName;
        return this;
    }

    /// <summary>Sets <see cref="Reason"/>.</summary>
    /// <param name="reason">The reason, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureOptions WithReason(string? reason)
    {
        Reason = reason;
        return this;
    }

    /// <summary>Sets <see cref="Location"/>.</summary>
    /// <param name="location">The location, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureOptions WithLocation(string? location)
    {
        Location = location;
        return this;
    }

    /// <summary>Sets <see cref="ContactInfo"/>.</summary>
    /// <param name="contactInfo">The contact information, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureOptions WithContactInfo(string? contactInfo)
    {
        ContactInfo = contactInfo;
        return this;
    }

    /// <summary>Sets <see cref="SignerName"/>.</summary>
    /// <param name="signerName">The signer's name, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureOptions WithSignerName(string? signerName)
    {
        SignerName = signerName;
        return this;
    }

    /// <summary>Sets <see cref="SigningTime"/>.</summary>
    /// <param name="signingTime">The time of signing, or <see langword="null"/> for the current time.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureOptions WithSigningTime(DateTimeOffset? signingTime)
    {
        SigningTime = signingTime;
        return this;
    }

    /// <summary>Sets <see cref="SubFilter"/>.</summary>
    /// <param name="subFilter">The signature's encoding.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.1, Table 260; §12.8.3.4.</remarks>
    public PdfSignatureOptions WithSubFilter(PdfSignatureSubFilter subFilter)
    {
        SubFilter = subFilter;
        return this;
    }

    /// <summary>Sets <see cref="DigestAlgorithm"/>.</summary>
    /// <param name="digestAlgorithm">SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384 or SHA3-512; <see langword="null"/> for the key's.</param>
    /// <returns>These options.</returns>
    /// <exception cref="NotSupportedException"><paramref name="digestAlgorithm"/> is none of the six digests.</exception>
    /// <remarks>ISO 32000-2 §12.8.3.1, Table 260; ISO/TS 32001 §5.1.4.</remarks>
    public PdfSignatureOptions WithDigestAlgorithm(HashAlgorithmName? digestAlgorithm)
    {
        DigestAlgorithm = digestAlgorithm;
        return this;
    }
}
